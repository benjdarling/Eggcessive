using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class GameMenuController : MonoBehaviour
{
    private const string EggcessiveUnlockedKey =
        "Eggcessive.Menu.EggcessiveUnlocked";
    private const string MasterVolumeKey = "Eggcessive.Audio.MasterVolume";
    private const string ShowTutorialTipsKey =
        "Eggcessive.Menu.ShowTutorialTips";
    private const string MainMenuBackgroundResource = "UI/menu_logo_bg";
    private const string MainMenuChickenResource = "UI/menu_logo_chicken";
    private const string MainMenuPrefabResource = "UI/prefab_MainMenu";
    private const string RetirementPopupPrefabResource =
        "UI/prefab_EggcessiveUnlocked";
    private const string MainMenuTitleResource = "UI/menu_logo_title";
    private const string MainMenuSlogansResource = "MenuSlogans";
    private const int EggcessiveUnlockRound = 100;
    private const float DefaultMasterVolume = 0.8f;
    private const float MenuReferenceWidth = 1920f;

    private static readonly Color EggYellow =
        new Color(1f, 0.78f, 0.2f, 1f);
    private static readonly Color MenuBackground = Color.black;
    private static readonly Color PauseBackground =
        new Color(0.025f, 0.02f, 0.018f, 0.9f);
    private static readonly Color RetirementBackground =
        new Color(0.025f, 0.02f, 0.018f, 0.58f);
    private static readonly string[] GameplayTutorialTips =
    {
        "TIP: KEEP CHICKENS FED TO SPEED UP EGG PRODUCTION.",
        "TIP: WITH THE COLLECTION TOOL SELECTED, CLICK EGGS TO LOAD THE CONTAINER, THEN CLICK YOUR PEN'S EGG CONTAINER TO SELL THEM.",
        "TIP: FILLING A TRUCK EARNS A CASH BONUS AND SENDS A NEW ONE.",
        "TIP: USE INTERMISSIONS TO BUY UPGRADES AND SCALE THE FARM."
    };

    private Canvas canvas;
    private GameObject overlayRoot;
    private Image overlayBackground;
    private GameObject mainPanel;
    private GameObject playChoicePanel;
    private GameObject optionsPanel;
    private GameObject leaderboardsPanel;
    private GameObject pausePanel;
    private GameObject confirmationPanel;
    private GameObject retirementPanel;
    private AudioSource retirementVictoryAudio;
    private EggcessiveUnlockedPopupEffects retirementPopupEffects;
    private GameObject currentPanel;
    private GameObject optionsReturnPanel;
    private GameObject confirmationReturnPanel;
    private GameObject eggcessiveModeBadge;
    private GameObject gameplayTipBanner;
    private TMP_Text eggcessiveButtonText;
    private TMP_Text eggcessiveLockText;
    private TMP_Text mainMenuSloganText;
    private TMP_Text showTipsToggleText;
    private TMP_Text gameplayTipText;
    private Button eggcessiveButton;
    private TMP_Text volumeValueText;
    private Slider volumeSlider;
    private TMP_Text confirmationTitleText;
    private TMP_Text confirmationBodyText;
    private TMP_Text confirmationAcceptText;
    private Button confirmationAcceptButton;
    private Action confirmationAction;
    private bool gameplaySuspended = true;
    private bool showMainAfterSceneLoad;
    private bool retirementPromptShown;
    private bool showTutorialTips = true;
    private Coroutine gameplayTipsCoroutine;
    private Coroutine mainMenuEntranceCoroutine;
    private CanvasGroup mainMenuCanvasGroup;
    private float mainMenuCanvasAlpha = 1f;
    private bool mainMenuCanvasInteractable = true;
    private bool mainMenuCanvasBlocksRaycasts = true;
    private RoundSystem pausedRoundSystem;
    private string[] mainMenuSlogans = Array.Empty<string>();
    private string currentMainMenuSlogan;

    public static GameMenuController Instance { get; private set; }
    public static bool IsEggcessiveMode { get; private set; }
    public static bool IsEggcessiveUnlocked =>
        PlayerPrefs.GetInt(EggcessiveUnlockedKey, 0) != 0;
    public static bool ShowTutorialTips => Instance != null
        ? Instance.showTutorialTips
        : PlayerPrefs.GetInt(ShowTutorialTipsKey, 1) != 0;
    public static float MasterVolume => AudioListener.volume;
    public bool IsMenuOpen => overlayRoot != null && overlayRoot.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        IsEggcessiveMode = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateBeforeSceneLoad()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject root = new GameObject(nameof(GameMenuController));
        DontDestroyOnLoad(root);
        root.AddComponent<GameMenuController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        ApplySavedVolume();
        showTutorialTips = PlayerPrefs.GetInt(ShowTutorialTipsKey, 1) != 0;
        BuildUi();
        ShowMainMenu();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        RoundSystem.PhaseChanged += HandleRoundPhaseChanged;
    }

    private void Start()
    {
        EnsureEventSystem();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        RoundSystem.PhaseChanged -= HandleRoundPhaseChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

    private void Update()
    {
        ApplyPauseToCurrentRoundSystem();

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (keyboard.f10Key.wasPressedThisFrame
            && RoundSystem.Instance != null
            && currentPanel == null)
        {
            ShowRetirementPopup();
            return;
        }
#endif

        if (!keyboard.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (!IsMenuOpen)
        {
            // Escape remains the placement-cancel shortcut while a food bag
            // is active. The placement controller consumes it this frame.
            if (!FoodShopController.IsPlacementActive)
            {
                UiSoundController.PlayBack();
                ShowPauseMenu();
            }

            return;
        }

        HandleMenuBack();
    }

    private void BuildUi()
    {
        GameObject canvasObject = new GameObject(
            "Game Menus",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        overlayRoot = CreateUiObject("Menu Overlay", canvasObject.transform);
        StretchToParent(overlayRoot.GetComponent<RectTransform>());
        overlayBackground = overlayRoot.AddComponent<Image>();
        overlayBackground.color = MenuBackground;

        mainPanel = BuildMainPanel();
        playChoicePanel = BuildPlayChoicePanel();
        optionsPanel = BuildOptionsPanel();
        leaderboardsPanel = BuildLeaderboardsPanel();
        pausePanel = BuildPausePanel();
        confirmationPanel = BuildConfirmationPanel();
        retirementPanel = BuildRetirementPanel();
        eggcessiveModeBadge = BuildModeBadge(canvasObject.transform);
        gameplayTipBanner = BuildGameplayTipBanner(canvasObject.transform);
    }

    private GameObject BuildMainPanel()
    {
        GameObject prefab = Resources.Load<GameObject>(MainMenuPrefabResource);
        if (prefab != null)
        {
            bool ownsCanvas = prefab.GetComponent<Canvas>() != null;
            GameObject prefabPanel = Instantiate(
                prefab,
                ownsCanvas ? transform : overlayRoot.transform,
                false);
            prefabPanel.name = "Main Menu";
            prefabPanel.SetActive(false);
            if (TryBindMainMenuPrefab(prefabPanel))
            {
                return prefabPanel;
            }

            Debug.LogError(
                "The editable main-menu prefab is missing required named UI objects. "
                + "Using the generated fallback menu instead.");
            Destroy(prefabPanel);
        }

        return BuildGeneratedMainPanel();
    }

    private bool TryBindMainMenuPrefab(GameObject panel)
    {
        Transform root = panel.transform;
        mainMenuSloganText = FindMainMenuElement(root, "Subtitle")
            ?.GetComponent<TMP_Text>();
        eggcessiveLockText = FindMainMenuElement(root, "Eggcessive Lock Note")
            ?.GetComponent<TMP_Text>();

        Button playButton = FindMainMenuButton(root, "PLAY Button");
        eggcessiveButton = FindMainMenuButton(root, "EGGCESSIVE Button");
        Button optionsButton = FindMainMenuButton(root, "OPTIONS Button");
        Button leaderboardsButton = FindMainMenuButton(
            root,
            "LEADERBOARDS Button");
        Button quitButton = FindMainMenuButton(root, "QUIT Button");
        eggcessiveButtonText = eggcessiveButton
            ?.GetComponentInChildren<TMP_Text>(true);

        if (mainMenuSloganText == null
            || eggcessiveLockText == null
            || playButton == null
            || eggcessiveButton == null
            || eggcessiveButtonText == null
            || optionsButton == null
            || leaderboardsButton == null
            || quitButton == null)
        {
            return false;
        }

        BindMainMenuButton(playButton, () =>
            ShowPanel(playChoicePanel, false));
        BindMainMenuButton(eggcessiveButton, () => BeginGameplay(true));
        BindMainMenuButton(optionsButton, () => ShowOptions(mainPanel));
        BindMainMenuButton(leaderboardsButton, () =>
            ShowPanel(leaderboardsPanel, false));
        BindMainMenuButton(quitButton, ConfirmQuitApplication);
        return true;
    }

    private static Button FindMainMenuButton(Transform root, string name)
    {
        return FindMainMenuElement(root, name)?.GetComponent<Button>();
    }

    private static Transform FindMainMenuElement(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name)
            {
                return child;
            }

            Transform nested = FindMainMenuElement(child, name);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void BindMainMenuButton(
        Button button,
        UnityEngine.Events.UnityAction action)
    {
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        SpringMenuButton springButton =
            button.GetComponent<SpringMenuButton>();
        if (springButton == null)
        {
            springButton = button.gameObject.AddComponent<SpringMenuButton>();
        }

        springButton.Initialize(button, label, action);
    }

    private GameObject BuildGeneratedMainPanel()
    {
        GameObject panel = CreatePanel("Main Menu");
        CreateMainMenuArtwork(panel.transform);
        mainMenuSloganText = CreateText(
            "Subtitle",
            panel.transform,
            string.Empty,
            24f,
            new Vector2(0f, -15f),
            new Vector2(900f, 45f),
            new Color(0.88f, 0.8f, 0.64f),
            FontStyles.Bold);

        CreateMenuButton(panel.transform, "PLAY", -95f, () =>
            ShowPanel(playChoicePanel, false));
        eggcessiveButton = CreateMenuButton(
            panel.transform,
            "EGGCESSIVE",
            -180f,
            () => BeginGameplay(true));
        eggcessiveButtonText = eggcessiveButton
            .GetComponentInChildren<TMP_Text>(true);
        eggcessiveLockText = CreateText(
            "Eggcessive Lock Note",
            panel.transform,
            "LOCKED - REACH LEVEL 100",
            18f,
            new Vector2(0f, -222f),
            new Vector2(900f, 28f),
            new Color(0.6f, 0.55f, 0.47f),
            FontStyles.Bold);
        CreateMenuButton(panel.transform, "OPTIONS", -270f, () =>
            ShowOptions(mainPanel));
        CreateMenuButton(panel.transform, "LEADERBOARDS", -355f, () =>
            ShowPanel(leaderboardsPanel, false));
        CreateMenuButton(panel.transform, "QUIT", -440f, ConfirmQuitApplication);
        return panel;
    }

    private static void CreateMainMenuArtwork(Transform parent)
    {
        CreateMainMenuArtworkLayer(
            parent,
            "Menu Background",
            MainMenuBackgroundResource,
            true);
        CreateMainMenuArtworkLayer(
            parent,
            "Menu Chicken",
            MainMenuChickenResource,
            false);
        CreateMainMenuArtworkLayer(
            parent,
            "Menu Title",
            MainMenuTitleResource,
            false);
    }

    private static void CreateMainMenuArtworkLayer(
        Transform parent,
        string objectName,
        string resourcePath,
        bool fillCanvasWidth)
    {
        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
        {
            Debug.LogWarning(
                $"Main-menu artwork was not found at Resources/{resourcePath}.");
            return;
        }

        GameObject layerObject = CreateUiObject(objectName, parent);
        RectTransform rect = layerObject.GetComponent<RectTransform>();
        float canvasWidthFraction = fillCanvasWidth
            ? 1f
            : texture.width / MenuReferenceWidth;
        float halfWidth = canvasWidthFraction * 0.5f;
        rect.anchorMin = new Vector2(0.5f - halfWidth, 1f);
        rect.anchorMax = new Vector2(0.5f + halfWidth, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        AspectRatioFitter aspectFitter =
            layerObject.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        aspectFitter.aspectRatio = texture.width / (float)texture.height;

        RawImage layer = layerObject.AddComponent<RawImage>();
        layer.texture = texture;
        layer.color = Color.white;
        layer.raycastTarget = false;
    }

    private GameObject BuildPlayChoicePanel()
    {
        GameObject panel = CreatePanel("Play Choice");
        CreateHeading(panel.transform, "PLAY", 92f, 270f);
        CreateText(
            "Question",
            panel.transform,
            "SHOW EXTRA GUIDANCE WHILE YOU PLAY.",
            30f,
            new Vector2(0f, 155f),
            new Vector2(1100f, 55f),
            Color.white,
            FontStyles.Bold);
        Button showTipsToggle = CreateMenuButton(
            panel.transform,
            GetShowTipsToggleLabel(),
            35f,
            ToggleTutorialTips,
            30f);
        showTipsToggle.name = "SHOW TIPS Toggle";
        showTipsToggleText = showTipsToggle
            .GetComponentInChildren<TMP_Text>(true);
        CreateMenuButton(panel.transform, "START PLAYING", -65f,
            StartStandardGameplay);
        CreateMenuButton(panel.transform, "BACK", -215f, ShowMainMenu, 34f);
        return panel;
    }

    private void ToggleTutorialTips()
    {
        showTutorialTips = !showTutorialTips;
        PlayerPrefs.SetInt(ShowTutorialTipsKey, showTutorialTips ? 1 : 0);
        PlayerPrefs.Save();
        RefreshShowTipsToggle();
    }

    private void StartStandardGameplay()
    {
        BeginGameplay(false);
    }

    private string GetShowTipsToggleLabel()
    {
        return showTutorialTips ? "[X]  SHOW TIPS" : "[ ]  SHOW TIPS";
    }

    private void RefreshShowTipsToggle()
    {
        if (showTipsToggleText != null)
        {
            showTipsToggleText.text = GetShowTipsToggleLabel();
        }
    }

    private GameObject BuildOptionsPanel()
    {
        GameObject panel = CreatePanel("Options");
        CreateHeading(panel.transform, "OPTIONS", 92f, 285f);
        CreateText(
            "Master Volume Label",
            panel.transform,
            "MASTER VOLUME",
            34f,
            new Vector2(0f, 115f),
            new Vector2(760f, 55f),
            Color.white,
            FontStyles.Bold);

        volumeSlider = CreateSlider(
            panel.transform,
            new Vector2(0f, 20f),
            new Vector2(640f, 34f));
        volumeValueText = CreateText(
            "Master Volume Value",
            panel.transform,
            "80%",
            34f,
            new Vector2(0f, -55f),
            new Vector2(300f, 50f),
            EggYellow,
            FontStyles.Bold);
        volumeSlider.SetValueWithoutNotify(AudioListener.volume);
        volumeSlider.onValueChanged.AddListener(SetMasterVolume);
        RefreshVolumeText();
        CreateMenuButton(panel.transform, "BACK", -245f, ReturnFromOptions, 34f);
        return panel;
    }

    private GameObject BuildLeaderboardsPanel()
    {
        GameObject panel = CreatePanel("Leaderboards");
        CreateHeading(panel.transform, "LEADERBOARDS", 82f, 260f);
        CreateMenuButton(panel.transform, "BACK", -285f, ShowMainMenu, 34f);
        return panel;
    }

    private GameObject BuildPausePanel()
    {
        GameObject panel = CreatePanel("Pause Menu");
        CreateHeading(panel.transform, "PAUSED", 96f, 275f);
        CreateMenuButton(panel.transform, "RESUME", 75f, ResumeGameplay);
        CreateMenuButton(panel.transform, "OPTIONS", -25f, () =>
            ShowOptions(pausePanel));
        CreateMenuButton(panel.transform, "QUIT", -125f, ConfirmQuitToMain);
        return panel;
    }

    private GameObject BuildConfirmationPanel()
    {
        GameObject panel = CreatePanel("Confirmation");
        confirmationTitleText = CreateHeading(
            panel.transform,
            "ARE YOU SURE?",
            78f,
            245f);
        confirmationBodyText = CreateText(
            "Confirmation Message",
            panel.transform,
            string.Empty,
            28f,
            new Vector2(0f, 75f),
            new Vector2(900f, 150f),
            Color.white,
            FontStyles.Normal);
        confirmationBodyText.textWrappingMode = TextWrappingModes.Normal;
        confirmationAcceptButton = CreateMenuButton(
            panel.transform,
            "CONFIRM",
            -75f,
            AcceptConfirmation);
        confirmationAcceptText = confirmationAcceptButton
            .GetComponentInChildren<TMP_Text>(true);
        CreateMenuButton(panel.transform, "CANCEL", -185f, CancelConfirmation, 34f);
        return panel;
    }

    private GameObject BuildRetirementPanel()
    {
        GameObject prefab = Resources.Load<GameObject>(
            RetirementPopupPrefabResource);
        if (prefab != null)
        {
            bool ownsCanvas = prefab.GetComponent<Canvas>() != null;
            GameObject prefabPanel = Instantiate(
                prefab,
                ownsCanvas ? transform : overlayRoot.transform,
                false);
            prefabPanel.name = "Eggcessive Unlocked";
            prefabPanel.SetActive(false);

            if (!ownsCanvas)
            {
                StretchToParent(prefabPanel.GetComponent<RectTransform>());
            }

            if (TryBindRetirementPanelPrefab(prefabPanel))
            {
                return prefabPanel;
            }

            Debug.LogError(
                "The editable Eggcessive-unlocked popup prefab is missing "
                + "its RETIRE/CONTINUE buttons or victory AudioSource. "
                + "Using the generated fallback popup instead.");
            Destroy(prefabPanel);
        }

        return BuildGeneratedRetirementPanel();
    }

    private bool TryBindRetirementPanelPrefab(GameObject panel)
    {
        Button retireButton = FindMainMenuButton(
            panel.transform,
            "RETIRE Button");
        Button continueButton = FindMainMenuButton(
            panel.transform,
            "CONTINUE Button");
        retirementVictoryAudio = panel.GetComponent<AudioSource>();
        retirementPopupEffects =
            panel.GetComponent<EggcessiveUnlockedPopupEffects>();

        if (retireButton == null
            || continueButton == null
            || retirementVictoryAudio == null
            || retirementVictoryAudio.clip == null
            || retirementPopupEffects == null
            || !retirementPopupEffects.IsConfigured)
        {
            retirementVictoryAudio = null;
            retirementPopupEffects = null;
            return false;
        }

        BindMainMenuButton(retireButton, ReturnToMainMenu);
        BindMainMenuButton(continueButton, ContinueAfterUnlock);
        return true;
    }

    private GameObject BuildGeneratedRetirementPanel()
    {
        GameObject panel = CreatePanel("Eggcessive Unlocked");
        CreateHeading(panel.transform, "EGGCESSIVE UNLOCKED", 76f, 285f);
        TMP_Text body = CreateText(
            "Retirement Message",
            panel.transform,
            "LEVEL 100 COMPLETE. YOUR LEGACY IS SECURE.\n\n" +
            "RETIRE TO THE MAIN MENU TO BEGIN EGGCESSIVE MODE,\n" +
            "OR CONTINUE PUSHING THIS FARM BEYOND REASON.",
            28f,
            new Vector2(0f, 80f),
            new Vector2(1050f, 230f),
            Color.white,
            FontStyles.Normal);
        body.textWrappingMode = TextWrappingModes.Normal;
        CreateMenuButton(panel.transform, "RETIRE", -105f, ReturnToMainMenu);
        CreateMenuButton(panel.transform, "CONTINUE", -205f, ContinueAfterUnlock);
        return panel;
    }

    private GameObject BuildModeBadge(Transform parent)
    {
        GameObject badge = CreateUiObject("Eggcessive Mode Badge", parent);
        RectTransform rect = badge.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -24f);
        rect.sizeDelta = new Vector2(320f, 52f);
        Image background = badge.AddComponent<Image>();
        background.color = new Color(0.12f, 0.055f, 0.02f, 0.88f);
        background.raycastTarget = false;
        TMP_Text label = CreateText(
            "Label",
            badge.transform,
            "EGGCESSIVE MODE",
            24f,
            Vector2.zero,
            rect.sizeDelta,
            EggYellow,
            FontStyles.Bold);
        StretchToParent(label.rectTransform);
        badge.SetActive(false);
        return badge;
    }

    private GameObject BuildGameplayTipBanner(Transform parent)
    {
        GameObject banner = CreateUiObject("Gameplay Tutorial Tip", parent);
        RectTransform rect = banner.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -34f);
        rect.sizeDelta = new Vector2(1120f, 74f);

        Image background = banner.AddComponent<Image>();
        background.color = new Color(0.055f, 0.035f, 0.018f, 0.92f);
        background.raycastTarget = false;
        gameplayTipText = CreateText(
            "Tip Text",
            banner.transform,
            string.Empty,
            25f,
            Vector2.zero,
            rect.sizeDelta,
            EggYellow,
            FontStyles.Bold);
        StretchToParent(gameplayTipText.rectTransform);
        gameplayTipText.margin = new Vector4(28f, 8f, 28f, 8f);
        banner.SetActive(false);
        return banner;
    }

    private void StartGameplayTips()
    {
        StopGameplayTips();
        if (!showTutorialTips || gameplayTipBanner == null)
        {
            return;
        }

        gameplayTipsCoroutine = StartCoroutine(RunGameplayTips());
    }

    private void StopGameplayTips()
    {
        if (gameplayTipsCoroutine != null)
        {
            StopCoroutine(gameplayTipsCoroutine);
            gameplayTipsCoroutine = null;
        }

        gameplayTipBanner?.SetActive(false);
    }

    private IEnumerator RunGameplayTips()
    {
        yield return WaitForActiveGameplay(1.5f);
        foreach (string tip in GameplayTutorialTips)
        {
            gameplayTipText.text = tip;
            gameplayTipBanner.SetActive(true);
            yield return WaitForActiveGameplay(5.5f, true);
            gameplayTipBanner.SetActive(false);
            yield return WaitForActiveGameplay(2f);
        }

        gameplayTipsCoroutine = null;
    }

    private IEnumerator WaitForActiveGameplay(
        float duration,
        bool keepTipVisible = false)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            bool activeGameplay = !gameplaySuspended && !IsMenuOpen;
            if (keepTipVisible && gameplayTipBanner != null)
            {
                gameplayTipBanner.SetActive(activeGameplay);
            }

            if (activeGameplay)
            {
                elapsed += Time.unscaledDeltaTime;
            }

            yield return null;
        }
    }

    private void ShowMainMenu()
    {
        StopGameplayTips();
        IsEggcessiveMode = false;
        retirementPromptShown = false;
        RefreshEggcessiveButton();
        RefreshMainMenuSlogan();
        eggcessiveModeBadge?.SetActive(false);
        ShowPanel(mainPanel, false);
        BeginMainMenuEntrance();
    }

    private void BeginMainMenuEntrance()
    {
        StopMainMenuEntrance();
        if (mainPanel == null || !mainPanel.activeInHierarchy)
        {
            return;
        }

        mainMenuEntranceCoroutine = StartCoroutine(AnimateMainMenuEntrance());
    }

    private IEnumerator AnimateMainMenuEntrance()
    {
        mainMenuCanvasGroup = mainPanel.GetComponent<CanvasGroup>();
        if (mainMenuCanvasGroup == null)
        {
            mainMenuCanvasGroup = mainPanel.AddComponent<CanvasGroup>();
        }

        mainMenuCanvasAlpha = mainMenuCanvasGroup.alpha;
        mainMenuCanvasInteractable = mainMenuCanvasGroup.interactable;
        mainMenuCanvasBlocksRaycasts = mainMenuCanvasGroup.blocksRaycasts;
        mainMenuCanvasGroup.alpha = 0f;
        mainMenuCanvasGroup.interactable = false;
        mainMenuCanvasGroup.blocksRaycasts = false;

        const float fadeDuration = 0.2f;
        PlayMainMenuButtonEntrance(fadeDuration);

        // Guarantee that the fully black starting pose reaches the renderer.
        yield return null;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float progress = Mathf.Clamp01(elapsed / fadeDuration);
            mainMenuCanvasGroup.alpha = mainMenuCanvasAlpha
                * Mathf.SmoothStep(0f, 1f, progress);
            yield return null;
        }

        RestoreMainMenuCanvasGroup();
        mainMenuEntranceCoroutine = null;
    }

    private void PlayMainMenuButtonEntrance(float initialDelay = 0f)
    {
        if (mainPanel == null || !mainPanel.activeInHierarchy)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        SpringMenuButton[] buttons =
            mainPanel.GetComponentsInChildren<SpringMenuButton>(false);
        Array.Sort(
            buttons,
            (left, right) => right.transform.position.y.CompareTo(
                left.transform.position.y));
        for (int index = 0; index < buttons.Length; index++)
        {
            float direction = (index & 1) == 0 ? -1f : 1f;
            buttons[index].PlayEntrance(
                initialDelay + index * 0.085f,
                direction);
        }
    }

    private void StopMainMenuEntrance()
    {
        if (mainMenuEntranceCoroutine != null)
        {
            StopCoroutine(mainMenuEntranceCoroutine);
            mainMenuEntranceCoroutine = null;
        }

        RestoreMainMenuCanvasGroup();
    }

    private void RestoreMainMenuCanvasGroup()
    {
        if (mainMenuCanvasGroup == null)
        {
            return;
        }

        mainMenuCanvasGroup.alpha = mainMenuCanvasAlpha;
        mainMenuCanvasGroup.interactable = mainMenuCanvasInteractable;
        mainMenuCanvasGroup.blocksRaycasts = mainMenuCanvasBlocksRaycasts;
    }

    private void ShowPauseMenu()
    {
        ShowPanel(pausePanel, true);
    }

    private void ShowOptions(GameObject returnPanel)
    {
        optionsReturnPanel = returnPanel;
        volumeSlider.SetValueWithoutNotify(AudioListener.volume);
        RefreshVolumeText();
        ShowPanel(optionsPanel, returnPanel == pausePanel);
    }

    private void ReturnFromOptions()
    {
        GameObject target = optionsReturnPanel != null
            ? optionsReturnPanel
            : mainPanel;
        ShowPanel(target, target == pausePanel);
    }

    private void ShowPanel(GameObject panel, bool pauseStyle)
    {
        if (panel == null)
        {
            return;
        }

        if (panel != mainPanel)
        {
            StopMainMenuEntrance();
        }

        overlayRoot.SetActive(true);
        overlayBackground.color = pauseStyle ? PauseBackground : MenuBackground;
        gameplayTipBanner?.SetActive(false);
        mainPanel?.SetActive(false);
        playChoicePanel?.SetActive(false);
        optionsPanel?.SetActive(false);
        leaderboardsPanel?.SetActive(false);
        pausePanel?.SetActive(false);
        confirmationPanel?.SetActive(false);
        retirementPanel?.SetActive(false);
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        currentPanel = panel;
        SetGameplaySuspended(true);
    }

    private void BeginGameplay(bool eggcessiveMode)
    {
        IsEggcessiveMode = eggcessiveMode && IsEggcessiveUnlocked;
        retirementPromptShown = IsEggcessiveUnlocked;
        RoundSystem.Instance?.SetEggcessiveHudLayout(IsEggcessiveMode);
        mainPanel?.SetActive(false);
        overlayRoot.SetActive(false);
        currentPanel = null;
        eggcessiveModeBadge.SetActive(IsEggcessiveMode);
        SetGameplaySuspended(false);
        RoundSystem.Instance?.ReplayActivePopupIntro();
        StartGameplayTips();
    }

    private void ResumeGameplay()
    {
        mainPanel?.SetActive(false);
        overlayRoot.SetActive(false);
        currentPanel = null;
        SetGameplaySuspended(false);
    }

    private void HandleMenuBack()
    {
        if (currentPanel == pausePanel)
        {
            UiSoundController.PlayBack();
            ResumeGameplay();
        }
        else if (currentPanel == optionsPanel)
        {
            UiSoundController.PlayBack();
            ReturnFromOptions();
        }
        else if (currentPanel == leaderboardsPanel
            || currentPanel == playChoicePanel)
        {
            UiSoundController.PlayBack();
            ShowMainMenu();
        }
        else if (currentPanel == confirmationPanel)
        {
            UiSoundController.PlayBack();
            CancelConfirmation();
        }
    }

    private void ConfirmQuitApplication()
    {
        ShowConfirmation(
            "QUIT EGGCESSIVE?",
            "YOUR CURRENT RUN WILL END AND THE GAME WILL CLOSE.",
            "QUIT",
            mainPanel,
            QuitApplication,
            false);
    }

    private void ConfirmQuitToMain()
    {
        ShowConfirmation(
            "QUIT TO MAIN MENU?",
            "PROGRESS FROM THIS RUN WILL BE LOST.",
            "QUIT",
            pausePanel,
            ReturnToMainMenu,
            true);
    }

    private void ShowConfirmation(
        string title,
        string body,
        string acceptLabel,
        GameObject returnPanel,
        Action action,
        bool pauseStyle)
    {
        confirmationTitleText.text = title;
        confirmationBodyText.text = body;
        confirmationAcceptText.text = acceptLabel;
        confirmationReturnPanel = returnPanel;
        confirmationAction = action;
        ShowPanel(confirmationPanel, pauseStyle);
    }

    private void AcceptConfirmation()
    {
        Action action = confirmationAction;
        confirmationAction = null;
        action?.Invoke();
    }

    private void CancelConfirmation()
    {
        confirmationAction = null;
        GameObject target = confirmationReturnPanel != null
            ? confirmationReturnPanel
            : mainPanel;
        ShowPanel(target, target == pausePanel);
    }

    private void ReturnToMainMenu()
    {
        IsEggcessiveMode = false;
        showMainAfterSceneLoad = true;
        SetGameplaySuspended(true);
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(activeScene.buildIndex);
        }
        else
        {
            SceneManager.LoadScene(activeScene.name);
        }
    }

    private void ContinueAfterUnlock()
    {
        mainPanel?.SetActive(false);
        retirementPanel?.SetActive(false);
        overlayRoot.SetActive(false);
        currentPanel = null;
        SetGameplaySuspended(false);
    }

    private void QuitApplication()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetGameplaySuspended(bool suspended)
    {
        gameplaySuspended = suspended;
        Time.timeScale = suspended ? 0f : 1f;
        AudioListener.pause = suspended;
        ApplyPauseToCurrentRoundSystem();
    }

    private void ApplyPauseToCurrentRoundSystem()
    {
        RoundSystem roundSystem = RoundSystem.Instance;
        if (pausedRoundSystem != null && pausedRoundSystem != roundSystem)
        {
            pausedRoundSystem = null;
        }

        if (roundSystem == null)
        {
            return;
        }

        roundSystem.SetExternalPause(gameplaySuspended);
        pausedRoundSystem = gameplaySuspended ? roundSystem : null;
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        pausedRoundSystem = null;
        EnsureEventSystem();
        WorldHandCursorController.EnsureCursorExists();
        if (showMainAfterSceneLoad)
        {
            showMainAfterSceneLoad = false;
            ShowMainMenu();
        }
    }

    private void HandleRoundPhaseChanged(RoundSystem.RoundPhase phase)
    {
        if (phase != RoundSystem.RoundPhase.Results
            || retirementPromptShown
            || RoundSystem.Instance == null
            || RoundSystem.Instance.RoundNumber < EggcessiveUnlockRound
            || !RoundSystem.Instance.DidPassRound)
        {
            return;
        }

        retirementPromptShown = true;
        PlayerPrefs.SetInt(EggcessiveUnlockedKey, 1);
        PlayerPrefs.Save();
        RefreshEggcessiveButton();
        ShowRetirementPopup();
    }

    private void ShowRetirementPopup(
        EggcessiveUnlockedPopupEffects previewSettings = null)
    {
        ShowPanel(retirementPanel, true);
        overlayBackground.color = RetirementBackground;
        if (retirementVictoryAudio != null)
        {
            retirementVictoryAudio.ignoreListenerPause = true;
            retirementVictoryAudio.Stop();
            retirementVictoryAudio.Play();
        }

        if (previewSettings != null)
        {
            retirementPopupEffects?.PlayConfetti(previewSettings);
        }
        else
        {
            retirementPopupEffects?.PlayConfetti();
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void DebugPreviewRetirementPopup(
        EggcessiveUnlockedPopupEffects previewSettings)
    {
        ShowRetirementPopup(previewSettings);
    }

    public void DebugReplayRetirementConfetti(
        EggcessiveUnlockedPopupEffects previewSettings)
    {
        retirementPopupEffects?.PlayConfetti(previewSettings);
    }

    public void DebugStopRetirementConfetti()
    {
        retirementPopupEffects?.StopConfetti();
    }
#endif

    private void RefreshEggcessiveButton()
    {
        if (eggcessiveButton == null || eggcessiveButtonText == null)
        {
            return;
        }

        bool unlocked = IsEggcessiveUnlocked;
        eggcessiveButton.interactable = unlocked;
        eggcessiveButtonText.text = "EGGCESSIVE";
        eggcessiveLockText?.gameObject.SetActive(!unlocked);

        FontStyles restingStyle = FontStyles.Bold;
        if (!unlocked)
        {
            restingStyle |= FontStyles.Strikethrough;
        }

        if (eggcessiveButton.TryGetComponent(
                out SpringMenuButton springButton))
        {
            springButton.SetRestingFontStyle(restingStyle);
        }
        else
        {
            eggcessiveButtonText.fontStyle = restingStyle;
        }

        if (unlocked)
        {
            // The authored locked layout raises the main label to make room
            // for the lock note (Top = -20). Once unlocked, fill the button
            // like the labels on the other main-menu buttons.
            RectTransform labelRect = eggcessiveButtonText.rectTransform;
            Vector2 offsetMax = labelRect.offsetMax;
            offsetMax.y = 0f;
            labelRect.offsetMax = offsetMax;
        }
    }

    private void RefreshMainMenuSlogan()
    {
        if (mainMenuSloganText == null)
        {
            return;
        }

        if (mainMenuSlogans.Length == 0)
        {
            mainMenuSlogans = LoadMainMenuSlogans();
        }

        if (mainMenuSlogans.Length == 0)
        {
            mainMenuSloganText.text = "BUILD THE FLOCK. BREAK THE NUMBERS.";
            return;
        }

        int index = UnityEngine.Random.Range(0, mainMenuSlogans.Length);
        if (mainMenuSlogans.Length > 1
            && mainMenuSlogans[index] == currentMainMenuSlogan)
        {
            index = (index + UnityEngine.Random.Range(1, mainMenuSlogans.Length))
                % mainMenuSlogans.Length;
        }

        currentMainMenuSlogan = mainMenuSlogans[index];
        mainMenuSloganText.text = currentMainMenuSlogan;
    }

    private static string[] LoadMainMenuSlogans()
    {
        TextAsset source = Resources.Load<TextAsset>(MainMenuSlogansResource);
        if (source == null)
        {
            return Array.Empty<string>();
        }

        string[] lines = source.text.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        List<string> slogans = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string slogan = line.Trim();
            if (slogan.Length > 0 && !slogan.StartsWith("#"))
            {
                slogans.Add(slogan);
            }
        }

        return slogans.ToArray();
    }

    private void ApplySavedVolume()
    {
        float volume = PlayerPrefs.HasKey(MasterVolumeKey)
            ? PlayerPrefs.GetFloat(MasterVolumeKey)
            : DefaultMasterVolume;
        AudioListener.volume = Mathf.Clamp01(volume);
    }

    private void SetMasterVolume(float volume)
    {
        AudioListener.volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MasterVolumeKey, AudioListener.volume);
        PlayerPrefs.Save();
        RefreshVolumeText();
    }

    private void RefreshVolumeText()
    {
        if (volumeValueText != null)
        {
            volumeValueText.text =
                $"{Mathf.RoundToInt(AudioListener.volume * 100f)}%";
        }
    }

    private GameObject CreatePanel(string objectName)
    {
        GameObject panel = CreateUiObject(objectName, overlayRoot.transform);
        StretchToParent(panel.GetComponent<RectTransform>());
        panel.SetActive(false);
        return panel;
    }

    private TMP_Text CreateHeading(
        Transform parent,
        string value,
        float fontSize,
        float y)
    {
        return CreateText(
            "Heading",
            parent,
            value,
            fontSize,
            new Vector2(0f, y),
            new Vector2(1450f, 150f),
            EggYellow,
            FontStyles.Bold);
    }

    private Button CreateMenuButton(
        Transform parent,
        string label,
        float y,
        UnityEngine.Events.UnityAction action,
        float fontSize = 48f,
        bool useSpringAnimation = true)
    {
        GameObject buttonObject = CreateUiObject(label + " Button", parent);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        float buttonWidth = Mathf.Clamp(
            label.Length * fontSize * 0.62f + 96f,
            240f,
            700f);
        SetCenteredRect(
            rect,
            new Vector2(0f, y),
            new Vector2(buttonWidth, 76f));
        Image image = buttonObject.AddComponent<Image>();
        image.color = EggYellow;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = image.color;
        colors.pressedColor = image.color;
        colors.selectedColor = image.color;
        colors.disabledColor = image.color;
        button.colors = colors;

        TMP_Text text = CreateText(
            "Label",
            buttonObject.transform,
            label,
            fontSize,
            Vector2.zero,
            rect.sizeDelta,
            new Color(0.055f, 0.04f, 0.012f, 1f),
            FontStyles.Bold);
        StretchToParent(text.rectTransform);
        if (useSpringAnimation)
        {
            SpringMenuButton springButton =
                buttonObject.AddComponent<SpringMenuButton>();
            springButton.Initialize(button, text, action);
        }
        else
        {
            button.onClick.AddListener(action);
        }
        return button;
    }

    private Slider CreateSlider(
        Transform parent,
        Vector2 position,
        Vector2 size)
    {
        GameObject sliderObject = CreateUiObject("Master Volume Slider", parent);
        RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
        SetCenteredRect(sliderRect, position, size);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;

        GameObject backgroundObject = CreateUiObject(
            "Background",
            sliderObject.transform);
        RectTransform backgroundRect =
            backgroundObject.GetComponent<RectTransform>();
        StretchToParent(backgroundRect);
        Image background = backgroundObject.AddComponent<Image>();
        background.color = new Color(0.22f, 0.18f, 0.13f, 1f);

        GameObject fillArea = CreateUiObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        StretchToParent(fillAreaRect);
        fillAreaRect.offsetMin = new Vector2(5f, 5f);
        fillAreaRect.offsetMax = new Vector2(-5f, -5f);
        GameObject fillObject = CreateUiObject("Fill", fillArea.transform);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        StretchToParent(fillRect);
        Image fill = fillObject.AddComponent<Image>();
        fill.color = EggYellow;

        GameObject handleArea = CreateUiObject(
            "Handle Slide Area",
            sliderObject.transform);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        StretchToParent(handleAreaRect);
        handleAreaRect.offsetMin = new Vector2(12f, 0f);
        handleAreaRect.offsetMax = new Vector2(-12f, 0f);
        GameObject handleObject = CreateUiObject("Handle", handleArea.transform);
        RectTransform handleRect = handleObject.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.sizeDelta = new Vector2(28f, 52f);
        Image handle = handleObject.AddComponent<Image>();
        handle.color = Color.white;

        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float fontSize,
        Vector2 position,
        Vector2 size,
        Color color,
        FontStyles style)
    {
        GameObject textObject = CreateUiObject(objectName, parent);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetCenteredRect(rect, position, size);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    private static void SetCenteredRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>(
            FindObjectsInactive.Include);
        if (eventSystem != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystemObject);
    }
}

[DisallowMultipleComponent]
internal sealed class SpringMenuButton : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    ISelectHandler,
    IDeselectHandler
{
    private const float RestScale = 1f;
    private const float HoverScale = 1.12f;
    private const float PressScale = 0.86f;
    private const float HoverFrequencyHz = 6.5f;
    private const float HoverDampingRatio = 0.48f;
    private const float PressFrequencyHz = 18f;
    private const float PressDampingRatio = 0.82f;
    private const float PressHoldDuration = 0.085f;
    private const float PressReleaseDuration = 0.035f;
    private const float EntranceDuration = 0.48f;
    private const float MinimumHoverRotation = 1.1f;
    private const float MaximumHoverRotation = 2.8f;

    private static readonly Color DefaultIdleBackgroundColor =
        new Color(1f, 0.78f, 0.2f, 1f);
    private static readonly Color DefaultHoverBackgroundColor =
        new Color(1f, 0.86f, 0.31f, 1f);
    private static readonly Color DefaultPressedBackgroundColor =
        new Color(0.88f, 0.62f, 0.1f, 1f);
    private static readonly Color DefaultDisabledBackgroundColor =
        new Color(0.45f, 0.38f, 0.22f, 0.82f);
    private static readonly Color DefaultIdleTextColor =
        new Color(0.055f, 0.04f, 0.012f, 1f);
    private static readonly Color DefaultHoverTextColor =
        new Color(0.32f, 0.21f, 0.015f, 1f);
    private static readonly Color DefaultDisabledTextColor =
        new Color(0.16f, 0.14f, 0.1f, 1f);

    private Button button;
    private TMP_Text label;
    private Image background;
    private UnityEngine.Events.UnityAction action;
    private SpringUtils.FloatSpring scaleSpring =
        new SpringUtils.FloatSpring(RestScale);
    private SpringUtils.FloatSpring rotationSpring =
        new SpringUtils.FloatSpring(0f);
    private Quaternion restingLocalRotation;
    private float hoverRotation;
    private Color idleBackgroundColor = DefaultIdleBackgroundColor;
    private Color hoverBackgroundColor = DefaultHoverBackgroundColor;
    private Color pressedBackgroundColor = DefaultPressedBackgroundColor;
    private Color disabledBackgroundColor = DefaultDisabledBackgroundColor;
    private Color idleTextColor = DefaultIdleTextColor;
    private Color hoverTextColor = DefaultHoverTextColor;
    private Color disabledTextColor = DefaultDisabledTextColor;
    private FontStyles restingFontStyle = FontStyles.Bold;
    private Coroutine pressSequence;
    private CanvasGroup entranceCanvasGroup;
    private float entranceDelayRemaining;
    private float entranceElapsed;
    private float entranceDirection = 1f;
    private int entranceHoldFrames;
    private float entranceOriginalAlpha = 1f;
    private bool entranceOriginalInteractable = true;
    private bool entranceOriginalBlocksRaycasts = true;
    private bool entranceAnimating;
    private bool isHovered;
    private bool isSelected;
    private bool selectedByPointer;
    private bool isPointerDown;
    private bool isPressAnimating;
    private bool previousInteractable;

    private void Awake()
    {
        restingLocalRotation = transform.localRotation;
    }

    public void Initialize(
        Button targetButton,
        TMP_Text targetLabel,
        UnityEngine.Events.UnityAction clickAction,
        bool fitWidthToLabel = true)
    {
        button = targetButton;
        label = targetLabel;
        background = button != null
            ? button.targetGraphic as Image
                ?? button.GetComponent<Image>()
            : null;
        action = clickAction;
        restingFontStyle = label != null
            ? label.fontStyle
            : FontStyles.Bold;
        previousInteractable = button != null && button.interactable;
        if (button != null)
        {
            button.transition = Selectable.Transition.None;
            ResetBackgroundTint();
        }

        if (fitWidthToLabel
            && label != null
            && button != null
            && button.TryGetComponent(out RectTransform fittedRect))
        {
            float fittedWidth = Mathf.Clamp(
                label.text.Length * label.fontSize * 0.62f + 96f,
                240f,
                700f);
            fittedRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                fittedWidth);
        }

        if (action != null)
        {
            button?.onClick.AddListener(HandleClicked);
        }

        RefreshVisualStyle();
    }

    public void PlayEntrance(float delay, float rotationDirection)
    {
        CancelEntrance();
        if (!isActiveAndEnabled)
        {
            return;
        }

        entranceCanvasGroup = GetComponent<CanvasGroup>();
        if (entranceCanvasGroup == null)
        {
            entranceCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        entranceOriginalAlpha = entranceCanvasGroup.alpha;
        entranceOriginalInteractable = entranceCanvasGroup.interactable;
        entranceOriginalBlocksRaycasts = entranceCanvasGroup.blocksRaycasts;
        entranceCanvasGroup.alpha = 0f;
        entranceCanvasGroup.interactable = false;
        entranceCanvasGroup.blocksRaycasts = false;
        entranceDelayRemaining = Mathf.Max(0f, delay);
        entranceElapsed = 0f;
        entranceHoldFrames = 1;
        entranceDirection = Mathf.Sign(rotationDirection);
        if (Mathf.Approximately(entranceDirection, 0f))
        {
            entranceDirection = 1f;
        }

        entranceAnimating = true;
        scaleSpring.Reset(RestScale);
        rotationSpring.Reset(0f);
        ApplyEntrancePose(0f, out _, out _);
    }

    public void SetColorPalette(
        Color idleBackground,
        Color hoverBackground,
        Color pressedBackground,
        Color disabledBackground,
        Color idleText,
        Color hoverText,
        Color disabledText)
    {
        idleBackgroundColor = idleBackground;
        hoverBackgroundColor = hoverBackground;
        pressedBackgroundColor = pressedBackground;
        disabledBackgroundColor = disabledBackground;
        idleTextColor = idleText;
        hoverTextColor = hoverText;
        disabledTextColor = disabledText;
        RefreshVisualStyle();
    }

    public void SetRestingFontStyle(FontStyles fontStyle)
    {
        restingFontStyle = fontStyle;
        RefreshVisualStyle();
    }

    private void OnEnable()
    {
        scaleSpring.Reset(RestScale);
        rotationSpring.Reset(0f);
        transform.localScale = Vector3.one;
        transform.localRotation = restingLocalRotation;
        hoverRotation = 0f;
        isHovered = false;
        isSelected = false;
        selectedByPointer = false;
        isPointerDown = false;
        isPressAnimating = false;
        previousInteractable = button != null && button.interactable;
        RefreshVisualStyle();
    }

    private void OnDisable()
    {
        if (pressSequence != null)
        {
            StopCoroutine(pressSequence);
            pressSequence = null;
        }

        CancelEntrance();

        scaleSpring.Reset(RestScale);
        rotationSpring.Reset(0f);
        transform.localScale = Vector3.one;
        transform.localRotation = restingLocalRotation;
        hoverRotation = 0f;
        isHovered = false;
        isSelected = false;
        selectedByPointer = false;
        isPointerDown = false;
        isPressAnimating = false;
        RefreshVisualStyle();
    }

    private void OnDestroy()
    {
        if (action != null)
        {
            button?.onClick.RemoveListener(HandleClicked);
        }
    }

    private void LateUpdate()
    {
        bool interactable = button != null && button.interactable;
        if (interactable != previousInteractable)
        {
            previousInteractable = interactable;
            RefreshVisualStyle();
        }

        bool pressing = interactable
            && (isPointerDown || isPressAnimating);
        bool highlighted = interactable && (isHovered || isSelected);
        float target = pressing
            ? PressScale
            : highlighted
                ? HoverScale
                : RestScale;
        float frequency = pressing ? PressFrequencyHz : HoverFrequencyHz;
        float damping = pressing
            ? PressDampingRatio
            : HoverDampingRatio;
        float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        SpringUtils.MotionParams motion = SpringUtils.CalculateMotionParams(
            deltaTime,
            frequency,
            damping);
        scaleSpring.Update(target, 0f, deltaTime, motion);
        scaleSpring.ClampValue(0.8f, 1.24f);

        float rotationTarget = interactable && isHovered
            ? hoverRotation
            : 0f;
        rotationSpring.Update(rotationTarget, 0f, deltaTime, motion);
        rotationSpring.ClampValue(-4f, 4f);
        UpdateEntrancePose(
            Time.unscaledDeltaTime,
            out float entranceScale,
            out float entranceRotation);
        transform.localScale = Vector3.one
            * (scaleSpring.Value * entranceScale);
        transform.localRotation = restingLocalRotation
            * Quaternion.Euler(
                0f,
                0f,
                rotationSpring.Value + entranceRotation);
        RefreshVisualStyle();
    }

    private void UpdateEntrancePose(
        float deltaTime,
        out float scale,
        out float rotation)
    {
        if (!entranceAnimating)
        {
            scale = 1f;
            rotation = 0f;
            return;
        }

        if (entranceHoldFrames > 0)
        {
            entranceHoldFrames--;
            ApplyEntrancePose(0f, out scale, out rotation);
            return;
        }

        // Editor startup and scene activation can produce a very large first
        // unscaled delta. Do not let that skip an entrance before it renders.
        deltaTime = Mathf.Min(deltaTime, 1f / 30f);

        if (entranceDelayRemaining > 0f)
        {
            entranceDelayRemaining -= deltaTime;
            ApplyEntrancePose(0f, out scale, out rotation);
            return;
        }

        entranceElapsed += deltaTime;
        float progress = Mathf.Clamp01(entranceElapsed / EntranceDuration);
        ApplyEntrancePose(progress, out scale, out rotation);
        if (progress >= 1f)
        {
            FinishEntrance();
            scale = 1f;
            rotation = 0f;
        }
    }

    private void ApplyEntrancePose(
        float progress,
        out float scale,
        out float rotation)
    {
        float decay = Mathf.Exp(-6.8f * progress);
        scale = 1f - 0.38f * decay * Mathf.Cos(progress * 13f);
        rotation = entranceDirection
            * 9f
            * decay
            * Mathf.Cos(progress * 15f);
        if (entranceCanvasGroup != null)
        {
            entranceCanvasGroup.alpha = entranceOriginalAlpha
                * Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(progress / 0.38f));
        }
    }

    private void FinishEntrance()
    {
        entranceAnimating = false;
        entranceDelayRemaining = 0f;
        entranceElapsed = 0f;
        entranceHoldFrames = 0;
        if (entranceCanvasGroup != null)
        {
            entranceCanvasGroup.alpha = entranceOriginalAlpha;
            entranceCanvasGroup.interactable = entranceOriginalInteractable;
            entranceCanvasGroup.blocksRaycasts = entranceOriginalBlocksRaycasts;
        }
    }

    private void CancelEntrance()
    {
        if (!entranceAnimating)
        {
            return;
        }

        FinishEntrance();
        transform.localScale = Vector3.one;
        transform.localRotation = restingLocalRotation;
    }

    public void OnPointerEnter(PointerEventData _)
    {
        if (button == null || !button.interactable)
        {
            return;
        }

        isHovered = true;
        float direction = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        hoverRotation = direction * UnityEngine.Random.Range(
            MinimumHoverRotation,
            MaximumHoverRotation);
        scaleSpring.AddImpulse(0.55f);
        rotationSpring.AddImpulse(direction * 5f);
        RefreshVisualStyle();
    }

    public void OnPointerExit(PointerEventData _)
    {
        isHovered = false;
        hoverRotation = 0f;
        isPointerDown = false;
        ClearPointerSelection();
        RefreshVisualStyle();
    }

    public void OnPointerDown(PointerEventData _)
    {
        if (button != null && button.interactable)
        {
            selectedByPointer = true;
            isPointerDown = true;
            RefreshVisualStyle();
        }
    }

    public void OnPointerUp(PointerEventData _)
    {
        isPointerDown = false;
        if (!isHovered)
        {
            ClearPointerSelection();
        }

        RefreshVisualStyle();
    }

    public void OnSelect(BaseEventData _)
    {
        isSelected = true;
        RefreshVisualStyle();
    }

    public void OnDeselect(BaseEventData _)
    {
        isSelected = false;
        selectedByPointer = false;
        hoverRotation = 0f;
        RefreshVisualStyle();
    }

    private void HandleClicked()
    {
        if (pressSequence == null
            && button != null
            && button.interactable)
        {
            pressSequence = StartCoroutine(PlayPressThenInvoke());
        }
    }

    private void ClearPointerSelection()
    {
        if (!isSelected
            || !selectedByPointer
            || EventSystem.current == null
            || EventSystem.current.currentSelectedGameObject != gameObject)
        {
            return;
        }

        isSelected = false;
        selectedByPointer = false;
        EventSystem.current.SetSelectedGameObject(null);
    }

    private IEnumerator PlayPressThenInvoke()
    {
        isPressAnimating = true;
        yield return new WaitForSecondsRealtime(PressHoldDuration);
        isPressAnimating = false;
        yield return new WaitForSecondsRealtime(PressReleaseDuration);
        pressSequence = null;
        action?.Invoke();
    }

    private void RefreshVisualStyle()
    {
        if (label == null && background == null)
        {
            return;
        }

        ResetBackgroundTint();

        bool interactable = button != null && button.interactable;
        bool highlighted = interactable && (isHovered || isSelected);
        bool pressed = interactable && (isPointerDown || isPressAnimating);

        if (background != null)
        {
            background.color = !interactable
                ? disabledBackgroundColor
                : pressed
                    ? pressedBackgroundColor
                    : highlighted
                        ? hoverBackgroundColor
                        : idleBackgroundColor;
        }

        if (label != null)
        {
            label.color = !interactable
                ? disabledTextColor
                : highlighted
                    ? hoverTextColor
                    : idleTextColor;
            label.fontStyle = restingFontStyle;
        }
    }

    private void ResetBackgroundTint()
    {
        if (background != null)
        {
            background.canvasRenderer.SetColor(Color.white);
        }
    }
}
