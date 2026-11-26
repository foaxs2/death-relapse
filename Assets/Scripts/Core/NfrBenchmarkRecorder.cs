using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/// <summary>
/// Công cụ đo thực tế NFR nhẹ (bật/tắt bằng phím F9, mặc định tắt khi không đo để giữ sạch giao diện).
/// - NFR-01: Đo FPS trung bình và 1% Low FPS trong phiên 60 giây (dùng mảng cấp phát sẵn -> 0 GC Alloc trong Update).
/// - NFR-04: Đo thời gian tải đấu trường (SceneLoadMs) bằng System.Diagnostics.Stopwatch.
/// Kết quả ghi ra Application.persistentDataPath/nfr_benchmark_report.txt.
/// </summary>
public class NfrBenchmarkRecorder : MonoBehaviour
{
    private const string ReportFileName = "nfr_benchmark_report.txt";

    [Header("Benchmark Configuration")]
    [Tooltip("Thời lượng phiên đo NFR-01 (giây)")]
    [Min(5f)]
    [SerializeField] private float measurementDurationSeconds = 60f;

    [Tooltip("Sức chứa tối đa số khung hình cấp phát sẵn (đủ cho 60s ở >270 FPS mà không cấp phát thêm bộ nhớ)")]
    [Min(1024)]
    [SerializeField] private int preallocatedSampleCapacity = 16384;

    [Tooltip("Chu kỳ cập nhật chữ hiển thị trên HUD đo (giây) để hạn chế overhead khi đang đo")]
    [Min(0.1f)]
    [SerializeField] private float hudRefreshIntervalSeconds = 0.5f;

    // Đo thời gian tải đấu trường (NFR-04) từ trước khi nạp Scene đến khi Scene sẵn sàng
    private static readonly Stopwatch SceneLoadStopwatch = new Stopwatch();
    private static double lastSceneLoadMs = 0.0;
    private static bool hooksRegistered = false;

    private InputAction toggleBenchmarkAction;
    private float[] frameDeltaSamples;
    private int sampleCount;
    private float elapsedSeconds;
    private float hudRefreshTimer;
    private bool isRecording;

    private GameObject hudRoot;
    private TMP_Text hudText;
    private readonly StringBuilder hudBuilder = new StringBuilder(256);

    public bool IsRecording => isRecording;
    public static double LastSceneLoadMs => lastSceneLoadMs;
    public string ReportFilePath => Path.Combine(Application.persistentDataPath, ReportFileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeSceneLoadStopwatch()
    {
        if (!hooksRegistered)
        {
            hooksRegistered = true;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        SceneLoadStopwatch.Restart();
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        SceneLoadStopwatch.Restart();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (SceneLoadStopwatch.IsRunning)
        {
            SceneLoadStopwatch.Stop();
            lastSceneLoadMs = SceneLoadStopwatch.Elapsed.TotalMilliseconds;
        }
    }

    private void Awake()
    {
        frameDeltaSamples = new float[Mathf.Max(1024, preallocatedSampleCapacity)];
        toggleBenchmarkAction = new InputAction("ToggleNfrBenchmark", InputActionType.Button, "<Keyboard>/f9");
    }

    private void Start()
    {
        // Trường hợp mở trực tiếp từ Editor nếu sceneLoaded chưa dừng đồng hồ thì chốt ngay frame Start đầu tiên
        if (SceneLoadStopwatch.IsRunning)
        {
            SceneLoadStopwatch.Stop();
            lastSceneLoadMs = SceneLoadStopwatch.Elapsed.TotalMilliseconds;
        }
    }

    private void OnEnable()
    {
        if (toggleBenchmarkAction != null)
        {
            toggleBenchmarkAction.performed += OnToggleBenchmarkPerformed;
            toggleBenchmarkAction.Enable();
        }
    }

    private void OnDisable()
    {
        if (toggleBenchmarkAction != null)
        {
            toggleBenchmarkAction.performed -= OnToggleBenchmarkPerformed;
            toggleBenchmarkAction.Disable();
        }
    }

    private void OnDestroy()
    {
        toggleBenchmarkAction?.Dispose();
    }

    private void Update()
    {
        // Mặc định tắt: thoát ngay lập tức với 0 GC Alloc và chi phí CPU gần bằng 0
        if (!isRecording)
        {
            return;
        }

        float dt = Time.unscaledDeltaTime;
        if (dt > 0f && sampleCount < frameDeltaSamples.Length)
        {
            frameDeltaSamples[sampleCount++] = dt;
        }

        elapsedSeconds += dt;
        hudRefreshTimer += dt;

        if (hudRefreshTimer >= hudRefreshIntervalSeconds)
        {
            hudRefreshTimer = 0f;
            RefreshHudStatus(false, 0f, 0f);
        }

        if (elapsedSeconds >= measurementDurationSeconds || sampleCount >= frameDeltaSamples.Length)
        {
            FinishAndWriteReport();
        }
    }

    private void OnToggleBenchmarkPerformed(InputAction.CallbackContext context)
    {
        if (isRecording)
        {
            FinishAndWriteReport();
        }
        else
        {
            StartBenchmarkSession();
        }
    }

    /// <summary>Bắt đầu phiên đo NFR-01 (60s) và hiện bảng nhỏ góc trên trái.</summary>
    public void StartBenchmarkSession()
    {
        sampleCount = 0;
        elapsedSeconds = 0f;
        hudRefreshTimer = 0f;
        isRecording = true;

        EnsureHud();
        if (hudRoot != null)
        {
            hudRoot.SetActive(true);
            hudRoot.transform.SetAsLastSibling();
        }

        RefreshHudStatus(false, 0f, 0f);
        Debug.Log($"[NfrBenchmarkRecorder] Bắt đầu phiên đo NFR ({measurementDurationSeconds:F0}s). Nhấn F9 lần nữa để dừng sớm và xuất báo cáo.");
    }

    /// <summary>Kết thúc phiên đo, tính Avg FPS + 1% Low FPS và xuất tệp nfr_benchmark_report.txt.</summary>
    public void FinishAndWriteReport()
    {
        if (!isRecording)
        {
            return;
        }

        isRecording = false;
        ComputeFpsMetrics(out float avgFps, out float onePercentLowFps);
        WriteReportFile(avgFps, onePercentLowFps);
        RefreshHudStatus(true, avgFps, onePercentLowFps);
    }

    private void ComputeFpsMetrics(out float avgFps, out float onePercentLowFps)
    {
        if (sampleCount <= 0 || elapsedSeconds <= 0f)
        {
            avgFps = 0f;
            onePercentLowFps = 0f;
            return;
        }

        avgFps = sampleCount / elapsedSeconds;

        // Sắp xếp tại chỗ mảng cấp phát sẵn để lấy 1% khung hình chậm nhất (deltaTime lớn nhất)
        Array.Sort(frameDeltaSamples, 0, sampleCount);
        int worstCount = Mathf.Max(1, Mathf.CeilToInt(sampleCount * 0.01f));
        double worstDeltaSum = 0.0;
        int startIndex = sampleCount - worstCount;
        for (int i = startIndex; i < sampleCount; i++)
        {
            worstDeltaSum += frameDeltaSamples[i];
        }

        double avgWorstDelta = worstDeltaSum / worstCount;
        onePercentLowFps = avgWorstDelta > 0.0 ? (float)(1.0 / avgWorstDelta) : 0f;
    }

    private void WriteReportFile(float avgFps, float onePercentLowFps)
    {
        string path = ReportFilePath;
        try
        {
            string timestampVn = DateTime.UtcNow.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            StringBuilder report = new StringBuilder(512);
            report.AppendLine("=== DEATH RELAPSE NFR BENCHMARK REPORT (v0.9) ===");
            report.AppendLine($"Timestamp (UTC+7): {timestampVn}");
            report.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
            report.AppendLine($"DurationSeconds: {elapsedSeconds.ToString("F2", CultureInfo.InvariantCulture)}");
            report.AppendLine($"SampledFrames: {sampleCount}");
            report.AppendLine($"NFR-01_AvgFPS: {avgFps.ToString("F2", CultureInfo.InvariantCulture)}");
            report.AppendLine($"NFR-01_1PercentLowFPS: {onePercentLowFps.ToString("F2", CultureInfo.InvariantCulture)}");
            report.AppendLine($"NFR-04_SceneLoadMs: {lastSceneLoadMs.ToString("F2", CultureInfo.InvariantCulture)}");
            report.AppendLine("=================================================");
            report.AppendLine();

            File.AppendAllText(path, report.ToString(), Encoding.UTF8);
            Debug.Log($"[NfrBenchmarkRecorder] Đã xuất báo cáo NFR -> {path} | AvgFPS={avgFps:F1}, 1%Low={onePercentLowFps:F1}, SceneLoadMs={lastSceneLoadMs:F1}ms");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NfrBenchmarkRecorder] Lỗi khi ghi báo cáo NFR '{path}': {ex.Message}");
        }
    }

    private void EnsureHud()
    {
        if (hudRoot != null) return;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        hudRoot = new GameObject("NfrBenchmarkHud", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        hudRoot.transform.SetParent(canvas.transform, false);

        RectTransform rect = hudRoot.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(18f, -18f);
        rect.sizeDelta = new Vector2(360f, 125f);

        Image bg = hudRoot.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.08f, 0.82f);
        bg.raycastTarget = false;

        GameObject textObj = new GameObject("NfrBenchmarkText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(hudRoot.transform, false);

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 10f);
        textRect.offsetMax = new Vector2(-12f, -10f);

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        TMP_Text refTmp = canvas.GetComponentInChildren<TMP_Text>(true);
        if (refTmp != null && refTmp.font != null)
        {
            tmp.font = refTmp.font;
        }
        tmp.fontSize = 18f;
        tmp.color = new Color(0.75f, 0.96f, 0.78f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        hudText = tmp;
    }

    private void RefreshHudStatus(bool finished, float finalAvgFps, float finalLowFps)
    {
        if (hudText == null) return;

        hudBuilder.Clear();
        if (!finished)
        {
            float curAvg = elapsedSeconds > 0f ? sampleCount / elapsedSeconds : 0f;
            hudBuilder.Append("[F9] ĐANG ĐO NFR: ")
                      .Append(elapsedSeconds.ToString("F1", CultureInfo.InvariantCulture))
                      .Append('/')
                      .Append(measurementDurationSeconds.ToString("F0", CultureInfo.InvariantCulture))
                      .AppendLine("s");
            hudBuilder.Append("Avg FPS tạm tính: ")
                      .AppendLine(curAvg.ToString("F1", CultureInfo.InvariantCulture));
            hudBuilder.Append("SceneLoadMs (NFR-04): ")
                      .Append(lastSceneLoadMs.ToString("F1", CultureInfo.InvariantCulture))
                      .Append(" ms");
        }
        else
        {
            hudBuilder.AppendLine("[F9] ĐÃ LƯU nfr_benchmark_report.txt");
            hudBuilder.Append("Avg FPS: ")
                      .Append(finalAvgFps.ToString("F1", CultureInfo.InvariantCulture))
                      .Append(" | 1% Low: ")
                      .AppendLine(finalLowFps.ToString("F1", CultureInfo.InvariantCulture));
            hudBuilder.Append("SceneLoadMs: ")
                      .Append(lastSceneLoadMs.ToString("F1", CultureInfo.InvariantCulture))
                      .Append(" ms (F9 để đo lại)");
        }

        hudText.text = hudBuilder.ToString();
    }
}
