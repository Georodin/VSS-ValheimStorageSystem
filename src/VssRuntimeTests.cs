using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VSS;

// Opt-in diagnostics only. All inventories below are temporary; no player or world inventory is touched.
internal static class VssRuntimeTests
{
    private static int _checks;
    private static string _stage = "inventory operations";
    internal static readonly bool Requested = Environment.GetCommandLineArgs().Contains("-vss-self-test");
    internal static void Run()
    {
        if (!Requested || _checks != 0) return;
        try
        {
            var source = Inv(2, 1); var dest = Inv(2, 1);
            var item = Put(source, Item(30), 0, 0); Put(dest, Item(45), 0, 0);
            Check(VssInventoryOps.Move(source, dest, item, 30, true) == 5, "partial stack first");
            Check(item.m_stack == 25 && dest.NrOfItems() == 1, "stack-only does not create slots");
            Check(VssInventoryOps.Move(source, dest, item, 30) == 25 && !source.ContainsItem(item), "empty slot accepts remainder");
            Check(Total(dest) == 75 && dest.NrOfItems() == 2, "quantity retained");
            VssGame.Notify(dest);
            Check(Math.Abs(dest.GetTotalWeight() - 75f) < 0.01f, "native weight update and reflection bindings");

            source = Inv(1, 1); dest = Inv(1, 1); item = Put(source, Item(20), 0, 0); Put(dest, Item(50), 0, 0);
            Check(VssInventoryOps.Move(source, dest, item, 20) == 0 && item.m_stack == 20, "full target unchanged");
            item.m_equipped = true;
            Check(VssInventoryOps.Move(source, Inv(2, 1), item, 20) == 0, "equipped source protected"); item.m_equipped = false;
            Check(VssInventoryOps.Move(Inv(1, 1), Inv(1, 1), item, 20) == 0, "stale source protected");
            Check(VssInventoryOps.Move(source, Inv(1, 1), item, 0) == 0, "zero request protected");

            var original = Item(20); original.m_customData["a"] = "bc"; original.m_customData["z"] = "tail";
            var clone = original.Clone(); clone.m_stack = 1;
            clone.m_customData = new Dictionary<string, string> { { "z", "tail" }, { "a", "bc" } };
            Check(new VssItemKey(original).Matches(clone), "canonical metadata order; stack count ignored");
            foreach (var change in new Action<ItemDrop.ItemData>[]
            {
                i => i.m_quality++, i => i.m_variant++, i => i.m_worldLevel++, i => i.m_durability++,
                i => i.m_crafterID++, i => i.m_crafterName = "another crafter", i => i.m_cheated = true,
                i => i.m_customData["a"] = "changed"
            })
            { clone = original.Clone(); change(clone); Check(!new VssItemKey(original).Matches(clone), "distinct metadata never merged"); }
            source = Inv(1, 1); dest = Inv(2, 1); item = Put(source, original.Clone(), 0, 0);
            clone = original.Clone(); clone.m_stack = 40; clone.m_customData["a"] = "different"; Put(dest, clone, 0, 0);
            Check(VssInventoryOps.Move(source, dest, item, 20) == 20 && dest.GetItemAt(0, 0).m_stack == 40, "different custom data uses separate slot");
            Check(dest.GetItemAt(1, 0).m_customData["a"] == "bc", "custom data retained");
            dest.GetItemAt(1, 0).m_customData["a"] = "new";
            Check(original.m_customData["a"] == "bc", "clone does not alias custom dictionary");

            source = Inv(1, 1); dest = Inv(2, 2); item = Put(source, Item(15), 0, 0);
            Check(VssInventoryOps.Move(source, dest, item, 7, false, 1, 1) == 7 && dest.GetItemAt(1, 1).m_stack == 7, "drop in requested slot");
            Check(item.m_stack == 8 && VssInventoryOps.Move(source, dest, item, 1, false, 9, 9) == 0, "invalid target retains source");

            Check(VssLeaseRules.Active(101, 100) && !VssLeaseRules.Active(100, 100), "lease expiry boundary");
            Check(!VssLeaseRules.CanCommit(false, "A", "A", 200, 100), "non-owner cannot commit");
            Check(!VssLeaseRules.CanCommit(true, "B", "A", 200, 100), "late response cannot commit newer lease");
            Check(!VssLeaseRules.CanCommit(true, "A", "A", 100, 100), "expired grant cannot commit");
            Check(VssLeaseRules.CanCommit(true, "A", "A", 200, 100), "current owner grant commits");

            // Random movement uses the actual installed Inventory.MoveItemToThis implementation.
            var random = new System.Random(72631);
            var inventories = new[] { Inv(8, 4), Inv(8, 4), Inv(8, 4), Inv(8, 4) };
            for (var n = 0; n < 40; n++)
            {
                var testItem = Item(random.Next(1, 51)); testItem.m_variant = n % 3;
                testItem.m_customData["batch"] = (n % 4).ToString();
                Put(inventories[n % 4], testItem, n / 4 % 8, n / 32);
            }
            var initial = Counts(inventories);
            for (var n = 0; n < 500; n++)
            {
                var from = inventories[random.Next(4)]; var to = inventories[random.Next(4)];
                var items = from.GetAllItems(); if (items.Count == 0) continue;
                var selected = items[random.Next(items.Count)];
                var before = selected.m_stack;
                var actual = VssInventoryOps.Move(from, to, selected, random.Next(1, 70));
                Check(actual == before - selected.m_stack, "actual moved quantity");
                Check(Counts(inventories).OrderBy(p => p.Key).SequenceEqual(initial.OrderBy(p => p.Key)), "per-identity conservation");
                foreach (var inv in inventories)
                {
                    Check(inv.GetAllItems().All(i => i.m_stack > 0 && i.m_stack <= i.m_shared.m_maxStackSize), "valid stack bounds");
                    Check(inv.GetAllItems().Select(i => $"{i.m_gridPos.x}:{i.m_gridPos.y}").Distinct().Count() == inv.NrOfItems(), "unique occupied slots");
                }
            }
            Check(VssAssets.Panel != null && VssAssets.Panel.texture.width > 1000, "AI frame loaded from installed asset");
            Check(VssAssets.Wood != null && VssAssets.Inset != null && VssAssets.Wood.texture != VssAssets.Panel.texture &&
                VssAssets.Inset.texture != VssAssets.Panel.texture, "background, outer frame and blank panel skin are separate assets");
            Check(VssAssets.Panel.texture.GetPixel(VssAssets.Panel.texture.width / 2, VssAssets.Panel.texture.height / 2).a < 0.01f,
                "outer frame has a genuinely transparent center");
            if (File.Exists(Path.Combine(Path.GetDirectoryName(typeof(VssPlugin).Assembly.Location), "vss_assets")))
                Check(VssAssets.HasStandModel, "installed Unity-exported bundle loads in Valheim's runtime");
            _stage = "installed vanilla assets and pedestal registration";
            SoftReferenceableAssets.Runtime.MakeAllAssetsLoadable();
            PrefabAndSerialization();
            _stage = "UI structure";
            Report("RUNNING: native inventory and prefab tests passed; checking UI.");
            UiStructure();
            VssPlugin.Log.LogInfo($"VSS_SELF_TEST_PASS: {_checks} checks against the installed game.");
            Report($"PASS: {_checks} checks against the installed game.");
            if (Environment.GetCommandLineArgs().Contains("-vss-preview")) VssPlugin.Instance.StartCoroutine(RenderPreview());
            else Application.Quit(0);
        }
        catch (Exception ex)
        {
            VssPlugin.Log.LogError("VSS_SELF_TEST_FAIL: " + ex);
            Report($"FAIL during {_stage}: {ex}");
            Application.Quit(1);
        }
    }
    private static Inventory Inv(int w, int h) => new("VSS temporary test", null, w, h);
    private static GameObject GamePrefab(string path)
    {
        var paths = SoftReferenceableAssets.Runtime.GetAllAssetPathsInBundleMappedToAssetID();
        Check(paths.TryGetValue(path, out var id), "installed game catalog contains " + path);
        var reference = new SoftReferenceableAssets.SoftReference<GameObject>(id);
        reference.Load();
        var prefab = reference.Asset;
        Check(prefab != null, "installed game asset loaded: " + path);
        // Retain references for the lifetime of this isolated test process.
        return prefab;
    }
    private static void PrefabAndSerialization()
    {
        var root = new GameObject("VSS isolated test fixtures"); root.SetActive(false);
        var testPlayer = root.AddComponent<Player>();
        var gameField = AccessTools.Field(typeof(Game), "<instance>k__BackingField");
        var oldGame = gameField.GetValue(null);
        gameField.SetValue(null, root.AddComponent<Game>());
        var db = root.AddComponent<ObjectDB>();
        var oldDb = AccessTools.Field(typeof(ObjectDB), "m_instance").GetValue(null);
        AccessTools.Field(typeof(ObjectDB), "m_instance").SetValue(null, db);
        var byHash = new Dictionary<int, GameObject>();
        AccessTools.Field(typeof(ObjectDB), "m_itemByHash").SetValue(db, byHash);
        var wood = GamePrefab("Assets/GameElements/Items/materials/Wood.prefab");
        var drop = wood.GetComponent<ItemDrop>();
        byHash.Add("Wood".GetStableHashCode(), wood);
        var hammer = GamePrefab("Assets/GameElements/Items/tools/Hammer.prefab");
        var hammerDrop = hammer.GetComponent<ItemDrop>();
        byHash.Add("Hammer".GetStableHashCode(), hammer);
        db.m_items.Add(wood); db.m_items.Add(hammer);
        var oldPieces = db.GetAllBuildPieces();
        Check(!oldPieces.Any(p => p.name == VssPieceRegistry.PrefabName), "native build cache starts without pedestal");
        VssPieceRegistry.RegisterRecipe(db);
        var earlyPrefab = hammerDrop.m_itemData.m_shared.m_buildPieces.m_pieces.Find(p => p.name == VssPieceRegistry.PrefabName);
        Check(earlyPrefab != null, "hammer recipe registered before network scene initialization");
        Check(db.GetAllBuildPieces().Contains(earlyPrefab.GetComponent<Piece>()), "native build cache refreshed after registration");
        var gameMain = GamePrefab("Assets/Systems/_GameMain.prefab");
        var scene = gameMain.GetComponentInChildren<ZNetScene>(true);
        Check(scene != null, "installed main game prefab contains ZNetScene");
        var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);
        // Mirror ZNetScene.Awake's native catalog registration without starting a world.
        foreach (var vanilla in scene.m_prefabs) named[vanilla.name.GetStableHashCode()] = vanilla;
        VssVanillaReferences.Export(scene);
        var stool = scene.GetPrefab(VssPieceRegistry.BasePrefabName);
        Check(stool != null && stool.GetComponent<Chair>() != null, "base stool exists in installed network prefab catalog");
        Check(hammerDrop.m_itemData.m_shared.m_buildPieces.m_pieces.Contains(stool), "base stool exists in installed hammer table");
        try
        {
            VssPieceRegistry.Register(scene);
            var prefab = scene.GetPrefab(VssPieceRegistry.PrefabName);
            Check(prefab != null && prefab.activeSelf && !prefab.activeInHierarchy, "world prefab activates when instantiated");
            Check(prefab.GetComponent<VssAccessStand>() != null && prefab.GetComponent<Chair>() == null, "pedestal opens storage instead of sitting");
            if (VssAssets.HasStandModel)
            {
                Check(prefab.GetComponentsInChildren<Renderer>(true).Length == 1 &&
                    prefab.transform.Find("vss_access_stand(Clone)/VSS_StorageInventory_001") != null, "bundle replaces vanilla stool geometry");
                var collider = prefab.GetComponent<BoxCollider>();
                Check(collider != null && Math.Abs(collider.size.y - 1.25f) < 0.01f &&
                    Math.Abs(collider.center.y - 0.625f) < 0.01f, "interaction and building collider fits full pedestal");
            }
            Check(prefab.GetComponent<Piece>().m_resources.Length == 1 && prefab.GetComponent<Piece>().m_resources[0].m_amount == 5 &&
                prefab.GetComponent<Piece>().m_resources[0].m_resItem == drop && prefab.GetComponent<Piece>().m_craftingStation == null, "exact 5 wood recipe without workbench");
            Check(hammerDrop.m_itemData.m_shared.m_buildPieces.m_pieces.Contains(prefab), "hammer registration");
            var piece = prefab.GetComponent<Piece>();
            Check(piece.m_category == Piece.PieceCategory.Furniture &&
                (piece.m_usage & Piece.UsageTagFlags.Furniture) != 0 &&
                (piece.m_usage & Piece.UsageTagFlags.Storage) != 0, "furniture category and native usage filters");
            var table = hammerDrop.m_itemData.m_shared.m_buildPieces;
            var knownMaterials = (HashSet<string>)AccessTools.Field(typeof(Player), "m_knownMaterial").GetValue(testPlayer);
            knownMaterials.Add(drop.m_itemData.m_shared.m_name);
            var haveRequirements = AccessTools.MethodDelegate<Func<Player, Piece, Player.RequirementMode, bool>>(
                AccessTools.Method(typeof(Player), "HaveRequirements", new[] { typeof(Piece), typeof(Player.RequirementMode) }));
            Check(haveRequirements(testPlayer, piece, Player.RequirementMode.IsKnown), "native player can discover pedestal knowing only wood");
            var playerInventory = Inv(8, 4);
            Put(playerInventory, hammerDrop.m_itemData.Clone(), 0, 0);
            AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(testPlayer, playerInventory);
            AccessTools.Method(typeof(Player), "UpdateKnownRecipesList").Invoke(testPlayer, null);
            var knownRecipes = (HashSet<string>)AccessTools.Field(typeof(Player), "m_knownRecipes").GetValue(testPlayer);
            Check(knownRecipes.Contains(piece.m_name), "native recipe discovery finds Storage Inventory with a hammer and known wood");
            table.UpdateAvailable(new HashSet<string> { piece.m_name }, testPlayer, false, false);
            var available = (List<List<Piece>>)AccessTools.Field(typeof(PieceTable), "m_availablePiecesByCategory").GetValue(table);
            Check(available[(int)Piece.PieceCategory.Furniture].Contains(piece), "native hammer furniture list includes Storage Inventory");
            var usage = new ByUsagePieceList("$hud_byusage");
            usage.UpdateAvailableTags(table);
            foreach (var tag in new[] { Piece.UsageTagFlags.Furniture, Piece.UsageTagFlags.Storage })
            {
                var tagId = Array.IndexOf((Piece.UsageTagFlags[])Enum.GetValues(typeof(Piece.UsageTagFlags)), tag);
                var shown = new List<Piece>(); usage.GetAvailablePiecesWithTag(tagId, table, shown);
                Check(shown.Contains(piece), "current build UI usage filter includes pedestal: " + tag);
            }
            VssPieceRegistry.Register(scene);
            Check(hammerDrop.m_itemData.m_shared.m_buildPieces.m_pieces.Count(p => p == prefab) == 1, "registration is idempotent");
            var before = Inv(2, 2); var item = drop.m_itemData.Clone(); item.m_stack = 23; item.m_dropPrefab = wood;
            item.m_quality = 1; item.m_variant = 0; item.m_durability = 83.57f; item.m_worldLevel = 1;
            item.m_crafterID = 42; item.m_crafterName = "VSS crafter"; item.m_customData["vss test data"] = "kept";
            Put(before, item, 1, 1);
            var package = new ZPackage(); before.Save(package);
            var after = Inv(2, 2); after.Load(new ZPackage(package.GetArray()));
            Check(after.NrOfItems() == 1 && after.GetItemAt(1, 1) != null, "native inventory serialization retains slot");
            // Current Valheim format 109 stores durability as integer hundredths, rather than a float.
            var expected = item.Clone(); expected.m_durability = (int)(item.m_durability * 100f) * 0.01f;
            Check(Math.Abs(after.GetItemAt(1, 1).m_durability - item.m_durability) < 0.011f, "native durability precision is within one hundredth");
            if (!new VssItemKey(expected).Matches(after.GetItemAt(1, 1)))
                VssPlugin.Log.LogWarning("Serialization fixture expected " + new VssItemKey(expected).Id + " actual " + new VssItemKey(after.GetItemAt(1, 1)).Id);
            Check(new VssItemKey(expected).Matches(after.GetItemAt(1, 1)) && after.GetItemAt(1, 1).m_stack == 23, "native serialization retains quantity and metadata at native precision");
            var shieldPath = SoftReferenceableAssets.Runtime.GetAllAssetPathsInBundleMappedToAssetID().Keys.First(p => p.EndsWith("/ShieldWood.prefab", StringComparison.Ordinal));
            var shield = GamePrefab(shieldPath); byHash[shield.name.GetStableHashCode()] = shield;
            var equipment = shield.GetComponent<ItemDrop>().m_itemData.Clone(); equipment.m_dropPrefab = shield;
            Check(equipment.m_shared.m_maxQuality >= 2 && equipment.m_shared.m_variants > 1, "serialization fixture uses an actual valid equipment quality and variant");
            equipment.m_quality = 2; equipment.m_variant = 1; equipment.m_durability = 83.57f;
            equipment.m_crafterID = 42; equipment.m_crafterName = "VSS crafter"; equipment.m_customData["vss equipment"] = "kept";
            before = Inv(2, 2); Put(before, equipment, 1, 1); package = new ZPackage(); before.Save(package);
            after = Inv(2, 2); after.Load(new ZPackage(package.GetArray()));
            expected = equipment.Clone(); expected.m_durability = (int)(equipment.m_durability * 100f) * 0.01f;
            Check(new VssItemKey(expected).Matches(after.GetItemAt(1, 1)), "native serialization preserves legitimate quality, variant, crafter and mod data");
            _stage = "storage crafting, building payment and consumption";
            VssResourceTests.Run(Check, testPlayer);
            _stage = "native owner-side machine acceptance and refill payment";
            VssRefillTests.Run(Check, scene);
        }
        finally
        {
            AccessTools.Field(typeof(ObjectDB), "m_instance").SetValue(null, oldDb);
            gameField.SetValue(null, oldGame);
            // Quit immediately after diagnostics; no synthetic objects enter a real scene or save.
        }
    }
    private static void UiStructure()
    {
        var nativeGui = GamePrefab("Assets/UI/prefabs/IngameGui/IngameGui_Inventory.prefab").GetComponentInChildren<InventoryGui>(true);
        Check(nativeGui != null && nativeGui.m_containerName.font != null, "native inventory font available");
        var equipped = nativeGui.m_player.GetComponentInChildren<InventoryGrid>(true).m_elementPrefab.GetComponent<InventoryElement>().m_equiped;
        Check(equipped != null && equipped.sprite != null, "native equipped highlight available");
        VssUi.BuildForDiagnostics(nativeGui.m_containerName.font, equipped);
        _stage = "UI grid geometry";
        var ui = UnityEngine.Object.FindFirstObjectByType<VssUi>();
        var storage = (RectTransform)AccessTools.Field(typeof(VssUi), "_storage").GetValue(ui);
        var inventory = (RectTransform)AccessTools.Field(typeof(VssUi), "_inventory").GetValue(ui);
        var panel = (RectTransform)AccessTools.Field(typeof(VssUi), "_panel").GetValue(ui);
        Check(storage.GetComponent<GridLayoutGroup>().constraintCount == 12 && inventory.GetComponent<GridLayoutGroup>().constraintCount == 8, "upper storage and lower player grids");
        Check(VssAssets.ControllerIcon != null && VssAssets.ChestIcon != null && VssSprites.StandIcon == VssAssets.ControllerIcon &&
            VssAssets.ControllerIcon.texture.width == 256 && VssAssets.ChestIcon.texture.width == 256,
            "production AI icons load at native-friendly 256 pixel resolution");
        Check(VssAssets.ControllerIcon.texture.GetPixel(0, 0).a < 0.01f && VssAssets.ChestIcon.texture.GetPixel(0, 0).a < 0.01f,
            "AI icons have transparent backgrounds");
        var auto = (TMP_Text)AccessTools.Field(typeof(VssUi), "_autoText").GetValue(ui);
        var collect = (TMP_Text)AccessTools.Field(typeof(VssUi), "_collectText").GetValue(ui);
        var amount = (TMP_Text)AccessTools.Field(typeof(VssUi), "_amountText").GetValue(ui);
        Check(auto.text == "Auto refill master: Off" && amount.text == "Amount: Stack", "refill master and transfer selectors have clear initial states");
        Check(collect.text == "Auto collect master: Off", "collection has a separate Off-by-default master");
        var refillRect = auto.GetComponentInParent<Button>(true).GetComponent<RectTransform>();
        var collectRect = collect.GetComponentInParent<Button>(true).GetComponent<RectTransform>();
        Check(refillRect.anchoredPosition.x + refillRect.rect.width <= collectRect.anchoredPosition.x,
            "refill and collect master buttons do not overlap");
        foreach (var expectedLabel in new[] { "Half", "One", "Stack" })
        {
            amount.GetComponentInParent<Button>(true).onClick.Invoke();
            Check(amount.text == "Amount: " + expectedLabel, "native UI amount button cycles to " + expectedLabel);
        }
        Check(storage.parent.GetComponent<ScrollRect>().vertical && inventory.parent.GetComponent<ScrollRect>().vertical, "both grids scroll");
        foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1440, 1080), new Vector2(1024, 768), new Vector2(800, 900) })
        {
            var scale = VssUi.PanelScale(size);
            Check(1040 * scale <= size.x - 47.9f && 950 * scale <= size.y - 47.9f, "frame fits viewport " + size);
        }
        Check(panel.Find("Storage recess") != null && panel.Find("Inventory recess") != null && panel.Find("Footer recess") != null,
            "purpose-generated art provides independent grid and footer frames");
        var frameImage = panel.Find("Complete outer frame").GetComponent<Image>();
        Check(frameImage.type == Image.Type.Sliced && !frameImage.fillCenter && !frameImage.raycastTarget,
            "complete outer frame preserves corners without intercepting item drags");
        Check(panel.Find("Continuous backing").GetComponent<Image>().type == Image.Type.Simple,
            "backing is one continuous image without tiling seams");
        Check(panel.GetComponentsInChildren<UnityEngine.UI.Button>(true).Count(b => b.name == "× button") == 1,
            "exactly one real close button, with no painted background seat");
        Check(storage.parent.GetComponent<RectMask2D>() != null && inventory.parent.GetComponent<RectMask2D>() != null,
            "scrolling clips only the grid interior");
        var snapshot = new VssSnapshot();
        for (var n = 0; n < 105; n++)
        {
            var item = Item(1); item.m_shared.m_name = "Item " + n.ToString("D3"); item.m_shared.m_icons = new[] { VssSprites.StandIcon };
            snapshot.Rows.Add(new VssItemRow { Key = new VssItemKey(item), Sample = item, MaxStack = 50, Total = n + 1, StackCount = 1 });
        }
        AccessTools.Field(typeof(VssUi), "_snapshot").SetValue(ui, snapshot);
        AccessTools.Method(typeof(VssUi), "DrawStorage").Invoke(ui, null);
        _stage = "UI search";
        Check(storage.GetComponentsInChildren<VssSlot>(true).Count(s => s.gameObject.activeSelf) == 105 && storage.sizeDelta.y > 342, "large networks create scrollable rows");
        var search = (TMP_InputField)AccessTools.Field(typeof(VssUi), "_search").GetValue(ui);
        search.SetTextWithoutNotify("Item 104"); AccessTools.Method(typeof(VssUi), "DrawStorage").Invoke(ui, null);
        Check(storage.GetComponentsInChildren<VssSlot>(true).Count(s => s.Row != null && s.gameObject.activeSelf) == 1, "search narrows the displayed grid");
        search.SetTextWithoutNotify(""); AccessTools.Field(typeof(VssUi), "_sort").SetValue(ui, 1);
        AccessTools.Field(typeof(VssUi), "_descending").SetValue(ui, true);
        AccessTools.Method(typeof(VssUi), "DrawStorage").Invoke(ui, null);
        Check(storage.GetComponentsInChildren<VssSlot>(true).First().Row.Total == 105, "quantity descending sort");

        _stage = "equipped highlights and inventory bounds";
        var playerRoot = new GameObject("VSS UI test player"); playerRoot.SetActive(false);
        var player = playerRoot.AddComponent<Player>(); var inv = Inv(8, 4);
        var equippedItem = Put(inv, Item(1), 0, 0); equippedItem.m_equipped = true;
        var ordinaryItem = Put(inv, Item(1), 1, 0);
        equippedItem.m_shared.m_icons = ordinaryItem.m_shared.m_icons = new[] { VssSprites.StandIcon };
        AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(player, inv);
        AccessTools.Field(typeof(VssUi), "_player").SetValue(ui, player);
        AccessTools.Method(typeof(VssUi), "DrawInventory").Invoke(ui, null);
        var slots = inventory.GetComponentsInChildren<VssSlot>(true);
        Check(slots.Length == 32 && slots[0].Equipped.enabled && !slots[1].Equipped.enabled && !slots[2].Equipped.enabled,
            "equipped item highlighted; ordinary and empty slots unhighlighted");
        Check(slots[0].Equipped.sprite == equipped.sprite && slots[0].Equipped.color == equipped.color && slots[0].Equipped.type == equipped.type,
            "highlight uses the actual vanilla sprite, tint and image type");
        Check(slots[0].Marker.text == "1" && slots[1].Marker.text == "2", "equipped hotbar items retain their number");
        equippedItem.m_equipped = false;
        AccessTools.Method(typeof(VssUi), "DrawInventory").Invoke(ui, null);
        Check(!slots[0].Equipped.enabled, "unequipping clears the highlight on redraw");
        equippedItem.m_equipped = true; slots[0].Bind(null, equippedItem, new Vector2i(0, 0));
        slots[0].Bind(null, null, new Vector2i(0, 0));
        Check(!slots[0].Equipped.enabled && !slots[0].Icon.enabled, "recycled empty slots clear highlight and icon");
        var storedSlot = storage.GetComponentsInChildren<VssSlot>(true).First();
        storedSlot.Row.Sample.m_equipped = true; storedSlot.Bind(storedSlot.Row, null, new Vector2i(-1, -1));
        Check(!storedSlot.Equipped.enabled, "stored item rows never inherit player equipment highlights");
        var canvasObject = (GameObject)AccessTools.Field(typeof(VssUi), "_canvas").GetValue(ui);
        canvasObject.SetActive(true); panel.gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(inventory);
        FrameClearance(panel, frameImage);
        foreach (var label in panel.GetComponentsInChildren<TMP_Text>().Where(t => !string.IsNullOrEmpty(t.text)))
        {
            label.ForceMeshUpdate();
            Check(label.textInfo.characterInfo.Take(label.textInfo.characterCount).Any(c => c.isVisible),
                "non-empty UI label has visible glyphs: " + label.text);
        }
        Check(!auto.isTextTruncated && !amount.isTextTruncated, "refill and amount selector labels fit without truncation");
        var last = (RectTransform)slots[31].transform;
        Check(last.anchoredPosition.x + last.rect.width * (1 - last.pivot.x) <= inventory.rect.width + 0.1f &&
            -last.anchoredPosition.y + last.rect.height * last.pivot.y <= ((RectTransform)inventory.parent).rect.height + 0.1f,
            "all 8x4 inventory slots fit inside the generated well");
        Check(inventory.parent.GetComponent<ScrollRect>().verticalScrollbarVisibility == ScrollRect.ScrollbarVisibility.AutoHide,
            "inventory scrollbar hides when all rows fit");
        AccessTools.Field(typeof(VssUi), "_player").SetValue(ui, null);
        panel.gameObject.SetActive(false); canvasObject.SetActive(false);
        MessageLayers(ui);
    }
    private static void FrameClearance(RectTransform panel, Image frame)
    {
        _stage = "artwork clearance";
        foreach (var name in new[] { "Header recess", "Toolbar recess", "Storage recess", "Inventory recess", "Footer recess" })
        {
            var rect = (RectTransform)panel.Find(name);
            var min = rect.anchoredPosition; min.y = -min.y;
            // Sample the actual generated frame's alpha along every section's four edges.
            // Bounding boxes alone missed the painted rails overlapping the previous footer.
            for (var n = 0; n <= 12; n++)
            {
                var fraction = n / 12f;
                foreach (var point in new[]
                {
                    min + new Vector2(rect.rect.width * fraction, 0),
                    min + new Vector2(rect.rect.width * fraction, rect.rect.height),
                    min + new Vector2(0, rect.rect.height * fraction),
                    min + new Vector2(rect.rect.width, rect.rect.height * fraction)
                })
                {
                    var sx = FramePixel(point.x, panel.rect.width, frame.sprite.texture.width, frame.sprite.border.x, frame.pixelsPerUnit);
                    var sy = FramePixel(point.y, panel.rect.height, frame.sprite.texture.height, frame.sprite.border.y, frame.pixelsPerUnit);
                    var alpha = frame.sprite.texture.GetPixelBilinear(sx / frame.sprite.texture.width, 1 - sy / frame.sprite.texture.height).a;
                    Check(alpha < 0.15f, "outer artwork does not overlap " + name + " at " + point + "; alpha=" + alpha);
                }
            }
        }
    }
    private static float FramePixel(float logical, float span, float pixels, float border, float pixelsPerUnit)
    {
        var edge = border / pixelsPerUnit;
        if (logical < edge) return logical * pixelsPerUnit;
        if (logical > span - edge) return pixels - (span - logical) * pixelsPerUnit;
        return border + (logical - edge) / (span - 2 * edge) * (pixels - 2 * border);
    }
    private static MessageHud _messageFixture;
    private static GameObject _messageCanvasObject;
    private static void MessageLayers(VssUi ui)
    {
        _stage = "native notification layers";
        var native = GamePrefab("Assets/UI/prefabs/IngameGui/IngameGui.prefab").GetComponentInChildren<MessageHud>(true);
        Check(native != null && native.m_messageText != null && native.m_messageCenterText != null, "native equip-message graphics found");
        _messageCanvasObject = new GameObject("VSS native message test canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.Object.DontDestroyOnLoad(_messageCanvasObject); _messageCanvasObject.SetActive(false);
        var canvas = _messageCanvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
        var scaler = _messageCanvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
        _messageFixture = _messageCanvasObject.AddComponent<MessageHud>(); _messageFixture.enabled = false;
        _messageFixture.m_messageText = MessageText("Top-left message", native.m_messageText, new Vector2(0, 1), new Vector2(80, -32));
        _messageFixture.m_messageCenterText = MessageText("Center message", native.m_messageCenterText, new Vector2(0.5f, 0.5f), new Vector2(0, 50));
        var iconRect = new GameObject("Native message icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        iconRect.SetParent(_messageCanvasObject.transform, false); iconRect.sizeDelta = new Vector2(32, 32);
        _messageFixture.m_messageIcon = iconRect.GetComponent<Image>(); _messageFixture.m_messageIcon.enabled = false;
        var stock = _messageFixture.m_messageCenterText.gameObject.AddComponent<Canvas>();
        _messageCanvasObject.SetActive(true);
        stock.overrideSorting = true; stock.sortingOrder = 250;
        var storage = ((GameObject)AccessTools.Field(typeof(VssUi), "_canvas").GetValue(ui)).GetComponent<Canvas>();
        VssMessageOverlay.Raise(_messageFixture, storage);
        foreach (var graphic in new Graphic[] { _messageFixture.m_messageText, _messageFixture.m_messageCenterText, _messageFixture.m_messageIcon })
            Check(graphic.GetComponent<Canvas>().overrideSorting && graphic.GetComponent<Canvas>().sortingOrder > storage.sortingOrder,
                "native notification graphic sorts above storage: " + graphic.name + "; order=" + graphic.GetComponent<Canvas>().sortingOrder + "; override=" + graphic.GetComponent<Canvas>().overrideSorting);
        VssMessageOverlay.Raise(_messageFixture, storage);
        Check(_messageFixture.m_messageCenterText.GetComponents<Canvas>().Length == 1, "repeated notification raise does not duplicate canvases");
        VssMessageOverlay.Restore();
        Check(stock.overrideSorting && stock.sortingOrder == 250, "existing message canvas settings restored after closing");
        Check(!_messageFixture.m_messageText.GetComponent<Canvas>().overrideSorting, "added message canvas resumes inherited vanilla sorting");
        _messageCanvasObject.SetActive(false);
    }
    private static TMP_Text MessageText(string name, TMP_Text native, Vector2 anchor, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
        rect.SetParent(_messageCanvasObject.transform, false); rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = new Vector2(640, 64);
        var text = rect.GetComponent<TMP_Text>(); text.font = native.font; text.fontSharedMaterial = native.fontSharedMaterial;
        text.fontSize = native.fontSize; text.color = native.color; text.alignment = native.alignment; text.raycastTarget = false;
        return text;
    }
    private static void Report(string text)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "-vss-test-report");
        if (index >= 0 && index + 1 < args.Length) File.WriteAllText(args[index + 1], text);
    }
    private static IEnumerator RenderPreview()
    {
        var ui = UnityEngine.Object.FindFirstObjectByType<VssUi>(); ui.enabled = false;
        var canvasObject = (GameObject)AccessTools.Field(typeof(VssUi), "_canvas").GetValue(ui);
        var canvas = canvasObject.GetComponent<Canvas>();
        var camera = new GameObject("VSS preview camera").AddComponent<Camera>();
        UnityEngine.Object.DontDestroyOnLoad(camera.gameObject);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.06f, 0.085f, 0.08f);
        var args = Environment.GetCommandLineArgs();
        var widthIndex = Array.IndexOf(args, "-screen-width"); var heightIndex = Array.IndexOf(args, "-screen-height");
        var width = widthIndex >= 0 ? int.Parse(args[widthIndex + 1]) : 1440;
        var height = heightIndex >= 0 ? int.Parse(args[heightIndex + 1]) : 1080;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        var messageCanvas = _messageCanvasObject.GetComponent<Canvas>();
        messageCanvas.renderMode = RenderMode.ScreenSpaceCamera; messageCanvas.worldCamera = camera; messageCanvas.planeDistance = 1;
        var panel = (RectTransform)AccessTools.Field(typeof(VssUi), "_panel").GetValue(ui);
        var playerObject = new GameObject("VSS preview player"); playerObject.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(playerObject);
        var player = playerObject.AddComponent<Player>();
        var inventory = Inv(8, 4);
        var names = new[] { "Hammer", "Bow", "Club", "Wood", "Stone", "Raspberry", "Resin", "ArrowWood", "HelmetLeather", "ArmorLeatherChest", "ArmorLeatherLegs", "CapeDeerHide" };
        var paths = SoftReferenceableAssets.Runtime.GetAllAssetPathsInBundleMappedToAssetID();
        var snapshot = new VssSnapshot();
        for (var n = 0; n < names.Length; n++)
        {
            var path = paths.Keys.First(p => p.StartsWith("Assets/GameElements/Items/", StringComparison.Ordinal) && p.EndsWith("/" + names[n] + ".prefab", StringComparison.Ordinal));
            var prefab = GamePrefab(path); var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone(); item.m_dropPrefab = prefab;
            item.m_stack = Math.Min(item.m_shared.m_maxStackSize, (n + 1) * 7);
            item.m_equipped = n == 1 || n == 7 || n >= 8;
            Put(inventory, item, n % 8, n / 8);
            snapshot.Rows.Add(new VssItemRow { Key = new VssItemKey(item), Sample = item, MaxStack = item.m_shared.m_maxStackSize, Total = item.m_stack, StackCount = 1 });
        }
        AccessTools.Field(typeof(VssUi), "_snapshot").SetValue(ui, snapshot);
        AccessTools.Method(typeof(VssUi), "DrawStorage").Invoke(ui, null);
        AccessTools.Field(typeof(Humanoid), "m_inventory").SetValue(player, inventory);
        AccessTools.Field(typeof(VssUi), "_player").SetValue(ui, player);
        AccessTools.Method(typeof(VssUi), "DrawInventory").Invoke(ui, null);
        ((TMP_Text)AccessTools.Field(typeof(VssUi), "_capacity").GetValue(ui)).text = "115/120 (96%) used  •  5 free  •  10 chests  •  20m";
        ((TMP_Text)AccessTools.Field(typeof(VssUi), "_weight").GetValue(ui)).text = "Weight 110 / 300";
        VssUi.Status("Click/drag: selected amount • Ctrl: one • Shift-drag: half • Right-click stored food: consume");
        panel.gameObject.SetActive(true); canvasObject.SetActive(true);
        yield return null; yield return null;
        try
        {
            RenderStandPreview();
            Canvas.ForceUpdateCanvases();
            AccessTools.Method(typeof(VssUi), "FitPanel").Invoke(ui, null);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4]; panel.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var viewportPoint = camera.WorldToViewportPoint(corner);
                Check(viewportPoint.x > 0 && viewportPoint.x < 1 && viewportPoint.y > 0 && viewportPoint.y < 1,
                    "complete frame lies inside the rendered viewport");
            }
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); RenderTexture.active = previous;
            var index = Array.IndexOf(args, "-vss-preview");
            File.WriteAllBytes(args[index + 1], ImageConversion.EncodeToPNG(texture));
            VssLayoutExport.Save(panel, Path.ChangeExtension(args[index + 1], ".layout.json"));
            // Render a second image with the actual native font/material and production sorting code.
            _messageFixture.m_messageCenterText.text = "Equipped Leather helmet";
            _messageFixture.m_messageCenterText.color = Color.white;
            _messageFixture.m_messageText.text = "Unequipped Leather cape";
            _messageFixture.m_messageText.color = Color.white;
            _messageCanvasObject.SetActive(true); VssMessageOverlay.Raise(_messageFixture, canvas);
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(args[index + 1]), "storage-messages-" + width + "x" + height + ".png"), ImageConversion.EncodeToPNG(texture));
            VssMessageOverlay.Restore();
            Report($"PASS: {_checks} checks against the installed game; native UI preview rendered.");
            Application.Quit(0);
        }
        catch (Exception ex) { Report("FAIL rendering preview: " + ex); Application.Quit(1); }
    }
    private static void RenderStandPreview()
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-vss-stand-preview");
        if (index < 0 || !VssAssets.HasStandModel) return;
        var model = VssAssets.GetStandModel();
        foreach (var child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials) Check(material != null && material.shader.isSupported, "exported pedestal shader supported by Valheim renderer");
        var camera = new GameObject("VSS model preview camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.09f, 0.105f, 0.115f);
        camera.cullingMask = 1 << 30; camera.orthographic = true; camera.orthographicSize = 0.82f;
        camera.transform.position = new Vector3(1.7f, 1.3f, -2.6f);
        camera.transform.LookAt(new Vector3(0, 0.625f, 0));
        var target = new RenderTexture(640, 800, 24); camera.targetTexture = target;
        var light = new GameObject("VSS model preview light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.3f; light.cullingMask = 1 << 30;
        light.transform.rotation = Quaternion.Euler(35, -35, 0);
        var oldAmbient = RenderSettings.ambientLight; var oldMode = RenderSettings.ambientMode;
        var previous = RenderTexture.active;
        try
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
            camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(640, 800, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 640, 800), 0, 0); texture.Apply();
            File.WriteAllBytes(args[index + 1], ImageConversion.EncodeToPNG(texture));
            UnityEngine.Object.Destroy(texture);
        }
        finally
        {
            RenderTexture.active = previous; RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmbient;
            UnityEngine.Object.Destroy(model); UnityEngine.Object.Destroy(camera.gameObject);
            UnityEngine.Object.Destroy(light.gameObject); target.Release(); UnityEngine.Object.Destroy(target);
        }
    }
    private static ItemDrop.ItemData Item(int amount) => new()
    {
        m_stack = amount, m_quality = 1, m_durability = 100,
        m_shared = new ItemDrop.ItemData.SharedData { m_name = "VSS test item", m_weight = 1, m_maxStackSize = 50, m_itemType = ItemDrop.ItemData.ItemType.Material }
    };
    private static ItemDrop.ItemData Put(Inventory inv, ItemDrop.ItemData item, int x, int y)
    { item.m_gridPos = new Vector2i(x, y); inv.GetAllItems().Add(item); return item; }
    private static int Total(Inventory inv) => inv.GetAllItems().Sum(i => i.m_stack);
    private static Dictionary<string, int> Counts(IEnumerable<Inventory> inventories) => inventories.SelectMany(i => i.GetAllItems())
        .GroupBy(i => new VssItemKey(i).Id).ToDictionary(g => g.Key, g => g.Sum(i => i.m_stack));
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("Check failed: " + label); _checks++; }
}
[HarmonyPatch(typeof(Player), "AddKnownPiece")]
internal static class VssRuntimeTestUnlockPatch
{
    private static bool Prepare() => VssRuntimeTests.Requested;
    private static bool Prefix(Player __instance, Piece __0)
    {
        // Keep native discovery, suppress HUD notifications in the isolated pre-login fixture.
        ((HashSet<string>)AccessTools.Field(typeof(Player), "m_knownRecipes").GetValue(__instance)).Add(__0.m_name);
        return false;
    }
}
[HarmonyPatch(typeof(Player), "UpdateAvailablePiecesList")]
internal static class VssRuntimeTestPlacementPatch
{
    private static bool Prepare() => VssRuntimeTests.Requested;
    private static bool Prefix() => false; // Native table filtering is tested directly; no world placement ghost in diagnostics.
}
[HarmonyPatch(typeof(FejdStartup), "Start")]
internal static class VssRuntimeTestStartupPatch
{
    private static void Postfix() => VssRuntimeTests.Run();
}

// Headless diagnostics run before Steam login. Item identity tests use raw names in that mode.
[HarmonyPatch(typeof(Localization), "get_instance")]
internal static class VssRuntimeTestLocalizationPatch
{
    private static bool Prepare() => VssRuntimeTests.Requested;
    private static bool Prefix(ref Localization __result) { __result = null; return false; }
}
