using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Ghi log Telemetry ra file JSON Lines (telemetry_log.jsonl) và CSV tại Application.persistentDataPath,
/// có chữ ký toàn vẹn HMAC-SHA256/SHA-256, chống CSV/Formula Injection và gửi bù hàng đợi ngoại tuyến (TC-12).
/// </summary>
public class TelemetryLogger : MonoBehaviour
{
    public const string EnvEndpointVariable = "DR_TELEMETRY_ENDPOINT";
    public const string EnvHmacKeyVariable = "DR_TELEMETRY_HMAC_KEY";
    public const string RuntimeConfigFileName = "telemetry_runtime_config.json";
    public const string DefaultLocalProxyEndpoint = "http://127.0.0.1:8080/api/telemetry";

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

    [Serializable]
    private class RuntimeTelemetryConfig
    {
        public string telemetryEndpoint;
        public string hmacSecretKey;
    }

    [Header("Telemetry Configuration")]
    [Tooltip("Tên file log lưu trong Application.persistentDataPath")]
    [SerializeField] private string logFileName = "telemetry_log.jsonl";

    [Tooltip("Nhóm thí nghiệm hiện tại (ví dụ: GroupA_NoTelegraph hoặc GroupB_WithTelegraph)")]
    [SerializeField] private string experimentGroup = "GroupA_NoTelegraph";

    [Header("Telemetry Proxy / Sync (Không hardcode khóa hay URL bí mật trong source)")]
    [Tooltip("Endpoint nhận POST JSON. Nếu để trống sẽ nạp từ biến môi trường -> telemetry_runtime_config.json -> proxy cục bộ.")]
    [SerializeField] private string googleSheetsWebhookUrl = string.Empty;

    [Tooltip("Tên file bảng tính CSV lưu trong Application.persistentDataPath")]
    [SerializeField] private string csvFileName = "telemetry_report.csv";

    [Tooltip("Tên file hàng đợi các bản ghi chưa gửi được (gửi bù trong phiên hoặc khi Replay)")]
    [SerializeField] private string unsentQueueFileName = "telemetry_unsent_queue.jsonl";

    [Tooltip("Thời gian chờ tối đa của mỗi request (giây)")]
    [Min(1)]
    [SerializeField] private int requestTimeoutSeconds = 8;

    private const string CsvHeader = "Timestamp,SessionID,ExperimentGroup,MatchResult,ElapsedSeconds,PlayerHP,BossHP,TotalBossAttacks,TotalHitsTaken,TotalDodgedIFrame,AttackBreakdown,SurveyAnswer,SyncStatus";

    /// <summary>Kết quả đồng bộ báo cho UI.</summary>
    public enum SyncResult
    {
        LocalOnly,      // Không cấu hình endpoint, chỉ lưu cục bộ
        Synced,         // Đã gửi thành công tới Telemetry Proxy / Google Sheets
        QueuedOffline   // Gửi lỗi, đã lưu vào hàng đợi offline
    }

    // 12 trường payload gửi lên proxy/Google Sheets (tên khớp schema cũ để tương thích hoàn toàn)
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
    private string runtimeHmacKey = string.Empty;
    private bool isFlushingQueue;

    // Hook transport phục vụ unit test EditMode mà không bắn HTTP thật
    private Func<string, string, long, bool> syncTransportOverride;

    public string SessionId => sessionId;
    public string LogFilePath => logFilePath;
    public string UnsentQueueFilePath => Path.Combine(Application.persistentDataPath, unsentQueueFileName);
    public string ActiveEndpoint => googleSheetsWebhookUrl;
    public bool IsFlushingQueue => isFlushingQueue;

    public string ExperimentGroup
    {
        get => experimentGroup;
        set => experimentGroup = value;
    }

    private void Awake()
    {
        LoadRuntimeConfiguration();
        InitializeSession();
    }

    private void Start()
    {
        FlushOfflineQueueAsync();
    }

    /// <summary>
    /// Nạp cấu hình Endpoint và khóa HMAC theo thứ tự ưu tiên:
    /// 1) Biến môi trường (DR_TELEMETRY_ENDPOINT, DR_TELEMETRY_HMAC_KEY)
    /// 2) Tệp cấu hình ngoài telemetry_runtime_config.json (cạnh file thực thi hoặc persistentDataPath)
    /// 3) Mặc định dev cục bộ (http://127.0.0.1:8080/api/telemetry) + cảnh báo nếu thiếu khóa ký.
    /// </summary>
    public void LoadRuntimeConfiguration()
    {
        string resolvedEndpoint = string.Empty;
        string resolvedKey = string.Empty;

        // Ưu tiên 1: Biến môi trường hệ điều hành
        string envEndpoint = Environment.GetEnvironmentVariable(EnvEndpointVariable);
        string envKey = Environment.GetEnvironmentVariable(EnvHmacKeyVariable);

        if (!string.IsNullOrWhiteSpace(envEndpoint))
        {
            resolvedEndpoint = envEndpoint.Trim();
        }
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            resolvedKey = envKey.Trim();
        }

        // Ưu tiên 2: Tệp cấu hình ngoài không commit vào Git
        if (string.IsNullOrWhiteSpace(resolvedEndpoint) || string.IsNullOrWhiteSpace(resolvedKey))
        {
            if (TryLoadConfigFromExternalFile(out RuntimeTelemetryConfig fileConfig) && fileConfig != null)
            {
                if (string.IsNullOrWhiteSpace(resolvedEndpoint) && !string.IsNullOrWhiteSpace(fileConfig.telemetryEndpoint))
                {
                    resolvedEndpoint = fileConfig.telemetryEndpoint.Trim();
                }

                if (string.IsNullOrWhiteSpace(resolvedKey) && !string.IsNullOrWhiteSpace(fileConfig.hmacSecretKey))
                {
                    resolvedKey = fileConfig.hmacSecretKey.Trim();
                }
            }
        }

        // Nếu Inspector đã cấu hình endpoint riêng và chưa có biến môi trường/file đè lên thì giữ lại
        if (string.IsNullOrWhiteSpace(resolvedEndpoint) && !string.IsNullOrWhiteSpace(googleSheetsWebhookUrl))
        {
            resolvedEndpoint = googleSheetsWebhookUrl.Trim();
        }

        // Ưu tiên 3: Mặc định về Docker proxy cục bộ cho chế độ phát triển nội bộ
        if (string.IsNullOrWhiteSpace(resolvedEndpoint))
        {
            resolvedEndpoint = DefaultLocalProxyEndpoint;
        }

        googleSheetsWebhookUrl = resolvedEndpoint;
        runtimeHmacKey = resolvedKey;

        if (string.IsNullOrWhiteSpace(runtimeHmacKey))
        {
            Debug.LogWarning("[TelemetrySecurity] Thiếu khóa ký HMAC (DR_TELEMETRY_HMAC_KEY hoặc telemetry_runtime_config.json). Đang dùng SHA-256 cục bộ cho recordHash.");
        }
    }

    /// <summary>
    /// Cho phép cấu hình khóa ký trong EditMode test mà không cần đọc biến môi trường.
    /// </summary>
    public void SetRuntimeHmacKeyForTesting(string key)
    {
        runtimeHmacKey = key ?? string.Empty;
    }

    /// <summary>
    /// Gắn transport giả lập trong EditMode test để xác minh logic gửi bù TC-12 mà không phát sinh HTTP thật.
    /// </summary>
    public void SetSyncTransportOverrideForTesting(Func<string, string, long, bool> transportOverride)
    {
        syncTransportOverride = transportOverride;
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
    /// Ghi một bản ghi sự kiện dạng JSONL có kèm timestampUnix và recordHash vào telemetry_log.jsonl.
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
            timestamp = GetVietnamTimestamp(),
            sessionId = TelemetrySecurityCore.SanitizeSpreadsheetField(sessionId),
            experimentGroup = TelemetrySecurityCore.SanitizeSpreadsheetField(experimentGroup),
            eventName = TelemetrySecurityCore.SanitizeSpreadsheetField(eventName),
            playerHP = Mathf.Round(playerHP * 100f) / 100f,
            bossHP = Mathf.Round(bossHP * 100f) / 100f,
            elapsedSeconds = Mathf.Round(elapsed * 1000f) / 1000f
        };

        string rawJson = JsonUtility.ToJson(record);
        long timestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signedJsonLine = TelemetrySecurityCore.CreateSignedEnvelopeJson(rawJson, timestampUnix, runtimeHmacKey, out _);

        try
        {
            File.AppendAllText(logFilePath, signedJsonLine + Environment.NewLine, Encoding.UTF8);
            Debug.Log($"[TelemetryLogger] Đã ghi '{eventName}': {signedJsonLine}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TelemetryLogger] Lỗi khi ghi file '{logFilePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Ghi báo cáo cuối trận (kết quả + thống kê đòn + khảo sát): kiểm tra bất biến, làm sạch đầu vào,
    /// ký recordHash, lưu JSONL + CSV cục bộ, rồi gửi lên endpoint nếu có cấu hình.
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

        int missed = totalBossAttacks - totalHitsTaken - totalDodgedIFrame;
        if (!TelemetrySecurityCore.ValidateTelemetryInvariants(totalBossAttacks, totalHitsTaken, totalDodgedIFrame, missed, out string invariantError))
        {
            Debug.LogWarning($"[TelemetrySecurity] Bỏ qua gửi báo cáo do vi phạm bất biến số liệu: {invariantError}");
            onDone?.Invoke(SyncResult.LocalOnly);
            return;
        }

        MatchReportPayload payload = new MatchReportPayload
        {
            timestamp = GetVietnamTimestamp(),
            sessionId = TelemetrySecurityCore.SanitizeSpreadsheetField(sessionId),
            experimentGroup = TelemetrySecurityCore.SanitizeSpreadsheetField(experimentGroup),
            matchResult = TelemetrySecurityCore.SanitizeSpreadsheetField(matchResult),
            elapsedSeconds = Mathf.Round(Mathf.Max(0f, Time.realtimeSinceStartup - sessionStartTime) * 1000f) / 1000f,
            playerHP = Mathf.Round(playerHP * 100f) / 100f,
            bossHP = Mathf.Round(bossHP * 100f) / 100f,
            totalBossAttacks = totalBossAttacks,
            totalHitsTaken = totalHitsTaken,
            totalDodgedIFrame = totalDodgedIFrame,
            attackBreakdown = TelemetrySecurityCore.SanitizeSpreadsheetField(attackBreakdown ?? string.Empty),
            surveyAnswer = TelemetrySecurityCore.SanitizeSpreadsheetField(surveyAnswer ?? string.Empty)
        };

        string rawJson = JsonUtility.ToJson(payload);
        long timestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signedJson = TelemetrySecurityCore.CreateSignedEnvelopeJson(rawJson, timestampUnix, runtimeHmacKey, out _);

        // 1. Lưu cục bộ bản ghi đã ký trước để không mất dữ liệu dù mạng hỏng
        AppendLine(logFilePath, signedJson);

        if (string.IsNullOrWhiteSpace(googleSheetsWebhookUrl))
        {
            AppendCsvRow(payload, "LocalOnly");
            onDone?.Invoke(SyncResult.LocalOnly);
            return;
        }

        // 2. Gửi lên proxy/cloud (coroutine, không chặn gameplay)
        StartCoroutine(SendReportRoutine(payload, signedJson, onDone));
    }

    /// <summary>
    /// Vá TC-12: Gửi bù các bản ghi tồn đọng trong telemetry_unsent_queue.jsonl ngay trong phiên
    /// (ví dụ khi bấm Replay hoặc kết thúc trận mới), chống gọi chồng bằng cờ isFlushingQueue.
    /// </summary>
    public void FlushOfflineQueueAsync()
    {
        if (isFlushingQueue)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(googleSheetsWebhookUrl) && syncTransportOverride == null)
        {
            return;
        }

        string queuePath = UnsentQueueFilePath;
        if (!File.Exists(queuePath))
        {
            return;
        }

        // Trong EditMode test khi có transport giả lập, bước qua cả coroutine lồng nhau để kiểm chứng đồng bộ không cần PlayMode
        if (syncTransportOverride != null && !Application.isPlaying)
        {
            StepEnumeratorSynchronously(FlushUnsentQueueRoutine());
            return;
        }

        if (isActiveAndEnabled)
        {
            StartCoroutine(FlushUnsentQueueRoutine());
        }
    }

    private static void StepEnumeratorSynchronously(IEnumerator enumerator)
    {
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is IEnumerator nested)
            {
                StepEnumeratorSynchronously(nested);
            }
        }
    }

    private IEnumerator SendReportRoutine(MatchReportPayload payload, string json, Action<SyncResult> onDone)
    {
        string signedJson = EnsureSignedJson(json, out string signature, out long timestampUnix);

        bool ok = false;
        yield return PostJson(signedJson, signature, timestampUnix, success => ok = success);

        if (ok)
        {
            AppendCsvRow(payload, "Synced");
            onDone?.Invoke(SyncResult.Synced);
            // Tiện đường mạng vừa thông: thử gửi bù luôn hàng đợi ngoại tuyến còn tồn (TC-12)
            FlushOfflineQueueAsync();
        }
        else
        {
            // Lưu hàng đợi đã ký để gửi bù ngay khi bấm Replay hoặc ở lần mở sau
            AppendLine(UnsentQueueFilePath, signedJson);
            AppendCsvRow(payload, "QueuedOffline");
            onDone?.Invoke(SyncResult.QueuedOffline);
        }
    }

    /// <summary>
    /// POST JSON lên Telemetry Security Proxy hoặc Webhook kèm các HTTP header bảo mật:
    /// X-DR-Signature, X-DR-Timestamp và X-DR-Client-Version.
    /// </summary>
    private IEnumerator PostJson(string json, string signature, long timestampUnix, Action<bool> onResult)
    {
        if (syncTransportOverride != null)
        {
            bool mockOk = syncTransportOverride.Invoke(json, signature, timestampUnix);
            onResult?.Invoke(mockOk);
            yield break;
        }

        using (UnityWebRequest request = new UnityWebRequest(googleSheetsWebhookUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-DR-Signature", signature ?? string.Empty);
            request.SetRequestHeader("X-DR-Timestamp", timestampUnix.ToString(CultureInfo.InvariantCulture));
            request.SetRequestHeader("X-DR-Client-Version", TelemetrySecurityCore.ClientVersion);
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            bool success = request.result == UnityWebRequest.Result.Success
                           && request.responseCode >= 200 && request.responseCode < 300;
            if (!success)
            {
                Debug.LogWarning($"[TelemetryLogger] Gửi Telemetry thất bại ({request.result}, HTTP {request.responseCode}): {request.error}");
            }
            onResult?.Invoke(success);
        }
    }

    /// <summary>
    /// Gửi bù các bản ghi trong hàng đợi offline; trước khi gửi xác minh chữ ký recordHash và bất biến T = H + D + M.
    /// Bản ghi bị sửa tay sẽ bị đánh dấu TamperDetected, từ chối gửi và loại khỏi hàng đợi.
    /// </summary>
    private IEnumerator FlushUnsentQueueRoutine()
    {
        if (isFlushingQueue)
        {
            yield break;
        }

        isFlushingQueue = true;
        string queuePath = UnsentQueueFilePath;

        try
        {
            if (!File.Exists(queuePath))
            {
                yield break;
            }

            List<string> pending = new List<string>();
            try
            {
                foreach (string line in File.ReadAllLines(queuePath, Encoding.UTF8))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        pending.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TelemetryLogger] Không đọc được hàng đợi offline: {ex.Message}");
                yield break;
            }

            while (pending.Count > 0)
            {
                string currentLine = pending[0];

                // Biện pháp bảo mật #2: Chống sửa file log cục bộ trước khi gửi lên server
                if (!TelemetrySecurityCore.TryVerifySignedLine(currentLine, runtimeHmacKey, out string errorReason, out string signature, out long timestampUnix))
                {
                    Debug.LogWarning($"[TelemetrySecurity] TamperDetected: Từ chối gửi bản ghi bị chỉnh sửa trong hàng đợi ngoại tuyến. Lý do: {errorReason}");
                    pending.RemoveAt(0);
                    PersistQueueLines(queuePath, pending);
                    continue;
                }

                bool ok = false;
                yield return PostJson(currentLine, signature, timestampUnix, success => ok = success);
                if (!ok)
                {
                    // Vẫn mất mạng: giữ lại các bản ghi hợp lệ còn lại cho lần Replay/gửi bù tiếp theo
                    break;
                }

                pending.RemoveAt(0);
                PersistQueueLines(queuePath, pending);
            }
        }
        finally
        {
            isFlushingQueue = false;
        }
    }

    private string EnsureSignedJson(string json, out string signature, out long timestampUnix)
    {
        if (TelemetrySecurityCore.TryVerifySignedLine(json, runtimeHmacKey, out _, out signature, out timestampUnix))
        {
            return json;
        }

        timestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return TelemetrySecurityCore.CreateSignedEnvelopeJson(json, timestampUnix, runtimeHmacKey, out signature);
    }

    private bool TryLoadConfigFromExternalFile(out RuntimeTelemetryConfig config)
    {
        config = null;

        // Kiểm tra cạnh file thực thi (hoặc thư mục gốc project trong Editor) trước, rồi tới persistentDataPath
        string appDir = Path.GetDirectoryName(Application.dataPath);
        string[] candidatePaths =
        {
            !string.IsNullOrEmpty(appDir) ? Path.Combine(appDir, RuntimeConfigFileName) : string.Empty,
            Path.Combine(Application.persistentDataPath, RuntimeConfigFileName)
        };

        for (int i = 0; i < candidatePaths.Length; i++)
        {
            string path = candidatePaths[i];
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                RuntimeTelemetryConfig parsed = JsonUtility.FromJson<RuntimeTelemetryConfig>(raw);
                if (parsed != null)
                {
                    config = parsed;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TelemetrySecurity] Không đọc được tệp cấu hình '{path}': {ex.Message}");
            }
        }

        return false;
    }

    private static void PersistQueueLines(string queuePath, List<string> pending)
    {
        try
        {
            if (pending.Count == 0)
            {
                if (File.Exists(queuePath))
                {
                    File.Delete(queuePath);
                }
            }
            else
            {
                File.WriteAllLines(queuePath, pending, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TelemetryLogger] Không cập nhật được hàng đợi offline: {ex.Message}");
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
                p.elapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                p.playerHP.ToString("0.##", CultureInfo.InvariantCulture),
                p.bossHP.ToString("0.##", CultureInfo.InvariantCulture),
                p.totalBossAttacks, p.totalHitsTaken, p.totalDodgedIFrame,
                CsvEscape(p.attackBreakdown), CsvEscape(p.surveyAnswer), CsvEscape(syncStatus));
            File.AppendAllText(path, row + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TelemetryLogger] Lỗi khi ghi CSV '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Định dạng mốc thời gian theo múi giờ Việt Nam (UTC+7): Ngày/Tháng/Năm - GiờhPhútpGiâys (ví dụ: 12/11/2026 - 22h26p30s)
    /// </summary>
    private static string GetVietnamTimestamp()
    {
        DateTime vnTime = DateTime.UtcNow.AddHours(7);
        return vnTime.ToString("dd/MM/yyyy - HH\\hmm\\pss\\s");
    }

    // Làm sạch chống Formula Injection trước khi bọc ngoặc kép để bảo vệ CSV/Excel/Google Sheets
    private static string CsvEscape(string value)
    {
        string safeValue = TelemetrySecurityCore.SanitizeSpreadsheetField(value ?? string.Empty);
        return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
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
    }
}
