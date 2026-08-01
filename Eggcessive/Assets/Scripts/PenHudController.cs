using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PenHudController : MonoBehaviour
{
    [SerializeField] private RectTransform buttonContent;
    [SerializeField] private PenButtonView buttonTemplate;

    private readonly List<PenButtonView> buttons = new List<PenButtonView>();
    private PenButtonView buyButton;
    private RectTransform panelRoot;
    private PenExpansionManager manager;

    private const float ButtonHeight = 40f;
    private const float ButtonSpacing = 6f;
    private const float PanelChromeHeight = 42f;

    private void Awake()
    {
        ConfigureSingleColumnLayout();
    }

    private void OnEnable()
    {
        EggScoreHud.BalanceChanged += HandleBalanceChanged;
        if (manager != null)
        {
            manager.StateChanged -= Refresh;
            manager.StateChanged += Refresh;
            Refresh();
        }
    }

    private void Start()
    {
        TryBindManager();
    }

    private void Update()
    {
        if (manager == null)
        {
            TryBindManager();
        }
    }

    private void OnDisable()
    {
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        if (manager != null)
        {
            manager.StateChanged -= Refresh;
        }
    }

    public void ActivatePen(int index)
    {
        if (manager != null && manager.IsPenOwned(index))
        {
            manager.FocusPen(index);
        }
    }

    public void PurchaseNextPen()
    {
        manager?.TryPurchaseNextPen();
    }

    private void TryBindManager()
    {
        PenExpansionManager candidate = PenExpansionManager.Instance;
        if (candidate == null || !candidate.IsInitialized)
        {
            return;
        }

        if (manager != null)
        {
            manager.StateChanged -= Refresh;
        }

        manager = candidate;
        manager.StateChanged += Refresh;
        BuildButtons();
        Refresh();
    }

    private void BuildButtons()
    {
        for (int index = 0; index < buttons.Count; index++)
        {
            if (buttons[index] != null)
            {
                Destroy(buttons[index].gameObject);
            }
        }

        if (buyButton != null)
        {
            Destroy(buyButton.gameObject);
            buyButton = null;
        }

        buttons.Clear();
        for (int index = 0; index < manager.PenCount; index++)
        {
            if (!manager.IsPenOwned(index))
            {
                continue;
            }

            PenButtonView view = Instantiate(buttonTemplate, buttonContent);
            view.name = $"Pen {index + 1} Button";
            view.InitializePen(this, index);
            buttons.Add(view);
        }

        int nextIndex = manager.NextUnownedPenIndex;
        if (nextIndex >= 0)
        {
            buyButton = Instantiate(buttonTemplate, buttonContent);
            buyButton.name = "Buy New Pen Button";
            buyButton.InitializePurchase(this, nextIndex);
        }
        else
        {
            buyButton = null;
        }

        buttonTemplate.gameObject.SetActive(false);
        ResizePanel(buttons.Count + (buyButton != null ? 1 : 0));
    }

    private void Refresh()
    {
        if (manager == null)
        {
            return;
        }

        long balance = EggScoreHud.CurrentCents;
        if (buttons.Count != manager.OwnedPenCount)
        {
            BuildButtons();
        }

        for (int index = 0; index < buttons.Count; index++)
        {
            buttons[index].RefreshOwned(
                buttons[index].PenIndex == manager.FocusedPenIndex);
        }

        int nextIndex = manager.NextUnownedPenIndex;
        if (buyButton != null && nextIndex >= 0)
        {
            int cost = manager.GetPenCostCents(nextIndex);
            buyButton.RefreshPurchase(
                nextIndex,
                balance >= cost,
                cost,
                balance);
        }
    }

    private void HandleBalanceChanged(long balance)
    {
        Refresh();
    }

    private void ConfigureSingleColumnLayout()
    {
        if (buttonContent == null)
        {
            return;
        }

        panelRoot = buttonContent.parent as RectTransform;
        GridLayoutGroup grid = buttonContent.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.enabled = false;
        }

        VerticalLayoutGroup layout =
            buttonContent.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = buttonContent.gameObject
                .AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = ButtonSpacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        RectTransform templateRect = buttonTemplate != null
            ? buttonTemplate.GetComponent<RectTransform>()
            : null;
        if (templateRect != null)
        {
            templateRect.sizeDelta = new Vector2(0f, ButtonHeight);
            LayoutElement element =
                templateRect.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = templateRect.gameObject.AddComponent<LayoutElement>();
            }

            element.minHeight = ButtonHeight;
            element.preferredHeight = ButtonHeight;
            element.flexibleHeight = 0f;
        }
    }

    private void ResizePanel(int visibleButtonCount)
    {
        if (panelRoot == null)
        {
            panelRoot = buttonContent != null
                ? buttonContent.parent as RectTransform
                : null;
        }

        if (panelRoot == null)
        {
            return;
        }

        float buttonsHeight = visibleButtonCount > 0
            ? visibleButtonCount * ButtonHeight
                + (visibleButtonCount - 1) * ButtonSpacing
            : 0f;
        panelRoot.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            PanelChromeHeight + buttonsHeight);
    }
}
