using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class SupplyShopGraphController : MonoBehaviour
{
    private sealed class EntranceElement
    {
        public RectTransform Rect;
        public CanvasGroup CanvasGroup;
        public Vector2 AnchoredPosition;
        public Vector3 Scale;
        public Quaternion Rotation;
        public float Alpha;
        public bool Interactable;
        public bool BlocksRaycasts;
        public float Delay;
        public float RotationSign;
    }

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
    private const float GridSpacing = 104f;
    private const float GraphNodeSize = 56f;
    private const float ConsumablesBarHeight = 104f;
    private const float DotTextureUvOrigin = 0.125f;
    private static readonly Vector2 StartPosition = Vector2.zero;

    private readonly Dictionary<NodeKey, ProgressionNodeButton> nodes = new();
    private readonly List<SupplyShopGraphConnector> connectors = new();
    private RectTransform card;
    private RectTransform viewport;
    private RectTransform content;
    private ScrollRect scrollRect;
    private ProgressionTreePreview preview;
    private RectTransform startNode;
    private RectTransform consumablesBar;
    private readonly List<EntranceElement> entranceElements = new();
    private Coroutine entranceAnimation;
    private Image entranceScreenBackground;
    private Image entranceCardBackground;
    private Color entranceScreenColor;
    private Color entranceCardColor;
    private Vector3 entranceCardScale;
    private Quaternion entranceCardRotation;
    private bool entrancePrepared;
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
        BuildConsumablesBar();
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

    private void OnDisable()
    {
        RestoreEntranceState();
    }

    public void PlayOpenAnimation()
    {
        if (!initialized || card == null || !gameObject.activeInHierarchy)
        {
            return;
        }

        RestoreEntranceState();
        entranceAnimation = StartCoroutine(AnimateShopOpen());
    }

    private IEnumerator AnimateShopOpen()
    {
        PrepareEntranceState();

        const float backgroundDuration = 0.18f;
        float elapsed = 0f;
        while (elapsed < backgroundDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / backgroundDuration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            SetImageAlpha(
                entranceScreenBackground,
                entranceScreenColor,
                eased);
            SetImageAlpha(
                entranceCardBackground,
                entranceCardColor,
                eased);
            float cardScale = Mathf.LerpUnclamped(
                0.965f,
                1f,
                eased + Mathf.Sin(progress * Mathf.PI) * 0.08f);
            card.localScale = Vector3.Scale(
                entranceCardScale,
                Vector3.one * cardScale);
            card.localRotation = entranceCardRotation
                * Quaternion.Euler(0f, 0f, Mathf.Lerp(-0.7f, 0f, eased));
            yield return null;
        }

        SetImageAlpha(entranceScreenBackground, entranceScreenColor, 1f);
        SetImageAlpha(entranceCardBackground, entranceCardColor, 1f);
        card.localScale = entranceCardScale;
        card.localRotation = entranceCardRotation;

        const float elementDuration = 0.34f;
        float totalDuration = entranceElements.Count > 0
            ? entranceElements[entranceElements.Count - 1].Delay + elementDuration
            : 0f;
        elapsed = 0f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            for (int index = 0; index < entranceElements.Count; index++)
            {
                EntranceElement element = entranceElements[index];
                float progress = Mathf.Clamp01(
                    (elapsed - element.Delay) / elementDuration);
                if (progress <= 0f)
                {
                    continue;
                }

                element.CanvasGroup.alpha = element.Alpha
                    * Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(progress / 0.42f));
                float decay = Mathf.Exp(-7.2f * progress);
                float scale = 1f
                    - 0.18f * decay * Mathf.Cos(progress * 13.5f);
                float rotation = element.RotationSign
                    * 5.5f
                    * decay
                    * Mathf.Cos(progress * 15f);
                float verticalOffset = -16f
                    * decay
                    * Mathf.Cos(progress * 11f);
                element.Rect.localScale = Vector3.Scale(
                    element.Scale,
                    Vector3.one * scale);
                element.Rect.localRotation = element.Rotation
                    * Quaternion.Euler(0f, 0f, rotation);
                element.Rect.anchoredPosition = element.AnchoredPosition
                    + Vector2.up * verticalOffset;
            }

            yield return null;
        }

        FinishEntranceState();
    }

    private void PrepareEntranceState()
    {
        entrancePrepared = true;
        entranceScreenBackground = card.parent != null
            ? card.parent.GetComponent<Image>()
            : null;
        entranceCardBackground = card.GetComponent<Image>();
        entranceScreenColor = entranceScreenBackground != null
            ? entranceScreenBackground.color
            : Color.white;
        entranceCardColor = entranceCardBackground != null
            ? entranceCardBackground.color
            : Color.white;
        entranceCardScale = card.localScale;
        entranceCardRotation = card.localRotation;
        SetImageAlpha(entranceScreenBackground, entranceScreenColor, 0f);
        SetImageAlpha(entranceCardBackground, entranceCardColor, 0f);

        entranceElements.Clear();
        string[] entranceOrder =
        {
            "Shop Title Frame",
            "Shop Title",
            "Cash Banner Frame",
            "Balance Coin",
            "Available Cash",
            "Done Shopping",
            "Progression Scroll View",
            "Persistent Consumables Bar",
            "Graph Pan Hint",
            "Shop Status"
        };
        HashSet<RectTransform> included = new();
        for (int index = 0; index < entranceOrder.Length; index++)
        {
            RectTransform rect = card.Find(entranceOrder[index]) as RectTransform;
            if (rect != null && rect.gameObject.activeSelf && included.Add(rect))
            {
                AddEntranceElement(rect, entranceElements.Count);
            }
        }

        for (int index = 0; index < card.childCount; index++)
        {
            RectTransform rect = card.GetChild(index) as RectTransform;
            if (rect != null && rect.gameObject.activeSelf && included.Add(rect))
            {
                AddEntranceElement(rect, entranceElements.Count);
            }
        }
    }

    private void AddEntranceElement(RectTransform rect, int sequenceIndex)
    {
        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = rect.gameObject.AddComponent<CanvasGroup>();
        }

        EntranceElement element = new EntranceElement
        {
            Rect = rect,
            CanvasGroup = group,
            AnchoredPosition = rect.anchoredPosition,
            Scale = rect.localScale,
            Rotation = rect.localRotation,
            Alpha = group.alpha,
            Interactable = group.interactable,
            BlocksRaycasts = group.blocksRaycasts,
            Delay = sequenceIndex * 0.045f,
            RotationSign = (sequenceIndex & 1) == 0 ? -1f : 1f
        };
        entranceElements.Add(element);
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        rect.localScale = Vector3.Scale(element.Scale, Vector3.one * 0.82f);
        rect.localRotation = element.Rotation
            * Quaternion.Euler(0f, 0f, element.RotationSign * 5.5f);
        rect.anchoredPosition = element.AnchoredPosition + Vector2.down * 16f;
    }

    private void FinishEntranceState()
    {
        RestoreEntranceValues();
        entranceElements.Clear();
        entranceAnimation = null;
        entrancePrepared = false;
    }

    private void RestoreEntranceState()
    {
        if (entranceAnimation != null)
        {
            StopCoroutine(entranceAnimation);
            entranceAnimation = null;
        }

        if (!entrancePrepared)
        {
            return;
        }

        RestoreEntranceValues();
        entranceElements.Clear();
        entrancePrepared = false;
    }

    private void RestoreEntranceValues()
    {
        SetImageAlpha(entranceScreenBackground, entranceScreenColor, 1f);
        SetImageAlpha(entranceCardBackground, entranceCardColor, 1f);
        if (card != null)
        {
            card.localScale = entranceCardScale;
            card.localRotation = entranceCardRotation;
        }

        for (int index = 0; index < entranceElements.Count; index++)
        {
            EntranceElement element = entranceElements[index];
            if (element.Rect == null || element.CanvasGroup == null)
            {
                continue;
            }

            element.Rect.anchoredPosition = element.AnchoredPosition;
            element.Rect.localScale = element.Scale;
            element.Rect.localRotation = element.Rotation;
            element.CanvasGroup.alpha = element.Alpha;
            element.CanvasGroup.interactable = element.Interactable;
            element.CanvasGroup.blocksRaycasts = element.BlocksRaycasts;
        }
    }

    private static void SetImageAlpha(Image image, Color baseColor, float alpha)
    {
        if (image == null)
        {
            return;
        }

        Color color = baseColor;
        color.a *= Mathf.Clamp01(alpha);
        image.color = color;
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
            bool visible = IsConsumableId(node.UpgradeId)
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
        scrollRoot.anchoredPosition = new Vector2(0f, 30f);
        scrollRoot.sizeDelta = new Vector2(1830f, 640f);

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

    private void BuildConsumablesBar()
    {
        Transform existing = card.Find("Persistent Consumables Bar");
        if (existing != null)
        {
            consumablesBar = existing as RectTransform;
            return;
        }

        GameObject panel = new GameObject(
            "Persistent Consumables Bar",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline));
        panel.transform.SetParent(card, false);
        consumablesBar = panel.transform as RectTransform;
        consumablesBar.anchorMin = consumablesBar.anchorMax =
            new Vector2(0.5f, 0f);
        consumablesBar.pivot = new Vector2(0.5f, 0f);
        consumablesBar.anchoredPosition = new Vector2(0f, 18f);
        consumablesBar.sizeDelta = new Vector2(1830f, ConsumablesBarHeight);
        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.055f, 0.065f, 0.06f, 0.98f);
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(0.28f, 0.34f, 0.3f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        TMP_Text title = CreateRuntimeText(
            "Consumables Title",
            consumablesBar,
            20f,
            TextAlignmentOptions.Center);
        title.text = "CONSUMABLES";
        title.fontStyle = FontStyles.Bold;
        title.color = new Color(1f, 0.83f, 0.38f, 1f);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax =
            new Vector2(0.5f, 0.5f);
        title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        title.rectTransform.anchoredPosition = new Vector2(-755f, 15f);
        title.rectTransform.sizeDelta = new Vector2(260f, 34f);

        TMP_Text caption = CreateRuntimeText(
            "Consumables Caption",
            consumablesBar,
            11f,
            TextAlignmentOptions.Center);
        caption.text = "RESTOCK ANY TIME";
        caption.color = new Color(0.62f, 0.68f, 0.64f, 1f);
        caption.rectTransform.anchorMin = caption.rectTransform.anchorMax =
            new Vector2(0.5f, 0.5f);
        caption.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        caption.rectTransform.anchoredPosition = new Vector2(-755f, -17f);
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
        float horizontalTiles = content.sizeDelta.x / GridSpacing;
        float verticalTiles = content.sizeDelta.y / GridSpacing;
        // Phase the repeating texture from the graph origin instead of the
        // content edge, so every GridSpacing multiple lands on a pinhole.
        dotsImage.uvRect = new Rect(
            DotTextureUvOrigin - horizontalTiles * 0.5f,
            DotTextureUvOrigin - verticalTiles * 0.5f,
            horizontalTiles,
            verticalTiles);
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

            if (IsRetiredTurboId(source.UpgradeId))
            {
                RetireAuthoredNode(source);
                continue;
            }

            NodeKey key = new NodeKey(source.UpgradeId, source.TargetLevel);
            if (nodes.ContainsKey(key))
            {
                RetireAuthoredNode(source);
                continue;
            }

            bool consumableButton = IsConsumableId(source.UpgradeId);
            Transform parent = consumableButton ? consumablesBar : content;
            GameObject instance = nodePrefab != null
                ? Instantiate(nodePrefab, parent, false)
                : Instantiate(source.gameObject, parent, false);
            // Repeatable purchase sources are intentionally inactive in the
            // authored map. Only their generated fixed-bar copies render.
            instance.SetActive(true);
            instance.name = consumableButton
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
            if (consumableButton)
            {
                int consumableIndex = GetConsumableIndex(source.UpgradeId);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(
                    -250f + consumableIndex * 540f,
                    0f);
                rect.sizeDelta = new Vector2(500f, 76f);
                ApplyConsumableVisual(node, iconAtlas, color);
                node.SetRevealDelay(0.02f + consumableIndex * 0.045f);
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = GetNodePosition(
                    source.UpgradeId,
                    source.TargetLevel);
                rect.sizeDelta = new Vector2(GraphNodeSize, GraphNodeSize);
                ApplyCompactVisual(node, iconAtlas, color);
                node.SetRevealDelay(GetRevealDelay(rect.anchoredPosition));
            }
            nodes.Add(key, node);
            RetireAuthoredNode(source);
        }

        BuildGeneratedPenBonusNodes(nodePrefab, iconAtlas);

        HideLegacyLayout();
        BuildStartNode(nodePrefab, iconAtlas);
    }

    private void BuildGeneratedPenBonusNodes(
        GameObject nodePrefab,
        Texture2D iconAtlas)
    {
        if (nodePrefab == null)
        {
            return;
        }

        Color color = GetBranchColor(ProgressionSystem.UpgradeId.PenBonus);
        for (int tier = 1;
             tier <= ProgressionSystem.MaximumPenBonusLevel;
             tier++)
        {
            NodeKey key = new NodeKey(
                ProgressionSystem.UpgradeId.PenBonus,
                tier);
            if (nodes.ContainsKey(key))
            {
                continue;
            }

            GameObject instance = Instantiate(nodePrefab, content, false);
            instance.name = $"Graph Node PenBonus {tier}";
            ProgressionNodeButton node =
                instance.GetComponent<ProgressionNodeButton>();
            if (node == null)
            {
                Destroy(instance);
                continue;
            }

            node.SetUpgrade(ProgressionSystem.UpgradeId.PenBonus, tier);
            node.SetVisualColor(color);
            RectTransform rect = instance.transform as RectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = GetNodePosition(
                ProgressionSystem.UpgradeId.PenBonus,
                tier);
            rect.sizeDelta = new Vector2(GraphNodeSize, GraphNodeSize);
            ApplyCompactVisual(node, iconAtlas, color);
            node.SetRevealDelay(GetRevealDelay(rect.anchoredPosition));
            nodes.Add(key, node);
        }
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
        startNode.sizeDelta = new Vector2(68f, 68f);
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
            if (IsConsumableId(key.Id))
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
            if (parent != null && IsConsumableId(parent.UpgradeId))
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

            // The repeatable turbo purchase lives in the fixed bar, so it
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
            ProgressionSystem.UpgradeId.PenBonus =>
                FindLast(ProgressionSystem.UpgradeId.EggValue),
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
        rect.anchoredPosition = new Vector2(0f, 126f);
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
            iconRect.sizeDelta = new Vector2(40f, 40f);
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
        icon.rectTransform.sizeDelta = new Vector2(34f, 34f);

        TMP_Text tier = transform.Find("Tier")?.GetComponent<TMP_Text>();
        if (tier != null)
        {
            tier.text = node.TargetLevel > 0 ? node.TargetLevel.ToString() : string.Empty;
            tier.color = new Color(1f, 0.91f, 0.65f, 1f);
            tier.fontSize = 9f;
            tier.rectTransform.anchoredPosition = new Vector2(0f, -20f);
        }

        Outline outline = node.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2f, -2f);
        }
    }

    private static void ApplyConsumableVisual(
        ProgressionNodeButton node,
        Texture2D iconAtlas,
        Color color)
    {
        ApplyCompactVisual(node, iconAtlas, color);
        Transform transform = node.transform;
        RawImage icon = transform.Find("Type Icon")?.GetComponent<RawImage>();
        if (icon != null)
        {
            icon.rectTransform.anchoredPosition = new Vector2(-205f, 0f);
            icon.rectTransform.sizeDelta = new Vector2(50f, 50f);
        }

        TMP_Text tier = transform.Find("Tier")?.GetComponent<TMP_Text>();
        if (tier != null)
        {
            tier.gameObject.SetActive(false);
        }

        TMP_Text label = CreateRuntimeText(
            "Consumable Label",
            transform,
            14f,
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
        label.rectTransform.anchoredPosition = new Vector2(35f, 0f);
        label.rectTransform.sizeDelta = new Vector2(390f, 56f);
    }

    private static bool IsConsumableId(ProgressionSystem.UpgradeId id)
    {
        return id is ProgressionSystem.UpgradeId.FoodBag
            or ProgressionSystem.UpgradeId.IncubatorTurbo;
    }

    private static bool IsRetiredTurboId(ProgressionSystem.UpgradeId id)
    {
        return id is ProgressionSystem.UpgradeId.CrosshatcherTurbo
            or ProgressionSystem.UpgradeId.CrosshatcherTurboPower
            or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration
            or ProgressionSystem.UpgradeId.RobotTurbo
            or ProgressionSystem.UpgradeId.RobotTurboPower
            or ProgressionSystem.UpgradeId.RobotTurboDuration;
    }

    private static int GetConsumableIndex(ProgressionSystem.UpgradeId id)
    {
        return id switch
        {
            ProgressionSystem.UpgradeId.FoodBag => 0,
            _ => 1
        };
    }

    private static float GetRevealDelay(Vector2 position)
    {
        float gridDistance = (Mathf.Abs(position.x) + Mathf.Abs(position.y))
            / GridSpacing;
        return Mathf.Min(0.22f, gridDistance * 0.011f);
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
            ProgressionSystem.UpgradeId.PenBonus =>
                new Vector2((18f + step) * s, -5f * s),
            ProgressionSystem.UpgradeId.IncubatorTurbo =>
                new Vector2(s, 3f * s),
            ProgressionSystem.UpgradeId.IncubatorTurboPower =>
                new Vector2((2f + step) * s, 4f * s),
            ProgressionSystem.UpgradeId.IncubatorTurboDuration =>
                new Vector2((2f + step) * s, 3f * s),
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
            ProgressionSystem.UpgradeId.PenBonus =>
                new Color(0.1f, 0.74f, 0.62f, 1f),
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
                or ProgressionSystem.UpgradeId.EggValue
                or ProgressionSystem.UpgradeId.PenBonus => 3,
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
