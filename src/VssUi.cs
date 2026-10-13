using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VSS;

public sealed class VssUi : MonoBehaviour
{
    private static VssUi _instance;
    private static readonly Color Gold = new(0.91f, 0.77f, 0.48f);
    private static readonly Color Paper = new(0.89f, 0.86f, 0.77f);
    private static readonly Color Well = new(0.08f, 0.07f, 0.055f, 0.94f);
    private static TMP_FontAsset _font;
    private GameObject _canvas;
    private RectTransform _panel, _storage, _inventory, _drag, _tooltip, _preferencePanel;
    private TMP_Text _capacity, _status, _weight, _sortText, _orderText, _filterText, _tooltipText;
    private TMP_Text _autoText, _collectText, _amountText;
    private TMP_InputField _search;
    private Image _dragIcon;
    private Image _equippedTemplate;
    private TMP_Text _dragCount;
    private Button _chestButton;
    private Image _chestIcon;
    private Image _chestBadge;
    private Container _openChest;
    private Player _player;
    private VssAccessStand _stand;
    private VssSnapshot _snapshot;
    private float _nextRefresh, _nextChestCheck;
    private int _sort;
    private int _amountMode;
    private bool _descending;
    private VssCategory _filter;
    private readonly List<VssSlot> _storageSlots = new();
    private readonly List<VssSlot> _inventorySlots = new();
    private ItemDrop.ItemData _dragItem;
    private VssItemRow _dragRow;
    private int _dragAmount;
    private float _openedAt;
    private float _suppressUseUntil;
    private static float _suppressMenuUntil;
    public static bool IsOpen => _instance != null && _instance._player != null;
    internal static bool SuppressMenu => IsOpen || Time.unscaledTime < _suppressMenuUntil;
    public static void EnsureCreated()
    {
        if (_instance != null) return;
        var go = new GameObject("VSS UI Manager");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<VssUi>();
    }
    public static void Open(Player player, VssAccessStand stand)
    {
        EnsureCreated();
        if (InventoryGui.instance == null) return;
        InventoryGui.instance.Hide();
        Hud.HidePieceSelection();
        _instance.Build();
        _instance._player = player; _instance._stand = stand;
        _instance._openedAt = Time.unscaledTime;
        _instance._panel.gameObject.SetActive(true);
        _instance.FitPanel();
        VssMessageOverlay.Raise(MessageHud.instance, _instance._canvas.GetComponent<Canvas>());
        _instance._search.SetTextWithoutNotify("");
        _instance._filter = VssCategory.Any;
        _instance.Refresh(true);
        Status(VssSettings.Ready ? "Click/drag: selected amount • Ctrl: one • Shift-drag: half • Right-click stored food: consume" : "Waiting for VSS settings from the host…");
    }
    public static void Status(string message)
    {
        if (_instance != null && _instance._status != null) _instance._status.text = message;
    }
    public static void RefreshNow() { if (IsOpen) _instance.Refresh(true); }
    public static void Close()
    {
        if (_instance == null) return;
        _suppressMenuUntil = Time.unscaledTime + 0.2f;
        VssTransfer.Cancel();
        _instance._player = null; _instance._stand = null; _instance._snapshot = null;
        _instance.EndDrag();
        if (_instance._panel != null) _instance._panel.gameObject.SetActive(false);
        _instance.HideTooltip();
        VssMessageOverlay.Restore();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }
    private void Update()
    {
        if (Time.unscaledTime >= _nextChestCheck)
        {
            _nextChestCheck = Time.unscaledTime + 0.25f;
            UpdateChestButton();
        }
        if (!IsOpen) return;
        if (_autoText != null) _autoText.text = "Auto refill master: " + (_stand != null && _stand.AutoRefill ? "On" : "Off");
        if (_collectText != null) _collectText.text = "Auto collect master: " + (_stand != null && _stand.AutoCollect ? "On" : "Off");
        FitPanel();
        if (_stand == null || _player == null || _player.IsDead() || _player.IsTeleporting() ||
            (_player.transform.position - _stand.transform.position).sqrMagnitude > 25f ||
            !PrivateArea.CheckAccess(_stand.transform.position, 0f, false, false)) { Close(); return; }
        if (Time.unscaledTime - _openedAt > 0.2f && (Input.GetKeyDown(KeyCode.Escape) ||
            (!_search.isFocused && (Input.GetKeyDown(KeyCode.Tab) || ZInput.GetButtonDown("Inventory")))))
        { Close(); return; }
        if (Input.GetMouseButtonDown(1) && (_dragItem != null || _dragRow != null))
        { _suppressUseUntil = Time.unscaledTime + 0.25f; EndDrag(); }
        if (Time.unscaledTime >= _nextRefresh && _dragItem == null && _dragRow == null && !VssTransfer.Busy) Refresh(false);
        if (_drag != null && _drag.gameObject.activeSelf)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform, Input.mousePosition, null, out var pos);
            _drag.anchoredPosition = pos;
        }
        if (_tooltip != null && _tooltip.gameObject.activeSelf)
        {
            var root = (RectTransform)_canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Input.mousePosition, null, out var pos);
            _tooltip.anchoredPosition = new Vector2(Mathf.Clamp(pos.x + 22, -root.rect.width / 2, root.rect.width / 2 - 320),
                Mathf.Clamp(pos.y - 18, -root.rect.height / 2 + 120, root.rect.height / 2));
        }
    }
    internal static void BuildForDiagnostics(TMP_FontAsset font, Image equippedTemplate = null)
    {
        EnsureCreated(); _instance._equippedTemplate = equippedTemplate; _instance.Build(font, true);
    }
    private void Build(TMP_FontAsset font = null, bool diagnostics = false)
    {
        if (_panel != null) return;
        _font = diagnostics ? font : InventoryGui.instance.m_containerName.font;
        if (!diagnostics)
            _equippedTemplate = InventoryGui.instance.m_playerGrid.m_elementPrefab.GetComponent<InventoryElement>().m_equiped;
        _canvas = new GameObject("VSS Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _canvas.SetActive(false);
        DontDestroyOnLoad(_canvas);
        var canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
        _panel = Rect("Storage Inventory", _canvas.transform, new Vector2(1040, 950));
        Background(_panel, Color.clear);
        var backing = Art("Continuous backing", VssAssets.Wood, 16, 16, 1008, 918, false);
        if (backing == null)
        {
            var rect = Rect("Fallback backing", _panel, new Vector2(1008, 918)); Top(rect, 16, 16, 1008, 918);
            Background(rect, new Color(0.12f, 0.10f, 0.07f)).raycastTarget = false;
        }
        Art("Header recess", VssAssets.Inset, 80, 30, 880, 70);
        Art("Toolbar recess", VssAssets.Inset, 32, 102, 976, 56);
        Art("Storage recess", VssAssets.Inset, 32, 166, 976, 378);
        Art("Inventory recess", VssAssets.Inset, 191, 584, 658, 306);
        Art("Footer recess", VssAssets.Inset, 80, 894, 880, 26);
        Frame();
        var controllerIcon = Rect("Controller icon", _panel, new Vector2(34, 34)); Top(controllerIcon, 98, 37, 34, 34);
        var controllerImage = Background(controllerIcon, Color.white); controllerImage.sprite = VssSprites.StandIcon;
        controllerImage.preserveAspect = true; controllerImage.raycastTarget = false;
        Label(_panel, "Storage Inventory", 140, 36, 350, 34, 30, Gold);
        _autoText = Button(_panel, "Auto refill master: Off", 502, 38, 188, 28, () =>
        {
            if (_stand != null && _player != null) _stand.ToggleAutoRefill(_player);
        }).GetComponentInChildren<TMP_Text>(); _autoText.fontSize = 15;
        _collectText = Button(_panel, "Auto collect master: Off", 702, 38, 188, 28, () =>
        {
            if (_stand != null && _player != null) _stand.ToggleAutoCollect(_player);
        }).GetComponentInChildren<TMP_Text>(); _collectText.fontSize = 15;
        Button(_panel, "×", 902, 38, 52, 52, Close);
        _capacity = Label(_panel, "", 98, 70, 784, 22, 18, Paper);
        _search = Search(_panel, 44, 110, 425, 40);
        _search.onValueChanged.AddListener(_ => DrawStorage());
        _sortText = Button(_panel, "Name", 481, 110, 158, 40, () => { _sort = (_sort + 1) % 3; DrawStorage(); }).GetComponentInChildren<TMP_Text>();
        _orderText = Button(_panel, "↑", 650, 110, 55, 40, () => { _descending = !_descending; DrawStorage(); }).GetComponentInChildren<TMP_Text>();
        _filterText = Button(_panel, "All items", 716, 110, 189, 40, () => { _filter = (VssCategory)(((int)_filter + 1) % 12); DrawStorage(); }).GetComponentInChildren<TMP_Text>();
        Button(_panel, "Refresh", 916, 110, 80, 40, () => Refresh(true));
        _storage = ScrollGrid(_panel, "Stored items", 44, 178, 952, 354, 12, true);
        var drop = _storage.parent.gameObject.AddComponent<VssDropZone>(); drop.Storage = true;
        Label(_panel, "YOUR INVENTORY", 203, 550, 176, 30, 22, Gold);
        _amountText = Button(_panel, "Amount: Stack", 391, 550, 154, 30, () =>
        {
            _amountMode = (_amountMode + 1) % 3; _amountText.text = "Amount: " + AmountLabel(_amountMode);
        }).GetComponentInChildren<TMP_Text>(); _amountText.fontSize = 17;
        _weight = Label(_panel, "", 557, 553, 280, 26, 17, Paper); _weight.alignment = TextAlignmentOptions.Right;
        _inventory = ScrollGrid(_panel, "Player inventory", 203, 596, 634, 282, 8, false);
        var inventoryDrop = _inventory.parent.gameObject.AddComponent<VssDropZone>(); inventoryDrop.Storage = false;
        _status = Label(_panel, "", 94, 897, 852, 20, 14, Paper);
        _drag = Rect("Dragged item", _canvas.transform, new Vector2(64, 64));
        _dragIcon = Background(_drag, Color.white); _dragIcon.preserveAspect = true; _dragIcon.raycastTarget = false;
        _dragCount = Label(_drag, "", 0, 44, 64, 20, 18, Color.white); _dragCount.alignment = TextAlignmentOptions.Right;
        _drag.gameObject.SetActive(false);
        _tooltip = Rect("Item tooltip", _canvas.transform, new Vector2(320, 120)); _tooltip.pivot = new Vector2(0, 1);
        Background(_tooltip, Well).raycastTarget = false;
        _tooltipText = Label(_tooltip, "", 12, 8, 296, 104, 17, Paper);
        _tooltipText.alignment = TextAlignmentOptions.TopLeft;
        _tooltip.gameObject.SetActive(false);
        _panel.gameObject.SetActive(false);
        _canvas.SetActive(!diagnostics);
    }
    internal static float PanelScale(Vector2 canvasSize) => Mathf.Min(1f,
        Mathf.Max(0.01f, (canvasSize.x - 48f) / 1040f), Mathf.Max(0.01f, (canvasSize.y - 48f) / 950f));
    private void FitPanel()
    {
        if (_panel != null) _panel.localScale = Vector3.one * PanelScale(((RectTransform)_canvas.transform).rect.size);
    }
    private Image Art(string name, Sprite sprite, float x, float y, float width, float height, bool sliced = true)
    {
        if (sprite == null) return null;
        var rect = Rect(name, _panel, new Vector2(width, height)); Top(rect, x, y, width, height);
        var image = Background(rect, Color.white); image.sprite = sprite;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple; image.raycastTarget = false;
        return image;
    }
    private void Frame()
    {
        var image = Art("Complete outer frame", VssAssets.Panel, 0, 0, 1040, 950);
        if (image != null) image.fillCenter = false;
    }
    private void Refresh(bool force)
    {
        if (_stand == null || _player == null) return;
        _nextRefresh = Time.unscaledTime + VssPlugin.ModConfig.RefreshSeconds.Value;
        _snapshot = VssStorageScanner.Scan(_stand.transform.position, _player);
        var total = _snapshot.TotalSlots;
        var percentage = total > 0 ? 100f * _snapshot.UsedSlots / total : 0f;
        _capacity.text = $"{_snapshot.UsedSlots}/{total} ({percentage:0}%) used  •  {_snapshot.EmptySlots} free  •  {_snapshot.Containers.Count} chests  •  {VssSettings.Radius:0.#}m" +
            (_snapshot.BusyContainers > 0 ? $"  •  {_snapshot.BusyContainers} busy" : "") +
            (total > 0 && _snapshot.EmptySlots == 0 ? "  •  FULL" : "");
        _weight.text = $"Weight {_player.GetInventory().GetTotalWeight():0.#} / {_player.GetMaxCarryWeight():0.#}";
        DrawStorage(); DrawInventory();
    }
    private void DrawStorage()
    {
        if (_snapshot == null || _storage == null) return;
        var query = _search.text.Trim();
        IEnumerable<VssItemRow> rows = _snapshot.Rows.Where(r =>
            (string.IsNullOrEmpty(query) || r.Key.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || r.Key.PrefabName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (_filter == VssCategory.Any || VssPreferences.Classify(r.Sample) == _filter));
        rows = _sort == 1 ? rows.OrderBy(r => r.Total).ThenBy(r => r.Key.DisplayName, StringComparer.CurrentCultureIgnoreCase) :
            _sort == 2 ? rows.OrderBy(r => VssPreferences.Classify(r.Sample)).ThenBy(r => r.Key.DisplayName, StringComparer.CurrentCultureIgnoreCase) :
            rows.OrderBy(r => r.Key.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Key.Id, StringComparer.Ordinal);
        if (_descending) rows = rows.Reverse();
        var list = rows.ToList();
        _sortText.text = _sort == 1 ? "Quantity" : _sort == 2 ? "Category" : "Name";
        _orderText.text = _descending ? "↓" : "↑"; _filterText.text = _filter == VssCategory.Any ? "All items" : VssPreferences.Label(_filter);
        Pool(_storageSlots, _storage, Math.Max(list.Count, 12), true);
        for (var i = 0; i < _storageSlots.Count; i++)
        {
            _storageSlots[i].gameObject.SetActive(i < Math.Max(list.Count, 12));
            if (i < Math.Max(list.Count, 12)) _storageSlots[i].Bind(i < list.Count ? list[i] : null, null, new Vector2i(-1, -1));
        }
        _storage.sizeDelta = new Vector2(936, Math.Max(354, (int)Math.Ceiling(Math.Max(list.Count, 12) / 12f) * 72 - 6));
    }
    private void DrawInventory()
    {
        var inventory = _player.GetInventory();
        var width = inventory.GetWidth(); var height = inventory.GetHeight();
        _inventory.GetComponent<GridLayoutGroup>().constraintCount = width;
        Pool(_inventorySlots, _inventory, width * height, false);
        for (var i = 0; i < _inventorySlots.Count; i++)
        {
            _inventorySlots[i].gameObject.SetActive(i < width * height);
            if (i < width * height)
                _inventorySlots[i].Bind(null, inventory.GetItemAt(i % width, i / width), new Vector2i(i % width, i / width));
        }
        _inventory.sizeDelta = new Vector2(Math.Max(634, width * 78 - 6), Math.Max(282, height * 72 - 6));
        _inventory.parent.GetComponent<ScrollRect>().horizontal = width > 8;
    }
    private void Pool(List<VssSlot> pool, Transform parent, int count, bool storage)
    {
        while (pool.Count < count)
        {
            var rect = Rect("Item slot", parent, new Vector2(72, 66));
            Background(rect, new Color(0.18f, 0.16f, 0.12f, 0.93f));
            var outline = rect.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0.4f, 0.32f, 0.18f, 0.7f); outline.effectDistance = new Vector2(1, -1);
            var equippedRect = Rect("Equipped highlight", rect, new Vector2(72, 66)); Top(equippedRect, 0, 0, 72, 66);
            var equipped = Background(equippedRect, _equippedTemplate != null ? _equippedTemplate.color : new Color(0.25f, 0.48f, 0.8f, 0.55f));
            if (_equippedTemplate != null)
            {
                equipped.sprite = _equippedTemplate.sprite; equipped.type = _equippedTemplate.type;
                equipped.material = _equippedTemplate.material; equipped.preserveAspect = _equippedTemplate.preserveAspect;
            }
            equipped.raycastTarget = false; equipped.enabled = false;
            var iconRect = Rect("Icon", rect, new Vector2(44, 44)); Top(iconRect, 14, 3, 44, 44);
            var icon = Background(iconRect, Color.white); icon.preserveAspect = true; icon.raycastTarget = false;
            var amount = Label(rect, "", 3, 45, 65, 19, 15, Color.white); amount.alignment = TextAlignmentOptions.Right;
            var marker = Label(rect, "", 3, 1, 66, 16, 12, Gold);
            var slot = rect.gameObject.AddComponent<VssSlot>();
            slot.Storage = storage; slot.Icon = icon; slot.Equipped = equipped; slot.Amount = amount; slot.Marker = marker;
            pool.Add(slot);
        }
    }
    internal static void BeginDrag(VssSlot slot)
    {
        if (!IsOpen || VssTransfer.Busy || (slot.Item == null && slot.Row == null)) return;
        if (slot.Item != null && slot.Item.m_equipped) { Status("Unequip items before moving them."); return; }
        _instance._dragItem = slot.Item; _instance._dragRow = slot.Row;
        var count = slot.Storage ? Math.Min(slot.Row.Total, slot.Row.MaxStack) : slot.Item.m_stack;
        _instance._dragAmount = Ctrl ? 1 : Shift ? SelectedAmount(count, 1) : SelectedAmount(count, _instance._amountMode);
        _instance._dragIcon.sprite = slot.Storage ? slot.Row.Icon : slot.Item.GetIcon();
        _instance._dragCount.text = _instance._dragAmount.ToString();
        _instance._drag.gameObject.SetActive(true); _instance._drag.SetAsLastSibling(); _instance.HideTooltip();
    }
    internal static void Drop(bool storage, Vector2i pos)
    {
        if (!IsOpen || VssTransfer.Busy) return;
        var self = _instance;
        if (storage && self._dragItem != null) VssTransfer.Deposit(self._player, self._stand, self._dragItem, self._dragAmount);
        else if (!storage && self._dragRow != null) VssTransfer.Withdraw(self._player, self._stand, self._dragRow, self._dragAmount, pos.x, pos.y);
        else if (!storage && self._dragItem != null && pos.x >= 0)
        {
            var inventory = self._player.GetInventory(); var item = self._dragItem;
            if (inventory.ContainsItem(item))
            {
                var target = inventory.GetItemAt(pos.x, pos.y);
                if (target == null && self._dragAmount == item.m_stack) item.m_gridPos = pos;
                else if (target != item && target != null && !target.m_equipped && self._dragAmount == item.m_stack && !new VssItemKey(item).Matches(target))
                { var old = item.m_gridPos; item.m_gridPos = target.m_gridPos; target.m_gridPos = old; }
                else if (target != item && (target == null || new VssItemKey(item).Matches(target)))
                {
                    var capacity = VssInventoryOps.Capacity(inventory, item, false, pos.x, pos.y);
                    if (capacity > 0)
                    {
                        inventory.MoveItemToThis(inventory, item, Math.Min(capacity, self._dragAmount), pos.x, pos.y);
                    }
                }
                VssGame.Notify(inventory);
            }
            self.DrawInventory();
        }
        self.EndDrag();
    }
    internal static void Click(VssSlot slot, PointerEventData data)
    {
        if (!IsOpen || VssTransfer.Busy) return;
        if (data.button == PointerEventData.InputButton.Right && Time.unscaledTime < _instance._suppressUseUntil) return;
        if (slot.Storage && slot.Row != null && data.button == PointerEventData.InputButton.Right)
        {
            VssTransfer.Consume(_instance._player, _instance._stand, slot.Row); return;
        }
        if (!slot.Storage && slot.Item != null && data.button == PointerEventData.InputButton.Right)
        {
            _instance._player.UseItem(_instance._player.GetInventory(), slot.Item, true);
            _instance.DrawInventory(); return;
        }
        if (data.button != PointerEventData.InputButton.Left) return;
        var mode = Ctrl ? 2 : Shift ? 0 : _instance._amountMode;
        if (slot.Storage && slot.Row != null) VssTransfer.Withdraw(_instance._player, _instance._stand, slot.Row, SelectedAmount(Math.Min(slot.Row.Total, slot.Row.MaxStack), mode));
        else if (!slot.Storage && slot.Item != null) VssTransfer.Deposit(_instance._player, _instance._stand, slot.Item, SelectedAmount(slot.Item.m_stack, mode));
    }
    internal static string AmountLabel(int mode) => mode == 1 ? "Half" : mode == 2 ? "One" : "Stack";
    internal static int SelectedAmount(int count, int mode) => count <= 0 ? 0 : mode == 2 ? 1 : mode == 1 ? Math.Max(1, (count + 1) / 2) : count;
    private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    private static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
    internal static void EndDragStatic() { if (_instance != null) _instance.EndDrag(); }
    private void EndDrag()
    {
        _dragItem = null; _dragRow = null;
        if (_drag != null) _drag.gameObject.SetActive(false);
    }
    internal static void Tooltip(VssSlot slot)
    {
        if (_instance == null || _instance._tooltip == null || _instance._dragItem != null || _instance._dragRow != null) return;
        var text = slot.Row?.Tooltip ?? (slot.Item != null ? $"{new VssItemKey(slot.Item).DisplayName}\n{slot.Item.m_stack} item(s) • Quality {slot.Item.m_quality}" +
            (slot.Item.m_equipped ? "\nEquipped" : "") : "");
        if (slot.Row != null && slot.Row.Sample.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable) text += "\nRight-click: consume one";
        _instance._tooltipText.text = text; _instance._tooltip.gameObject.SetActive(!string.IsNullOrEmpty(text));
        _instance._tooltip.SetAsLastSibling();
    }
    internal static void HideTooltipStatic() { if (_instance != null) _instance.HideTooltip(); }
    private void HideTooltip() { if (_tooltip != null) _tooltip.gameObject.SetActive(false); }
    private void UpdateChestButton()
    {
        var gui = InventoryGui.instance;
        var chest = gui != null ? VssGame.CurrentContainer(gui) : null;
        var visible = !IsOpen && chest != null && gui.m_container.gameObject.activeInHierarchy && VssStorageScanner.Linked(chest);
        if (!visible)
        {
            if (_chestButton != null) _chestButton.gameObject.SetActive(false);
            if (_preferencePanel != null) _preferencePanel.gameObject.SetActive(false);
            _openChest = null; return;
        }
        if (_font == null) _font = gui.m_containerName.font;
        if (_chestButton == null || _chestButton.transform.parent != gui.m_container)
        {
            _chestButton = Button(gui.m_container, "", 0, 0, 42, 42, TogglePreferences);
            var rect = (RectTransform)_chestButton.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.anchoredPosition = new Vector2(8, -8);
            var imageRect = Rect("Preference icon", rect, new Vector2(32, 32));
            _chestIcon = Background(imageRect, Color.white); _chestIcon.preserveAspect = true; _chestIcon.raycastTarget = false;
            _chestIcon.sprite = VssAssets.ChestIcon ?? VssSprites.StandIcon;
            var badgeRect = Rect("Preferred resource badge", rect, new Vector2(17, 17)); Top(badgeRect, 25, 25, 17, 17);
            _chestBadge = Background(badgeRect, Color.white); _chestBadge.preserveAspect = true; _chestBadge.raycastTarget = false;
            // The selection menu grows inward, staying on screen even when the chest sits at the right edge.
            _preferencePanel = Rect("Preferred resource", gui.m_container, new Vector2(240, 438));
            _preferencePanel.anchorMin = _preferencePanel.anchorMax = new Vector2(1, 1); _preferencePanel.pivot = new Vector2(1, 1);
            _preferencePanel.anchoredPosition = new Vector2(-8, -54); Background(_preferencePanel, Well);
            Label(_preferencePanel, "Preferred resource", 10, 6, 220, 26, 19, Gold);
            for (var i = 0; i < 12; i++)
            {
                var category = (VssCategory)i;
                Button(_preferencePanel, category == VssCategory.Any ? "Any resource" : VssPreferences.Label(category), 10, 38 + i * 32, 220, 29, () =>
                {
                    if (_openChest != null && VssPreferences.Set(_openChest, category)) UpdateChestBadge();
                    _preferencePanel.gameObject.SetActive(false);
                });
            }
            _preferencePanel.gameObject.SetActive(false);
        }
        if (_openChest != chest) _preferencePanel.gameObject.SetActive(false);
        _openChest = chest; _chestButton.gameObject.SetActive(true); UpdateChestBadge();
    }
    private void UpdateChestBadge()
    {
        if (_chestBadge == null || _openChest == null) return;
        var category = VssPreferences.Get(_openChest); _chestBadge.enabled = category != VssCategory.Any;
        _chestBadge.sprite = VssPreferences.Icon(category);
    }
    private void TogglePreferences()
    {
        if (_preferencePanel != null) { _preferencePanel.gameObject.SetActive(!_preferencePanel.gameObject.activeSelf); _preferencePanel.SetAsLastSibling(); }
    }
    internal static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
    }
    private static void Top(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
    private static Image Background(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image;
    }
    private static TMP_Text Label(Transform parent, string value, float x, float y, float width, float height, int size, Color color)
    {
        var rect = Rect("Label", parent, new Vector2(width, height)); Top(rect, x, y, width, height);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = _font; text.text = value; text.fontSize = size;
        text.color = color; text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    private static Button Button(Transform parent, string value, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(value + " button", parent, new Vector2(width, height)); Top(rect, x, y, width, height);
        var image = Background(rect, new Color(0.24f, 0.20f, 0.13f));
        if (VssAssets.Inset != null) { image.sprite = VssAssets.Inset; image.type = Image.Type.Sliced; image.color = Color.white; }
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(1.35f, 1.25f, 1.05f); colors.pressedColor = new Color(0.75f, 0.65f, 0.4f); button.colors = colors;
        var text = Label(rect, value, 5, 0, width - 10, height, 18, Gold); text.alignment = TextAlignmentOptions.Center;
        button.onClick.AddListener(action); return button;
    }
    private static TMP_InputField Search(Transform parent, float x, float y, float width, float height)
    {
        var rect = Rect("Search", parent, new Vector2(width, height)); Top(rect, x, y, width, height);
        var background = Background(rect, Well);
        if (VssAssets.Inset != null) { background.sprite = VssAssets.Inset; background.type = Image.Type.Sliced; background.color = Color.white; }
        var field = rect.gameObject.AddComponent<TMP_InputField>();
        var area = Rect("Text area", rect, new Vector2(width - 20, height)); Top(area, 10, 0, width - 20, height);
        var text = Label(area, "", 0, 0, width - 20, height, 19, Paper);
        var placeholder = Label(area, "Search items…", 0, 0, width - 20, height, 19, new Color(0.6f, 0.56f, 0.47f));
        field.textViewport = area; field.textComponent = (TMP_Text)text; field.placeholder = placeholder;
        field.characterLimit = 100; field.lineType = TMP_InputField.LineType.SingleLine; return field;
    }
    private static RectTransform ScrollGrid(Transform parent, string name, float x, float y, float width, float height, int columns, bool storage)
    {
        var viewport = Rect(name + " viewport", parent, new Vector2(width, height)); Top(viewport, x, y, width, height);
        Background(viewport, Color.clear); viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect(name, viewport, new Vector2(width - 16, height)); Top(content, 0, 0, width - 16, height);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(72, 66); grid.spacing = new Vector2(6, 6);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = columns;
        grid.childAlignment = storage ? TextAnchor.UpperLeft : TextAnchor.UpperCenter;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.content = content; scroll.viewport = viewport;
        scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 100; scroll.movementType = ScrollRect.MovementType.Clamped;
        var track = Rect("Scrollbar", viewport, new Vector2(10, height)); Top(track, width - 10, 0, 10, height); Background(track, Well);
        var handle = Rect("Handle", track, new Vector2(10, 30)); var handleImage = Background(handle, new Color(0.48f, 0.38f, 0.22f));
        var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }
}

public sealed class VssSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    internal bool Storage;
    internal VssItemRow Row;
    internal ItemDrop.ItemData Item;
    internal Vector2i Position;
    internal Image Icon, Equipped;
    internal TMP_Text Amount, Marker;
    internal void Bind(VssItemRow row, ItemDrop.ItemData item, Vector2i pos)
    {
        Row = row; Item = item; Position = pos;
        Icon.sprite = row?.Icon ?? item?.GetIcon(); Icon.enabled = Icon.sprite != null;
        var amount = row?.Total ?? item?.m_stack ?? 0;
        Amount.text = amount > 1 ? amount.ToString("N0") : "";
        Equipped.enabled = !Storage && item != null && item.m_equipped;
        Marker.text = !Storage && pos.y == 0 ? (pos.x + 1).ToString() :
            row != null && row.Sample.m_quality > 1 ? $"★{row.Sample.m_quality}" : "";
    }
    public void OnBeginDrag(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) VssUi.BeginDrag(this); }
    public void OnDrag(PointerEventData data) { }
    public void OnEndDrag(PointerEventData data) => VssUi.EndDragStatic();
    public void OnDrop(PointerEventData data) => VssUi.Drop(Storage, Position);
    public void OnPointerClick(PointerEventData data) => VssUi.Click(this, data);
    public void OnPointerEnter(PointerEventData data) => VssUi.Tooltip(this);
    public void OnPointerExit(PointerEventData data) => VssUi.HideTooltipStatic();
}
public sealed class VssDropZone : MonoBehaviour, IDropHandler
{
    internal bool Storage;
    public void OnDrop(PointerEventData data) => VssUi.Drop(Storage, new Vector2i(-1, -1));
}
[HarmonyPatch(typeof(InventoryGui), "IsVisible")]
internal static class VssUiVisiblePatch
{
    private static void Postfix(ref bool __result) { if (VssUi.IsOpen) __result = true; }
}
[HarmonyPatch(typeof(InventoryGui), "Update")]
internal static class VssUiVanillaUpdatePatch
{
    private static bool Prefix() => !VssUi.IsOpen;
}
[HarmonyPatch(typeof(InventoryGui), "Show")]
internal static class VssUiShowPatch
{
    private static void Prefix() { if (VssUi.IsOpen) VssUi.Close(); }
}
[HarmonyPatch(typeof(Menu), "Update")]
internal static class VssUiEscapePatch
{
    private static bool Prefix() => !VssUi.SuppressMenu;
}
[HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
internal static class VssUiHoverPatch
{
    private static void Postfix(Hud __instance)
    {
        if (!VssUi.IsOpen) return;
        __instance.m_hoverName.text = "";
        __instance.m_crosshair.gameObject.SetActive(false);
        __instance.m_crosshairBow.gameObject.SetActive(false);
        // Vanilla UpdateCrosshair restores these on the first frame after closing storage.
    }
}
