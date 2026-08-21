using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "UiSoundSettings", menuName = "Eggcessive/UI Sound Settings")]
public sealed class UiSoundSettings : ScriptableObject
{
    private const string ResourcePath = "UI/UiSoundSettings";

    [SerializeField] private AudioClip buttonConfirm = null;
    [SerializeField] private AudioClip pointerTick = null;
    [SerializeField] private AudioClip slideOut = null;
    [SerializeField, Range(0f, 1f)] private float buttonConfirmVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float pointerTickVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float slideOutVolume = 1f;

    internal AudioClip ButtonConfirm => buttonConfirm;
    internal AudioClip PointerTick => pointerTick;
    internal AudioClip SlideOut => slideOut;
    internal float ButtonConfirmVolume => buttonConfirmVolume;
    internal float PointerTickVolume => pointerTickVolume;
    internal float SlideOutVolume => slideOutVolume;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreatePlayer()
    {
        UiSoundController.Create(Resources.Load<UiSoundSettings>(ResourcePath));
    }
}

[DefaultExecutionOrder(-900)]
[DisallowMultipleComponent]
internal sealed class UiSoundController : MonoBehaviour
{
    private const float ButtonDiscoveryInterval = 0.5f;

    private static readonly string[] BackButtonNameTokens =
    {
        "back",
        "cancel",
        "close",
        "resume",
        "done shopping"
    };

    private static UiSoundController instance;

    private UiSoundSettings settings;
    private AudioSource audioSource;
    private float nextButtonDiscoveryTime;
    private Coroutine sceneDiscovery;

    internal static void Create(UiSoundSettings soundSettings)
    {
        if (instance != null)
        {
            return;
        }

        if (soundSettings == null)
        {
            Debug.LogError(
                "UI sound settings were not found at Resources/UI/UiSoundSettings.");
            return;
        }

        GameObject root = new GameObject(nameof(UiSoundController));
        DontDestroyOnLoad(root);
        UiSoundController controller = root.AddComponent<UiSoundController>();
        controller.settings = soundSettings;
        controller.DiscoverButtons();
    }

    internal static void PlayBack()
    {
        instance?.PlaySlideOut();
    }

    internal static void PlayConfirmFeedback()
    {
        instance?.PlayConfirm();
    }

    internal static void Attach(Button button)
    {
        instance?.AttachToButton(button);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerPause = true;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextButtonDiscoveryTime)
        {
            return;
        }

        DiscoverButtons();
    }

    internal void PlayConfirm()
    {
        Play(settings.ButtonConfirm, settings.ButtonConfirmVolume);
    }

    internal void PlayTick()
    {
        Play(settings.PointerTick, settings.PointerTickVolume);
    }

    internal void PlaySlideOut()
    {
        Play(settings.SlideOut, settings.SlideOutVolume);
    }

    internal void PlayButtonClick(Button button)
    {
        if (IsBackButton(button))
        {
            PlaySlideOut();
        }
        else
        {
            PlayConfirm();
        }
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip, volume);
        }
    }

    private void DiscoverButtons()
    {
        nextButtonDiscoveryTime = Time.unscaledTime + ButtonDiscoveryInterval;
        Button[] buttons = FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (Button button in buttons)
        {
            AttachToButton(button);
        }
    }

    private void AttachToButton(Button button)
    {
        if (button == null)
        {
            return;
        }

        UiButtonSoundEmitter emitter =
            button.GetComponent<UiButtonSoundEmitter>();
        if (emitter == null)
        {
            emitter = button.gameObject.AddComponent<UiButtonSoundEmitter>();
        }

        emitter.Initialize(this, button);
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        if (sceneDiscovery != null)
        {
            StopCoroutine(sceneDiscovery);
        }

        sceneDiscovery = StartCoroutine(DiscoverAfterSceneLoad());
    }

    private IEnumerator DiscoverAfterSceneLoad()
    {
        yield return null;
        DiscoverButtons();
        sceneDiscovery = null;
    }

    private static bool IsBackButton(Button button)
    {
        if (button == null)
        {
            return false;
        }

        string buttonName = button.gameObject.name;
        foreach (string token in BackButtonNameTokens)
        {
            if (buttonName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}

[DisallowMultipleComponent]
internal sealed class UiButtonSoundEmitter : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler,
    ISubmitHandler
{
    private UiSoundController controller;
    private Button button;
    private bool pointerInside;

    internal void Initialize(UiSoundController soundController, Button targetButton)
    {
        controller = soundController;
        button = targetButton;
    }

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        pointerInside = false;
    }

    private void OnDisable()
    {
        pointerInside = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (pointerInside || button == null || !button.IsInteractable())
        {
            return;
        }

        pointerInside = true;
        if (eventData.delta.sqrMagnitude > 0.0001f)
        {
            controller?.PlayTick();
        }
    }

    public void OnPointerExit(PointerEventData _)
    {
        pointerInside = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            PlayButtonClick();
        }
    }

    public void OnSubmit(BaseEventData _)
    {
        PlayButtonClick();
    }

    private void PlayButtonClick()
    {
        if (button == null || !button.IsInteractable())
        {
            return;
        }

        controller?.PlayButtonClick(button);
    }
}
