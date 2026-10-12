# VSS assets

## UI frame

Version 0.2.4 uses three independent, unmodified PNG assets generated with the **built-in image_gen tool**:

| Asset in `assets/ui` | Purpose |
| --- | --- |
| `outer-frame.png` (1309×1201) | Complete connected outer frame, with genuine alpha in the center and around the ornaments |
| `backing.png` (1310×1201) | One uninterrupted smoked-oak surface, displayed without tiling |
| `panel-skin.png` (1254×1254) | One blank inset skin shared by the wells and controls |

Exact prompts are saved in `ui/generation-prompts.txt`. `ui/layout.json` records the final layout; rendered diagnostics export every real RectTransform to `tests/runtime/storage-preview.layout.json` and the corresponding resolution-specific file.

Unity consumes each entire PNG as a separate layer. The full outer frame is nine-sliced with a fixed 72-unit corner area and no center fill. No corner or close-button images are cropped from a composed interface. The background is inset underneath the rails and drawn once, so it has no block boundaries or hard texture joins. The reusable panel skin uses a small border for nine-slicing. TMP text, buttons and item icons remain independent interactive elements.

The panel uses a 1040×950 logical area and fits inside the screen with margins even at narrow aspect ratios. The storage viewport is 952×354, the centered inventory viewport is 634×282, and all normal 8×4 slots fit in full. The footer ends before the bottom frame artwork. Regression tests sample the frame's actual alpha along every section edge to detect overlap and check visible TMP glyphs. Scrollbars appear only when content overflows. Equipped slots reuse the installed game's `InventoryElement.m_equiped` sprite, tint and image type; hotbar numbers remain visible.

While storage is open, nested canvases on the actual `MessageHud` text and icon graphics draw above the storage canvas. Native text, position, updates and fading are retained. Closing restores the previous sorting settings; the rest of the HUD stays below storage. Notification previews use native font/material assets and this production sorting code.

The old atlas frame and corresponding source/DLL are preserved under `backups/0.2.3-ui`; the earlier generic frame is under `backups/0.2.2-ui`. The installed mod no longer loads `vss-panel.png`.

## Pedestal asset bundle

`vss_assets` was exported through the open Editor's Unity MCP bridge from `VSS_StorageInventory_001` in `Assets/Scenes/ValheimBundle.unity`, project `C:/Workspace/Unity/TestGrid`. The original scene object and its imported GLB remain unchanged. The export copy is `Assets/VSSExport/vss_access_stand.prefab` with separate mesh, material and texture assets.

The prefab contains the **complete book/quill/pedestal model**, with its base at y=0 and height normalized to 1.25m. Its glTF shader was converted to built-in Standard materials, preserving the albedo and normal textures. Exported texture import settings use sRGB for albedo and NormalMap for normals. The bundle uses address `vss_access_stand`, LZ4 chunk compression and the Windows 64-bit target.

The exporter ran in Unity 6000.3.9f1 with the version string stripped and type trees retained. This specific bundle was subsequently loaded and rendered successfully inside the installed Valheim Unity 6000.0.75 runtime. `tests/runtime/pedestal-preview.png` records the native render.

The model is visual-only, with no ZNetView, Piece, Container or custom MonoBehaviour. VSS supplies the networked building base and health/effects by cloning the native stool (`piece_chair`), replacing its geometry and colliders, then adding the complete model and a fitted box collider. VSS adds the E interaction and exact 5-wood recipe. If the optional bundle is absent, the procedural cube-on-stool fallback remains available.

Copy `vss_assets` beside `VSS.dll` on clients. The DLL build and package scripts include it automatically. `ExportStorageModel.cs.txt` contains the C# method body used with Unity MCP `execute_code` for this export; the output path targets this checkout. For a different checkout, adjust that path before rebuilding.

## Native-style icons (0.3.0)

`icons/controller.png` and `icons/chest-preference.png` are original AI-generated transparent sprites, resized to 256×256 for the game. The full-resolution outputs are retained as `controller-master.png` and `chest-preference-master.png`, with exact prompts in `icons/generation-prompts.txt`.

The controller sprite depicts the existing book/quill/stool model and appears in the hammer build menu and storage header. The chest preference sprite depicts a small iron-banded chest with a paper category tag. The selected category remains a separate native item badge, so it can update without repainting the AI sprite.

Generation used `tests/runtime/vanilla-icon-reference.png`, an isolated render of actual installed chest, stool, workbench, forge, wood, hammer, resin and coal sprites, to match their camera angle, muted palette, rough materials and soft shading. The reference is a development diagnostic and is excluded from Git and packages; no vanilla sprite is shipped. Package and installation scripts preserve the two production PNGs under `plugins/VSS/icons`.
