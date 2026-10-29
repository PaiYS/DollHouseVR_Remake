# DollHouseVR_Remake

Modern rebuild of the 2016 "layout V3.1" asymmetric VR layout-planning prototype
(Dollhouse VR concept: one user inhabits the room in VR, the other looks down into
it on a flat touchscreen and rearranges it).

## Stack

| 2016 original                       | Remake                                            |
|-------------------------------------|---------------------------------------------------|
| Unity 5.6.1                         | Unity 6000.3.18f1 (URP)                           |
| Oculus Rift + Xbox gamepad (OVR SDK)| Quest 2 / Quest 3 / Quest Pro, OpenXR + XR Interaction Toolkit 3 |
| Photon PUN classic                  | Normcore 3                                        |
| Webcam plugged into the VR PC       | Webcam frames streamed over the network (JPEG via Normcore datastore) |
| Role switch with keyboard key `1`   | Automatic: Android build = VR player, desktop/editor = bird's-eye |

## Roles

- **User A — VR player (Quest):** walks the room at human scale. Thumbstick
  locomotion/turning and teleport come from the XRI Starter Assets rig.
  Looking up shows User B's webcam feed on the ceiling.
- **User B — bird's-eye (Windows touchscreen):** sees the room top-down in
  orthographic view. Touch (or mouse) anywhere shows the **giant finger** in the
  VR player's world; press-and-drag furniture to rearrange the room live.

## One-time setup

1. Create an app at https://dashboard.normcore.io → copy the **App Key**.
2. In Unity, open `Assets/DollHouse/Settings/NormcoreAppSettings.asset` and paste
   the key (it currently holds `YOUR_NORMCORE_APP_KEY_HERE`).
3. Open `Assets/DollHouse/Scenes/DollHouse.unity`.

## Running

- **Editor play mode** = bird's-eye role by default (no headset needed).
  Use the `RoleManager` component on the `Normcore` object to force a role.
- **Quest build:** File → Build Profiles → Android, Build & Run. OpenXR is
  pre-configured (Meta Quest Support + Touch/Touch Pro/Touch Plus profiles,
  IL2CPP, ARM64, ASTC, Vulkan).
- **Touchscreen build:** Windows standalone. No XR is initialized on desktop.

Both builds join Normcore room `DollHouse` automatically.

## Where things live

- `Assets/DollHouse/Scripts` — all gameplay/network code (RoleManager,
  BirdsEyeController, WebcamCeilingStream, GiantFinger, AvatarVisuals).
- `Assets/Resources/VRAvatar.prefab`, `GiantFinger.prefab` — Normcore-instantiated.
- `Assets/Legacy` — reused 2016 furniture/texture/model assets (materials
  auto-converted to URP).
- `Assets/DollHouse/Editor/DollHouseSetup.cs` — the headless bootstrap that
  generated the scene, prefabs and project settings (safe to re-run).

## Known follow-ups

- The sketch/annotation tool from 2016 was never networked and is not yet rebuilt.
- The giant finger's orientation is auto-derived from `pointer.fbx` bounds; if it
  points the wrong way, rotate the `Model` child inside
  `Assets/Resources/GiantFinger.prefab`.
- Webcam stream is ~4 fps, 320×240 JPEG over the Normcore datastore; swap for a
  proper video transport if higher quality is needed.
