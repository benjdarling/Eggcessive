using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-300)]
[DisallowMultipleComponent]
public sealed class GrassInteractor : MonoBehaviour
{
    private static readonly List<GrassInteractor> ActiveInteractors = new List<GrassInteractor>();

    [SerializeField, Min(0.01f)] private float radius = 0.18f;
    [SerializeField, Min(0f)] private float bendStrength = 1f;
    [SerializeField, Range(0f, 1f)] private float flattenStrength = 0.85f;
    [SerializeField, Range(0f, 1f)] private float velocityDirectionInfluence = 0.7f;
    [SerializeField, Range(0.25f, 4f)] private float falloffPower = 2f;

    private Vector3 previousPosition;
    private float radiusScale = 1f;

    public static IReadOnlyList<GrassInteractor> ActiveInstances => ActiveInteractors;
    public Vector3 Position => transform.position;
    public Vector3 PlanarVelocity { get; private set; }
    public float WorldRadius
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            return radius * radiusScale * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
    }
    public float BendStrength => bendStrength;
    public float FlattenStrength => flattenStrength;
    public float VelocityDirectionInfluence => velocityDirectionInfluence;
    public float FalloffPower => falloffPower;

    public void SetRadiusScale(float scale)
    {
        radiusScale = Mathf.Max(0.01f, scale);
    }

    private void OnEnable()
    {
        previousPosition = transform.position;
        PlanarVelocity = Vector3.zero;
        if (!ActiveInteractors.Contains(this))
        {
            ActiveInteractors.Add(this);
        }
    }

    private void Update()
    {
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 velocity = (transform.position - previousPosition) / deltaTime;
        velocity.y = 0f;
        PlanarVelocity = velocity;
        previousPosition = transform.position;
    }

    private void OnDisable()
    {
        ActiveInteractors.Remove(this);
    }

    private void OnValidate()
    {
        radius = Mathf.Max(0.01f, radius);
        bendStrength = Mathf.Max(0f, bendStrength);
        flattenStrength = Mathf.Clamp01(flattenStrength);
        velocityDirectionInfluence = Mathf.Clamp01(velocityDirectionInfluence);
        falloffPower = Mathf.Clamp(falloffPower, 0.25f, 4f);
    }
}
