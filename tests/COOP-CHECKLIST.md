# Live co-op acceptance checks

These require two players with VSS 0.3.2, plus VSS on the host/server. The scripted diagnostics do not replace this session. Version 0.2.4's storage grid passed author-reported co-op testing; these additions need a new live session.

- Build a pedestal for exactly 5 wood without a workbench. Rejoin and confirm it remains interactable.
- Put each supported chest type inside 20m and another outside 20m. Verify inclusion uses distance from the pedestal, including height.
- Open the grid with both players. Search, sort by quantity, and scroll with a populated network.
- Drag items in both directions, into specific player slots, and onto inventory background. Test Shift/Ctrl clicks and half-stack drags.
- Fill every chest slot with partial wood stacks. Deposit wood and verify those stacks fill without adding slots. Repeat with full stacks and incompatible item types.
- Set different chest preferences; confirm the other player sees the icon/category and new stacks route in the documented order. Rejoin to verify persistence.
- Both players withdraw the same last stack at once. Count the total across both player inventories and every chest before and after. It must remain unchanged.
- Both players deposit at once into the same nearly full chest. Confirm no duplication, lost quantity or overwritten stack.
- Keep a chest open normally on one player while the other uses the grid. That chest must reject grid transfers until the normal inventory closes.
- Open a chest normally during a grid transfer. Its normal open/stack/take-all requests must honor the temporary lease.
- Change chest contents normally while another player has the grid open. Confirm the display refreshes and a stale drag cannot create items.
- Disconnect, close the grid or die while waiting for ownership. Items must stay in the player's inventory if no commit occurred; the chest becomes available after lease expiry.
- Test ward-denied access, another player's personal chest, moving outside pedestal interaction range, destroying a pedestal/chest during a pending request, and reopening the UI after returning to the menu.
- Repeat owner handoffs with a dedicated server and both client players. Change the server radius and verify clients use the new value after reconnect/restart.
- Check BepInEx/LogOutput.log for errors, then save/reload the world and both characters to confirm persisted quantities and preferences.

## Resource-network update

- Put crafting/building materials only in linked chests. Craft at a workbench and forge, upgrade equipment, and build several hammer pieces. Verify exact costs and normal station/shelter/level restrictions.
- Mix player and chest materials. Confirm the player's resources are spent first. Try a missing second ingredient and a full output inventory: no resources may be spent.
- Test a recipe with alternative ingredients, different qualities and multiple crafting amounts. Verify the result quantity and that only the selected ingredient quality pays the cost.
- Both players craft/build against the same last resources simultaneously. Exactly the affordable number of outputs/pieces may be created, with no duplicated or lost resources.
- Cancel crafting, switch recipes or walk away while a chest lease is pending. Try moving the building ghost during a pending ownership handoff. Confirm cancelled actions do not spend resources.
- Empty the player's fuel/input items, then manually refill a campfire, torch, lamp, kiln, smelter, blast furnace and oven from the network. Test one item, Shift multi-add, full capacity, fractional fuel and a machine owned by the other player.
- Verify that both the controller master and newly encountered machines start Off. Enable the master and opt individual objects in with G. Confirm unselected objects remain untouched.
- Verify both players see the same object choices and hover prompts. Toggle objects owned by the other player and reject controls outside interaction range or behind a denied ward.
- Turn the master Off while object choices remain On: every supply must pause. Turn it back On and confirm selected objects resume. Turn one object's switch Off while the master stays On: only that object must stop.
- Confirm automatic supplies and normal-interaction VSS supplies never remove fine/core/other special woods, even with the host's extra exclusion list empty. Check that normal Wood feeds the kiln and coal/resin still refill their native users.
- Confirm supplies skip busy/private/ward-denied chests and never automatically put raw food on a cooking rack.
- Remove the controller, move a machine outside range and unload the area. Refilling must stop. Save/rejoin to verify master and object choices remain saved.
- Overlap two controller ranges with the nearest eligible controller Off and a farther controller On. Confirm the object stays paused under the nearest controller's master.
- Repeat automatic supplies on a dedicated server while both players are nearby; count accepted inputs/fuel against chest removals. Change machine ownership mid-request and disconnect its owner.
- Select Half and One. Test odd stacks, single items and a nearly full inventory; repeat with Shift/Ctrl overrides.
- Right-click stored food/mead with a full player inventory. Test normal food limits and mead cooldowns, then have both players consume the last stored item at once.
- Save/reload after resource actions and compare remaining chest amounts, player items, outputs, machine queues and fuel levels.
- Check building, normal and alternate-ingredient workbench recipes, and refill hover in a large base. Change another player's chest contents or ward permissions while the UI is open; counts should refresh and denied actions must never spend items.
