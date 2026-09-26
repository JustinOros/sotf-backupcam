# BackupCam

Shows a backup camera on the golf cart's GPS screen and turns on rear lights
while you reverse in Sons of the Forest.

## Multiplayer

Client side only. Nothing gets installed on a dedicated server, and other
players do not need the mod. Works on dedicated servers, on games hosted by a
player, and in single player.

## Installation

### Easy install

Close the game, open PowerShell and paste:

```powershell
irm https://raw.githubusercontent.com/JustinOros/sotf-backupcam/main/Install.ps1 | iex
```

It finds your game, installs RedLoader if needed, and installs the latest
BackupCam. Run it again any time to update.

### Manual install

#### Step 1: Install RedLoader

[RedLoader](https://github.com/ToniMacaroni/RedLoader/releases/latest) is the mod
loader for Sons of the Forest. The game cannot load any mod without it, so
install it first. You only have to do this once.

1. Download `RedLoader.zip` from the
   [latest RedLoader release](https://github.com/ToniMacaroni/RedLoader/releases/latest)
2. Extract it into your Sons of the Forest folder, the one containing
   `SonsOfTheForest.exe`, usually
   `C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest`
3. Launch the game once and wait until you reach the main menu. The first launch
   takes a few minutes while RedLoader processes the game files
4. Check that `MODS` appears on the main menu, then quit

#### Step 2: Install BackupCam

1. Download `BackupCam.zip` from the
   [latest release](https://github.com/JustinOros/sotf-backupcam/releases/latest)
2. Extract it into the `Mods` folder inside your game folder
3. You should end up with:

```
Mods\BackupCam.dll
Mods\BackupCam\manifest.json
```

## Usage

Get in a golf cart and drive backward. The camera feed replaces the map on the
cart's GPS screen, mirrored like a real backup camera, and the map comes back
shortly after you stop reversing. Rear lights also turn on while reversing so
the camera can see at night. If a cart has no GPS screen, the feed is shown at
the bottom of your screen instead.

The rear lights and camera are only visible to players who have the mod.

Press F1 to open the console, then use these commands:

| Command | Action |
| --- | --- |
| `backupcamoffset` | Show the current camera height, distance and tilt |
| `backupcamoffset 1.1 -1.3 20` | Set camera height, distance behind the cart, and downward tilt |
| `backupcamdump` | Write the golf cart's objects, components and textures to `UserData\BackupCamDump.txt` |

## Uninstall

Close the game, open PowerShell and paste:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/JustinOros/sotf-backupcam/main/Install.ps1))) -Remove
```

Or from a clone of this repo, run `.\Install.ps1 -Remove`. This removes only
BackupCam. RedLoader and your other mods are left alone.

## Building from source

Requires the .NET 8 SDK and RedLoader installed with its game assemblies
generated.

```powershell
.\build.ps1 -Install
```

Use `-Package` to build `BackupCam.zip` for a release. Pass `-GameDir "path"` if
the game is not found automatically.

## Troubleshooting

Check `_RedLoader\Latest.log` in your game folder. BackupCam logs a line when it
loads. If the camera never shows, run `backupcamdump` while sitting in the cart
and include the log when reporting the problem.
