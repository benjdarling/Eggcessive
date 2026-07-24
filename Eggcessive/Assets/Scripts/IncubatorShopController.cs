using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class IncubatorShopController : MonoBehaviour
{
    [Header("Scene Incubator")]
    [SerializeField] private IncubatorController incubator = null;

    [Header("Authored HUD")]
    [SerializeField] private Button purchaseButton = null;
    [SerializeField] private TMP_Text levelText = null;
    [SerializeField] private TMP_Text detailsText = null;
    [SerializeField] private TMP_Text statusText = null;
    [SerializeField] private TMP_Text purchaseButtonText = null;
    [SerializeField] private Image affordabilityProgressFill = null;

    [Header("Prices")]
    [SerializeField, Min(1)] private int levelOneCostCents = 500;
    [SerializeField, Min(1)] private int levelTwoCostCents = 1500;

    private int PurchasedLevel =>
        incubator != null && incubator.gameObject.activeSelf
            ? incubator.CurrentLevel
            : 0;

    private void OnEnable()
    {
        if (purchaseButton != null)
        {
            purchaseButton.onClick.AddListener(PurchaseNextLevel);
        }

        EggScoreHud.BalanceChanged += HandleBalanceChanged;
    }

    private void Start()
    {
        RefreshUi();
    }

    private void OnDisable()
    {
        if (purchaseButton != null)
        {
            purchaseButton.onClick.RemoveListener(PurchaseNextLevel);
        }

        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
    }

    private void PurchaseNextLevel()
    {
        if (incubator == null)
        {
            SetStatus("Incubator is not connected");
            return;
        }

        int nextLevel = PurchasedLevel + 1;

        if (nextLevel > 2)
        {
            SetStatus("Maximum level");
            return;
        }

        int cost = GetCost(nextLevel);

        if (!EggScoreHud.TrySpendCents(cost))
        {
            SetStatus($"Need {FormatMoney(cost)}");
            return;
        }

        incubator.InstallOrUpgrade(nextLevel);
        SetStatus(nextLevel == 1 ? "Incubator installed" : "Incubator upgraded");
        RefreshUi();
    }

    private void HandleBalanceChanged(int _)
    {
        RefreshUi();
    }

    private void RefreshUi()
    {
        int level = PurchasedLevel;

        if (levelText != null)
        {
            levelText.text = level == 0 ? "NOT INSTALLED" : $"LEVEL {level}";
        }

        if (detailsText != null)
        {
            detailsText.text = level switch
            {
                0 => "Level 1  |  1 egg  |  30 sec",
                1 => "Next: 3 eggs  |  20 sec each",
                _ => "3 eggs  |  20 sec each"
            };
        }

        bool hasUpgrade = level < 2;
        int cost = hasUpgrade ? GetCost(level + 1) : 0;

        if (purchaseButton != null)
        {
            purchaseButton.interactable = hasUpgrade;
        }

        if (purchaseButtonText != null)
        {
            purchaseButtonText.text = hasUpgrade
                ? $"{(level == 0 ? "BUY" : "UPGRADE")}  {FormatMoney(cost)}"
                : "MAX LEVEL";
        }

        if (affordabilityProgressFill != null)
        {
            affordabilityProgressFill.fillAmount = hasUpgrade
                ? Mathf.Clamp01(EggScoreHud.CurrentCents / (float)cost)
                : 1f;
        }
    }

    private int GetCost(int level)
    {
        return level >= 2 ? levelTwoCostCents : levelOneCostCents;
    }

    private static string FormatMoney(int cents)
    {
        return $"${cents / 100}.{cents % 100:D2}";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void OnValidate()
    {
        levelOneCostCents = Mathf.Max(1, levelOneCostCents);
        levelTwoCostCents = Mathf.Max(1, levelTwoCostCents);
    }
}
