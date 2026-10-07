# VSS — Valheim Storage System

**A shared storage grid inspired by the Refined Storage mod for Minecraft.**

Build a book-and-quill pedestal, press **E**, and search all your nearby chests from one screen. Your items stay in the actual chests.

![VSS storage grid](https://raw.githubusercontent.com/Georodin/VSS-ValheimStorageSystem/main/assets/screenshots/storage-grid.png)

## Features

- **Cheap to build:** Storage Inventory costs just **5 wood**, with no workbench required.
- **Nearby storage:** combines accessible chests within **20 metres**; the host can configure **1–100 metres**.
- **Search and sort:** search item names, sort by name, quantity or category, and filter resources.
- **Drag and drop:** transfer items between the storage grid and your actual player inventory.
- **Stack-first deposits:** fills compatible partial stacks before taking new chest slots.
- **Capacity at a glance:** used/total slots, percentage occupied, free slots, chest count and radius.
- **Chest preferences:** choose the preferred resource category with the small icon beside a linked chest's normal inventory.
- **Native inventory behavior:** equipped-item highlights, hotbar numbers, right-click equip/unequip/use, and native notifications above the storage window.
- **Co-op support:** synchronized chest access, permissions and host-controlled radius. Successful live multiplayer testing reported by the author.

Wooden, reinforced and black metal chests, barrels, and accessible personal chests are supported. Containers from other mods that you build with the hammer, such as OdinsKingdom crates, are supported too. Chest privacy and ward permissions apply. Carts, ships, graves, the Obliterator, world loot chests and temporary loot containers are excluded.

## Getting started

1. Install VSS through a Thunderstore-compatible mod manager. The required **BepInExPack Valheim** dependency installs automatically.
2. Install **the same VSS version on every player and the host/dedicated server**.
3. Restart Valheim, equip your hammer and build **Storage Inventory** under **Furniture** or **Storage**. You need to have discovered wood normally.
4. Place the pedestal near your chests and press **E** to open the storage grid.

The pedestal refunds its 5 wood when removed. The window closes when you leave interaction range, die, teleport, or press Escape/Tab.

For a manual installation, install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and copy the package's `plugins/VSS` folder into `BepInEx/plugins`. Keep `VSS.dll`, `vss_assets` and the complete `ui` folder together. Avoid duplicate installations of the old mod.

## Controls

| Action | Control |
| --- | --- |
| Store items | Drag from your inventory onto the upper grid |
| Take items | Drag from storage onto an inventory slot or its empty background |
| Move a whole stack quickly | Shift-click |
| Move one item quickly | Ctrl-click |
| Drag half a stack | Hold Shift when starting the drag |
| Drag one item | Hold Ctrl when starting the drag |
| Equip, unequip or consume | Right-click an inventory item |
| Cancel a drag | Right-click or release outside a destination |
| Search | Type an item name or prefab name |
| Sort | Cycle Name, Quantity or Category; use the arrow for direction |
| Filter | Cycle the category button |

Withdrawals take at most one item's normal maximum stack size. A full player inventory accepts only the amount that fits. Unequip equipment before transferring it.

The capacity indicator counts **occupied item-stack slots**, not individual items. For example, `120/120 (100%) used • 0 free • FULL`. A chest with every slot occupied can still accept items into compatible partial stacks.

## Preferred resources

Open a chest normally while it is within range of a pedestal. Click the small icon beside its inventory and choose **Any, Wood, Raw food, Food, Mead, Weapons, Armor, Ammo, Materials, Trophies, Tools or Misc**.

Preferences are stored on the chest and shared with other players. Deposits fill matching partial stacks first, then prefer category-matched chests, unassigned chests, and finally other categories. Preferences guide new deposits; they do not relocate existing contents or block other items.

Item quality, variants, durability, crafter information and custom item data are preserved.

## Configuration

Edit `BepInEx/config/georo.vss.cfg`, then restart the game:

```ini
[General]
RegisterStand = true

[Storage]
RadiusMeters = 20
ModdedContainers = true
ExcludedContainers =

[UI]
RefreshSeconds = 0.75
```

Set the radius on the host/server; clients use the host's value. Only loaded chests are scanned, so raising the radius does not force distant world sectors to load.

`ModdedContainers` links containers from other mods that players build with the hammer. Storage that belongs to a ship or cart is never linked, and neither are chests the world generated.

The first time VSS links a modded container type, it writes `Linking modded container <name>` to the BepInEx log. To leave a container type out, add its name to `ExcludedContainers`. The list takes comma-separated prefab names and also works for vanilla chests, for example `piece_chest_private`.

Use the same values for both settings on every player. They are not synchronized from the host, and the player who currently owns a chest checks them before allowing a transfer.

## Multiplayer

The author tested co-op successfully. All participants need the same mod version. Chest owners validate access, distance and the pedestal, then grant a short exclusive lease before transfers update the normal chest inventory. Vanilla chest operations respect the lease.

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

**5,494 native runtime checks passed during development**, including inventory quantity/identity conservation, stacking, full inventories, equipment, lease expiry, native recipe discovery, asset-bundle loading, and UI layout at 1440×1080 and 800×900. Tests use isolated temporary inventories and saves, and require Valheim to be closed. The screenshot above is an in-game runtime preview with test inventories.

## Credits

Created by **Georodin**. Inspired by the **Refined Storage mod for Minecraft**.

The thumbnail and decorative UI layers were made with AI image generation; code development also used AI assistance. The square thumbnail master and exact prompt are in `assets/branding`, and UI prompts/layout are in `assets/ui`. Pedestal export details are in `assets/README.md`.

VSS is an unofficial Valheim mod.
