# Changelog

## 0.4.0 — machine output collection

- Add a separate auto-collect master in the storage window and a saved F7 toggle while facing a producing machine. Both start Off, independently of auto-refill.
- Collect finished food from grills/cooking stations and ovens, plus output from charcoal kilns, smelters, blast furnaces, windmills and spinning wheels.
- Deposit into existing compatible stacks before using free slots and respect chest resource preferences, range, permissions and exclusive leases.
- Retain food on cooking stations and remaining machine drops when storage is full or busy; never sweep up unrelated loose items.
- Preserve native item identity, world/cheat flags and output provenance across save/load. Only the machine owner collects, with fresh checks before deposit.
- Move the auto-refill default from G to F6 because vanilla uses G for the radial menu. Migrate the old unmodified G default; preserve other custom shortcuts. Both new keys are configurable.
- Keep automatic cooking inputs disabled and special-wood protection unchanged. Retain the 0.3.2 availability and hover performance fixes.

## 0.3.2 — performance fix

- Remove scene-wide Unity object searches from ward checks; read Valheim's live ward registry instead.
- Cache controller lookup and indexed item totals for repeated build and workbench availability checks, including when no controller is nearby.
- Refresh availability on inventory changes, movement, station changes, radius/world changes, transaction changes and a short periodic timeout.
- Reuse refill hover hints and avoid finding the controller twice on every hover update.
- Avoid temporary argument arrays and allocated scopes in native requirement patches.
- Preserve fresh permission checks, chest leases and exact resource planning for actual actions.
- Add an independent low-priority headless test runner that leaves an open game's installation, config, logs and saves untouched.
- Prevent normal build/install commands from replacing the mod while Valheim is running.

## 0.3.1

- Keep the storage controller master Off by default and start every refillable object's automatic supplies Off too.
- Add a configurable G key control while facing a linked machine, fire or lamp, with a native VSS auto-refill On/Off hover prompt.
- Save and share individual object choices in co-op; the master switch pauses the whole network without clearing those choices.
- Enforce both switches during chest authorization and again before automatic supplies commit.
- Use the nearest eligible controller consistently when network ranges overlap.
- Always protect core wood, fine wood and other special woods from VSS station supplies, even with an empty exclusion list. Crafting/building retain access to their required woods.
- Add native owner-RPC tests for toggles, sender identity, actor distance/death, master/object gating and the real kiln's normal-wood conversion.

## 0.3.0 — local test build

- Craft and upgrade at linked workbenches, forges and other crafting stations using chest resources, with player inventory taking priority.
- Build with the hammer using the storage network while inside a pedestal's radius.
- Refill native fuel users and processing machines directly from storage, including fires, torches, lamps, kilns, smelters, blast furnaces, windmills and spinning wheels.
- Add a saved, shared Auto refill toggle per pedestal. Protect special woods by default with host-synchronized exclusions.
- Support manual cooking input and automatic oven fuel, while keeping automatic cooking inputs disabled to avoid burning food.
- Add a visible Stack/Half/One transfer selector and direct right-click consumption of stored food and mead.
- Add AI-generated controller and chest-preference icons styled using actual installed vanilla sprite references.
- Acquire required chest leases before crafting/building, preserve native recipe gates, and debit refill items only after owner-side acceptance.
- Expand isolated native tests for exact resource payments, changed items, alternate-ingredient batch quality, direct consumption and partial-stack controls.

## 0.2.4

- First public release, inspired by the Refined Storage mod for Minecraft.
- Searchable shared grid over nearby chests, with name/quantity/category sorting and drag-and-drop transfers.
- Book-and-quill Storage Inventory pedestal, available for 5 wood.
- Chest resource preferences and stack-first deposit routing.
- Multiplayer chest leases, permission checks and host-synchronized storage radius.
- Separate AI-generated UI layers with complete corners, clear panel spacing and native equipped-item highlights.
- Native equip/unequip messages appear above the storage window.
- AI-generated square thumbnail and Thunderstore packaging that preserves the UI asset folders.
- Successful live multiplayer testing reported by the author; 5,494 native runtime checks passed during development.

## 0.2.3

- Redesigned storage screen and equipped-item indicators.

## 0.2.2

- Added the exported pedestal model and corrected native furniture recipe registration.

## 0.2.1

- Corrected the stool prefab used for the building base.
