using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Ghi log Telemetry ra file JSON Lines (telemetry_log.jsonl) tại Application.persistentDataPath.
/// Phục vụ thu thập dữ liệu nghiên cứu thí nghiệm A/B cho đồ án Death Relapse.
/// </summary>
public class TelemetryLogger : MonoBehaviour
{
    [Serializable]
    public class TelemetryEventRecord
    {
        public string timestamp;
        public string sessionId;
        public string experimentGroup;
        public string eventName;
        public float playerHP;
        public float bossHP;
        public float elapsedSeconds;
    }

    [Header("Telemetry Configuration")]
    [Tooltip("Tên file log lưu trong Application.persistentDataPath")]
    [SerializeField] private string logFileName = "telemetry_log.jsonl";

    [Tooltip("Nhóm thí nghiệm hiện tại (ví dụ: GroupA_NoTelegraph hoặc GroupB_WithTelegraph)")]
    [SerializeField] private string experimentGroup = "GroupA_NoTelegraph";

    private string sessionId;
    private float sessionStartTime;
    private string logFilePath;

    public string SessionId => sessionId;
    public string LogFilePath => logFilePath;
    public string ExperimentGroup
    {
        get => experimentGroup;
        set => experimentGroup = value;
    }

    private void Awake()
    {
        InitializeSession();
    }

    /// <summary>
    /// Khởi tạo mã phiên mới và đường dẫn file ghi log.
    /// </summary>
    public void InitializeSession()
    {
        sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        sessionStartTime = Time.realtimeSinceStartup;
        logFilePath = Path.Combine(Application.persistentDataPath, logFileName);

        Debug.Log($"[TelemetryLogger] Khởi tạo phiên '{sessionId}' ({experimentGroup}) | File log: {logFilePath}");
    }

    /// <summary>
    /// Sự kiện 1: Ghi nhận thời điểm bắt đầu trận đấu (Player xuất hiện cùng Boss).
    /// </summary>
    public void LogSessionStart(float playerHP, float bossHP)
    {
        LogEvent("SessionStart", playerHP, bossHP);
    }

    /// <summary>
    /// Sự kiện 2: Ghi nhận khi người chơi bị Boss hạ gục (PlayerHealth.OnDied).
    /// </summary>
    public void LogPlayerDefeated(float playerHP, float bossHP)
    {
        LogEvent("PlayerDefeated", playerHP, bossHP);
    }

    /// <summary>
    /// Sự kiện 3: Ghi nhận khi Boss bị người chơi hạ gục (BossHealth.OnDied).
    /// </summary>
    public void LogBossDefeated(float playerHP, float bossHP)
    {
        LogEvent("BossDefeated", playerHP, bossHP);
    }

    /// <summary>
    /// Ghi một bản ghi sự kiện dạng JSON trên 1 dòng vào file telemetry_log.jsonl.
    /// </summary>
    public void LogEvent(string eventName, float playerHP, float bossHP)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            InitializeSession();
        }

        float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - sessionStartTime);

        TelemetryEventRecord record = new TelemetryEventRecord
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            sessionId = sessionId,
            experimentGroup = experimentGroup,
            eventName = eventName,
            playerHP = Mathf.Round(playerHP * 100f) / 100f,
            bossHP = Mathf.Round(bossHP * 100f) / 100f,
            elapsedSeconds = Mathf.Round(elapsed * 1000f) / 1000f
        };

        string jsonLine = JsonUtility.ToJson(record);

        try
        {
            File.AppendAllText(logFilePath, jsonLine + Environment.NewLine, Encoding.UTF8);
            Debug.Log($"[TelemetryLogger] Đã ghi '{eventName}': {jsonLine}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TelemetryLogger] Lỗi khi ghi file '{logFilePath}': {ex.Message}");
        }
    }
}

