using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SupplyShopGraphController : MonoBehaviour
{
    private readonly struct NodeKey : IEquatable<NodeKey>
    {
        public readonly ProgressionSystem.UpgradeId Id;
        public readonly int Tier;

        public NodeKey(ProgressionSystem.UpgradeId id, int tier)
        {
            Id = id;
            Tier = tier;
        }

        public bool Equals(NodeKey other) => Id == other.Id && Tier == other.Tier;
        public override bool Equals(object obj) => obj is NodeKey other && Equals(other);
        public override int GetHashCode() => ((int)Id * 397) ^ Tier;
    }

    private const string NodePrefabPath =
        "SupplyShop/prefab_SupplyShopNodePlaceholder";
    private const string PopupPrefabPath =
        "SupplyShop/prefab_SupplyShopPopupPlaceholder";
    private const string ConnectorPrefabPath =
        "SupplyShop/prefab_SupplyShopConnectorPlaceholder";
    private const string DotsTexturePath = "SupplyShop/t_supply_shop_grid_dots";
    private const string VignetteTexturePath = "SupplyShop/t_supply_shop_vignette";
    private const float GridSpacing = 120f;
    private static readonly Vector2 StartPosition = Vector2.zero;

    private readonly Dictionary<NodeKey, ProgressionNodeButton> nodes = new();
    private readonly List<SupplyShopGraphConnector> connectors = new();
    private RectTransform card;
    private RectTransform viewport;
    private RectTransform content;
    private ScrollRect scrollRect;
    private ProgressionTreePreview preview;
    private RectTransform startNode;
    private RectTransform sidebar;
    private bool initialized;

    public static SupplyShopGraphController Install(GameObject shopScreen)
    {
        RectTransform card = shopScreen != null
            ? shopScreen.transform.Find("Supplies") as RectTransform
            : null;
        RectTransform scrollRoot = card != null
            ? card.Find("Progression Scroll View") as RectTransform
            : null;
        if (scrollRoot == null)
        {
            return null;
        }

        SupplyShopGraphController controller =
            scrollRoot.GetComponent<SupplyShopGraphController>();
        if (controller == null)
        {
            controller = scrollRoot.gameObject.AddComponent<SupplyShopGraphController>();
        }
        controller.Initialize(card);
        return controller;
    }

    public void Initialize(RectTransform shopCard)
    {
        if (initialized || shopCard == null)
        {
            return;
        }

        card = shopCard;
        scrollRect = GetComponent<ScrollRect>();
        viewport = transform.Find("Tree Viewport") as RectTransform;
        content = viewport != null
            ? viewport.Find("Tree Content") as RectTransform
            : null;
        preview = card.GetComponent<ProgressionTreePreview>();
        if (scrollRect == null || viewport == null || content == null || preview == null)
        {
            return;
        }

        initialized = true;
        ConfigureCanvas();
        BuildSidebar();
        InstallBackdrop();
        BuildNodes();
        BuildConnectors();
        InstallPopup();
        InstallPanHint();
        ProgressionSystem.Changed += RefreshAll;
        RefreshAll();
    }

    private void OnDestroy()
    {
        ProgressionSystem.Changed -= RefreshAll;
    }

    public void RefreshAll()
    {
        foreach (ProgressionNodeButton node in nodes.Values)
        {
            if (node == null)
            {
                continue;
            }

            ProgressionSystem.NodeState state = node.GetNodeState();
            bool visible = IsSidebarId(node.UpgradeId)
                || state.IsMaxed
                || (state.Visible && state.PrerequisiteMet);
            node.SetGraphVisible(visible);
            node.Refresh();
        }

        for (int index = 0; index < connectors.Count; index++)
        {
            connectors[index]?.Refresh();
        }
    }

    private void ConfigureCanvas()
    {
        scrollRect.horizontal = true;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.08f;
        scrollRect.scrollSensitivity = 0f;

        content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
        content.pivot = new Vector2(0.5f, 0.5f);
        content.sizeDelta = new Vector2(5760f, 5760f);
        content.anchoredPosition = Vector2.zero;

        RectTransform scrollRoot = transform as RectTransform;
        scrollRoot.anchorMin = scrollRoot.anchorMax = new Vector2(0.5f, 0.5f);
        scrollRoot.pivot = new Vector2(0.5f, 0.5f);
        scrollRoot.anchoredPosition = new Vector2(170f, -28f);
        scrollRoot.sizeDelta = new Vector2(1470f, 800f);

        Image viewportImage = viewport.GetComponent<Image>();
        if (viewportImage != null)
        {
            viewportImage.color = new Color(0.035f, 0.04f, 0.04f, 1f);
        }

        ProgressionTreePanController pan =
            GetComponent<ProgressionTreePanController>();
        if (pan == null)
        {
            pan = gameObject.AddComponent<ProgressionTreePanController>();
        }
        pan.Configure(scrollRect, viewport, preview);
        pan.SetInitialFocus(StartPosition);
    }

    private void BuildSidebar()
    {
        Transform existing = card.Find("Persistent Supplies Sidebar");
        if (existing != null)
        {
            sidebar = existing as RectTransform;
            return;
        }

        GameObject panel = new GameObject(
            "Persistent Supplies Sidebar",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline));
        panel.transform.SetParent(card, false);
        sidebar = panel.transform as RectTransform;
        sidebar.anchorMin = sidebar.anchorMax = new Vector2(0.5f, 0.5f);
        sidebar.pivot = new Vector2(0.5f, 0.5f);
        sidebar.anchoredPosition = new Vector2(-760f, -28f);
        sidebar.sizeDelta = new Vector2(300f, 800f);
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.055f, 0.065f, 0.06f, 0.98f);
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(0.28f, 0.34f, 0.3f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_Text title = CreateRuntimeText(
            "Sidebar Title",
            sidebar,
            22f,
            TextAlignmentOptions.Center);
        title.text = "CONSUMABLES";
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 0.83f, 0.38f, 1f);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax =
            new Vector2(0.5f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.anchoredPosition = new Vector2(0f, -22f);
        title.rectTransform.sizeDelta = new Vector2(260f, 34f);

        TMP_Text caption = CreateRuntimeText(
            "Sidebar Caption",
            sidebar,
            11f,
            TextAlignmentOptions.Center);
        caption.text = "RESTOCK ANY TIME";
        caption.color = new Color(0.62f, 0.68f, 0.64f, 1f);
        caption.rectTransform.anchorMin = caption.rectTransform.anchorMax =
            new Vector2(0.5f, 1f);
        caption.rectTransform.pivot = new Vector2(0.5f, 1f);
        caption.rectTransform.anchoredPosition = new Vector2(0f, -58f);
        caption.rectTransform.sizeDelta = new Vector2(260f, 24f);
    }

    private void InstallBackdrop()
    {
        RectTransform dots = EnsureRawImage("Graph Dot Grid", content);
        dots.anchorMin = dots.anchorMax = new Vector2(0.5f, 0.5f);
        dots.anchoredPosition = Vector2.zero;
        dots.sizeDelta = content.sizeDelta;
        RawImage dotsImage = dots.GetComponent<RawImage>();
        dotsImage.texture = Resources.Load<Texture2D>(DotsTexturePath)
            ?? Resources.Load<Texture2D>("SupplyShop/t_supply_shop_dots");
        dotsImage.uvRect = new Rect(
            0.125f,
            0.125f,
            content.sizeDelta.x / GridSpacing,
            content.sizeDelta.y / GridSpacing);
        dotsImage.color = new Color(1f, 1f, 1f, 0.66f);
        dotsImage.raycastTarget = false;
        dots.SetAsFirstSibling();

        RectTransform vignette = EnsureRawImage("Graph Vignette", viewport);
        vignette.anchorMin = Vector2.zero;
        vignette.anchorMax = Vector2.one;
        vignette.offsetMin = vignette.offsetMax = Vector2.zero;
        RawImage vignetteImage = vignette.GetComponent<RawImage>();
        vignetteImage.texture = Resources.Load<Texture2D>(VignetteTexturePath);
        vignetteImage.color = Color.white;
        vignetteImage.raycastTarget = false;
        vignette.SetAsLastSibling();
    }

    private void BuildNodes()
    {
        ProgressionNodeButton[] authored =
            content.GetComponentsInChildren<ProgressionNodeButton>(true);
        GameObject nodePrefab = Resources.Load<GameObject>(NodePrefabPath);
        Texture2D iconAtlas = Resources.Load<Texture2D>("UI/SuppliesIconAtlas");

        for (int index = 0; index < authored.Length; index++)
        {
            ProgressionNodeButton source = authored[index];
            if (source == null)
            {
                continue;
            }

            NodeKey key = new NodeKey(source.UpgradeId, source.TargetLevel);
            if (nodes.ContainsKey(key))
            {
                RetireAuthoredNode(source);
                continue;
            }

            bool sidebarButton = IsSidebarId(source.UpgradeId);
            Transform parent = sidebarButton ? sidebar : content;
            GameObject instance = nodePrefab != null
                ? Instantiate(nodePrefab, parent, false)
                : Instantiate(source.gameObject, parent, false);
            // Repeatable purchase sources are intentionally inactive in the
            // authored map. Only their generated fixed-sidebar copies render.
            instance.SetActive(true);
            instance.name = sidebarButton
                ? $"Supply Button {source.UpgradeId}"
                : $"Graph Node {source.UpgradeId} {source.TargetLevel}";
            ProgressionNodeButton node = instance.GetComponent<ProgressionNodeButton>();
            if (node == null)
            {
                Destroy(instance);
                continue;
            }

            node.SetUpgrade(source.UpgradeId, source.TargetLevel);
            Color color = GetBranchColor(source.UpgradeId);
            node.SetVisualColor(color);
            RectTransform rect = instance.transform as RectTransform;
            rect.SetParent(parent, false);
            if (sidebarButton)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(
                    0f,
                    -105f - GetSidebarIndex(source.UpgradeId) * 126f);
                rect.sizeDelta = new Vector2(250f, 104f);
                ApplySidebarVisual(node, iconAtlas, color);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = GetNodePosition(
                    source.UpgradeId,
                    source.TargetLevel);
                rect.sizeDelta = new Vector2(64f, 64f);
                ApplyCompactVisual(node, iconAtlas, color);
            }
            nodes.Add(key, node);
            RetireAuthoredNode(source);
        }

        HideLegacyLayout();
        BuildStartNode(nodePrefab, iconAtlas);
    }

    private static void RetireAuthoredNode(ProgressionNodeButton source)
    {
        if (source == null)
        {
            return;
        }

        source.enabled = false;
        source.gameObject.SetActive(false);
    }

    private void BuildStartNode(GameObject nodePrefab, Texture2D iconAtlas)
    {
        GameObject instance;
        if (nodePrefab != null)
        {
            instance = Instantiate(nodePrefab, content, false);
        }
        else
        {
            instance = new GameObject(
                "Supply Graph Start",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline));
            instance.transform.SetParent(content, false);
        }

        instance.name = "Supply Graph Start";
        ProgressionNodeButton progressionNode =
            instance.GetComponent<ProgressionNodeButton>();
        if (progressionNode != null)
        {
            progressionNode.enabled = false;
        }
        Button button = instance.GetComponent<Button>();
        if (button != null)
        {
            button.interactable = false;
        }

        startNode = instance.transform as RectTransform;
        startNode.anchorMin = startNode.anchorMax = new Vector2(0.5f, 0.5f);
        startNode.pivot = new Vector2(0.5f, 0.5f);
        startNode.anchoredPosition = StartPosition;
        startNode.sizeDelta = new Vector2(82f, 82f);
        Image background = instance.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color(0.64f, 0.24f, 0.12f, 1f);
        }
        Outline outline = instance.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = new Color(1f, 0.75f, 0.25f, 1f);
            outline.effectDistance = new Vector2(3f, -3f);
        }

        RawImage icon = instance.transform.Find("Type Icon")?.GetComponent<RawImage>();
        if (icon != null)
        {
            icon.texture = iconAtlas;
            icon.uvRect = GetIconUv(0);
            icon.color = Color.white;
        }
        TMP_Text tier = instance.transform.Find("Tier")?.GetComponent<TMP_Text>();
        if (tier != null)
        {
            tier.text = "START";
            tier.fontSize = 11f;
        }
    }

    private void BuildConnectors()
    {
        HashSet<string> made = new();
        foreach (KeyValuePair<NodeKey, ProgressionNodeButton> pair in nodes)
        {
            NodeKey key = pair.Key;
            if (IsSidebarId(key.Id))
            {
                continue;
            }

            ProgressionNodeButton previous = FindPreviousTier(key.Id, key.Tier);
            if (previous != null)
            {
                Connect(previous.transform as RectTransform, pair.Value, key.Id, made);
                continue;
            }

            ProgressionNodeButton parent = FindCustomParent(key.Id);
            if (parent != null && IsSidebarId(parent.UpgradeId))
            {
                continue;
            }
            if (parent != null)
            {
                Connect(parent.transform as RectTransform, pair.Value, key.Id, made);
            }
            else
            {
                Connect(startNode, pair.Value, key.Id, made);
            }
        }

        ProgressionNodeButton vacuum = FindFirst(
            ProgressionSystem.UpgradeId.VacuumUnlock);
        ProgressionNodeButton lastReach = FindLast(
            ProgressionSystem.UpgradeId.BasketReach);
        if (vacuum != null && lastReach != null)
        {
            Connect(lastReach.transform as RectTransform, vacuum,
                ProgressionSystem.UpgradeId.VacuumUnlock, made);
        }

        ConnectTurboUpgradeRails(
            ProgressionSystem.UpgradeId.IncubatorTurboPower,
            ProgressionSystem.UpgradeId.IncubatorTurboDuration,
            made);
        ConnectTurboUpgradeRails(
            ProgressionSystem.UpgradeId.CrosshatcherTurboPower,
            ProgressionSystem.UpgradeId.CrosshatcherTurboDuration,
            made);
        ConnectTurboUpgradeRails(
            ProgressionSystem.UpgradeId.RobotTurboPower,
            ProgressionSystem.UpgradeId.RobotTurboDuration,
            made);
    }

    private void ConnectTurboUpgradeRails(
        ProgressionSystem.UpgradeId powerId,
        ProgressionSystem.UpgradeId durationId,
        HashSet<string> made)
    {
        int maximumTier = Mathf.Max(
            TurboConsumableSystem.MaximumPowerLevel,
            TurboConsumableSystem.MaximumDurationLevel);
        for (int tier = 1; tier <= maximumTier; tier++)
        {
            if (!nodes.TryGetValue(
                    new NodeKey(powerId, tier),
                    out ProgressionNodeButton power)
                || !nodes.TryGetValue(
                    new NodeKey(durationId, tier),
                    out ProgressionNodeButton duration))
            {
                continue;
            }

            // The repeatable turbo purchase lives in the fixed sidebar, so it
            // cannot supply a stable scrolling-map connector. Vertical rungs
            // join the power and duration upgrade rails instead, including the
            // first visible tier that was previously left floating.
            Connect(
                power.transform as RectTransform,
                duration,
                powerId,
                made);
        }
    }

    private void Connect(
        RectTransform source,
        ProgressionNodeButton destination,
        ProgressionSystem.UpgradeId colorId,
        HashSet<string> made)
    {
        if (source == null || destination == null)
        {
            return;
        }
        string key = source.GetInstanceID() + ":" + destination.GetInstanceID();
        if (!made.Add(key))
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(ConnectorPrefabPath);
        GameObject instance;
        if (prefab != null)
        {
            instance = Instantiate(prefab, content, false);
        }
        else
        {
            instance = new GameObject(
                "Graph Connector",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(SupplyShopGraphConnector));
            instance.transform.SetParent(content, false);
        }
        instance.name = "Graph Connector";
        instance.transform.SetAsFirstSibling();
        SupplyShopGraphConnector connector =
            instance.GetComponent<SupplyShopGraphConnector>();
        ProgressionNodeButton sourceNode =
            source.GetComponent<ProgressionNodeButton>();
        connector.Configure(
            source,
            destination.transform as RectTransform,
            GetBranchColor(colorId),
            sourceNode,
            destination);
        connectors.Add(connector);
    }

    private ProgressionNodeButton FindPreviousTier(
        ProgressionSystem.UpgradeId id,
        int tier)
    {
        if (tier <= 0)
        {
            return null;
        }

        ProgressionNodeButton best = null;
        int bestTier = int.MinValue;
        foreach (KeyValuePair<NodeKey, ProgressionNodeButton> pair in nodes)
        {
            if (pair.Key.Id == id && pair.Key.Tier < tier && pair.Key.Tier > bestTier)
            {
                bestTier = pair.Key.Tier;
                best = pair.Value;
            }
        }
        return best;
    }

    private ProgressionNodeButton FindCustomParent(ProgressionSystem.UpgradeId id)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.EggWeight =>
                FindTierOrFirst(ProgressionSystem.UpgradeId.RareEggChance, 2),
            ProgressionSystem.UpgradeId.EggValue =>
                FindFirst(ProgressionSystem.UpgradeId.EggWeight),
            ProgressionSystem.UpgradeId.ChickenPerks =>
                FindTierOrLast(ProgressionSystem.UpgradeId.RareEggChance, 8),
            ProgressionSystem.UpgradeId.TruckBonus =>
                FindTierOrLast(ProgressionSystem.UpgradeId.EggValue, 2),
            ProgressionSystem.UpgradeId.BasketReach =>
                FindFirst(ProgressionSystem.UpgradeId.BasketCapacity),
            ProgressionSystem.UpgradeId.VacuumUnlock =>
                FindLast(ProgressionSystem.UpgradeId.BasketCapacity),
            ProgressionSystem.UpgradeId.VacuumPower
                or ProgressionSystem.UpgradeId.VacuumRange =>
                FindFirst(ProgressionSystem.UpgradeId.VacuumUnlock),
            ProgressionSystem.UpgradeId.IncubatorTurboPower
                or ProgressionSystem.UpgradeId.IncubatorTurboDuration =>
                FindFirst(ProgressionSystem.UpgradeId.IncubatorTurbo),
            ProgressionSystem.UpgradeId.CrosshatcherTurboPower
                or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration =>
                FindFirst(ProgressionSystem.UpgradeId.CrosshatcherTurbo),
            ProgressionSystem.UpgradeId.RobotTurboPower
                or ProgressionSystem.UpgradeId.RobotTurboDuration =>
                FindFirst(ProgressionSystem.UpgradeId.RobotTurbo),
            _ => null
        };
    }

    private ProgressionNodeButton FindFirst(ProgressionSystem.UpgradeId id)
    {
        ProgressionNodeButton best = null;
        int bestTier = int.MaxValue;
        foreach (KeyValuePair<NodeKey, ProgressionNodeButton> pair in nodes)
        {
            if (pair.Key.Id == id && pair.Key.Tier < bestTier)
            {
                bestTier = pair.Key.Tier;
                best = pair.Value;
            }
        }
        return best;
    }

    private ProgressionNodeButton FindLast(ProgressionSystem.UpgradeId id)
    {
        ProgressionNodeButton best = null;
        int bestTier = int.MinValue;
        foreach (KeyValuePair<NodeKey, ProgressionNodeButton> pair in nodes)
        {
            if (pair.Key.Id == id && pair.Key.Tier > bestTier)
            {
                bestTier = pair.Key.Tier;
                best = pair.Value;
            }
        }
        return best;
    }

    private ProgressionNodeButton FindTierOrFirst(
        ProgressionSystem.UpgradeId id,
        int tier)
    {
        return nodes.TryGetValue(new NodeKey(id, tier), out ProgressionNodeButton node)
            ? node
            : FindFirst(id);
    }

    private ProgressionNodeButton FindTierOrLast(
        ProgressionSystem.UpgradeId id,
        int tier)
    {
        return nodes.TryGetValue(new NodeKey(id, tier), out ProgressionNodeButton node)
            ? node
            : FindLast(id);
    }

    private void InstallPopup()
    {
        GameObject prefab = Resources.Load<GameObject>(PopupPrefabPath);
        if (prefab == null)
        {
            return;
        }

        Transform old = card.Find("Node Preview");
        if (old != null)
        {
            old.gameObject.SetActive(false);
        }

        GameObject panel = Instantiate(prefab, card, false);
        panel.name = "Node Preview";
        ProgressionTreePopupHoverGuard hoverGuard =
            panel.GetComponent<ProgressionTreePopupHoverGuard>();
        if (hoverGuard == null)
        {
            hoverGuard = panel.AddComponent<ProgressionTreePopupHoverGuard>();
        }
        hoverGuard.Configure(preview);
        TMP_Text title = panel.transform.Find("Title")?.GetComponent<TMP_Text>();
        TMP_Text level = panel.transform.Find("Level")?.GetComponent<TMP_Text>();
        TMP_Text description = panel.transform.Find("Description")?.GetComponent<TMP_Text>();
        if (description != null)
        {
            description.overflowMode = TextOverflowModes.Truncate;
        }
        TMP_Text price = panel.transform.Find("Price")?.GetComponent<TMP_Text>();
        TMP_Text savings = panel.transform.Find("Savings")?.GetComponent<TMP_Text>();
        Image fill = panel.transform.Find("Savings Track/Fill")?.GetComponent<Image>();
        Button buy = panel.transform.Find("Buy Button")?.GetComponent<Button>();
        TMP_Text buyText = panel.transform.Find("Buy Button/Label")?.GetComponent<TMP_Text>();
        Button dismiss = card.parent != null
            ? card.parent.GetComponent<Button>()
            : null;
        if (title != null && level != null && description != null
            && price != null && savings != null && fill != null
            && buy != null && buyText != null)
        {
            preview.Configure(
                panel,
                title,
                level,
                description,
                price,
                savings,
                fill,
                buy,
                buyText,
                dismiss);
        }
    }

    private void InstallPanHint()
    {
        Transform existing = card.Find("Graph Pan Hint");
        if (existing != null)
        {
            return;
        }

        GameObject hintObject = new GameObject(
            "Graph Pan Hint",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        hintObject.transform.SetParent(card, false);
        RectTransform rect = hintObject.transform as RectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 14f);
        rect.sizeDelta = new Vector2(1050f, 24f);
        TMP_Text hint = hintObject.GetComponent<TMP_Text>();
        hint.text = "HOVER FOR DETAILS  .  CLICK TO PIN  .  CLICK AWAY TO CLOSE  .  "
            + "DRAG / WASD TO PAN  .  H RETURN HOME";
        hint.alignment = TextAlignmentOptions.Center;
        hint.fontSize = 13f;
        hint.color = new Color(0.72f, 0.76f, 0.72f, 0.72f);
        hint.raycastTarget = false;
    }

    private void HideLegacyLayout()
    {
        string[] names =
        {
            "CONSUMABLES Branch",
            "FOOD Branch",
            "TECH Branch",
            "COLLECTION Branch",
            "Food Tree Group",
            "Tech Tree Group",
            "Collection Tree Group",
            "Consumables Column Frame"
        };
        for (int index = 0; index < names.Length; index++)
        {
            Transform child = content.Find(names[index]);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        RectTransform[] rects = content.GetComponentsInChildren<RectTransform>(true);
        for (int index = 0; index < rects.Length; index++)
        {
            RectTransform rect = rects[index];
            if (rect != null
                && rect != content
                && (rect.name == "Branch Connector"
                    || rect.name == "Active Tree Frame"))
            {
                rect.gameObject.SetActive(false);
            }
        }
    }

    private static void ApplyCompactVisual(
        ProgressionNodeButton node,
        Texture2D iconAtlas,
        Color color)
    {
        Transform transform = node.transform;
        string[] hide = { "Label", "Node Cost", "Node Affordability", "Node Icon", "Generated Shop Icon" };
        for (int index = 0; index < hide.Length; index++)
        {
            Transform child = transform.Find(hide[index]);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        RawImage icon = transform.Find("Type Icon")?.GetComponent<RawImage>();
        if (icon == null)
        {
            RectTransform iconRect = EnsureRawImage("Type Icon", transform);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(0f, 5f);
            iconRect.sizeDelta = new Vector2(48f, 48f);
            icon = iconRect.GetComponent<RawImage>();
        }
        Texture2D standaloneIcon = GetStandaloneIcon(node.UpgradeId);
        Texture2D hudIconAtlas = node.UpgradeId
                == ProgressionSystem.UpgradeId.TruckBonus
            ? Resources.Load<Texture2D>("UI/HudIconAtlas")
            : null;
        icon.texture = hudIconAtlas ?? standaloneIcon ?? iconAtlas;
        icon.uvRect = hudIconAtlas != null
            ? RoundSystem.GetHudIconUv(4)
            : standaloneIcon != null
                ? new Rect(0f, 0f, 1f, 1f)
                : GetIconUv(GetIconIndex(node.UpgradeId));
        icon.color = Color.white;
        icon.raycastTarget = false;
        icon.rectTransform.anchoredPosition = new Vector2(0f, 4f);
        icon.rectTransform.sizeDelta = new Vector2(40f, 40f);

        TMP_Text tier = transform.Find("Tier")?.GetComponent<TMP_Text>();
        if (tier != null)
        {
            tier.text = node.TargetLevel > 0 ? node.TargetLevel.ToString() : string.Empty;
            tier.color = new Color(1f, 0.91f, 0.65f, 1f);
            tier.fontSize = 10f;
            tier.rectTransform.anchoredPosition = new Vector2(0f, -24f);
        }

        Outline outline = node.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2f, -2f);
        }
    }

    private static void ApplySidebarVisual(
        ProgressionNodeButton node,
        Texture2D iconAtlas,
        Color color)
    {
        ApplyCompactVisual(node, iconAtlas, color);
        Transform transform = node.transform;
        RawImage icon = transform.Find("Type Icon")?.GetComponent<RawImage>();
        if (icon != null)
        {
            icon.rectTransform.anchoredPosition = new Vector2(-82f, 4f);
            icon.rectTransform.sizeDelta = new Vector2(62f, 62f);
        }

        TMP_Text tier = transform.Find("Tier")?.GetComponent<TMP_Text>();
        if (tier != null)
        {
            tier.gameObject.SetActive(false);
        }

        TMP_Text label = CreateRuntimeText(
            "Sidebar Label",
            transform,
            15f,
            TextAlignmentOptions.Left);
        ProgressionSystem.NodeState state = node.GetNodeState();
        string title = string.IsNullOrWhiteSpace(state.Title)
            ? node.UpgradeId.ToString()
            : state.Title;
        label.text = $"<b>{title.ToUpperInvariant()}</b>\n"
            + "<size=10><color=#AAB7AE>HOVER FOR DETAILS</color></size>";
        label.color = Color.white;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax =
            new Vector2(0.5f, 0.5f);
        label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        label.rectTransform.anchoredPosition = new Vector2(38f, 0f);
        label.rectTransform.sizeDelta = new Vector2(150f, 62f);
    }

    private static bool IsSidebarId(ProgressionSystem.UpgradeId id)
    {
        return id is ProgressionSystem.UpgradeId.FoodBag
            or ProgressionSystem.UpgradeId.IncubatorTurbo
            or ProgressionSystem.UpgradeId.CrosshatcherTurbo
            or ProgressionSystem.UpgradeId.RobotTurbo;
    }

    private static int GetSidebarIndex(ProgressionSystem.UpgradeId id)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.FoodBag => 0,
            ProgressionSystem.UpgradeId.IncubatorTurbo => 1,
            ProgressionSystem.UpgradeId.CrosshatcherTurbo => 2,
            ProgressionSystem.UpgradeId.RobotTurbo => 3,
            _ => 4
        };
    }

    private static TMP_Text CreateRuntimeText(
        string name,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject instance = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        instance.transform.SetParent(parent, false);
        TMP_Text text = instance.GetComponent<TMP_Text>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static Vector2 GetNodePosition(
        ProgressionSystem.UpgradeId id,
        int tier)
    {
        int step = Mathf.Max(0, tier - 1);
        float s = GridSpacing;
        return id switch
        {
            ProgressionSystem.UpgradeId.FeedSpeed =>
                new Vector2(
                    -Mathf.Min(5f, 1f + Mathf.Max(0, tier - 2)) * s,
                    (1f + Mathf.Max(0, tier - 2)) * s),
            ProgressionSystem.UpgradeId.PrimeFeed =>
                new Vector2((-1f - step) * s, (-1f - step) * s),
            ProgressionSystem.UpgradeId.RareEggChance =>
                new Vector2(0f, (-1f - step) * s),
            ProgressionSystem.UpgradeId.EggWeight =>
                new Vector2((1f + step) * s, -3f * s),
            ProgressionSystem.UpgradeId.EggValue =>
                new Vector2((1f + step) * s, -4f * s),
            ProgressionSystem.UpgradeId.ChickenPerks =>
                new Vector2((-1f - step) * s, -9f * s),
            ProgressionSystem.UpgradeId.BasketCapacity =>
                new Vector2((1f + step) * s, 0f),
            ProgressionSystem.UpgradeId.BasketReach =>
                new Vector2((1f + step) * s, s),
            ProgressionSystem.UpgradeId.VacuumUnlock => new Vector2(5f * s, 0f),
            ProgressionSystem.UpgradeId.VacuumPower =>
                new Vector2(
                    (6f + Mathf.Max(0, tier - 2)) * s,
                    (1f + Mathf.Max(0, tier - 2)) * s),
            ProgressionSystem.UpgradeId.VacuumRange =>
                new Vector2(
                    (6f + Mathf.Max(0, tier - 2)) * s,
                    (-1f - Mathf.Max(0, tier - 2)) * s),
            ProgressionSystem.UpgradeId.TruckBonus =>
                new Vector2((3f + step) * s, -5f * s),
            ProgressionSystem.UpgradeId.IncubatorTurboPower =>
                new Vector2((-4f + step) * s, 6f * s),
            ProgressionSystem.UpgradeId.IncubatorTurboDuration =>
                new Vector2((-4f + step) * s, 5f * s),
            ProgressionSystem.UpgradeId.CrosshatcherTurboPower =>
                new Vector2((1f + step) * s, 6f * s),
            ProgressionSystem.UpgradeId.CrosshatcherTurboDuration =>
                new Vector2((1f + step) * s, 5f * s),
            ProgressionSystem.UpgradeId.RobotTurboPower =>
                new Vector2((-2f + step) * s, 8f * s),
            ProgressionSystem.UpgradeId.RobotTurboDuration =>
                new Vector2((-2f + step) * s, 7f * s),
            _ => new Vector2(0f, (1f + step) * s)
        };
    }

    private static Color GetBranchColor(ProgressionSystem.UpgradeId id)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.FoodBag
                or ProgressionSystem.UpgradeId.FeedSpeed
                or ProgressionSystem.UpgradeId.PrimeFeed =>
                new Color(0.9f, 0.48f, 0.12f, 1f),
            ProgressionSystem.UpgradeId.RareEggChance
                or ProgressionSystem.UpgradeId.ChickenPerks =>
                new Color(0.72f, 0.34f, 0.76f, 1f),
            ProgressionSystem.UpgradeId.EggWeight =>
                new Color(0.82f, 0.65f, 0.16f, 1f),
            ProgressionSystem.UpgradeId.EggValue =>
                new Color(0.24f, 0.67f, 0.34f, 1f),
            ProgressionSystem.UpgradeId.IncubatorTurbo
                or ProgressionSystem.UpgradeId.IncubatorTurboPower
                or ProgressionSystem.UpgradeId.IncubatorTurboDuration =>
                new Color(0.95f, 0.48f, 0.12f, 1f),
            ProgressionSystem.UpgradeId.CrosshatcherTurbo
                or ProgressionSystem.UpgradeId.CrosshatcherTurboPower
                or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration =>
                new Color(0.35f, 0.76f, 0.32f, 1f),
            ProgressionSystem.UpgradeId.RobotTurbo
                or ProgressionSystem.UpgradeId.RobotTurboPower
                or ProgressionSystem.UpgradeId.RobotTurboDuration =>
                new Color(0.68f, 0.42f, 0.9f, 1f),
            _ => new Color(0.3f, 0.64f, 0.88f, 1f)
        };
    }

    private static int GetIconIndex(ProgressionSystem.UpgradeId id)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.FoodBag => 0,
            ProgressionSystem.UpgradeId.FeedSpeed
                or ProgressionSystem.UpgradeId.PrimeFeed => 1,
            ProgressionSystem.UpgradeId.RareEggChance
                or ProgressionSystem.UpgradeId.ChickenPerks => 2,
            ProgressionSystem.UpgradeId.EggWeight
                or ProgressionSystem.UpgradeId.EggValue => 3,
            ProgressionSystem.UpgradeId.IncubatorTurbo
                or ProgressionSystem.UpgradeId.IncubatorTurboPower
                or ProgressionSystem.UpgradeId.IncubatorTurboDuration => 4,
            ProgressionSystem.UpgradeId.CrosshatcherTurbo
                or ProgressionSystem.UpgradeId.CrosshatcherTurboPower
                or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration => 5,
            ProgressionSystem.UpgradeId.BasketCapacity
                or ProgressionSystem.UpgradeId.BasketReach
                or ProgressionSystem.UpgradeId.VacuumUnlock
                or ProgressionSystem.UpgradeId.VacuumPower
                or ProgressionSystem.UpgradeId.VacuumRange => 6,
            _ => 7
        };
    }

    private static Rect GetIconUv(int index)
    {
        int clamped = Mathf.Clamp(index, 0, 7);
        int column = clamped % 4;
        int row = clamped / 4;
        return new Rect(column * 0.25f, row == 0 ? 0.5f : 0f, 0.25f, 0.5f);
    }

    private static Texture2D GetStandaloneIcon(ProgressionSystem.UpgradeId id)
    {
        TurboConsumableSystem.TurboType? type = id switch
        {
            ProgressionSystem.UpgradeId.IncubatorTurbo =>
                TurboConsumableSystem.TurboType.Incubator,
            ProgressionSystem.UpgradeId.CrosshatcherTurbo =>
                TurboConsumableSystem.TurboType.Crosshatcher,
            ProgressionSystem.UpgradeId.RobotTurbo =>
                TurboConsumableSystem.TurboType.Robot,
            _ => null
        };
        return type.HasValue
            ? Resources.Load<Texture2D>(TurboConsumableSystem.GetResourcePath(type.Value))
            : null;
    }

    private static RectTransform EnsureRawImage(string name, Transform parent)
    {
        RawImage existing = parent.Find(name)?.GetComponent<RawImage>();
        if (existing != null)
        {
            return existing.rectTransform;
        }

        GameObject instance = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(RawImage));
        instance.transform.SetParent(parent, false);
        return instance.transform as RectTransform;
    }
}
