using UnityEngine;

[DisallowMultipleComponent]
public sealed class EggcessiveUnlockedPopupEffects : MonoBehaviour
{
    [SerializeField]
    private GameObject confettiPrefab = null;

    [SerializeField]
    [Tooltip("World-space offset from the gameplay camera. Positive Z is in front of the camera.")]
    private Vector3 cameraLocalPosition = new Vector3(0f, 0f, 12f);

    [SerializeField]
    private Vector3 cameraLocalEulerAngles = new Vector3(0f, 180f, 0f);

    [SerializeField, Min(0.01f)]
    [Tooltip("Uniform scale applied to the spawned confetti prefab.")]
    private float effectScale = 0.35f;

    [SerializeField, Range(0f, 0.02f)]
    [Tooltip("Minimum viewport size for the thin particles emitted by the child named confetti.")]
    private float minimumConfettiScreenSize = 0.0025f;

    [SerializeField, Min(0.1f)]
    private float cleanupDelay = 10f;

    private GameObject activeConfetti;

    public bool IsConfigured => confettiPrefab != null;

    public void PlayConfetti()
    {
        PlayConfetti(this);
    }

    public void PlayConfetti(EggcessiveUnlockedPopupEffects settings)
    {
        settings = settings != null ? settings : this;
        if (settings.confettiPrefab == null)
        {
            return;
        }

        Camera effectCamera = Camera.main;
        if (effectCamera == null)
        {
            effectCamera = FindFirstObjectByType<Camera>();
        }

        if (effectCamera == null)
        {
            Debug.LogWarning(
                "Cannot play the Eggcessive unlock confetti because no camera is active.");
            return;
        }

        StopConfetti();

        Transform cameraTransform = effectCamera.transform;
        activeConfetti = Instantiate(
            settings.confettiPrefab,
            cameraTransform.TransformPoint(settings.cameraLocalPosition),
            cameraTransform.rotation
                * Quaternion.Euler(settings.cameraLocalEulerAngles));
        activeConfetti.name = "Eggcessive Unlock Confetti";
        activeConfetti.transform.localScale =
            Vector3.one * settings.effectScale;

        ParticleSystem[] particleSystems =
            activeConfetti.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particleSystem in particleSystems)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            // The authored systems use Local scaling, which ignores the
            // scale applied to the spawned prefab root. Hierarchy scaling
            // makes one popup setting resize every nested emitter together.
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = true;
            if (particleSystem.name.Equals(
                    "confetti",
                    System.StringComparison.OrdinalIgnoreCase)
                && particleSystem.TryGetComponent(
                    out ParticleSystemRenderer confettiRenderer))
            {
                confettiRenderer.minParticleSize =
                    settings.minimumConfettiScreenSize;
            }

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Play(true);
        }

        UnscaledEffectLifetime lifetime =
            activeConfetti.AddComponent<UnscaledEffectLifetime>();
        lifetime.Initialize(settings.cleanupDelay);
    }

    public void StopConfetti()
    {
        if (activeConfetti == null)
        {
            return;
        }

        Destroy(activeConfetti);
        activeConfetti = null;
    }
}

internal sealed class UnscaledEffectLifetime : MonoBehaviour
{
    private float remainingLifetime;

    public void Initialize(float lifetime)
    {
        remainingLifetime = Mathf.Max(0.1f, lifetime);
    }

    private void Update()
    {
        remainingLifetime -= Time.unscaledDeltaTime;
        if (remainingLifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }
}
