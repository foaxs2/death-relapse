using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

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

    [Header("Google Sheets Sync")]
    [Tooltip("URL Web App (Google Apps Script) nhận POST JSON. Để trống = chỉ lưu cục bộ, không gửi mạng.")]
    [SerializeField] private string googleSheetsWebhookUrl = "https://script.google.com/macros/s/AKfycbyKMGYLU9kXVd2Z-3XXnX877mOXHu4EcfF5R68q87cTkG-Qh-c2HBiZLrlJfxZe5wYQrA/exec";

    [Tooltip("Tên file bảng tính CSV lưu trong Application.persistentDataPath")]
    [SerializeField] private string csvFileName = "telemetry_report.csv";

    [Tooltip("Tên file hàng đợi các bản ghi chưa gửi được (gửi bù ở lần khởi động sau)")]
    [SerializeField] private string unsentQueueFileName = "telemetry_unsent_queue.jsonl";

    [Tooltip("Thời gian chờ tối đa của mỗi request (giây)")]
    [Min(1)]
    [SerializeField] private int requestTimeoutSeconds = 8;

    private const string CsvHeader = "Timestamp,SessionID,ExperimentGroup,MatchResult,ElapsedSeconds,PlayerHP,BossHP,TotalBossAttacks,TotalHitsTaken,TotalDodgedIFrame,AttackBreakdown,SurveyAnswer,SyncStatus";

    // Chỉ gửi bù hàng đợi 1 lần mỗi lần mở game (scene reload khi Replay sẽ tạo lại component này)
    private static bool hasFlushedQueueThisRun;

    /// <summary>Kết quả đồng bộ báo cho UI.</summary>
    public enum SyncResult
    {
        LocalOnly,      // Không cấu hình webhook, chỉ lưu cục bộ
        Synced,         // Đã gửi lên Google Sheets
        QueuedOffline   // Gửi lỗi, đã lưu vào hàng đợi offline
    }

    // 12 trường payload gửi lên Google Sheets (tên khớp yêu cầu để Apps Script đọc)
    [Serializable]
    private class MatchReportPayload
    {
        public string timestamp;
        public string sessionId;
        public string experimentGroup;
        public string matchResult;
        public float elapsedSeconds;
        public float playerHP;
        public float bossHP;
        public int totalBossAttacks;
        public int totalHitsTaken;
        public int totalDodgedIFrame;
        public string attackBreakdown;
        public string surveyAnswer;
    }

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
        if (string.IsNullOrWhiteSpace(googleSheetsWebhookUrl))
        {
            googleSheetsWebhookUrl = "https://script.google.com/macros/s/AKfycbyKMGYLU9kXVd2Z-3XXnX877mOXHu4EcfF5R68q87cTkG-Qh-c2HBiZLrlJfxZe5wYQrA/exec";
        }
        InitializeSession();
    }

    private void Start()
    {
        if (!hasFlushedQueueThisRun && !string.IsNullOrWhiteSpace(googleSheetsWebhookUrl))
        {
            hasFlushedQueueThisRun = true;
            StartCoroutine(FlushUnsentQueueRoutine());
        }
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

    /// <summary>
    /// Ghi báo cáo cuối trận (kết quả + thống kê đòn + khảo sát): lưu JSONL và CSV cục bộ,
    /// sau đó gửi lên Google Sheets nếu có webhook. onDone báo kết quả đồng bộ cho UI.
    /// </summary>
    public void SubmitMatchReport(
        string matchResult, float playerHP, float bossHP,
        int totalBossAttacks, int totalHitsTaken, int totalDodgedIFrame,
        string attackBreakdown, string surveyAnswer, Action<SyncResult> onDone)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            InitializeSession();
        }

        MatchReportPayload payload = new MatchReportPayload
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            sessionId = sessionId,
            experimentGroup = experimentGroup,
            matchResult = matchResult,
            elapsedSeconds = Mathf.Round(Mathf.Max(0f, Time.realtimeSinceStartup - sessionStartTime) * 1000f) / 1000f,
            playerHP = Mathf.Round(playerHP * 100f) / 100f,
            bossHP = Mathf.Round(bossHP * 100f) / 100f,
            totalBossAttacks = totalBossAttacks,
            totalHitsTaken = totalHitsTaken,
            totalDodgedIFrame = totalDodgedIFrame,
            attackBreakdown = attackBreakdown ?? string.Empty,
            surveyAnswer = surveyAnswer ?? string.Empty
        };

        string json = JsonUtility.ToJson(payload);

        // 1. Lưu cục bộ trước để không mất dữ liệu dù mạng hỏng
        AppendLine(logFilePath, json);

        if (string.IsNullOrWhiteSpace(googleSheetsWebhookUrl))
        {
            AppendCsvRow(payload, "LocalOnly");
            onDone?.Invoke(SyncResult.LocalOnly);
            return;
        }

        // 2. Gửi lên cloud (coroutine, không chặn game)
        StartCoroutine(SendReportRoutine(payload, json, onDone));
    }

    private IEnumerator SendReportRoutine(MatchReportPayload payload, string json, Action<SyncResult> onDone)
    {
        bool ok = false;
        yield return PostJson(json, success => ok = success);

        if (ok)
        {
            AppendCsvRow(payload, "Synced");
            onDone?.Invoke(SyncResult.Synced);
        }
        else
        {
            // Lưu hàng đợi để gửi bù ở lần khởi động sau
            AppendLine(Path.Combine(Application.persistentDataPath, unsentQueueFileName), json);
            AppendCsvRow(payload, "QueuedOffline");
            onDone?.Invoke(SyncResult.QueuedOffline);
        }
    }

    /// <summary>
    /// POST JSON lên webhook. Google Apps Script trả 302 sang URL kết quả; UnityWebRequest tự theo redirect
    /// nên request vẫn tính là thành công khi cuối cùng nhận 2xx. Mọi lỗi chỉ cảnh báo, không ném exception.
    /// </summary>
    private IEnumerator PostJson(string json, Action<bool> onResult)
    {
        using (UnityWebRequest request = new UnityWebRequest(googleSheetsWebhookUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success
                           && request.responseCode >= 200 && request.responseCode < 300;
            if (!success)
            {
                Debug.LogWarning($"[TelemetryLogger] Gửi Google Sheets thất bại ({request.result}, HTTP {request.responseCode}): {request.error}");
            }
            onResult?.Invoke(success);
        }
    }

    /// <summary>Gửi bù các bản ghi trong hàng đợi offline; mỗi bản gửi thành công sẽ bị xóa khỏi file.</summary>
    private IEnumerator FlushUnsentQueueRoutine()
    {
        string queuePath = Path.Combine(Application.persistentDataPath, unsentQueueFileName);
        if (!File.Exists(queuePath)) yield break;

        List<string> pending = new List<string>();
        try
        {
            foreach (string line in File.ReadAllLines(queuePath, Encoding.UTF8))
            {
                if (!string.IsNullOrWhiteSpace(line)) pending.Add(line);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TelemetryLogger] Không đọc được hàng đợi offline: {ex.Message}");
            yield break;
        }

        while (pending.Count > 0)
        {
            bool ok = false;
            yield return PostJson(pending[0], success => ok = success);
            if (!ok) break; // Vẫn mất mạng: giữ lại phần còn lại cho lần sau

            pending.RemoveAt(0);
            try
            {
                if (pending.Count == 0) File.Delete(queuePath);
                else File.WriteAllLines(queuePath, pending, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TelemetryLogger] Không cập nhật được hàng đợi offline: {ex.Message}");
                break;
            }
        }
    }

    private void AppendCsvRow(MatchReportPayload p, string syncStatus)
    {
        string path = Path.Combine(Application.persistentDataPath, csvFileName);
        try
        {
            if (!File.Exists(path))
            {
                File.AppendAllText(path, CsvHeader + Environment.NewLine, Encoding.UTF8);
            }

            string row = string.Join(",",
                CsvEscape(p.timestamp), CsvEscape(p.sessionId), CsvEscape(p.experimentGroup), CsvEscape(p.matchResult),
                p.elapsedSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                p.playerHP.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                p.bossHP.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                p.totalBossAttacks, p.totalHitsTaken, p.totalDodgedIFrame,
                CsvEscape(p.attackBreakdown), CsvEscape(p.surveyAnswer), CsvEscape(syncStatus));
            File.AppendAllText(path, row + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TelemetryLogger] Lỗi khi ghi CSV '{path}': {ex.Message}");
        }
    }

    // Bọc ngoặc kép vì AttackBreakdown chứa dấu phẩy
    private static string CsvEscape(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    private static void AppendLine(string path, string line)
    {
        try
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TelemetryLogger] Lỗi khi ghi file '{path}': {ex.Message}");
        }
    }}
