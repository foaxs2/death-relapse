using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Quản lý vòng lặp trận đấu (Battle Loop):
/// - Ghi log Telemetry 3 mốc chính: SessionStart, PlayerDefeated, BossDefeated.
/// - Hiển thị màn hình Thắng (Victory) / Thua (Defeat) kèm nút bấm "Chơi lại".
/// - Tự động reset màn chơi nếu nhân vật rơi ra ngoài bản đồ (Y thấp hơn ngưỡng fallOutOfMapY).
/// </summary>
public class BattleLoopManager : MonoBehaviour
{
    [Header("Core References")]
    [Tooltip("Tham chiếu PlayerHealth (nếu để trống sẽ tự tìm trong Scene)")]
    [SerializeField] private PlayerHealth playerHealth;

    [Tooltip("Tham chiếu BossHealth (nếu để trống sẽ tự tìm trong Scene)")]
    [SerializeField] private BossHealth bossHealth;

    [Tooltip("Tham chiếu TelemetryLogger (nếu để trống sẽ tự tìm hoặc gắn thêm)")]
    [SerializeField] private TelemetryLogger telemetryLogger;

    [Header("Map Boundary Settings")]
    [Tooltip("Độ cao Y tối thiểu: nếu nhân vật rơi xuống thấp hơn mức này sẽ tự động reset màn chơi")]
    [SerializeField] private float fallOutOfMapY = -5f;

    [Header("End-of-Battle UI Panels")]
    [Tooltip("Bảng UI hiển thị khi người chơi thua (0 HP). Nếu để trống, script sẽ tự tạo bảng UI mặc định.")]
    [SerializeField] private GameObject defeatPanel;

    [Tooltip("Bảng UI hiển thị khi hạ gục Boss (0 HP). Nếu để trống, script sẽ tự tạo bảng UI mặc định.")]
    [SerializeField] private GameObject victoryPanel;

    [Tooltip("Nút Chơi Lại trên bảng Thua (tự động gắn sự kiện RestartBattle)")]
    [SerializeField] private Button defeatReplayButton;

    [Tooltip("Nút Chơi Lại trên bảng Thắng (tự động gắn sự kiện RestartBattle)")]
    [SerializeField] private Button victoryReplayButton;

    private bool isBattleEnded = false;
    private GameObject runtimeGeneratedOverlay;

    public bool IsBattleEnded => isBattleEnded;

    private void Awake()
    {
        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealth>();
        }

        if (bossHealth == null)
        {
            bossHealth = FindFirstObjectByType<BossHealth>();
        }

        if (telemetryLogger == null)
        {
            telemetryLogger = FindFirstObjectByType<TelemetryLogger>();
            if (telemetryLogger == null)
            {
                telemetryLogger = gameObject.AddComponent<TelemetryLogger>();
            }
        }

        if (defeatPanel != null)
        {
            defeatPanel.SetActive(false);
        }

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(false);
        }

        if (defeatReplayButton != null)
        {
            defeatReplayButton.onClick.RemoveListener(RestartBattle);
            defeatReplayButton.onClick.AddListener(RestartBattle);
        }

        if (victoryReplayButton != null)
        {
            victoryReplayButton.onClick.RemoveListener(RestartBattle);
            victoryReplayButton.onClick.AddListener(RestartBattle);
        }
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDied -= HandlePlayerDied;
            playerHealth.OnDied += HandlePlayerDied;
        }

        if (bossHealth != null)
        {
            bossHealth.OnDied -= HandleBossDied;
            bossHealth.OnDied += HandleBossDied;
        }
    }

    private void Start()
    {
        float initialPlayerHP = playerHealth != null ? playerHealth.CurrentHealth : 300f;
        float initialBossHP = bossHealth != null ? bossHealth.CurrentHealth : 3000f;

        if (telemetryLogger != null)
        {
            telemetryLogger.LogSessionStart(initialPlayerHP, initialBossHP);
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDied -= HandlePlayerDied;
        }

        if (bossHealth != null)
        {
            bossHealth.OnDied -= HandleBossDied;
        }
    }

    private void Update()
    {
        // Tự động reset lại màn chơi nếu nhân vật rơi ra ngoài bản đồ
        if (playerHealth != null && playerHealth.transform.position.y < fallOutOfMapY)
        {
            Debug.Log("[BattleLoopManager] Nhân vật rơi ra ngoài bản đồ -> Tự động nạp lại màn chơi.");
            RestartBattle();
        }
    }

    private void HandlePlayerDied()
    {
        if (isBattleEnded) return;
        isBattleEnded = true;

        float remainingBossHP = bossHealth != null ? bossHealth.CurrentHealth : 0f;
        if (telemetryLogger != null)
        {
            telemetryLogger.LogPlayerDefeated(0f, remainingBossHP);
        }

        if (defeatPanel != null)
        {
            defeatPanel.SetActive(true);
        }
        else
        {
            ShowFallbackEndScreen("YOU DIED", new Color(0.85f, 0.15f, 0.15f, 1f));
        }
    }

    private void HandleBossDied()
    {
        if (isBattleEnded) return;
        isBattleEnded = true;

        float remainingPlayerHP = playerHealth != null ? playerHealth.CurrentHealth : 0f;
        if (telemetryLogger != null)
        {
            telemetryLogger.LogBossDefeated(remainingPlayerHP, 0f);
        }

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
        }
        else
        {
            ShowFallbackEndScreen("VICTORY", new Color(1f, 0.82f, 0.2f, 1f));
        }
    }

    /// <summary>
    /// Khởi động lại trận đấu bằng cách nạp lại Scene hiện tại.
    /// Được gọi khi bấm nút "Chơi lại" trên màn hình Thắng/Thua hoặc khi rơi khỏi bản đồ.
    /// </summary>
    public void RestartBattle()
    {
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
    }

    /// <summary>
    /// Tự động dựng màn hình kết thúc kèm nút bấm "CHƠI LẠI" nếu chưa gán Panel trong Inspector.
    /// </summary>
    private void ShowFallbackEndScreen(string titleMessage, Color titleColor)
    {
        if (runtimeGeneratedOverlay != null)
        {
            Destroy(runtimeGeneratedOverlay);
        }

        Canvas targetCanvas = FindFirstObjectByType<Canvas>();
        if (targetCanvas == null)
        {
            GameObject canvasObj = new GameObject("BattleEndCanvas");
            targetCanvas = canvasObj.AddComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        runtimeGeneratedOverlay = new GameObject("RuntimeEndScreenOverlay");
        runtimeGeneratedOverlay.transform.SetParent(targetCanvas.transform, false);

        RectTransform overlayRect = runtimeGeneratedOverlay.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image bgImage = runtimeGeneratedOverlay.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.72f);

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 1. Dòng chữ tiêu đề (YOU DIED / VICTORY)
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(runtimeGeneratedOverlay.transform, false);
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0f, 60f);
        titleRect.sizeDelta = new Vector2(800f, 120f);

        Text titleText = titleObj.AddComponent<Text>();
        titleText.font = defaultFont;
        titleText.text = titleMessage;
        titleText.fontSize = 64;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = titleColor;

        // 2. Nút bấm "CHƠI LẠI"
        GameObject buttonObj = new GameObject("ReplayButton");
        buttonObj.transform.SetParent(runtimeGeneratedOverlay.transform, false);
        RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.anchoredPosition = new Vector2(0f, -50f);
        buttonRect.sizeDelta = new Vector2(260f, 64f);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.2f, 0.25f, 0.95f);

        Button replayBtn = buttonObj.AddComponent<Button>();
        replayBtn.targetGraphic = buttonImage;
        replayBtn.onClick.AddListener(RestartBattle);

        GameObject btnTextObj = new GameObject("ButtonText");
        btnTextObj.transform.SetParent(buttonObj.transform, false);
        RectTransform btnTextRect = btnTextObj.AddComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;

        Text btnText = btnTextObj.AddComponent<Text>();
        btnText.font = defaultFont;
        btnText.text = "CHƠI LẠI";
        btnText.fontSize = 26;
        btnText.fontStyle = FontStyle.Bold;
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.white;
    }
}
