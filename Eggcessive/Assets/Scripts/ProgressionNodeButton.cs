using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class ProgressionNodeButton : MonoBehaviour
{
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

    public ProgressionSystem.UpgradeId UpgradeId => upgradeId;
    public int TargetLevel => targetLevel;
    public bool IsTierNode => targetLevel > 0;

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
        Refresh();
    }

    private void OnDisable()
    {
        button?.onClick.RemoveListener(Select);
        ProgressionSystem.Changed -= Refresh;
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
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

    public void Refresh()
    {
        ProgressionSystem progression = ProgressionSystem.Instance;

        if (progression == null)
        {
            return;
        }

        ProgressionSystem.NodeState state = GetNodeState();
        gameObject.SetActive(true);

        bool maxed = state.IsMaxed;
        bool affordable = state.Cost <= EggScoreHud.CurrentCents;
        bool unlocked = state.Visible && state.PrerequisiteMet;
        bool canPurchase = unlocked && !maxed && affordable;
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
                    ? $"OWNED {state.Level}"
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
            costText.text = maxed
                ? IsTierNode ? "OWNED" : "MAX"
                : $"{FormatMoney(state.Cost)}";
            costText.color = !unlocked && !maxed
                ? new Color(0.5f, 0.52f, 0.5f)
                : new Color(1f, 0.88f, 0.35f);
        }

        if (progressFill != null)
        {
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

    private void Select()
    {
        if (treePreview == null)
        {
            treePreview = GetComponentInParent<ProgressionTreePreview>(true);
        }

        treePreview?.Select(this);
    }

    private void HandleBalanceChanged(int _)
    {
        Refresh();
    }

    private string GetTierLabel()
    {
        return upgradeId switch
        {
            ProgressionSystem.UpgradeId.FeedSpeed => "FEED\nSPEED",
            ProgressionSystem.UpgradeId.RareEggChance => "PREMIUM\nEGGS",
            ProgressionSystem.UpgradeId.EggValue => "EGG\nVALUE",
            ProgressionSystem.UpgradeId.IncubatorCapacity =>
                $"CAPACITY\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.IncubatorSpeed =>
                $"HATCH RATE\nTIER {targetLevel}",
            ProgressionSystem.UpgradeId.BasketCapacity => "BASKET\nCAPACITY",
            ProgressionSystem.UpgradeId.VacuumPower => "VACUUM\nPOWER",
            ProgressionSystem.UpgradeId.VacuumRange => "VACUUM\nRANGE",
            ProgressionSystem.UpgradeId.RobotSpeed => "ROBOT\nSPEED",
            ProgressionSystem.UpgradeId.RobotCapacity => "ROBOT\nCAPACITY",
            ProgressionSystem.UpgradeId.RobotSmartness => "ROBOT\nLOGIC",
            _ => GetNodeState().Title.ToUpperInvariant()
        };
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100:N0}.{Mathf.Abs(cents % 100):D2}";
    }
}
