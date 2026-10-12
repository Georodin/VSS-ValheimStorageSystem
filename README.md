# VSS — Valheim Storage System

**A shared storage and resource network inspired by the Refined Storage mod for Minecraft.**

Build a book-and-quill pedestal, press **E**, and search all your nearby chests from one screen. Your items stay in the actual chests.

![VSS storage grid](https://raw.githubusercontent.com/Georodin/VSS-ValheimStorageSystem/v0.3.1/assets/screenshots/storage-grid.png)

The storage controller and linked-chest preference button now use new icons styled to fit Valheim's inventory artwork.

| Storage controller | Chest preferences |
| --- | --- |
| ![Book-and-quill storage controller icon](https://raw.githubusercontent.com/Georodin/VSS-ValheimStorageSystem/v0.3.1/assets/icons/controller.png) | ![Linked-chest preference icon](https://raw.githubusercontent.com/Georodin/VSS-ValheimStorageSystem/v0.3.1/assets/icons/chest-preference.png) |

## Features

- **Cheap to build:** Storage Inventory costs just **5 wood**, with no workbench required.
- **Nearby storage:** combines accessible chests within **20 metres**; the host can configure **1–100 metres**.
- **Search and sort:** search item names, sort by name, quantity or category, and filter resources.
- **Drag and drop:** transfer items between the storage grid and your actual player inventory.
- **Stack-first deposits:** fills compatible partial stacks before taking new chest slots.
- **Capacity at a glance:** used/total slots, percentage occupied, free slots, chest count and radius.
- **Chest preferences:** choose the preferred resource category with the small icon beside a linked chest's normal inventory.
- **Native inventory behavior:** equipped-item highlights, hotbar numbers, right-click equip/unequip/use, and native notifications above the storage window.
- **Craft and build from storage:** workbenches, forges and other crafting stations can use linked chest resources, including upgrades and alternate-ingredient recipes. Your hammer can build using the network too.
- **Station supplies:** add fuel and processing inputs directly from storage to kilns, smelters, blast furnaces, fires, torches, lamps, cooking stations, ovens and other pieces using the same native components.
- **Automatic refill:** one master switch per storage network plus a saved on/off choice for each nearby fuel user or processing machine. Both start off.
- **Partial stacks and consumption:** select Stack/Half/One and right-click stored food or mead to consume it directly.
- **Co-op support:** synchronized chest access, permissions and host-controlled settings. The author reported successful multiplayer testing of 0.2.4; the new 0.3.1 resource features need live co-op testing.

Wooden, reinforced and black metal chests, barrels, and accessible personal chests are supported. Chest privacy and ward permissions apply. Carts, ships, graves and temporary loot containers are excluded.

## Getting started

1. Install VSS through a Thunderstore-compatible mod manager. The required **BepInExPack Valheim** dependency installs automatically.
2. Install **the same VSS version on every player and the host/dedicated server**.
3. Restart Valheim, equip your hammer and build **Storage Inventory** under **Furniture** or **Storage**. You need to have discovered wood normally.
4. Place the pedestal near your chests and press **E** to open the storage grid.

The pedestal refunds its 5 wood when removed. The window closes when you leave interaction range, die, teleport, or press Escape/Tab.

For a manual installation, install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and copy the package's `plugins/VSS` folder into `BepInEx/plugins`. Keep `VSS.dll`, `vss_assets` and the complete `ui` and `icons` folders together. Avoid duplicate installations of the old mod.

## Controls

| Action | Control |
| --- | --- |
| Store items | Drag from your inventory onto the upper grid |
| Take items | Drag from storage onto an inventory slot or its empty background |
| Choose how much to transfer | Click **Amount: Stack / Half / One** above your inventory; applies to normal clicks and drags |
| Move a whole stack quickly | Shift-click |
| Move one item quickly | Ctrl-click |
| Drag half a stack | Hold Shift when starting the drag |
| Drag one item | Hold Ctrl when starting the drag |
| Equip, unequip or consume | Right-click an inventory item |
| Consume stored food or mead | Right-click it in the upper storage grid; normal food limits and cooldowns apply |
| Toggle the network's automatic supplies | Click **Auto refill master: Off / On** at the top of the storage window |
| Enable/disable automatic supplies for one object | Face its normal interaction point and press **G**; the hover prompt shows **VSS auto-refill: On / Off** |
| Add station fuel or an input from storage | Use the station's normal interaction when that resource is absent from your inventory |
| Add multiple station resources | Hold Shift while using its refill/input interaction, up to 50 items |
| Cancel a drag | Right-click or release outside a destination |
| Search | Type an item name or prefab name |
| Sort | Cycle Name, Quantity or Category; use the arrow for direction |
| Filter | Cycle the category button |

Withdrawals take at most one item's normal maximum stack size. A full player inventory accepts only the amount that fits. Unequip equipment before transferring it.

Stored consumables can be used even when your player inventory is full. Half-stack transfers round upward for odd stacks.

The capacity indicator counts **occupied item-stack slots**, not individual items. For example, `120/120 (100%) used • 0 free • FULL`. A chest with every slot occupied can still accept items into compatible partial stacks.

## Preferred resources

Open a chest normally while it is within range of a pedestal. Click the small icon beside its inventory and choose **Any, Wood, Raw food, Food, Mead, Weapons, Armor, Ammo, Materials, Trophies, Tools or Misc**.

Preferences are stored on the chest and shared with other players. Deposits fill matching partial stacks first, then prefer category-matched chests, unassigned chests, and finally other categories. Preferences guide new deposits; they do not relocate existing contents or block other items.

Item quality, variants, durability, crafter information and custom item data are preserved.

## Crafting, building and station supplies

Place a crafting station or fuel-using piece within a pedestal's radius. Use its normal game interface. Crafting and upgrading use resources from your inventory first, then accessible linked chests. Native recipe discovery, station level, shelter, DLC, inventory space and stamina requirements still apply. Building uses a nearby pedestal while the player is inside its radius. The placed piece can be outside that radius when the player is within reach.

For manual station refilling, your own inventory keeps the normal priority. When you have none of the required fuel or input, VSS supplies it from a linked chest. Cooking racks and ovens support manual input as well as fuel refilling. Native conversion lists determine accepted inputs, including the kiln, smelter, blast furnace, windmill and spinning wheel.

**Auto refill starts off at both levels.** Enable the master switch in a pedestal's storage window, then face each machine, fire or lamp you want supplied and press **G**. The hover prompt shows **VSS auto-refill: On / Off**. These choices are saved on the actual objects and shared in co-op. If you opt an object in while its master is off, the prompt shows **master Off**, and supplies remain paused. Turning the master off pauses every opted-in object in that network while preserving their individual choices. With overlapping networks, the nearest eligible controller supplies the object; an enabled farther controller cannot bypass its master's Off switch.

Loaded, opted-in machines receive fuel and processing inputs periodically, with normal capacity limits. Auto refill uses the pedestal builder's chest and ward permissions; manual actions and object toggles use the acting player's permissions. Automatic cooking inputs are deliberately disabled because unattended cooked food can burn; ovens still receive automatic fuel.

**VSS station supplies use only normal Wood when consuming wood.** Fine wood, core wood, ancient bark, Yggdrasil wood and ashwood are always protected, including when VSS fetches supplies through the normal refill interaction. Clearing `AutoRefillExcludedItems` cannot disable this protection. Coal, resin, ores and other accepted supplies remain available. Building and crafting can still use their required special woods normally. Explicitly using an item through the game's own item-selection interaction retains vanilla behavior.

Use `AutoRefillExcludedItems` for additional automatic-supply exclusions. Auto refill operates on loaded pieces and stops when the controller or object's switch is off, the pedestal is removed, or the object leaves range.

## Configuration

Edit `BepInEx/config/georo.vss.cfg`, then restart the game:

```ini
[General]
RegisterStand = true

[Storage]
RadiusMeters = 20
AutoRefillExcludedItems = FineWood,RoundLog,ElderBark,YggdrasilWood,Blackwood

[Controls]
AutoRefillToggleKey = G

[UI]
RefreshSeconds = 0.75
```

Set the radius and additional automatic-refill exclusions on the host/server; clients use the host's values. The toggle key is a local preference and can be remapped. Only loaded chests and machines are scanned, so raising the radius does not force distant world sectors to load.

## Multiplayer

The author tested the 0.2.4 storage grid in co-op successfully. Version 0.3.1 adds crafting, building and automatic supplies with per-object controls; these new features have passed isolated native checks and still need broader live co-op validation. All participants need the same mod version. Chest owners validate access, distance and the pedestal, then grant a short exclusive lease before transfers update the normal chest inventory. Vanilla chest operations respect the lease.

Crafting and building acquire all required chest leases before the native action. Changed resources or a cancelled crafting action abort before payment. Refill operations run on the machine's owner and remove a resource only after the native machine actually accepts it.

Busy chests are skipped. Cancelled or unanswered requests do not remove player items. Leases expire after 10 seconds if a player disconnects.

For broader server testing, use the [co-op checklist](https://github.com/Georodin/VSS-ValheimStorageSystem/blob/main/tests/COOP-CHECKLIST.md). Development tests cover local transfer and lease behavior; they do not replace every live network scenario.

## Source and development

Source: [Georodin/VSS-ValheimStorageSystem](https://github.com/Georodin/VSS-ValheimStorageSystem). Bug reports: [GitHub Issues](https://github.com/Georodin/VSS-ValheimStorageSystem/issues).

Clone this repository as `VSS-ValheimStorageSystem` inside your Valheim installation. The build references your locally installed game's managed assemblies and BepInEx; it requires the .NET SDK.

```powershell
.\build.ps1 -NoInstall
.\test.ps1
.\test.ps1 -RenderPreview
.\package.ps1
```

Running `build.ps1` without `-NoInstall` also installs the mod locally. `package.ps1` writes the Thunderstore ZIP under `dist` without changing the installed mod.

**6,064 native runtime checks passed at both 1440×1080 and 800×900** for version 0.3.1. Diagnostics cover inventory quantity/identity conservation, stacking, full inventories, equipment, lease expiry, native recipe discovery, asset-bundle loading, storage payment planning, scoped native recipe checks, alternate-ingredient batches, direct consumption, owner-side station acceptance, fuel/queue limits, authenticated object toggles, saved switch states, special-wood protection and UI layout. Tests use isolated temporary inventories and saves, and require Valheim to be closed. The screenshot above is an in-game runtime preview with test inventories.

## Credits

Created by **Georodin**. Inspired by the **Refined Storage mod for Minecraft**.

The thumbnail, decorative UI layers and controller/chest-preference icons were made with AI image generation; code development also used AI assistance. The square thumbnail master and exact prompt are in `assets/branding`, UI prompts/layout are in `assets/ui`, and icon masters/prompts are in `assets/icons`. Icon styling used a locally rendered reference sheet of installed vanilla sprites; vanilla artwork is not redistributed. Pedestal export details are in `assets/README.md`.

VSS is an unofficial Valheim mod.
