using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FoodPile : MonoBehaviour
{
    private static readonly List<FoodPile> Piles = new List<FoodPile>();

    [SerializeField, Min(0.01f)] private float startingFood = 100f;
    [SerializeField] private Transform visualRoot = null;
    [SerializeField, Range(0.05f, 1f)] private float depletedScale = 0.2f;

    private float remainingFood;
    private Vector3 fullScale;
    private GrassInteractor grassInteractor;

    public static IReadOnlyList<FoodPile> ActivePiles => Piles;
    public float RemainingFood => remainingFood;
    public bool IsAvailable => isActiveAndEnabled && remainingFood > 0f;

    private void Awake()
    {
        if (visualRoot == null)
        {
            visualRoot = transform;
        }

        fullScale = visualRoot.localScale;
        grassInteractor = GetComponent<GrassInteractor>();
        remainingFood = startingFood;
        RefreshVisualScale();
    }

    private void OnEnable()
    {
        if (!Piles.Contains(this))
        {
            Piles.Add(this);
        }
    }

    private void OnDisable()
    {
        Piles.Remove(this);
    }

    public float Consume(float amountRequested)
    {
        if (!IsAvailable || amountRequested <= 0f)
        {
            return 0f;
        }

        float amountConsumed = Mathf.Min(amountRequested, remainingFood);
        remainingFood -= amountConsumed;

        if (remainingFood <= 0.0001f)
        {
            remainingFood = 0f;
            Destroy(gameObject);
        }
        else
        {
            RefreshVisualScale();
        }

        return amountConsumed;
    }

    private void RefreshVisualScale()
    {
        if (visualRoot == null || startingFood <= 0f)
        {
            return;
        }

        float remainingNormalized = Mathf.Clamp01(remainingFood / startingFood);
        float scale = Mathf.Lerp(depletedScale, 1f, remainingNormalized);
        visualRoot.localScale = Vector3.Scale(fullScale, new Vector3(scale, scale, scale));
        if (grassInteractor != null)
        {
            grassInteractor.SetRadiusScale(scale);
        }
    }

    private void OnValidate()
    {
        startingFood = Mathf.Max(0.01f, startingFood);
        depletedScale = Mathf.Clamp(depletedScale, 0.05f, 1f);
    }
}
