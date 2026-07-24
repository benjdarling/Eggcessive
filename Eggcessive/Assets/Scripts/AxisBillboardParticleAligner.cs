using UnityEngine;

/// <summary>
/// Cylindrically billboards mesh particles around the particle system's local Z
/// axis. The beam length remains on Z while only its width turns toward camera.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
[RequireComponent(typeof(ParticleSystemRenderer))]
public sealed class AxisBillboardParticleAligner : MonoBehaviour
{
    [SerializeField] private Camera targetCamera = null;
    [SerializeField] private float angleOffset = 0f;

    private ParticleSystem particleSystemComponent;
    private ParticleSystemRenderer particleRenderer;
    private ParticleSystem.Particle[] particles;

    private void OnEnable()
    {
        CacheComponents();
        ForceLocalMeshAlignment();
    }

    private void OnValidate()
    {
        CacheComponents();
        ForceLocalMeshAlignment();
    }

    private void LateUpdate()
    {
        CacheComponents();

        Camera resolvedCamera = targetCamera != null ? targetCamera : Camera.main;
        if (particleSystemComponent == null || resolvedCamera == null)
            return;

        ParticleSystem.MainModule main = particleSystemComponent.main;
        int capacity = Mathf.Max(1, main.maxParticles);
        if (particles == null || particles.Length < capacity)
            particles = new ParticleSystem.Particle[capacity];

        int particleCount = particleSystemComponent.GetParticles(particles);
        if (particleCount == 0)
            return;

        Vector3 cameraPosition = resolvedCamera.transform.position;
        for (int i = 0; i < particleCount; i++)
        {
            Vector3 worldPosition = GetWorldPosition(particles[i].position, main);
            Vector3 cameraDirectionLocal = transform.InverseTransformDirection(
                cameraPosition - worldPosition);

            // Project onto the plane perpendicular to the preserved local Z axis.
            Vector2 projectedDirection = new Vector2(
                cameraDirectionLocal.x,
                cameraDirectionLocal.y);
            if (projectedDirection.sqrMagnitude < 0.000001f)
                continue;

            // The authored quad's normal is local +Y.
            float zRotation = Mathf.Atan2(
                -projectedDirection.x,
                projectedDirection.y) * Mathf.Rad2Deg + angleOffset;

            Vector3 rotation = particles[i].rotation3D;
            rotation.x = 0f;
            rotation.y = 0f;
            rotation.z = zRotation;
            particles[i].rotation3D = rotation;
        }

        particleSystemComponent.SetParticles(particles, particleCount);
    }

    private Vector3 GetWorldPosition(
        Vector3 particlePosition,
        ParticleSystem.MainModule main)
    {
        switch (main.simulationSpace)
        {
            case ParticleSystemSimulationSpace.World:
                return particlePosition;

            case ParticleSystemSimulationSpace.Custom:
                Transform customSpace = main.customSimulationSpace;
                return customSpace != null
                    ? customSpace.TransformPoint(particlePosition)
                    : transform.TransformPoint(particlePosition);

            default:
                return transform.TransformPoint(particlePosition);
        }
    }

    private void CacheComponents()
    {
        if (particleSystemComponent == null)
            particleSystemComponent = GetComponent<ParticleSystem>();

        if (particleRenderer == null)
            particleRenderer = GetComponent<ParticleSystemRenderer>();
    }

    private void ForceLocalMeshAlignment()
    {
        if (particleRenderer != null)
            particleRenderer.alignment = ParticleSystemRenderSpace.Local;
    }
}
