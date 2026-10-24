# Pyrite9 Drink Can

Version 0.1.0 (2026-10-24)

A drink can gimmick for VRChat avatars. Drop the prefab into any humanoid avatar — when you build, it fits the can to that avatar's hand.
Take it out, open the tab, drink it sip by sip and throw the empty can away.

日本語: `README_jp.md` / 한국어: `README_ko.md` / License: `LICENSE.txt` / Label guide: `Labels/LabelGuide_en.md`

## Features

- Take out, open, drink sip by sip, throw away — menu buttons on desktop; in VR, open the tab with a finger and drink by raising the can to your mouth
- The drink stays level as you tilt the can and shows through the opening; the level goes down with each sip
- Beer head (a dome of foam welling up as it opens and a foam layer that fades away within about 15 seconds, so the drink's colour shows) or fizz (droplets when it opens), in a foam colour you choose
- Swing an empty can to throw it; it rolls on the floor, and the landing sound is timed to the fall
- Switch hands from the menu while playing
- Custom size (four presets or any height and diameter), label image and drink colour
- Fits the avatar's hand at build time, with fingers that close around the can (Auto), or your own hand pose (Manual)
- Aluminium that reflects the world around it (lilToon, reflection probes)

## Requirements

- Unity 2022.3 (VRChat)
- VRChat SDK – Avatars 3.10 or later
- Modular Avatar
- NDMF (Non-Destructive Modular Framework)
- lilToon 2.x (VPM or `.unitypackage`)

Tested with: Unity 2022.3.22f1, VRChat SDK Avatars 3.10.5, Modular Avatar 1.18.7, NDMF 1.14.8, lilToon 2.3.4 and 2.4.1 (also in a new, empty avatar project).
Older versions (including lilToon 1.x) and preview (alpha / beta) versions have not been tested.

- The can adds about 24,000 triangles and up to 7 material slots, even while it is put away, so your Performance Rank may drop (details in "Notes").
- If you also upload an Android / Quest version of the same avatar, delete the `DrinkCan` object before building for Android (turning it off is not enough).

## Installation

1. **Add the required packages.** In VCC / ALCOM, open your avatar project with "Manage Project" and add **Modular Avatar** and **lilToon**.
   NDMF is installed automatically together with Modular Avatar. Use the stable (non-preview) versions.
   - If they are not in the list, add their repositories first (on each package's website: "Add to VCC").
   - If lilToon is already in the project as a `.unitypackage` (often bundled with avatars, in `Assets/lilToon`), do not add it a second time. If that copy is 1.x, update it following lilToon's own instructions.
2. **Import this package.** Double-click the `.unitypackage` file (or in Unity: `Assets > Import Package > Custom Package…`), leave everything checked and press **Import**. The files go to `Assets/Pyrite9/DrinkCan/`.

## How to use

1. In the Project window, open `Assets/Pyrite9/DrinkCan/` and drag **`DrinkCan.prefab`** onto your avatar (the object with the VRC Avatar Descriptor) in the Hierarchy. It becomes a child of the avatar.
   - Any place inside the avatar is fine. **If it ends up outside the avatar (e.g. at the scene root), nothing happens.**
   - Do not unpack the prefab (updates would no longer reach it).
2. Select `DrinkCan` in the Hierarchy to show its inspector. In the first section (Basics), **Menu language** (`EN` / `日本語` / `한국어`) sets the language of the in-game menu and of the inspector (default: EN).
3. (Optional) Change the other settings (see "Settings"). **Preview in hand** at the top of the inspector shows the can in the avatar's hand.
4. Test it:
   - Pressing **Play** in Unity builds the can and fills in **Last build** in the inspector (shown after you stop Play). The menu does not work in Play mode — try the can in VRChat.
   - `VRChat SDK > Show Control Panel > Builder` → **Build & Test**, then in VRChat change into the test avatar and open the **Drink Can** menu and press **Take out**, **Open** and **Drink** in turn.
   - When it works, **Build & Publish**. Build & Test shows only your own view — what others see and hear can be checked after a (Private) upload.
5. Check the result. **Last build** shows the avatar's name, the build time and **Warnings: none** when the can was fitted normally.
   - "Put the can inside an avatar" means the can is outside the avatar. "No build yet" after a build means the build did not reach the can (for example, red compile errors in the Console).
   - Warnings also appear in the Console (`Window > General > Console`) as yellow lines starting with **`[Pyrite9 Drink Can] WARN`** — see "Troubleshooting". Details are written to `Logs/Pyrite9_DrinkCan_build.txt` in your project folder.

At build time your avatar's hands and head are measured to set the can's size (from the hand and the avatar's height), its position in each hand, how the fingers close around it, and where the mouth is. Nothing in your scene is changed — only the build copy.

## Expression Menu

A single submenu **Drink Can** is added to the main menu (labels follow **Menu language**):

| Item | Type | Parameter | What it does |
|---|---|---|---|
| Take out | Toggle | `Pyrite9/DrinkCan/Out` | On: puts a new, full, unopened can in your hand. Off: throws the can (it falls from your hand, or flies the way your hand moves). There is no "put away" — the thrown can disappears 8 seconds after it comes to rest |
| Open | Button | `Pyrite9/DrinkCan/OpenBtn` | Opens the tab |
| Drink | Button | `Pyrite9/DrinkCan/Drink` | Raises the can to your mouth for one sip — **open it first** (an unopened or empty can does nothing) |
| Switch hand | Toggle | `Pyrite9/DrinkCan/SwapHand` | Moves the can to the other hand |

Synced parameters: **12 bits** (`Out`, `Opened`, `Level`, `Drink`, `SwapHand`, all under `Pyrite9/DrinkCan/`). An avatar can have up to 256 bits; if it goes over, the SDK will not upload.
Nothing is saved — reloading the avatar puts the can away. The other parameters (`OpenBtn`, contacts, sensors) are local only.

## How it works in game

| | Desktop | VR |
|---|---|---|
| Take out | Menu `Take out` | Same |
| Open | Menu `Open` | Touch the tab with your **other hand's index finger** (the menu button works too) |
| Drink | Menu `Drink` — the can goes to your mouth and back (1.6 s), one sip | Hold the top of the can **against your mouth** for a moment — one sip. Take it away and back for the next sip (the menu button works too) |
| Throw | Turn `Take out` off — the can drops in front of you and rolls a little | Turn `Take out` off while swinging — it flies that way. **An empty can is thrown just by swinging it** (starting 1 s after the last sip) |
| Switch hand | Menu `Switch hand` | Same |
| New can | `Take out` off and on again | Same |

- The thrown can lands, rolls and comes to rest, then disappears after 8 seconds (or when you take out a new can).
- Only you can open your can and drink from it. Everyone sees the can, whether it is open, the drink level and the thrown can.
- Sounds: the opening and the can landing are heard by everyone (full volume up to 2 m, fading out by 5 m). The gulp is heard only by you. Each volume can be set in the inspector (Sound).

## Settings (Drink Can component)

Names as shown in the inspector with Menu language EN (the tooltip name in brackets where it differs).

| Section | Field | Meaning |
|---|---|---|
| Basics | Menu language | `EN` / `日本語` / `한국어` — menu labels (applied at build) and the inspector's language. Default EN |
| | Spawn hand | Right hand (default) / Left hand — the hand the can appears in. **Switch hand** in the menu changes it while playing; reloading the avatar goes back to this one |
| | Hand pose — Close the fingers round the can (Grip Hand Pose) | On (default): while holding, that hand's fingers close around the can, and finger tracking of that hand pauses. Off: the fingers stay free |
| Can | Size (Size Preset) | 350 (66 × 122 mm, default) / 500 (66 × 168) / 250 slim (53 × 133) / 190 coffee (53 × 104) / Custom — fills in Height and Diameter |
| | Height, Diameter | mm (height 60–220, diameter 45–90). Changing them switches Size to Custom. Double-click a gauge to reset it to the preset value |
| | Label (Label Texture) | The image wrapped around the straight body. **Empty = plain aluminium.** **Open template** opens the drawing template for the chosen size, **Label guide** the guide (see "Drawing a label") |
| | Label fit | Stretch (fills the body) / Keep aspect (wraps once at the image's own ratio — a wide image leaves metal bands above and below, a tall one is cropped at the top and bottom) / Repeat (tiles around the can) |
| Drink | Drink colour (Liquid Color) | The colour seen through the opening |
| | Sips | Sips until the can is empty (3–10, default 6) |
| | Foam | None / Fizz (cola, cider) / Beer head (default) |
| | Foam colour (Foam Color) | The foam layer and the foam / droplets when it opens |
| Sound | Opening (Open Volume) | The tab and the pop, heard by everyone nearby. 1 = the loudest VRChat lets an avatar play, 0 = off (default 1) |
| | Gulp (Sip Volume) | The gulp on each sip, heard only by you (default 0.5) |
| | Landing (Drop Volume) | A thrown can landing, heard by everyone nearby (default 1) |
| Fine tuning | Size scale | Can size relative to the hand (0.5–2). 1 = a real can in a real-sized hand |
| | Off the palm / Toward fingertips / Toward thumb (Grip Offset X / Y / Z) | Moves the can in hand space (metres, at the avatar's reference size). Mirrored for the other hand |
| | Twist (Grip Twist) | Turns the can around its own axis (degrees) |
| | Tilt to fingertips / Tilt off the palm (Grip Tilt X / Y) | Leans the can about its middle (degrees) |
| Hand pose | Fit (Hand Pose Mode) | **Auto** (default): each finger is curled until it lies on the can and the can goes where the hand closes best (a few seconds per build, up to about 20 s on detailed hands; if no grip is found, the Manual pose is used). **Manual**: the finger sliders (humanoid muscles; moving one switches to Manual) |
| | Default pose | Puts the sliders back to the default hand pose |
| | Export… / Import… | Save the hand pose and the can's offsets / twist / tilt as a `.json` file, or load one (also into another avatar's can; loading switches to Manual) |

The inspector also shows (in the Menu language):

- **Version**: once a day it asks GitHub for the latest version and tells you when a newer one is out (see Notes).
- **Preview in hand**: builds a copy of the avatar with the current settings and puts the can in its hand (takes a few seconds). Scene view only, never saved to the scene. **While it is on, the original avatar is hidden in the Scene view**; turning it off puts everything back. Press **Refresh** after changing settings.
- **Last build**: the avatar, the time, the can scale, hand lengths, spawn hand, sips and warnings from the last Play, Build & Test, upload or Preview in hand, with buttons for the build log and this README. **OK** folds a warning you have seen for that avatar.
- **Changelog**: this version's changes (read from this README).

## Drawing a label

- **Step-by-step guide: `Labels/LabelGuide_en.md`** (also the **Label guide** button next to the label in the inspector) — template zones, Label fit, custom sizes, image tips.
- `Labels/` has a template for each size: `DrinkCan_LabelTemplate_350ml.png`, `_500ml`, `_250mlSlim`, `_190gCoffee` (and a 2:1 `DrinkCan_LabelTemplate.png` for custom sizes).
  Each is 2048 px wide at the exact aspect ratio of that can body, with a 10 mm grid, the front area, the seam and safe margins.
  **Draw at the template's size** and Stretch does not distort the label. The circle on the template should stay round on the can.
- The left and right edges meet at the seam under the opening; the middle of the image is the front.
- `Labels/DrinkCan_Label_PyriteLager.png` is the sample label on the prefab. It is 2:1, so on 350 ml the inspector notes that Stretch distorts it a little — that is expected.
- There are also four more sample labels, one per size (fictional brands). Choose the matching **Size** and drag the PNG from the Project window into **Label**:
  `DrinkCan_Label_WhiteStatic_500ml.png` (energy drink, 500 ml) · `DrinkCan_Label_RidgeDry_350ml.png` (lager, 350 ml) · `DrinkCan_Label_NightLimeCola_250mlSlim.png` (lime cola, 250 ml slim) · `DrinkCan_Label_DailyBean_190gCoffee.png` (coffee, 190 g).
- Keep your own labels in your own folder (e.g. `Assets/MyLabels/`), not inside `Assets/Pyrite9/DrinkCan/`.
- After assigning your image, the next Play or upload may open the **NDMF** window (also in the Console) with "Some of your textures have mip streaming disabled." — press **Auto Fix** there. Or select your image and turn on **Streaming Mipmaps** in its import settings, then **Apply**. The VRChat SDK builder does not show this one.

## Troubleshooting

The Console shows the warnings in English (except `Build failed`, which appears only as a red error); **Last build** in the inspector shows the same warnings in the Menu language.

| Console message (`[Pyrite9 Drink Can] WARN …`) or symptom | What to do |
|---|---|
| Red errors such as `error CS0246: The type or namespace name 'nadena' could not be found` or `error CS0234: … 'modular_avatar' does not exist in the namespace 'nadena.dev'` right after importing; Play / Build does not start | Modular Avatar (and NDMF) is not installed. Add Modular Avatar in VCC / ALCOM; the errors go away after Unity recompiles. |
| The can is pink / magenta, or the metal looks wrong | lilToon is missing or too old. Install lilToon 2.x (see "Installation"). |
| `The can needs a humanoid avatar with a VRC Avatar Descriptor — not built` | Only humanoid avatars are supported (Rig: Humanoid, with a VRC Avatar Descriptor on the avatar root). |
| `Hand or head bone missing — not built` | Check the avatar's humanoid bone mapping (hands, head). |
| `No lip-sync visemes — the mouth is guessed from the view position` | Set up lip-sync visemes on the avatar descriptor, or adjust the View Position. Drinking on desktop may miss the mouth. |
| `Hand lengths differ by more than 10 % …` | The can is sized for the Spawn hand. Usually fine; adjust Size scale if the other hand looks off. |
| `Right/Left hand: hand / index finger bones missing`, `no skinned mesh on the hand`, `no hand vertices on …` | That hand uses a default grip without the finger pose. Check the finger bone mapping. |
| `Right/Left hand: the hand pose does not fit this avatar …` | That hand's fingers follow the controller. Set **Hand pose ▸ Fit** to Manual, adjust the sliders with Preview in hand on, then Refresh. |
| `Right/Left hand: the hand sinks … mm into the can` | Move the can with **Off the palm** or lower **Size scale**. |
| `… DrinkCan components — only the first one is built` | Use only one can per avatar. Delete the extra one. |
| `Build failed: …` (Last build), with a red error in the Console | The can is missing or incomplete; the rest of the avatar still builds. Please send the red error and `Logs/Pyrite9_DrinkCan_build.txt` (see "Contact"). |
| `Material template missing …`, `liquid mask material not found`, `particle shader not found`, `foam texture missing`, `sound … not found`, `throw particle mesh not made`, `no metal material` | Files of the package are missing or the prefab was edited. Delete the can from the avatar and drag `DrinkCan.prefab` in again; if that does not help, import the package again. |
| The Drink Can menu does not appear | The prefab is outside the avatar, or the build stopped with a "… — not built" warning (see above). After fixing, build and upload again. |
| The menu is in English | Set **Menu language** in the inspector and build again. |
| `Drink` does nothing | Open the can first (`Open`, or in VR the other hand's index finger on the tab). An empty can cannot be drunk — take out a new one. |
| Swinging does not throw the can | Only an **empty** can is thrown by swinging (wait a second after the last sip). Any can is thrown by turning `Take out` off. |
| Opening by finger does nothing | Use the **other** hand's index finger on the tab ring. Desktop: use the `Open` button. |
| The fingers of the holding hand do not move | By design while holding. To keep them free, turn off **Hand pose — Close the fingers round the can**. |
| The can sits wrong in the hand | Adjust Fine tuning (Off the palm, Toward fingertips, Toward thumb, Twist, Tilt) or Hand pose, with Preview in hand on. |
| The can looks too big / too small | The size follows the hand and the avatar's height. Adjust **Size scale** (0.5–2) with Preview in hand on. |
| Others cannot hear the opening / landing | They need avatar audio from you allowed (VRChat Safety / Show Avatar) and their avatar volume up; the sound fades out by 5 m. Check that Opening / Landing are not 0. |
| I cannot hear the gulp | Raise **Gulp** (default 0.5) and check your avatar volume in VRChat's audio settings. |
| The SDK says the parameters are over 256 bits | The can uses 12 synced bits. Free bits by removing or un-syncing other gimmicks' parameters. |
| The label is blurry, stretched, cut at the seam, or shows black / white patches | See `Labels/LabelGuide_en.md` ("Image tips", "Label Fit"): draw at the template size, set Compression to High Quality, no transparency. |
| The thrown can falls through the floor | It only lands on colliders in the Default and Environment layers (the world's floor); others are passed through. |

## Updating / removing

- **Update:** import the new package over the old one (leave everything checked and press Import). Your settings are kept — they are stored on the `DrinkCan` object in your scene. Do not change files inside `Assets/Pyrite9/DrinkCan/` (prefab, materials, templates) — an update overwrites them. Keep your own labels and exported `.json` files in your own folder.
- **Remove from an avatar:** delete the `DrinkCan` object from the avatar. Turning the object off in the Hierarchy does **not** remove the can from the build.
- **Remove from the project:** remove the can from your avatars first, then delete the `Assets/Pyrite9/DrinkCan/` folder. If an avatar still shows "Missing (Mono Script)" on a `DrinkCan` object, delete that object — the SDK does not upload with missing scripts.
- The inspector reads its changelog from this README — keep the README files in the package folder.

## Changelog

- **0.1.0** (2026-10-24): First release.

## Notes

- Humanoid avatars only. One can per avatar.
- Uses 7 FX layers and up to 3 Gesture layers (one empty; the others move only the holding hand's fingers, only while holding; none when Hand pose is off). The layers follow your avatar's Write Defaults setting (Modular Avatar "Match Avatar Write Defaults"), so avatars with WD on or off both work.
- Does not use the Action, Base or Additive layers, so it works alongside GoGo Loco and emotes (desktop drinking moves only the can, not your arm). Another gimmick that animates fingers in the Gesture layer may override the grip.
- The drink stays level relative to the avatar root — on a station that tilts the avatar, it tilts with it.
- The thrown can is a low-poly copy (no tab or opening) and may land in a slightly different place for each viewer.
- The landing sound assumes the can falls straight down from your hand — when the can is thrown hard, it plays slightly early, from below your hand. If the floor is more than 6 m below your hand, there is no landing sound.
- When drinking on desktop, the can briefly sits right in front of your view.
- When you open the Drink Can inspector in the Unity Editor, it asks GitHub (`api.github.com`) for this package's latest version once a day. Nothing is sent; click the version text to check again right away.
- Performance (350 ml, beer head): about 24,000 triangles (can 21,864), up to 7 material slots, 3 PhysBones (tilt and swing sensors), 2 Contact receivers + 1 sender, 7 constraints, 4 particle systems (including the thrown can, with collision; 5 with fizz), 2 audio sources, 1 VRC Raycast, 5 menu icons (256×256). No lights. All of it is counted even while the can is put away.

## Contact

- Bug reports and questions: a message on Booth (https://pyrite9.booth.pm/) or X **@Pyrite9_VRC**.
- Please include: the version (in the Drink Can inspector), the warnings from **Last build** or the Console lines starting with `[Pyrite9 Drink Can]` (and any red error), the file `Logs/Pyrite9_DrinkCan_build.txt` from your project folder, and your Unity / VRChat SDK / Modular Avatar / NDMF / lilToon versions.

## License (summary — the full text is in `LICENSE.txt`)

- Free. You may use and modify it, and upload avatars that use it (public or private).
- You may use those avatars in videos, streams and screenshots, including monetized ones.
- You may install it on someone else's avatar (e.g. commissions) only if that person downloaded this package themselves.
- You may not redistribute, share or sell the files, original or modified — including inside avatar data or assets you sell or give away. Labels you draw yourself are yours.
- Credit is not required, but appreciated ("Pyrite9 Drink Can © Pyrite9").
- Provided as is, without warranty. The terms in the version you downloaded apply to that version.
- Sounds and aluminium textures are derived from CC0 works (`Sounds/CREDITS.txt`, `Textures/THIRD_PARTY.txt`).
- The package is also on GitHub: https://github.com/Pyrite9/vrchat-drink-can (viewing and forking there are fine).

© Pyrite9
