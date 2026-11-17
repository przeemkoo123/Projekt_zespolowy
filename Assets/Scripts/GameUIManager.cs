using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Menedżer interfejsu użytkownika (UI).
/// Zarządza:
/// - Paskiem zdrowia gracza (pasek + tekst wartości HP),
/// - Ekranem końca gry (GameOver) z przyciskiem "Graj od nowa" (Restart),
/// - Samodzielnym, automatycznym tworzeniem komponentów Canvas, jeśli nie istnieją na scenie.
/// </summary>
[DisallowMultipleComponent]
public class GameUIManager : MonoBehaviour
{
    public static GameUIManager Instance { get; private set; }

    [Header("Referencje UI (Opcjonalne - wygenerują się same jeśli puste)")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image healthBarFill;
    [SerializeField] private Text healthText;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Button restartButton;

    private PlayerHealth playerHealth;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        EnsureEventSystem();
        EnsureUIHierarchy();
    }

    private void Start()
    {
        ConnectPlayer();
    }

    private void Update()
    {
        // Jeśli referencja do gracza została utracona (np. po spawnie nowego gracza), podłącz ponownie
        if (playerHealth == null)
        {
            ConnectPlayer();
        }
    }

    /// <summary>
    /// Łączy zdarzenia gracza (zmiana zdrowia, śmierć) z interfejsem UI.
    /// </summary>
    public void ConnectPlayer()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null)
        {
            PlayerHealth ph = Object.FindAnyObjectByType<PlayerHealth>();
            if (ph != null) playerObj = ph.gameObject;
        }

        if (playerObj != null)
        {
            PlayerHealth newHealth = playerObj.GetComponent<PlayerHealth>();
            if (newHealth != null && newHealth != playerHealth)
            {
                if (playerHealth != null)
                {
                    playerHealth.OnHealthChanged -= UpdateHealthBar;
                    playerHealth.OnDied -= ShowGameOver;
                }

                playerHealth = newHealth;
                playerHealth.OnHealthChanged += UpdateHealthBar;
                playerHealth.OnDied += ShowGameOver;

                UpdateHealthBar(playerHealth.CurrentHealth, playerHealth.MaxHealth);
            }
        }
    }

    /// <summary>
    /// Aktualizuje pasek zdrowia i wartość tekstową.
    /// </summary>
    public void UpdateHealthBar(float current, float max)
    {
        if (max <= 0f) max = 100f;
        float ratio = Mathf.Clamp01(current / max);

        if (healthBarFill != null)
        {
            healthBarFill.fillAmount = ratio;

            // Płynny gradient od zieleni do czerwieni
            if (ratio > 0.5f)
            {
                healthBarFill.color = Color.Lerp(new Color(0.95f, 0.75f, 0.1f), new Color(0.25f, 0.85f, 0.35f), (ratio - 0.5f) * 2f);
            }
            else
            {
                healthBarFill.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f), new Color(0.95f, 0.75f, 0.1f), ratio * 2f);
            }
        }

        if (healthText != null)
        {
            healthText.text = $"HP: {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }
    }

    /// <summary>
    /// Wyświetla planszę końca gry.
    /// </summary>
    public void ShowGameOver()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
    }

    /// <summary>
    /// Ukrywa planszę końca gry.
    /// </summary>
    public void HideGameOver()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Wywoływane po kliknięciu przycisku "Graj od nowa".
    /// </summary>
    public void OnRestartClicked()
    {
        HideGameOver();

        if (DungeonManager.Instance != null)
        {
            DungeonManager.Instance.RestartGame();
        }
        else
        {
            // Fallback: reset gracza i przeładowanie sceny jeśli brak DungeonManager
            if (playerHealth != null)
            {
                playerHealth.ResetHealth();
            }
        }
    }

    #region Programowe Budowanie Canvasu i Elementów UI

    private void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
            esObj.AddComponent<InputSystemUIInputModule>();
#elif ENABLE_LEGACY_INPUT_MANAGER
            esObj.AddComponent<StandaloneInputModule>();
#endif
        }
    }

    private void EnsureUIHierarchy()
    {
        if (canvas == null)
        {
            canvas = GetComponentInChildren<Canvas>();
        }

        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("GameHUD_Canvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();
        }

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null)
        {
            defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        // 1. Pasek Zdrowia (Lewy górny róg)
        if (healthBarFill == null || healthText == null)
        {
            BuildHealthBarUI(canvas.transform, defaultFont);
        }

        // 2. Ekran Koniec Gry (Wyśrodkowany)
        if (gameOverPanel == null)
        {
            BuildGameOverUI(canvas.transform, defaultFont);
        }
    }

    private void BuildHealthBarUI(Transform parent, Font font)
    {
        // Kontener paska zdrowia
        GameObject container = new GameObject("HealthBar_Container");
        container.transform.SetParent(parent, false);

        RectTransform rectContainer = container.AddComponent<RectTransform>();
        rectContainer.anchorMin = new Vector2(0f, 1f);
        rectContainer.anchorMax = new Vector2(0f, 1f);
        rectContainer.pivot = new Vector2(0f, 1f);
        rectContainer.anchoredPosition = new Vector2(30f, -30f);
        rectContainer.sizeDelta = new Vector2(280f, 38f);

        // Tło paska
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(container.transform, false);
        RectTransform rectBg = bgObj.AddComponent<RectTransform>();
        rectBg.anchorMin = Vector2.zero;
        rectBg.anchorMax = Vector2.one;
        rectBg.sizeDelta = Vector2.zero;

        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);

        // Wypełnienie paska (Fill)
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(container.transform, false);
        RectTransform rectFill = fillObj.AddComponent<RectTransform>();
        rectFill.anchorMin = Vector2.zero;
        rectFill.anchorMax = Vector2.one;
        rectFill.offsetMin = new Vector2(4f, 4f);
        rectFill.offsetMax = new Vector2(-4f, -4f);

        healthBarFill = fillObj.AddComponent<Image>();
        healthBarFill.type = Image.Type.Filled;
        healthBarFill.fillMethod = Image.FillMethod.Horizontal;
        healthBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        healthBarFill.color = new Color(0.25f, 0.85f, 0.35f, 1f);
        healthBarFill.fillAmount = 1f;

        // Tekst paska zdrowia
        GameObject textObj = new GameObject("HP_Text");
        textObj.transform.SetParent(container.transform, false);
        RectTransform rectText = textObj.AddComponent<RectTransform>();
        rectText.anchorMin = Vector2.zero;
        rectText.anchorMax = Vector2.one;
        rectText.sizeDelta = Vector2.zero;

        healthText = textObj.AddComponent<Text>();
        healthText.font = font;
        healthText.fontSize = 18;
        healthText.fontStyle = FontStyle.Bold;
        healthText.alignment = TextAnchor.MiddleCenter;
        healthText.color = Color.white;
        healthText.text = "HP: 100 / 100";

        // Dodanie cienia dla czytelności
        Shadow shadow = textObj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private void BuildGameOverUI(Transform parent, Font font)
    {
        // Główny panel (przyciemniający całe okno gry)
        gameOverPanel = new GameObject("GameOver_Panel");
        gameOverPanel.transform.SetParent(parent, false);

        RectTransform rectPanel = gameOverPanel.AddComponent<RectTransform>();
        rectPanel.anchorMin = Vector2.zero;
        rectPanel.anchorMax = Vector2.one;
        rectPanel.sizeDelta = Vector2.zero;

        Image panelImage = gameOverPanel.AddComponent<Image>();
        panelImage.color = new Color(0.04f, 0.04f, 0.06f, 0.88f);

        // Tytuł: KONIEC GRY
        GameObject titleObj = new GameObject("Title_Text");
        titleObj.transform.SetParent(gameOverPanel.transform, false);
        RectTransform rectTitle = titleObj.AddComponent<RectTransform>();
        rectTitle.anchorMin = new Vector2(0.5f, 0.5f);
        rectTitle.anchorMax = new Vector2(0.5f, 0.5f);
        rectTitle.pivot = new Vector2(0.5f, 0.5f);
        rectTitle.anchoredPosition = new Vector2(0f, 90f);
        rectTitle.sizeDelta = new Vector2(600f, 100f);

        Text titleText = titleObj.AddComponent<Text>();
        titleText.font = font;
        titleText.fontSize = 58;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = new Color(0.92f, 0.22f, 0.22f, 1f);
        titleText.text = "KONIEC GRY";

        Shadow titleShadow = titleObj.AddComponent<Shadow>();
        titleShadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        titleShadow.effectDistance = new Vector2(3f, -3f);

        // Podtytuł: Opis
        GameObject subtitleObj = new GameObject("Subtitle_Text");
        subtitleObj.transform.SetParent(gameOverPanel.transform, false);
        RectTransform rectSub = subtitleObj.AddComponent<RectTransform>();
        rectSub.anchorMin = new Vector2(0.5f, 0.5f);
        rectSub.anchorMax = new Vector2(0.5f, 0.5f);
        rectSub.pivot = new Vector2(0.5f, 0.5f);
        rectSub.anchoredPosition = new Vector2(0f, 25f);
        rectSub.sizeDelta = new Vector2(500f, 40f);

        Text subText = subtitleObj.AddComponent<Text>();
        subText.font = font;
        subText.fontSize = 22;
        subText.alignment = TextAnchor.MiddleCenter;
        subText.color = new Color(0.85f, 0.85f, 0.88f, 0.95f);
        subText.text = "Bohater poległ w lochach...";

        // Przycisk "GRAJ OD NOWA"
        GameObject buttonObj = new GameObject("Restart_Button");
        buttonObj.transform.SetParent(gameOverPanel.transform, false);
        RectTransform rectBtn = buttonObj.AddComponent<RectTransform>();
        rectBtn.anchorMin = new Vector2(0.5f, 0.5f);
        rectBtn.anchorMax = new Vector2(0.5f, 0.5f);
        rectBtn.pivot = new Vector2(0.5f, 0.5f);
        rectBtn.anchoredPosition = new Vector2(0f, -65f);
        rectBtn.sizeDelta = new Vector2(260f, 58f);

        Image btnImage = buttonObj.AddComponent<Image>();
        btnImage.color = new Color(0.20f, 0.65f, 0.32f, 1f);

        restartButton = buttonObj.AddComponent<Button>();
        ColorBlock colors = restartButton.colors;
        colors.normalColor = new Color(0.20f, 0.65f, 0.32f, 1f);
        colors.highlightedColor = new Color(0.28f, 0.78f, 0.40f, 1f);
        colors.pressedColor = new Color(0.15f, 0.50f, 0.25f, 1f);
        colors.selectedColor = colors.normalColor;
        restartButton.colors = colors;

        restartButton.onClick.AddListener(OnRestartClicked);

        // Tekst na przycisku
        GameObject btnTextObj = new GameObject("Text");
        btnTextObj.transform.SetParent(buttonObj.transform, false);
        RectTransform rectBtnText = btnTextObj.AddComponent<RectTransform>();
        rectBtnText.anchorMin = Vector2.zero;
        rectBtnText.anchorMax = Vector2.one;
        rectBtnText.sizeDelta = Vector2.zero;

        Text btnText = btnTextObj.AddComponent<Text>();
        btnText.font = font;
        btnText.fontSize = 22;
        btnText.fontStyle = FontStyle.Bold;
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.white;
        btnText.text = "GRAJ OD NOWA";

        // Domyślnie plansza GameOver jest ukryta
        gameOverPanel.SetActive(false);
    }

    #endregion
}
