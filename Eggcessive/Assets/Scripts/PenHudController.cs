using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PenHudController : MonoBehaviour
{
    public static PenHudController Instance { get; private set; }

    [SerializeField] private RectTransform buttonContent = null;
    [SerializeField] private List<PenButtonView> authoredButtons =
        new List<PenButtonView>();

    private readonly List<PenButtonView> buttons = new List<PenButtonView>();
    private PenButtonView buyButton;
    private RectTransform panelRoot;
    private PenExpansionManager manager;
    private float rateRefreshTimer;

    private float buttonWidth = 64f;
    private float buttonHeight = 64f;
    private float buttonSpacing = 6f;
    private const float RateRefreshInterval = 0.25f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    private void Awake()
    {
        Instance = this;
        ConfigureHorizontalLayout();
        HideAuthoredButtonsForPlayMode();
        PenEquipmentHudController equipmentHud =
            GetComponent<PenEquipmentHudController>();
        if (equipmentHud == null)
        {
            Debug.LogError(
                $"{nameof(PenHudController)} on {name} is missing its "
                + "authored PenEquipmentHudController prefab component.",
                this);
            enabled = false;
            return;
        }

        PenButtonView firstButton = authoredButtons.Count > 0
            ? authoredButtons[0]
            : null;
        TMP_Text styleSource = firstButton != null
            ? firstButton.GetComponentInChildren<TMP_Text>(true)
            : null;
        equipmentHud.Initialize(transform, panelRoot, styleSource);
    }

    private void OnEnable()
    {
        EggScoreHud.BalanceChanged += HandleBalanceChanged;
        RoundSystem.PhaseChanged += HandleRoundPhaseChanged;
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
            return;
        }

        HandlePenCyclingInput();

        rateRefreshTimer -= Time.unscaledDeltaTime;
        if (rateRefreshTimer <= 0f)
        {
            rateRefreshTimer = RateRefreshInterval;
            RefreshOwnedButtons();
        }
    }

    private void OnDisable()
    {
        EggScoreHud.BalanceChanged -= HandleBalanceChanged;
        RoundSystem.PhaseChanged -= HandleRoundPhaseChanged;
        if (manager != null)
        {
            manager.StateChanged -= Refresh;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static void ShowPenEarnings(int penIndex, int cents)
    {
        if (Instance == null || cents <= 0)
        {
            return;
        }

        for (int index = 0; index < Instance.buttons.Count; index++)
        {
            PenButtonView view = Instance.buttons[index];
            if (view != null && view.PenIndex == penIndex)
            {
                view.ShowEarnings(cents);
                return;
            }
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

    public Button GetPurchaseButton()
    {
        return buyButton != null ? buyButton.Button : null;
    }

    private void HandlePenCyclingInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null
            || !keyboard.tabKey.wasPressedThisFrame
            || manager.OwnedPenCount <= 1
            || IsTextInputFocused())
        {
            return;
        }

        bool cycleBackward = keyboard.leftShiftKey.isPressed
            || keyboard.rightShiftKey.isPressed;
        int direction = cycleBackward ? -1 : 1;
        int penCount = manager.PenCount;
        int currentIndex = manager.FocusedPenIndex;
        for (int offset = 1; offset <= penCount; offset++)
        {
            int candidateIndex = (currentIndex + direction * offset)
                % penCount;
            if (candidateIndex < 0)
            {
                candidateIndex += penCount;
            }

            if (manager.IsPenOwned(candidateIndex))
            {
                manager.FocusPen(candidateIndex);
                return;
            }
        }
    }

    private static bool IsTextInputFocused()
    {
        GameObject selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject
            : null;
        if (selected == null)
        {
            return false;
        }

        TMP_InputField tmpInput = selected.GetComponentInParent<TMP_InputField>();
        if (tmpInput != null && tmpInput.isFocused)
        {
            return true;
        }

        InputField input = selected.GetComponentInParent<InputField>();
        return input != null && input.isFocused;
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
        buttons.Clear();
        buyButton = null;
        int nextIndex = manager.NextUnownedPenIndex;
        for (int index = 0; index < authoredButtons.Count; index++)
        {
            PenButtonView view = authoredButtons[index];
            if (view == null)
            {
                continue;
            }

            if (index >= manager.PenCount)
            {
                view.gameObject.SetActive(false);
            }
            else if (manager.IsPenOwned(index))
            {
                view.InitializePen(this, index);
                buttons.Add(view);
            }
            else if (index == nextIndex)
            {
                view.InitializePurchase(this, index);
                buyButton = view;
            }
            else
            {
                view.gameObject.SetActive(false);
            }
        }

        if (authoredButtons.Count < manager.PenCount)
        {
            Debug.LogError(
                $"Pen HUD has {authoredButtons.Count} authored buttons for "
                + $"{manager.PenCount} configured pens.",
                this);
        }

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

        RefreshOwnedButtons();

        int nextIndex = manager.NextUnownedPenIndex;
        if (buyButton != null && nextIndex >= 0)
        {
            int cost = manager.GetPenCostCents(nextIndex);
            bool purchaseAvailable = RoundSystem.Instance == null
                || RoundSystem.Instance.IsRoundInProgress;
            buyButton.RefreshPurchase(
                nextIndex,
                manager.AreAdditionalPensUnlocked,
                purchaseAvailable
                    && !manager.IsPenPurchaseInProgress
                    && manager.AreAdditionalPensUnlocked
                    && balance >= cost,
                cost,
                balance);
        }
    }

    private void HandleRoundPhaseChanged(RoundSystem.RoundPhase phase)
    {
        Refresh();
    }

    private void HandleBalanceChanged(long balance)
    {
        Refresh();
    }

    private void RefreshOwnedButtons()
    {
        if (manager == null)
        {
            return;
        }

        RoundSystem roundSystem = RoundSystem.Instance;
        for (int index = 0; index < buttons.Count; index++)
        {
            PenButtonView view = buttons[index];
            float eggsPerMinute = roundSystem != null
                ? roundSystem.GetPenEggsPerMinute(view.PenIndex)
                : 0f;
            view.RefreshOwned(
                view.PenIndex == manager.FocusedPenIndex,
                eggsPerMinute);
        }
    }

    private void ConfigureHorizontalLayout()
    {
        if (buttonContent == null)
        {
            return;
        }

        panelRoot = buttonContent.parent as RectTransform;
        if (panelRoot != null)
        {
            panelRoot.anchorMin = new Vector2(1f, 0f);
            panelRoot.anchorMax = new Vector2(1f, 0f);
            panelRoot.pivot = new Vector2(1f, 0f);
            panelRoot.anchoredPosition = new Vector2(-24f, 24f);
            panelRoot.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                buttonHeight);
        }

        buttonContent.anchorMin = Vector2.zero;
        buttonContent.anchorMax = Vector2.one;
        buttonContent.anchoredPosition = Vector2.zero;
        buttonContent.sizeDelta = Vector2.zero;

        GridLayoutGroup grid = buttonContent.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.enabled = false;
        }

        VerticalLayoutGroup layout =
            buttonContent.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.enabled = false;
        }

        HorizontalLayoutGroup horizontalLayout =
            buttonContent.GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayout == null)
        {
            horizontalLayout = buttonContent.gameObject
                .AddComponent<HorizontalLayoutGroup>();
        }

        horizontalLayout.padding = new RectOffset(0, 0, 0, 0);
        buttonSpacing = horizontalLayout.spacing;
        horizontalLayout.childAlignment = TextAnchor.MiddleRight;
        horizontalLayout.childControlWidth = false;
        horizontalLayout.childControlHeight = false;
        horizontalLayout.childForceExpandWidth = false;
        horizontalLayout.childForceExpandHeight = false;

        PenButtonView firstButton = authoredButtons.Count > 0
            ? authoredButtons[0]
            : null;
        RectTransform firstButtonRect = firstButton != null
            ? firstButton.GetComponent<RectTransform>()
            : null;
        if (firstButtonRect != null)
        {
            LayoutElement element = firstButtonRect.GetComponent<LayoutElement>();
            buttonWidth = element != null && element.preferredWidth > 0f
                ? element.preferredWidth
                : firstButtonRect.rect.width;
            buttonHeight = element != null && element.preferredHeight > 0f
                ? element.preferredHeight
                : firstButtonRect.rect.height;
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

        float buttonsWidth = visibleButtonCount > 0
            ? visibleButtonCount * buttonWidth
                + (visibleButtonCount - 1) * buttonSpacing
            : 0f;
        panelRoot.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            buttonsWidth);
        panelRoot.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            buttonHeight);
    }

    private void HideAuthoredButtonsForPlayMode()
    {
        for (int index = 0; index < authoredButtons.Count; index++)
        {
            if (authoredButtons[index] != null)
            {
                authoredButtons[index].gameObject.SetActive(false);
            }
        }
    }

}
