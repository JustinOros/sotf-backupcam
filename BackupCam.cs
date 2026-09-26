using System.Globalization;
using System.Text;
using RedLoader;
using RedLoader.Utils;
using Sons.Gameplay;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Utils;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BackupCam;

public class BackupCam : SonsMod
{
    const float ReverseSpeed = -0.4f;
    const float HideDelay = 0.6f;
    const float SearchInterval = 0.5f;
    const float SearchRadius = 2.5f;
    const string ScreenPath = "GolfCartScreen/GolfCartGps/Canvas/ScreenMask";
    const string ScreenImageName = "BackupCamScreen";
    const string HeadLightPath = "LightsGroup/HeadLights";
    const string RearLightName = "BackupCamRearLight";
    const float ClipScanInterval = 0.2f;
    const float ClipScanRadius = 8f;

    static readonly string[] TextureProps =
    {
        "_MainTex", "_BaseMap", "_BaseColorMap", "_EmissiveColorMap", "_EmissionMap", "_UnlitColorMap", "_ColorMap"
    };

    static BackupCam _instance;

    GolfCartController _cart;
    Rigidbody _body;
    Camera _cam;
    RenderTexture _rt;
    GameObject _canvasGo;
    RawImage _image;
    GameObject _screenGo;
    GameObject _rearLightGo;
    Light _headLight;
    Light _rearLight;
    float _screenAspect;
    float _searchTimer;
    float _lastReverse = -10f;
    bool _showing;

    float _camY = 1.1f;
    float _camZ = -1.3f;
    float _camPitch = 20f;

    float _lightIntensity = 3f;
    float _lightRange = 1.5f;

    bool _clipping;
    float _clipTimer;
    readonly List<Collider> _cartColliders = new();
    readonly List<Collider> _ignored = new();
    readonly HashSet<int> _ignoredIds = new();

    public BackupCam()
    {
        _instance = this;
        OnUpdateCallback = Tick;
    }

    protected override void OnSdkInitialized()
    {
        RLog.Msg("BackupCam 1.3.0 loaded. Reverse the golf cart to show the camera on its GPS screen and turn on rear lights. Console: backupcam, backupcamoffset, backupcamlight, backupcamdump");
    }

    protected override void OnGameStart()
    {
        BuildOverlay();
    }

    void BuildOverlay()
    {
        if (_canvasGo != null) return;

        _rt = new RenderTexture(640, 360, 24);
        _rt.Create();

        _canvasGo = new GameObject("BackupCamCanvas");
        Object.DontDestroyOnLoad(_canvasGo);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        var imgGo = new GameObject("BackupCamImage");
        imgGo.transform.SetParent(_canvasGo.transform, false);
        _image = imgGo.AddComponent<RawImage>();
        _image.texture = _rt;
        _image.uvRect = new Rect(1f, 0f, -1f, 1f);
        _image.raycastTarget = false;

        var rect = _image.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 40f);
        rect.sizeDelta = new Vector2(480f, 270f);

        _canvasGo.SetActive(false);
    }

    void Tick()
    {
        if (_canvasGo == null || LocalPlayer.Transform == null)
        {
            SetShowing(false);
            return;
        }

        if (_cart == null || _body == null || !IsInCart(_body))
        {
            SetShowing(false);
            _searchTimer -= Time.deltaTime;
            if (_searchTimer > 0f) return;
            _searchTimer = SearchInterval;
            if (!FindCart()) return;
        }

        if (_clipping) UpdateClipping();

        var localVel = _body.transform.InverseTransformDirection(_body.velocity);
        if (localVel.z < ReverseSpeed) _lastReverse = Time.time;
        SetShowing(Time.time - _lastReverse < HideDelay);
    }

    bool FindCart()
    {
        RestoreCollisions();
        _cart = null;
        _body = null;
        _screenGo = null;
        _rearLightGo = null;
        _headLight = null;
        _rearLight = null;

        var player = LocalPlayer.Transform;
        var root = player.root;
        if (root != player && TryUseCart(root.GetComponentInChildren<GolfCartController>())) return true;

        foreach (var col in Physics.OverlapSphere(player.position, SearchRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col == null) continue;
            if (TryUseCart(col.GetComponentInParent<GolfCartController>())) return true;
        }

        return false;
    }

    bool TryUseCart(GolfCartController cart)
    {
        if (cart == null) return false;

        var body = cart.GetComponentInParent<Rigidbody>();
        if (body == null) body = cart.GetComponentInChildren<Rigidbody>();
        if (body == null || !IsInCart(body)) return false;

        _cart = cart;
        _body = body;
        AttachCamera();
        AttachScreen();
        AttachRearLight();
        return true;
    }

    bool IsInCart(Rigidbody body)
    {
        var player = LocalPlayer.Transform;
        if (player.IsChildOf(body.transform.root)) return true;
        return Vector3.Distance(player.position, body.position) < 1.8f;
    }

    void AttachCamera()
    {
        if (_cam == null)
        {
            var go = new GameObject("BackupCam");
            _cam = go.AddComponent<Camera>();
            _cam.fieldOfView = 100f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 200f;
            _cam.targetTexture = _rt;
            _cam.enabled = false;
            var main = Camera.main;
            if (main != null) _cam.cullingMask = main.cullingMask;
        }

        _cam.transform.SetParent(_body.transform, false);
        ApplyOffset();
    }

    void AttachScreen()
    {
        var mask = _cart.transform.root.Find(ScreenPath);
        if (mask == null)
        {
            RLog.Msg("BackupCam: GPS screen not found on this cart, using the screen overlay");
            return;
        }

        var existing = mask.Find(ScreenImageName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(ScreenImageName);
            go.layer = mask.gameObject.layer;
            go.transform.SetParent(mask, false);

            var raw = go.AddComponent<RawImage>();
            raw.texture = _rt;
            raw.uvRect = new Rect(1f, 0f, -1f, 1f);
            raw.raycastTarget = false;

            var rect = raw.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        go.transform.SetAsLastSibling();
        go.SetActive(false);
        _screenGo = go;

        var maskRect = mask.GetComponent<RectTransform>().rect;
        _screenAspect = maskRect.height > 0f ? maskRect.width / maskRect.height : 0f;
    }

    void AttachRearLight()
    {
        var root = _cart.transform.root;
        var head = root.Find(HeadLightPath);
        if (head == null)
        {
            RLog.Msg("BackupCam: headlights not found on this cart, no rear lights");
            return;
        }
        _headLight = head.GetComponent<Light>();

        var existing = root.Find(RearLightName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = Object.Instantiate(head.gameObject, root);
            go.name = RearLightName;

            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetIl2CppType().Name == "UL_FastGI")
                    Object.Destroy(c);

            var localPos = root.InverseTransformPoint(head.position);
            var localRot = Quaternion.Inverse(root.rotation) * head.rotation;
            go.transform.localPosition = new Vector3(localPos.x, localPos.y, -localPos.z);
            go.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.up) * localRot;
        }

        go.SetActive(false);
        _rearLightGo = go;
        _rearLight = go.GetComponent<Light>();
        ApplyLight();
    }

    void ApplyLight()
    {
        if (_rearLight == null || _headLight == null) return;
        _rearLight.enabled = true;
        _rearLight.intensity = _headLight.intensity * _lightIntensity;
        _rearLight.range = _headLight.range * _lightRange;
        if (_rearLight.type == LightType.Spot)
            _rearLight.spotAngle = Mathf.Min(_headLight.spotAngle * 1.25f, 150f);
    }

    void UpdateClipping()
    {
        _clipTimer -= Time.deltaTime;
        if (_clipTimer > 0f) return;
        _clipTimer = ClipScanInterval;

        var root = _body.transform.root;
        if (_cartColliders.Count == 0)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                if (c != null && !c.isTrigger && c.TryCast<WheelCollider>() == null)
                    _cartColliders.Add(c);
        }

        foreach (var other in Physics.OverlapSphere(_body.position, ClipScanRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (other == null || other.isTrigger) continue;
            if (other.attachedRigidbody != null) continue;
            if (other.TryCast<TerrainCollider>() != null) continue;
            if (other.TryCast<CharacterController>() != null) continue;
            if (other.transform.IsChildOf(root)) continue;
            if (!_ignoredIds.Add(other.GetInstanceID())) continue;

            foreach (var mine in _cartColliders)
                if (mine != null) Physics.IgnoreCollision(mine, other, true);
            _ignored.Add(other);
        }
    }

    void RestoreCollisions()
    {
        foreach (var other in _ignored)
        {
            if (other == null) continue;
            foreach (var mine in _cartColliders)
                if (mine != null) Physics.IgnoreCollision(mine, other, false);
        }
        _ignored.Clear();
        _ignoredIds.Clear();
        _cartColliders.Clear();
    }

    void ApplyOffset()
    {
        if (_cam == null) return;
        _cam.transform.localPosition = new Vector3(0f, _camY, _camZ);
        _cam.transform.localRotation = Quaternion.Euler(_camPitch, 180f, 0f);
    }

    void SetShowing(bool show)
    {
        if (show == _showing) return;
        _showing = show;

        var onScreen = _screenGo != null;
        if (_screenGo != null) _screenGo.SetActive(show);
        if (_canvasGo != null) _canvasGo.SetActive(show && !onScreen);
        if (_rearLightGo != null) _rearLightGo.SetActive(show);

        if (_cam == null) return;
        if (onScreen && _screenAspect > 0f) _cam.aspect = _screenAspect;
        else _cam.ResetAspect();
        _cam.enabled = show;
    }

    [DebugCommand("backupcam")]
    static void MainCommand(string args)
    {
        if (_instance == null) return;
        _instance.HandleMain(args);
    }

    void HandleMain(string args)
    {
        var parts = (args ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0] == "clipping" && (parts[1] == "on" || parts[1] == "off"))
        {
            _clipping = parts[1] == "on";
            if (!_clipping) RestoreCollisions();
            RLog.Msg(_clipping ? "BackupCam clipping on: the cart drives through trees and objects" : "BackupCam clipping off");
            return;
        }
        RLog.Msg($"backupcam clipping <on|off>  current: {(_clipping ? "on" : "off")}");
    }

    [DebugCommand("backupcamoffset")]
    static void OffsetCommand(string args)
    {
        if (_instance == null) return;
        _instance.SetOffset(args);
    }

    void SetOffset(string args)
    {
        var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            RLog.Msg($"backupcamoffset <y> <z> <pitch>  current: {_camY} {_camZ} {_camPitch}");
            return;
        }
        _camY = float.Parse(parts[0], CultureInfo.InvariantCulture);
        _camZ = float.Parse(parts[1], CultureInfo.InvariantCulture);
        _camPitch = float.Parse(parts[2], CultureInfo.InvariantCulture);
        ApplyOffset();
        RLog.Msg($"BackupCam offset set to {_camY} {_camZ} {_camPitch}");
    }

    [DebugCommand("backupcamlight")]
    static void LightCommand(string args)
    {
        if (_instance == null) return;
        _instance.SetLight(args);
    }

    void SetLight(string args)
    {
        var parts = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            RLog.Msg($"backupcamlight <intensity multiplier> <range multiplier>  current: {_lightIntensity} {_lightRange}");
            return;
        }
        _lightIntensity = float.Parse(parts[0], CultureInfo.InvariantCulture);
        _lightRange = float.Parse(parts[1], CultureInfo.InvariantCulture);
        ApplyLight();
        RLog.Msg($"BackupCam rear light set to {_lightIntensity}x intensity, {_lightRange}x range");
    }

    [DebugCommand("backupcamdump")]
    static void DumpCommand(string args)
    {
        if (_instance == null) return;
        _instance.WriteDump();
    }

    void WriteDump()
    {
        RLog.Msg("BackupCam dump starting");
        var sb = new StringBuilder();
        var path = Path.Combine(LoaderEnvironment.UserDataDirectory, "BackupCamDump.txt");

        try
        {
            var cart = _cart != null ? _cart : NearestCart();
            if (cart == null)
            {
                RLog.Msg("No golf cart found");
                return;
            }
            sb.Append("GolfCartController on: ").AppendLine(cart.gameObject.name);
            Dump(cart.transform.root, 0, sb);
        }
        catch (Exception e)
        {
            sb.AppendLine("DUMP FAILED: " + e);
            RLog.Error("BackupCam dump failed: " + e);
        }

        File.WriteAllText(path, sb.ToString());
        RLog.Msg($"BackupCam dump written to {path}");
    }

    GolfCartController NearestCart()
    {
        GolfCartController best = null;
        var bestDist = float.MaxValue;
        var from = LocalPlayer.Transform != null ? LocalPlayer.Transform.position : Vector3.zero;
        foreach (var cart in Object.FindObjectsOfType<GolfCartController>())
        {
            var d = Vector3.Distance(from, cart.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = cart;
            }
        }
        return best;
    }

    void Dump(Transform t, int depth, StringBuilder sb)
    {
        var pad = new string(' ', depth * 2);
        sb.Append(pad).Append(t.name);
        if (!t.gameObject.activeInHierarchy) sb.Append(" (inactive)");
        sb.AppendLine();

        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            try
            {
                DumpComponent(c, pad, sb);
            }
            catch (Exception e)
            {
                sb.Append(pad).Append("  ! error: ").AppendLine(e.Message);
            }
        }

        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, sb);
    }

    void DumpComponent(Component c, string pad, StringBuilder sb)
    {
        sb.Append(pad).Append("  - ").Append(c.GetIl2CppType().Name);

        var canvas = c.TryCast<Canvas>();
        if (canvas != null) sb.Append(" renderMode=").Append(canvas.renderMode.ToString());

        var raw = c.TryCast<RawImage>();
        if (raw != null && raw.texture != null)
            sb.Append(" texture=").Append(raw.texture.name).Append(" (").Append(raw.texture.GetIl2CppType().Name).Append(')');

        var cam = c.TryCast<Camera>();
        if (cam != null && cam.targetTexture != null)
            sb.Append(" targetTexture=").Append(cam.targetTexture.name);

        sb.AppendLine();

        var r = c.TryCast<Renderer>();
        if (r != null)
            foreach (var m in r.sharedMaterials)
                AppendMaterial(m, pad + "      ", sb);
    }

    void AppendMaterial(Material m, string pad, StringBuilder sb)
    {
        if (m == null)
        {
            sb.Append(pad).AppendLine("material null");
            return;
        }

        sb.Append(pad).Append("material ").Append(m.name).Append(" / ").Append(m.shader.name).AppendLine();
        foreach (var prop in TextureProps)
        {
            try
            {
                if (!m.HasProperty(prop)) continue;
                var tex = m.GetTexture(prop);
                if (tex == null) continue;
                sb.Append(pad).Append("  ").Append(prop).Append(" = ").Append(tex.name)
                  .Append(" (").Append(tex.GetIl2CppType().Name).Append(')').AppendLine();
            }
            catch (Exception e)
            {
                sb.Append(pad).Append("  ! ").Append(prop).Append(" error: ").AppendLine(e.Message);
            }
        }
    }
}
