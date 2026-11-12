using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

/// <summary>Chế độ Build thí nghiệm A/B, cấu hình cố định trong Inspector khi xuất bản.</summary>
public enum ExperimentBuildMode
{
    GroupA_NoTelegraph,
    GroupB_WithTelegraph
}

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

    [Tooltip("Tham chiếu BossAttackController (nếu để trống sẽ tự tìm trong Scene)")]
    [SerializeField] private BossAttackController bossAttackController;

    [Header("Experiment A/B (cố định theo Build, KHÔNG có nút đổi trên UI)")]
    [Tooltip("GroupA = không hiện vùng cảnh báo; GroupB = có vùng cảnh báo. Chỉnh trong Inspector trước khi build.")]
    [SerializeField] private ExperimentBuildMode buildMode = ExperimentBuildMode.GroupA_NoTelegraph;

    [Header("Survey UI (tự dựng trong Scene, chỉnh vị trí/kích thước/màu tùy ý)")]
    [Tooltip("GameObject cha chứa câu hỏi + 2 nút + dòng trạng thái. Script sẽ tắt lúc đầu và bật khi trận kết thúc.")]
    [SerializeField] private GameObject surveyRoot;

    [Tooltip("Nút trả lời A: giá trị gửi đi 'PlayerSkill_Timing'")]
    [SerializeField] private Button surveyButtonA;

    [Tooltip("Nút trả lời B: giá trị gửi đi 'BossTelegraph_Unclear'")]
    [SerializeField] private Button surveyButtonB;

    [Tooltip("Dòng chữ trạng thái đồng bộ (TextMeshPro, có thể để trống)")]
    [SerializeField] private TMP_Text surveyStatusText;

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

    // Dữ liệu chốt lúc kết thúc trận để gửi kèm khảo sát
    private string endMatchResult;
    private float endPlayerHP;
    private float endBossHP;
    private bool surveyAnswered;
    private Button surveyReplayButton;

    private const string SurveyValueSkill = "PlayerSkill_Timing";
    private const string SurveyValueTelegraph = "BossTelegraph_Unclear";
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

        if (bossAttackController == null)
        {
            bossAttackController = FindFirstObjectByType<BossAttackController>();
        }

        if (telemetryLogger == null)
        {
            telemetryLogger = FindFirstObjectByType<TelemetryLogger>();
            if (telemetryLogger == null)
            {
                telemetryLogger = gameObject.AddComponent<TelemetryLogger>();
            }
        }

        if (surveyRoot != null)
        {
            surveyRoot.SetActive(false);
        }

        if (surveyButtonA != null)
        {
            surveyButtonA.onClick.RemoveListener(OnClickSurveyA);
            surveyButtonA.onClick.AddListener(OnClickSurveyA);
        }

        if (surveyButtonB != null)
        {
            surveyButtonB.onClick.RemoveListener(OnClickSurveyB);
            surveyButtonB.onClick.AddListener(OnClickSurveyB);
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
        ApplyBuildMode();

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

    /// <summary>
    /// Áp dụng Build A/B: bật/tắt vùng cảnh báo, truyền tên nhóm cho Telemetry, xóa thống kê đòn của trận cũ.
    /// Gọi trong Start (mỗi lần nạp Scene, tức cũng là mỗi lần Replay).
    /// </summary>
    private void ApplyBuildMode()
    {
        bool withTelegraph = buildMode == ExperimentBuildMode.GroupB_WithTelegraph;

        if (bossAttackController != null)
        {
            bossAttackController.SetShowTelegraphVisuals(withTelegraph);
            bossAttackController.ResetAttackStatistics();
        }

        if (telemetryLogger != null)
        {
            telemetryLogger.ExperimentGroup = buildMode.ToString();
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

        endMatchResult = "Defeat";
        endPlayerHP = 0f;
        endBossHP = remainingBossHP;

        if (defeatPanel != null)
        {
            defeatPanel.SetActive(true);
            ShowSurvey(ResolveReplayButton(defeatPanel, defeatReplayButton));
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

        endMatchResult = "Victory";
        endPlayerHP = remainingPlayerHP;
        endBossHP = 0f;

        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);
            ShowSurvey(ResolveReplayButton(victoryPanel, victoryReplayButton));
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
        ShowSurvey(replayBtn);

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

    private Button ResolveReplayButton(GameObject panel, Button assigned)
    {
        return assigned != null ? assigned : panel.GetComponentInChildren<Button>(true);
    }

    /// <summary>
    /// Hiện khối khảo sát do bạn dựng sẵn trong Scene (surveyRoot) và khóa Replay cho tới khi trả lời.
    /// </summary>
    private void ShowSurvey(Button replayButton)
    {
        surveyAnswered = false;
        surveyReplayButton = replayButton;

        if (surveyRoot == null || surveyButtonA == null || surveyButtonB == null)
        {
            Debug.LogWarning("[BattleLoopManager] Chưa gán đủ Survey Root / Survey Button A / Survey Button B trong Inspector -> bỏ qua khảo sát.");
            return;
        }

        if (replayButton != null)
        {
            replayButton.interactable = false;
        }

        surveyButtonA.interactable = true;
        surveyButtonB.interactable = true;
        if (surveyStatusText != null)
        {
            surveyStatusText.text = string.Empty;
        }

        surveyRoot.SetActive(true);
        // Đảm bảo khối khảo sát vẽ phía trên bảng Thắng/Thua cùng cấp
        surveyRoot.transform.SetAsLastSibling();
    }

    private void OnClickSurveyA() => OnSurveyAnswered(SurveyValueSkill);

    private void OnClickSurveyB() => OnSurveyAnswered(SurveyValueTelegraph);
    /// <summary>
    /// Người chơi chọn đáp án: khóa 2 nút, ghi báo cáo (kết quả + thống kê đòn + khảo sát), rồi mở khóa Replay khi xong.
    /// </summary>
    private void OnSurveyAnswered(string answerValue)
    {
        if (surveyAnswered) return;
        surveyAnswered = true;

        surveyButtonA.interactable = false;
        surveyButtonB.interactable = false;
        if (surveyStatusText != null)
        {
            surveyStatusText.text = "Đang đồng bộ dữ liệu nghiên cứu...";
        }

        int totalCasts = 0, totalHits = 0, totalDodges = 0;
        string breakdown = string.Empty;
        if (bossAttackController != null)
        {
            bossAttackController.GetAttackStatisticsSummary(out totalCasts, out totalHits, out totalDodges, out breakdown);
        }

        if (telemetryLogger == null)
        {
            FinishSurvey(TelemetryLogger.SyncResult.LocalOnly);
            return;
        }

        telemetryLogger.SubmitMatchReport(endMatchResult, endPlayerHP, endBossHP,
            totalCasts, totalHits, totalDodges, breakdown, answerValue, FinishSurvey);
    }

    private void FinishSurvey(TelemetryLogger.SyncResult result)
    {
        // Callback có thể chạy sau khi scene bị nạp lại (ví dụ rơi khỏi map) nên kiểm tra null
        if (surveyStatusText != null)
        {
            surveyStatusText.text = result == TelemetryLogger.SyncResult.QueuedOffline
                ? "Không kết nối được mạng: đã lưu vào bộ nhớ đệm Offline. Bạn có thể bấm Replay."
                : "Đã lưu thành công! Bạn có thể bấm Replay.";
        }
        if (surveyReplayButton != null)
        {
            surveyReplayButton.interactable = true;
        }
    }}
