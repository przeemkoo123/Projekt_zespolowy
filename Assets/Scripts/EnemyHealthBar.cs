using UnityEngine;

/// <summary>
/// Pływający pasek życia (Floating Health Bar) nad głową przeciwnika w świecie 2.5D.
/// Wyświetla tło, obramowanie oraz dynamicznie skalowany pasek życia w zależności od EnemyAI.CurrentHealth.
/// Zwraca się zawsze w stronę kamery (Billboard), zmienia kolor (zielony -> żółty -> czerwony)
/// i znika płynnie dopiero po śmierci wroga.
/// </summary>
[DisallowMultipleComponent]
public class EnemyHealthBar : MonoBehaviour
{
    [Header("Referencje")]
    [SerializeField] private EnemyAI enemyAI;

    [Header("Wymiary i Pozycja")]
    [Tooltip("Wysokość paska życia nad pozycją wroga.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 2.15f, 0f);

    [Tooltip("Całkowita szerokość paska życia w metrach.")]
    [SerializeField] private float barWidth = 1.15f;

    [Tooltip("Wysokość paska życia w metrach.")]
    [SerializeField] private float barHeight = 0.14f;

    [Header("Kolory")]
    [SerializeField] private Color fullHealthColor = new Color(0.2f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color midHealthColor = new Color(0.95f, 0.8f, 0.15f, 1f);
    [SerializeField] private Color lowHealthColor = new Color(0.9f, 0.2f, 0.15f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.1f, 0.1f, 0.12f, 0.85f);
    [SerializeField] private Color borderColor = new Color(0.02f, 0.02f, 0.02f, 0.95f);

    private Camera targetCamera;
    private Transform barRoot;
    private Transform fillPivot;
    private SpriteRenderer fillRenderer;
    private SpriteRenderer bgRenderer;
    private SpriteRenderer borderRenderer;

    private float currentPercent = 1f;
    private float targetPercent = 1f;
    private bool isInitialized = false;

    private static Sprite sharedLeftPivotSprite;
    private static Sprite sharedCenterPivotSprite;

    private void Awake()
    {
        if (enemyAI == null)
        {
            enemyAI = GetComponentInParent<EnemyAI>();
        }

        targetCamera = Camera.main;
        InitializeVisuals();
    }

    private void OnEnable()
    {
        if (enemyAI != null)
        {
            enemyAI.OnHealthChanged += HandleHealthChanged;
            if (enemyAI.MaxHealth > 0f)
            {
                targetPercent = Mathf.Clamp01(enemyAI.CurrentHealth / enemyAI.MaxHealth);
                currentPercent = targetPercent;
                UpdateFillScale(currentPercent);
            }
        }
    }

    private void OnDisable()
    {
        if (enemyAI != null)
        {
            enemyAI.OnHealthChanged -= HandleHealthChanged;
        }
    }

    private void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (enemyAI != null && enemyAI.MaxHealth > 0f)
        {
            targetPercent = Mathf.Clamp01(enemyAI.CurrentHealth / enemyAI.MaxHealth);
            currentPercent = targetPercent;
            UpdateFillScale(currentPercent);
        }
    }

    private void LateUpdate()
    {
        if (barRoot == null) return;

        // Dopasowanie pozycji nad wrogiem
        Transform parentTransform = enemyAI != null ? enemyAI.transform : transform.parent;
        if (parentTransform != null)
        {
            barRoot.position = parentTransform.position + offset;
        }

        // Orientacja izometryczna / Billboard w stronę kamery
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera != null)
        {
            barRoot.rotation = targetCamera.transform.rotation;
        }

        // Płynna animacja paska
        if (Mathf.Abs(currentPercent - targetPercent) > 0.002f)
        {
            currentPercent = Mathf.Lerp(currentPercent, targetPercent, Time.deltaTime * 14f);
            UpdateFillScale(currentPercent);
        }

        // Ukrycie paska po śmierci
        if (enemyAI != null && enemyAI.IsDead)
        {
            if (barRoot.gameObject.activeSelf && currentPercent <= 0.01f)
            {
                barRoot.gameObject.SetActive(false);
            }
        }
    }

    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        if (maxHealth <= 0f) return;

        targetPercent = Mathf.Clamp01(currentHealth / maxHealth);

        if (barRoot != null && !barRoot.gameObject.activeSelf && currentHealth > 0f)
        {
            barRoot.gameObject.SetActive(true);
        }

        // Jeśli zginął, zbij pasek natychmiast do zera
        if (currentHealth <= 0f)
        {
            targetPercent = 0f;
            currentPercent = 0f;
            UpdateFillScale(0f);
        }
    }

    private void InitializeVisuals()
    {
        if (isInitialized) return;

        EnsureSpritesCreated();

        // Korzeń paska życia
        GameObject rootObj = new GameObject("HealthBar_Root");
        rootObj.transform.SetParent(transform, false);
        rootObj.transform.localPosition = offset;
        barRoot = rootObj.transform;

        float borderWidth = 0.035f;

        // 1. Obramowanie (czarne tło zewnętrzne)
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(barRoot, false);
        borderObj.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        borderRenderer = borderObj.AddComponent<SpriteRenderer>();
        borderRenderer.sprite = sharedCenterPivotSprite;
        borderRenderer.color = borderColor;
        borderRenderer.sortingOrder = 14;
        borderObj.transform.localScale = new Vector3(barWidth + borderWidth * 2f, barHeight + borderWidth * 2f, 1f);

        // 2. Tło (ciemne wnętrze paska)
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(barRoot, false);
        bgObj.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        bgRenderer = bgObj.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = sharedCenterPivotSprite;
        bgRenderer.color = backgroundColor;
        bgRenderer.sortingOrder = 15;
        bgObj.transform.localScale = new Vector3(barWidth, barHeight, 1f);

        // 3. Wypełnienie (kolorowy pasek kurczący się od prawej do lewej)
        GameObject fillPivotObj = new GameObject("FillPivot");
        fillPivotObj.transform.SetParent(barRoot, false);
        fillPivotObj.transform.localPosition = new Vector3(-barWidth * 0.5f, 0f, 0f);
        fillPivot = fillPivotObj.transform;

        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillPivot, false);
        fillObj.transform.localPosition = Vector3.zero;
        fillRenderer = fillObj.AddComponent<SpriteRenderer>();
        fillRenderer.sprite = sharedLeftPivotSprite;
        fillRenderer.color = fullHealthColor;
        fillRenderer.sortingOrder = 16;
        fillObj.transform.localScale = new Vector3(barWidth, barHeight * 0.88f, 1f);

        isInitialized = true;
        UpdateFillScale(1f);
    }

    private void UpdateFillScale(float percent)
    {
        if (fillPivot == null || fillRenderer == null) return;

        float clamped = Mathf.Clamp01(percent);
        fillPivot.localScale = new Vector3(clamped, 1f, 1f);

        // Zmiana koloru w zależności od poziomu zdrowia (zielony -> żółty -> czerwony)
        if (clamped > 0.5f)
        {
            float t = (clamped - 0.5f) * 2f;
            fillRenderer.color = Color.Lerp(midHealthColor, fullHealthColor, t);
        }
        else
        {
            float t = clamped * 2f;
            fillRenderer.color = Color.Lerp(lowHealthColor, midHealthColor, t);
        }
    }

    private static void EnsureSpritesCreated()
    {
        if (sharedLeftPivotSprite != null && sharedCenterPivotSprite != null) return;

        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        {
            name = "HealthBar_Tex",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color[] pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        // Pivot na lewej krawędzi (x = 0, y = 0.5) dla wypełnienia
        sharedLeftPivotSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
        sharedLeftPivotSprite.name = "HealthBar_Fill_Sprite";

        // Pivot w centrum (x = 0.5, y = 0.5) dla tła i ramki
        sharedCenterPivotSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        sharedCenterPivotSprite.name = "HealthBar_Center_Sprite";
    }
}
