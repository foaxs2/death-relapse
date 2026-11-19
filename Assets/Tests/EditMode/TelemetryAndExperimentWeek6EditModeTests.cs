using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Bộ kiểm thử tự động EditMode cho Tuần 6:
/// 1) Cách ly biến thí nghiệm A/B trên BattleLoopManager và bảo toàn thống kê đòn đánh của Boss.
/// 2) Tuần tự hóa 12 trường báo cáo Telemetry, định dạng giờ Việt Nam và hàng đợi ngoại tuyến.
/// </summary>
public class TelemetryAndExperimentWeek6EditModeTests
{
    private const string LogFileName = "telemetry_log.jsonl";
    private const string CsvFileName = "telemetry_report.csv";
    private const string UnsentQueueFileName = "telemetry_unsent_queue.jsonl";

    private readonly List<GameObject> createdObjects = new List<GameObject>();
    private readonly List<UnityEngine.Object> createdAssets = new List<UnityEngine.Object>();

    // Lưu bản sao dự phòng trong bộ nhớ để khôi phục nguyên trạng máy cục bộ sau khi test xong
    private readonly Dictionary<string, byte[]> fileBackups = new Dictionary<string, byte[]>();
    private readonly Dictionary<string, bool> fileExistedBefore = new Dictionary<string, bool>();

    [Serializable]
    private class DeserializedMatchReport
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

    [SetUp]
    public void SetUp()
    {
        createdObjects.Clear();
        createdAssets.Clear();
        BackupAndClearPersistentFiles();
    }

    [TearDown]
    public void TearDown()
    {
        // Hủy toàn bộ GameObject giả lập để tránh rò rỉ giữa các test
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
        createdObjects.Clear();

        // Hủy toàn bộ ScriptableObject tạm thời sinh ra trong lúc test
        for (int i = createdAssets.Count - 1; i >= 0; i--)
        {
            if (createdAssets[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(createdAssets[i]);
            }
        }
        createdAssets.Clear();

        RestorePersistentFiles();
    }

    [Test]
    public void BattleLoopAndBossStats_EnforcesABModeAndConservesAttackBreakdown()
    {
        // 1. Khởi tạo Player giả lập (PlayerDodge kéo theo PlayerMovement và CharacterController)
        GameObject playerGo = CreateTestGameObject("TestPlayer");
        PlayerDodge playerDodge = playerGo.AddComponent<PlayerDodge>();
        PlayerHealth playerHealth = playerGo.AddComponent<PlayerHealth>();
        CapsuleCollider playerCollider = playerGo.AddComponent<CapsuleCollider>();

        // Đặt Player ngay trước mặt Boss để nằm trong vùng cảnh báo, tránh LogWarning [VALIDITY]
        playerGo.transform.position = new Vector3(0f, 0f, 1.5f);

        // Tắt hitStun coroutine trong EditMode để tránh coroutine treo khi gây sát thương nhiều lần
        SetPrivateField(playerHealth, "hitStunDuration", 0f);
        InvokePrivateMethod(playerHealth, "Awake");

        // 2. Khởi tạo Boss và SwordHitbox giả lập (đủ bladeBase/bladeTip để không phát sinh Debug.LogError)
        GameObject bossGo = CreateTestGameObject("TestBoss");
        bossGo.transform.position = Vector3.zero;
        bossGo.transform.rotation = Quaternion.identity;

        BossHealth bossHealth = bossGo.AddComponent<BossHealth>();
        BossAttackController bossAttack = bossGo.AddComponent<BossAttackController>();

        GameObject swordGo = new GameObject("TestSwordHitbox");
        swordGo.transform.SetParent(bossGo.transform, false);
        SwordHitbox swordHitbox = swordGo.AddComponent<SwordHitbox>();

        GameObject bladeBaseGo = new GameObject("BladeBase");
        bladeBaseGo.transform.SetParent(swordGo.transform, false);
        GameObject bladeTipGo = new GameObject("BladeTip");
        bladeTipGo.transform.SetParent(swordGo.transform, false);

        SetPrivateField(swordHitbox, "bladeBase", bladeBaseGo.transform);
        SetPrivateField(swordHitbox, "bladeTip", bladeTipGo.transform);

        SetPrivateField(bossAttack, "swordHitbox", swordHitbox);
        SetPrivateField(bossAttack, "player", playerGo.transform);

        InvokePrivateMethod(bossHealth, "Awake");
        InvokePrivateMethod(bossAttack, "Awake");

        // 3. Khởi tạo BattleLoopManager + TelemetryLogger (vô hiệu hóa webhook để không gửi mạng)
        GameObject managerGo = CreateTestGameObject("TestBattleLoopManager");
        TelemetryLogger telemetryLogger = managerGo.AddComponent<TelemetryLogger>();
        SetPrivateField(telemetryLogger, "googleSheetsWebhookUrl", string.Empty);
        telemetryLogger.InitializeSession();

        BattleLoopManager battleLoop = managerGo.AddComponent<BattleLoopManager>();
        SetPrivateField(battleLoop, "playerHealth", playerHealth);
        SetPrivateField(battleLoop, "bossHealth", bossHealth);
        SetPrivateField(battleLoop, "bossAttackController", bossAttack);
        SetPrivateField(battleLoop, "telemetryLogger", telemetryLogger);
        InvokePrivateMethod(battleLoop, "Awake");

        // 4. Kiểm tra cách ly biến thí nghiệm A/B qua ApplyBuildMode()
        SetPrivateField(battleLoop, "buildMode", ExperimentBuildMode.GroupA_NoTelegraph);
        InvokePrivateMethod(battleLoop, "ApplyBuildMode");

        Assert.IsFalse(bossAttack.ShowTelegraphVisuals,
            "Khi ApplyBuildMode() với GroupA_NoTelegraph, showTelegraphVisuals phải là false.");
        Assert.AreEqual("GroupA_NoTelegraph", telemetryLogger.ExperimentGroup,
            "TelemetryLogger phải đồng bộ đúng tên nhóm GroupA_NoTelegraph.");

        SetPrivateField(battleLoop, "buildMode", ExperimentBuildMode.GroupB_WithTelegraph);
        InvokePrivateMethod(battleLoop, "ApplyBuildMode");

        Assert.IsTrue(bossAttack.ShowTelegraphVisuals,
            "Khi ApplyBuildMode() với GroupB_WithTelegraph, showTelegraphVisuals phải là true.");
        Assert.AreEqual("GroupB_WithTelegraph", telemetryLogger.ExperimentGroup,
            "TelemetryLogger phải đồng bộ đúng tên nhóm GroupB_WithTelegraph.");

        // 5. Tạo BossAttackData tạm và reset thống kê trước khi mô phỏng các tình huống tung đòn
        BossAttackData attackData = ScriptableObject.CreateInstance<BossAttackData>();
        createdAssets.Add(attackData);

        SetPrivateField(bossAttack, "currentAttack", attackData);
        bossAttack.ResetAttackStatistics();

        // Mô phỏng 4 lần tung đòn: 2 Trúng (H=2), 1 Né I-frame (D=1), 1 Hụt (M=1) -> T = 4
        SimulateAttackOutcome(bossAttack, attackData, playerGo.transform, playerDodge, playerCollider, AttackOutcome.Hit);
        SimulateAttackOutcome(bossAttack, attackData, playerGo.transform, playerDodge, playerCollider, AttackOutcome.DodgedIFrame);
        SimulateAttackOutcome(bossAttack, attackData, playerGo.transform, playerDodge, playerCollider, AttackOutcome.Miss);
        SimulateAttackOutcome(bossAttack, attackData, playerGo.transform, playerDodge, playerCollider, AttackOutcome.Hit);

        bossAttack.GetAttackStatisticsSummary(
            out int totalCasts,
            out int totalHits,
            out int totalIframeDodges,
            out string breakdown);

        Assert.AreEqual(4, totalCasts, "Tổng số lần tung đòn T phải bằng 4.");
        Assert.AreEqual(2, totalHits, "Tổng số lần trúng đòn H phải bằng 2.");
        Assert.AreEqual(1, totalIframeDodges, "Tổng số lần né I-frame D phải bằng 1.");

        // Phân tích chuỗi breakdown để xác minh phương trình bảo toàn T == H + D + M
        Match breakdownMatch = Regex.Match(breakdown, @"HorizontalSlash\(T:(\d+),H:(\d+),D:(\d+),M:(\d+)\)");
        Assert.IsTrue(breakdownMatch.Success,
            $"Chuỗi breakdown phải chứa đúng định dạng thống kê đòn đánh. Thực tế: '{breakdown}'");

        int parsedT = int.Parse(breakdownMatch.Groups[1].Value);
        int parsedH = int.Parse(breakdownMatch.Groups[2].Value);
        int parsedD = int.Parse(breakdownMatch.Groups[3].Value);
        int parsedM = int.Parse(breakdownMatch.Groups[4].Value);

        Assert.AreEqual(parsedH + parsedD + parsedM, parsedT,
            "Tổng số lần tung đòn phải bảo toàn: T == H + D + M.");
        Assert.AreEqual(totalCasts, parsedT, "Giá trị T trong breakdown phải khớp out totalCasts.");
        Assert.AreEqual(totalHits, parsedH, "Giá trị H trong breakdown phải khớp out totalHits.");
        Assert.AreEqual(totalIframeDodges, parsedD, "Giá trị D trong breakdown phải khớp out totalIframeDodges.");
        Assert.AreEqual(1, parsedM, "Số lần đánh hụt M phải bằng 1.");
    }

    [Test]
    public void TelemetryLogger_SubmitMatchReport_Serializes12FieldsAndQueuesOffline()
    {
        // 1. Khởi tạo TelemetryLogger và ép googleSheetsWebhookUrl = "" (không gọi Awake() để tránh tự điền URL thật)
        GameObject loggerGo = CreateTestGameObject("TestTelemetryLogger");
        TelemetryLogger logger = loggerGo.AddComponent<TelemetryLogger>();
        SetPrivateField(logger, "googleSheetsWebhookUrl", string.Empty);
        logger.InitializeSession();
        logger.ExperimentGroup = "GroupA_NoTelegraph";

        const string sampleMatchResult = "Defeat";
        const float samplePlayerHP = 0f;
        const float sampleBossHP = 1425.75f;
        const int sampleTotalAttacks = 9;
        const int sampleHitsTaken = 4;
        const int sampleDodgedIFrame = 3;
        const string sampleBreakdown = "HorizontalSlash(T:6,H:3,D:2,M:1);StrongRedAttack(T:3,H:1,D:1,M:1)";
        const string sampleSurveyAnswer = "BossTelegraph_Unclear";

        // 2. Gọi SubmitMatchReport với webhook rỗng -> chỉ lưu cục bộ, tuyệt đối không bắn HTTP request
        TelemetryLogger.SyncResult? localSyncResult = null;
        logger.SubmitMatchReport(
            sampleMatchResult,
            samplePlayerHP,
            sampleBossHP,
            sampleTotalAttacks,
            sampleHitsTaken,
            sampleDodgedIFrame,
            sampleBreakdown,
            sampleSurveyAnswer,
            result => localSyncResult = result);

        Assert.AreEqual(TelemetryLogger.SyncResult.LocalOnly, localSyncResult,
            "Khi URL webhook rỗng, SubmitMatchReport phải hoàn tất ở chế độ LocalOnly và không gửi mạng.");

        // 3. Kiểm tra file telemetry_log.jsonl đã ghi dòng mới đủ 12 trường
        string logPath = Path.Combine(Application.persistentDataPath, LogFileName);
        Assert.IsTrue(File.Exists(logPath), "File telemetry_log.jsonl phải được tạo sau khi SubmitMatchReport.");

        string[] logLines = File.ReadAllLines(logPath, Encoding.UTF8);
        Assert.GreaterOrEqual(logLines.Length, 1, "File telemetry_log.jsonl phải có ít nhất 1 dòng bản ghi.");
        string lastJsonLine = logLines[logLines.Length - 1].Trim();

        string[] expectedJsonKeys =
        {
            "timestamp",
            "sessionId",
            "experimentGroup",
            "matchResult",
            "elapsedSeconds",
            "playerHP",
            "bossHP",
            "totalBossAttacks",
            "totalHitsTaken",
            "totalDodgedIFrame",
            "attackBreakdown",
            "surveyAnswer"
        };

        foreach (string key in expectedJsonKeys)
        {
            Assert.IsTrue(lastJsonLine.Contains($"\"{key}\":"),
                $"Dòng JSON trong telemetry_log.jsonl thiếu trường '{key}'. JSON: {lastJsonLine}");
        }

        DeserializedMatchReport parsed = JsonUtility.FromJson<DeserializedMatchReport>(lastJsonLine);
        Assert.IsNotNull(parsed, "Phải giải tuần tự hóa (deserialize) được bản ghi JSON từ telemetry_log.jsonl.");

        Assert.AreEqual(logger.SessionId, parsed.sessionId);
        Assert.AreEqual("GroupA_NoTelegraph", parsed.experimentGroup);
        Assert.AreEqual(sampleMatchResult, parsed.matchResult);
        Assert.GreaterOrEqual(parsed.elapsedSeconds, 0f);
        Assert.AreEqual(samplePlayerHP, parsed.playerHP, 0.01f);
        Assert.AreEqual(sampleBossHP, parsed.bossHP, 0.01f);
        Assert.AreEqual(sampleTotalAttacks, parsed.totalBossAttacks);
        Assert.AreEqual(sampleHitsTaken, parsed.totalHitsTaken);
        Assert.AreEqual(sampleDodgedIFrame, parsed.totalDodgedIFrame);
        Assert.AreEqual(sampleBreakdown, parsed.attackBreakdown);
        Assert.AreEqual(sampleSurveyAnswer, parsed.surveyAnswer);

        // 4. Kiểm tra định dạng Timestamp giờ Việt Nam: dd/MM/yyyy - HHhMMpSSs
        Assert.IsTrue(
            Regex.IsMatch(parsed.timestamp ?? string.Empty, @"^\d{2}/\d{2}/\d{4} - \d{2}h\d{2}p\d{2}s$"),
            $"Timestamp '{parsed.timestamp}' không khớp định dạng giờ Việt Nam.");

        // 5. Kiểm tra luồng đưa vào hàng đợi ngoại tuyến (telemetry_unsent_queue.jsonl) khi chưa đồng bộ lên cloud:
        // Vì SubmitMatchReport với URL rỗng trả về LocalOnly sớm, ta điều khiển trực tiếp coroutine SendReportRoutine
        // qua 2 bước MoveNext() mà không thực thi PostJson, mô phỏng đồng bộ cloud thất bại với 0 HTTP request.
        Type payloadType = typeof(TelemetryLogger).GetNestedType("MatchReportPayload", BindingFlags.NonPublic);
        Assert.IsNotNull(payloadType, "Không tìm thấy kiểu nội bộ MatchReportPayload trong TelemetryLogger.");
        object payloadObj = JsonUtility.FromJson(lastJsonLine, payloadType);

        MethodInfo sendRoutineMethod = typeof(TelemetryLogger).GetMethod("SendReportRoutine", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(sendRoutineMethod, "Không tìm thấy phương thức SendReportRoutine trong TelemetryLogger.");

        TelemetryLogger.SyncResult? offlineSyncResult = null;
        Action<TelemetryLogger.SyncResult> onOfflineDone = res => offlineSyncResult = res;
        IEnumerator routine = (IEnumerator)sendRoutineMethod.Invoke(logger, new object[] { payloadObj, lastJsonLine, onOfflineDone });

        // Bước 1 dừng ngay tại yield return PostJson(...) nên UnityWebRequest không bao giờ được khởi tạo
        Assert.IsTrue(routine.MoveNext(), "SendReportRoutine phải tạm dừng ở bước đầu tiên (yield return PostJson).");
        // Bước 2 tiếp tục với ok == false, kích hoạt nhánh lưu hàng đợi offline
        Assert.IsFalse(routine.MoveNext(), "SendReportRoutine phải kết thúc sau khi ghi hàng đợi offline.");

        Assert.AreEqual(TelemetryLogger.SyncResult.QueuedOffline, offlineSyncResult,
            "Khi đồng bộ cloud thất bại, callback phải trả về SyncResult.QueuedOffline.");

        string unsentQueuePath = Path.Combine(Application.persistentDataPath, UnsentQueueFileName);
        Assert.IsTrue(File.Exists(unsentQueuePath),
            "File telemetry_unsent_queue.jsonl phải được tạo khi bản ghi chưa đồng bộ lên cloud.");

        string[] queuedLines = File.ReadAllLines(unsentQueuePath, Encoding.UTF8);
        Assert.AreEqual(1, queuedLines.Length, "Hàng đợi ngoại tuyến phải chứa đúng 1 bản ghi vừa thất bại.");
        Assert.AreEqual(lastJsonLine, queuedLines[0].Trim(),
            "Bản ghi trong telemetry_unsent_queue.jsonl phải khớp nguyên vẹn với dòng JSON đã lưu cục bộ.");
    }

    private enum AttackOutcome
    {
        Hit,
        DodgedIFrame,
        Miss
    }

    private void SimulateAttackOutcome(
        BossAttackController controller,
        BossAttackData attackData,
        Transform playerTransform,
        PlayerDodge playerDodge,
        Collider playerCollider,
        AttackOutcome outcome)
    {
        // Thiết lập trạng thái gồng đòn như AttackRoutine để NotifyHitWindowOpen/Close hợp lệ
        SetPrivateField(controller, "isAttacking", true);
        SetPrivateField(controller, "hitWindowOpened", false);
        SetPrivateField(controller, "hitWindowClosed", false);

        float duration = Mathf.Max(0.01f, attackData.TelegraphDuration);
        SetPrivateField(controller, "lockedCenter", controller.transform.position);
        SetPrivateField(controller, "lockedForward", controller.transform.forward);
        SetPrivateField(controller, "lockedRadius", attackData.Radius);
        SetPrivateField(controller, "lockedArcAngle", attackData.ArcAngleDegrees);
        SetPrivateField(controller, "lockedDuration", duration);
        SetPrivateField(controller, "lockedShape", attackData.ShapeType);
        SetPrivateField(controller, "attackTriggerRealtime", Time.realtimeSinceStartup - duration);

        controller.NotifyHitWindowOpen();

        if (outcome == AttackOutcome.Hit)
        {
            SetPrivateField(playerDodge, "isInvulnerable", false);
            InvokePrivateMethod(controller, "HandlePlayerHit", playerCollider);
        }
        else if (outcome == AttackOutcome.DodgedIFrame)
        {
            SetPrivateField(playerDodge, "isInvulnerable", true);
            InvokePrivateMethod(controller, "HandlePlayerHit", playerCollider);
        }

        controller.NotifyHitWindowClose();

        // Trả lại trạng thái nghỉ giữa các đòn mô phỏng
        SetPrivateField(playerDodge, "isInvulnerable", false);
        SetPrivateField(controller, "isAttacking", false);
    }

    private GameObject CreateTestGameObject(string name)
    {
        GameObject go = new GameObject(name);
        createdObjects.Add(go);
        return go;
    }

    private void BackupAndClearPersistentFiles()
    {
        fileBackups.Clear();
        fileExistedBefore.Clear();

        string[] fileNames = { LogFileName, CsvFileName, UnsentQueueFileName };
        foreach (string fileName in fileNames)
        {
            string fullPath = Path.Combine(Application.persistentDataPath, fileName);
            bool existed = File.Exists(fullPath);
            fileExistedBefore[fullPath] = existed;

            if (existed)
            {
                fileBackups[fullPath] = File.ReadAllBytes(fullPath);
                File.Delete(fullPath);
            }
        }
    }

    private void RestorePersistentFiles()
    {
        foreach (KeyValuePair<string, bool> entry in fileExistedBefore)
        {
            string fullPath = entry.Key;
            bool existed = entry.Value;

            if (existed)
            {
                if (fileBackups.TryGetValue(fullPath, out byte[] originalBytes))
                {
                    File.WriteAllBytes(fullPath, originalBytes);
                }
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        fileBackups.Clear();
        fileExistedBefore.Clear();
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Không tìm thấy field private '{fieldName}' trên {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static object InvokePrivateMethod(object target, string methodName, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"Không tìm thấy method private '{methodName}' trên {target.GetType().Name}.");
        return method.Invoke(target, args);
    }
}
