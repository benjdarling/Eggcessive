using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class ProgressionNodeButton : MonoBehaviour
    , IPointerEnterHandler
    , IPointerExitHandler
    , IPointerDownHandler
    , IPointerUpHandler
{
    private const float HoverScale = 1.09f;
    private const float SelectedScale = 0.965f;
    private const float PressedScale = 0.88f;
    private const float HoverTilt = 2.25f;
    private const float RevealDuration = 0.3f;
    private const float RevealStartScale = 0.02f;
    private const float RevealStartRotation = 12f;

    [SerializeField] private ProgressionSystem.UpgradeId upgradeId;
    [SerializeField] private TMP_Text iconText = null;
    [SerializeField] private TMP_Text nodeText = null;
    [SerializeField] private TMP_Text costText = null;
    [SerializeField] private Image progressFill = null;
    [SerializeField] private Outline selectionOutline = null;
    [SerializeField, Min(0)] private int targetLevel;
    [SerializeField] private Color unlockedColor = new Color(0.22f, 0.55f, 0.3f);

    private Button button;
    private ProgressionTreePreview treePreview;
    private CanvasGroup graphCanvasGroup;
    private bool graphVisible = true;
    private Vector3 restingScale = Vector3.one;
    private Quaternion restingRotation = Quaternion.identity;
    private SpringUtils.FloatSpring scaleSpring =
        new SpringUtils.FloatSpring(1f);
    private SpringUtils.AngleSpring rotationSpring =
        new SpringUtils.AngleSpring(0f);
    private bool motionInitialized;
    private bool isHovered;
    private bool isPointerDown;
    private bool wasPinned;
    private float hoverDirection = 1f;
    private float revealDelay;
    private float revealDelayRemaining;
    private float revealElapsed;
    private float revealAlpha = 1f;
    private Color revealBaseColor = Color.white;
    private bool revealPending;
    private bool revealAnimating;

    public ProgressionSystem.UpgradeId UpgradeId => upgradeId;
    public int TargetLevel => targetLevel;
    public bool IsTierNode => targetLevel > 0;
    public bool IsGraphVisible => graphVisible;

    public ProgressionSystem.NodeState GetNodeState()
    {
        return ProgressionSystem.Instance != null
            ? ProgressionSystem.Instance.GetNodeState(upgradeId, targetLevel)
            : default;
    }

    private void Awake()
    {
        button = GetComponent<Button>();
        treePreview = GetComponentInParent<ProgressionTreePreview>(true);
        InitializeMotion();
    }

    private void OnEnable()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        button.onClick.AddListener(Select);
        ProgressionSystem.Changed += Refresh;
        EggScoreHud.BalanceChanged += HandleBalanceChanged;
        ResetMotion();
        Refresh();
    }

    private void OnDisable()
    {
        button?.onClick.RemoveListener(Select);
        ProgressionSystem.Changed -= Refresh;
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        CancelReveal();
        ResetMotion();
    }

    private void LateUpdate()
    {
        if (!motionInitialized)
        {
            InitializeMotion();
        }

        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        if (revealPending)
        {
            revealDelayRemaining -= Time.unscaledDeltaTime;
            if (revealDelayRemaining > 0f)
            {
                ApplyRevealVisual(0f, 0f);
                transform.localScale = Vector3.Scale(
                    restingScale,
                    Vector3.one * RevealStartScale);
                transform.localRotation = restingRotation
                    * Quaternion.Euler(
                        0f,
                        0f,
                        hoverDirection * RevealStartRotation);
                return;
            }

            BeginRevealMotion();
        }

        bool pinned = graphVisible
            && treePreview != null
            && treePreview.IsPinned(this);
        if (pinned != wasPinned)
        {
            scaleSpring.AddImpulse(pinned ? -0.8f : 0.62f);
            rotationSpring.AddImpulse(
                hoverDirection * (pinned ? -22f : 16f));
            wasPinned = pinned;
        }

        bool pressed = graphVisible && isPointerDown;
        bool hovered = graphVisible && isHovered && !pinned;
        float targetScale = pressed
            ? PressedScale
            : pinned
                ? SelectedScale
                : hovered
                    ? HoverScale
                    : 1f;
        float targetRotation = pressed
            ? -hoverDirection * 1.25f
            : hovered
                ? hoverDirection * HoverTilt
                : 0f;
        float frequency = revealAnimating
            ? 8f
            : pressed
                ? 17f
                : 7.5f;
        float damping = revealAnimating
            ? 0.34f
            : pressed
                ? 0.78f
                : 0.5f;
        SpringUtils.MotionParams motion = SpringUtils.CalculateMotionParams(
            deltaTime,
            frequency,
            damping);
        scaleSpring.Update(targetScale, 0f, deltaTime, motion);
        rotationSpring.Update(targetRotation, 0f, deltaTime, motion);
        scaleSpring.ClampValue(
            revealAnimating ? 0.01f : 0.84f,
            revealAnimating ? 1.34f : 1.18f);
        rotationSpring.ClampValue(
            revealAnimating ? -15f : -5f,
            revealAnimating ? 15f : 5f);
        transform.localScale = Vector3.Scale(
            restingScale,
            Vector3.one * scaleSpring.Value);
        transform.localRotation = restingRotation
            * Quaternion.Euler(0f, 0f, rotationSpring.Value);

        if (revealAnimating)
        {
            revealElapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(revealElapsed / RevealDuration);
            revealAlpha = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(progress / 0.42f));
            float whitePulse = Mathf.Pow(
                Mathf.Sin(progress * Mathf.PI),
                0.55f) * 1.45f;
            ApplyRevealVisual(revealAlpha, whitePulse);
            if (progress >= 1f)
            {
                revealAnimating = false;
                revealAlpha = 1f;
                ApplyRevealVisual(1f, 0f);
            }
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!graphVisible)
        {
            return;
        }

        isHovered = true;
        scaleSpring.AddImpulse(0.58f);
        rotationSpring.AddImpulse(hoverDirection * 32f);
        treePreview?.Preview(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPointerDown = false;
        scaleSpring.AddImpulse(-0.2f);
        treePreview?.ScheduleHide(0.1f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!graphVisible
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        isPointerDown = true;
        scaleSpring.AddImpulse(-1.2f);
        rotationSpring.AddImpulse(-hoverDirection * 38f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isPointerDown)
        {
            return;
        }

        isPointerDown = false;
        scaleSpring.AddImpulse(0.36f);
    }

    private void InitializeMotion()
    {
        restingScale = transform.localScale;
        restingRotation = transform.localRotation;
        hoverDirection = (GetInstanceID() & 1) == 0 ? 1f : -1f;
        motionInitialized = true;
        ResetMotion();
    }

    private void ResetMotion()
    {
        if (!motionInitialized)
        {
            return;
        }

        isHovered = false;
        isPointerDown = false;
        wasPinned = false;
        scaleSpring.Reset(1f);
        rotationSpring.Reset(0f);
        transform.localScale = restingScale;
        transform.localRotation = restingRotation;
    }

    public void Configure(
        ProgressionSystem.UpgradeId id,
        TMP_Text icon,
        TMP_Text label,
        TMP_Text price,
        Image fill,
        Outline outline,
        Color color,
        int purchaseTargetLevel = 0)
    {
        upgradeId = id;
        iconText = icon;
        nodeText = label;
        costText = price;
        progressFill = fill;
        selectionOutline = outline;
        unlockedColor = color;
        targetLevel = Mathf.Max(0, purchaseTargetLevel);
    }

    public void SetTargetLevel(int purchaseTargetLevel)
    {
        targetLevel = Mathf.Max(0, purchaseTargetLevel);
        Refresh();
    }

    public void SetUpgrade(
        ProgressionSystem.UpgradeId id,
        int purchaseTargetLevel = 0)
    {
        upgradeId = id;
        targetLevel = Mathf.Max(0, purchaseTargetLevel);
        Refresh();
    }

    public void SetVisualColor(Color color)
    {
        unlockedColor = color;
        Refresh();
    }

    public void SetRevealDelay(float delay)
    {
        revealDelay = Mathf.Max(0f, delay);
        if (revealPending)
        {
            revealDelayRemaining = revealDelay;
        }
    }

    public void SetGraphVisible(bool visible)
    {
        bool becameVisible = visible && !graphVisible;
        graphVisible = visible;
        EnsureGraphCanvasGroup();

        if (!visible)
        {
            CancelReveal();
            graphCanvasGroup.alpha = 0f;
        }
        else if (becameVisible)
        {
            QueueReveal();
        }
        else
        {
            graphCanvasGroup.alpha = revealPending || revealAnimating
                ? revealAlpha
                : 1f;
        }

        graphCanvasGroup.interactable = visible;
        graphCanvasGroup.blocksRaycasts = visible;
    }

    public void Refresh()
    {
        if (!enabled)
        {
            return;
        }

        if (IsLegacySidebarSourceInsideGraph())
        {
            gameObject.SetActive(false);
            return;
        }

        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (treePreview == null)
        {
            treePreview = GetComponentInParent<ProgressionTreePreview>(true);
        }

        if (button == null)
        {
            return;
        }

        ProgressionSystem progression = ProgressionSystem.Instance;

        if (progression == null)
        {
            return;
        }

        ProgressionSystem.NodeState state = GetNodeState();
        gameObject.SetActive(true);
        SetGraphVisible(graphVisible);

        bool maxed = state.IsMaxed;
        bool affordable = state.Cost <= EggScoreHud.CurrentCents;
        bool unlocked = state.Visible && state.PrerequisiteMet;
        bool ownershipOnly = !IsTierNode && state.MaximumLevel == 1;
        bool selected = treePreview != null && treePreview.IsSelected(this);
        button.interactable = true;
        Image background = button.targetGraphic as Image;
        Color baseColor = selected
            ? Color.Lerp(unlockedColor, Color.white, 0.48f)
            : maxed
                ? new Color(0.13f, 0.5f, 0.25f, 1f)
                : !unlocked
                    ? new Color(0.16f, 0.17f, 0.16f, 1f)
                    : affordable
                        ? unlockedColor
                        : Color.Lerp(
                            new Color(0.19f, 0.2f, 0.19f, 1f),
                            unlockedColor,
                            0.34f);

        if (background != null)
        {
            background.color = baseColor;
            revealBaseColor = baseColor;
        }

        ColorBlock colors = button.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.62f);
        colors.selectedColor = Color.Lerp(baseColor, Color.white, 0.72f);
        colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.28f);
        colors.colorMultiplier = 1.15f;
        colors.fadeDuration = 0.06f;
        button.colors = colors;

        if (selectionOutline != null)
        {
            selectionOutline.effectColor = selected
                ? new Color(1f, 0.86f, 0.3f, 1f)
                : new Color(0f, 0f, 0f, 0.48f);
            selectionOutline.effectDistance = selected
                ? new Vector2(3f, -3f)
                : new Vector2(1.5f, -1.5f);
        }

        if (iconText != null)
        {
            iconText.text = state.Icon;
            iconText.color = !unlocked
                ? new Color(0.48f, 0.5f, 0.48f)
                : maxed
                    ? new Color(1f, 0.86f, 0.3f)
                    : Color.white;
        }

        if (nodeText != null)
        {
            if (IsTierNode)
            {
                nodeText.text = $"<b>{GetTierLabel()}</b>";
            }
            else
            {
                string level = state.IsRepeatable
                    ? $"IN STOCK {state.Level}"
                    : state.MaximumLevel == 1
                        ? maxed ? "OWNED" : "UNLOCK"
                    : $"LV {state.Level}/{state.MaximumLevel}";
                nodeText.text =
                    $"<b>{state.Title}</b>\n" +
                    $"<size=10><color=#FFD95A>{level}</color></size>";
            }

            nodeText.color = unlocked || maxed
                ? Color.white
                : new Color(0.5f, 0.52f, 0.5f);
        }

        if (costText != null)
        {
            costText.gameObject.SetActive(!ownershipOnly || !maxed);
            costText.text = maxed
                ? IsTierNode ? "OWNED" : string.Empty
                : $"{FormatMoney(state.Cost)}";
            costText.color = !unlocked && !maxed
                ? new Color(0.5f, 0.52f, 0.5f)
                : new Color(1f, 0.88f, 0.35f);
        }

        if (progressFill != null)
        {
            bool showSavingsProgress = !ownershipOnly || !maxed;
            progressFill.transform.parent.gameObject.SetActive(showSavingsProgress);
            if (!showSavingsProgress)
            {
                return;
            }

            float progress = state.Cost > 0
                ? Mathf.Clamp01(EggScoreHud.CurrentCents / (float)state.Cost)
                : 1f;
            progressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
            progressFill.rectTransform.offsetMin = Vector2.zero;
            progressFill.rectTransform.offsetMax = Vector2.zero;
            progressFill.color = !unlocked
                ? new Color(0.3f, 0.31f, 0.3f)
                : new Color(1f, 0.73f, 0.16f);
        }
    }

    private bool IsLegacySidebarSourceInsideGraph()
    {
        bool repeatableSupply = upgradeId
            is ProgressionSystem.UpgradeId.FoodBag
            or ProgressionSystem.UpgradeId.IncubatorTurbo
            or ProgressionSystem.UpgradeId.CrosshatcherTurbo
            or ProgressionSystem.UpgradeId.RobotTurbo;
        return repeatableSupply
            && GetComponentInParent<SupplyShopGraphController>(true) != null;
    }

    private void Select()
    {
        if (treePreview == null)
        {
            treePreview = GetComponentInParent<ProgressionTreePreview>(true);
        }

        treePreview?.Select(this);
        scaleSpring.AddImpulse(-0.72f);
        rotationSpring.AddImpulse(-hoverDirection * 24f);
    }

    private void QueueReveal()
    {
        if (!graphVisible)
        {
            return;
        }

        revealPending = true;
        revealAnimating = false;
        revealDelayRemaining = revealDelay;
        revealElapsed = 0f;
        revealAlpha = 0f;
        scaleSpring.Reset(RevealStartScale);
        rotationSpring.Reset(hoverDirection * RevealStartRotation);
        ApplyRevealVisual(0f, 0f);
    }

    private void BeginRevealMotion()
    {
        revealPending = false;
        revealAnimating = true;
        revealElapsed = 0f;
        scaleSpring.Reset(RevealStartScale);
        scaleSpring.AddImpulse(5.5f);
        rotationSpring.Reset(hoverDirection * RevealStartRotation);
        rotationSpring.AddImpulse(-hoverDirection * 110f);
    }

    private void CancelReveal()
    {
        revealPending = false;
        revealAnimating = false;
        revealDelayRemaining = 0f;
        revealElapsed = 0f;
        revealAlpha = 1f;
        ApplyRevealVisual(graphVisible ? 1f : 0f, 0f);
    }

    private void ApplyRevealVisual(float alpha, float whitePulse)
    {
        EnsureGraphCanvasGroup();
        graphCanvasGroup.alpha = graphVisible ? alpha : 0f;
        Image background = button != null
            ? button.targetGraphic as Image
            : null;
        if (background != null)
        {
            background.color = Color.Lerp(
                revealBaseColor,
                Color.white,
                Mathf.Clamp01(whitePulse));
        }
    }

    private void EnsureGraphCanvasGroup()
    {
        if (graphCanvasGroup != null)
        {
            return;
        }

        graphCanvasGroup = GetComponent<CanvasGroup>();
        if (graphCanvasGroup == null)
        {
            graphCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void HandleBalanceChanged(long _)
    {
        Refresh();
    }

    private string GetTierLabel()
    {
        return upgradeId switch
        {
            ProgressionSystem.UpgradeId.FeedSpeed =>
                $"FEED SPEED\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.PrimeFeed =>
                $"PRIME FEED\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.RareEggChance =>
                $"PREMIUM EGGS\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.ChickenPerks =>
                $"CHICKEN PERKS\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.EggWeight =>
                $"EGG WEIGHT/SIZE\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.EggValue =>
                $"EGG VALUE\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.TruckBonus =>
                $"TRUCK BONUS\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.PenBonus =>
                $"PEN BONUS\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.IncubatorCapacity =>
                $"CAPACITY\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.IncubatorSpeed =>
                $"HATCH RATE\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.CrosshatcherSpeed =>
                $"SPEED\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.CrosshatcherQuality =>
                $"BREED QUALITY\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.BasketCapacity => "BASKET\nCAPACITY",
            ProgressionSystem.UpgradeId.BasketReach =>
                $"BASKET REACH\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.VacuumPower =>
                $"VACUUM POWER\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.VacuumRange =>
                $"VACUUM RANGE\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.RobotSpeed => "ROBOT\nSPEED",
            ProgressionSystem.UpgradeId.RobotCapacity => "ROBOT\nCAPACITY",
            ProgressionSystem.UpgradeId.RobotSmartness => "ROBOT\nLOGIC",
            ProgressionSystem.UpgradeId.IncubatorTurboPower
                or ProgressionSystem.UpgradeId.CrosshatcherTurboPower
                or ProgressionSystem.UpgradeId.RobotTurboPower =>
                $"BOOST +%\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.IncubatorTurboDuration
                or ProgressionSystem.UpgradeId.CrosshatcherTurboDuration
                or ProgressionSystem.UpgradeId.RobotTurboDuration =>
                $"DURATION\nTIER {targetLevel}",
            _ => GetNodeState().Title.ToUpperInvariant()
        };
    }

    private static string FormatMoney(long cents)
    {
        return $"${cents / 100:N0}.{System.Math.Abs(cents % 100):D2}";
    }
}
