using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Bộ kiểm thử tự động EditMode cho Tuần 7 (v0.9 - Đóng băng tính năng):
/// 1) Chặn CSV/Formula Injection (OWASP A03).
/// 2) Phát hiện chỉnh sửa file log cục bộ (TamperDetected) bằng chữ ký toàn vẹn HMAC-SHA256.
/// 3) Từ chối bộ đếm vi phạm bất biến T = H + D + M hoặc số âm.
/// 4) Vá TC-12: Gửi bù hàng đợi ngoại tuyến khi bấm Replay, chỉ gửi bản ghi hợp lệ và chặn bản ghi bị sửa tay.
/// </summary>
public class SecurityAndWeek7EditModeTests
{
    private const string LogFileName = "telemetry_log.jsonl";
    private const string CsvFileName = "telemetry_report.csv";
    private const string UnsentQueueFileName = "telemetry_unsent_queue.jsonl";
    private const string TestSecretKey = "week7-unit-test-hmac-secret-key";

    private readonly List<GameObject> createdObjects = new List<GameObject>();
    private readonly Dictionary<string, byte[]> fileBackups = new Dictionary<string, byte[]>();
    private readonly Dictionary<string, bool> fileExistedBefore = new Dictionary<string, bool>();

    [SetUp]
    public void SetUp()
    {
        createdObjects.Clear();
        BackupAndClearPersistentFiles();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }
        }
        createdObjects.Clear();
        RestorePersistentFiles();
    }

    [Test]
    public void SanitizeSpreadsheetField_BlocksFormulaInjection()
    {
        string[] dangerousPayloads =
        {
            "=HYPERLINK(\"http://evil.example\",\"Click\")",
            "+cmd|' /C calc'!A0",
            "-1+1",
            "@SUM(1+1)*cmd|' /C calc'!A0",
            "\t=1+2",
            "\r=3+4"
        };

        foreach (string payload in dangerousPayloads)
        {
            // Chế độ mặc định: tiền tố hóa bằng dấu nháy đơn (') và lọc bỏ ký tự điều khiển độc hại
            string sanitized = TelemetrySecurityCore.SanitizeSpreadsheetField(payload);
            Assert.IsTrue(sanitized.StartsWith("'"),
                $"Payload nguy hiểm '{payload}' phải được tiền tố hóa bằng dấu nháy đơn. Thực tế: '{sanitized}'");
            Assert.IsFalse(sanitized.Contains("\t") || sanitized.Contains("\r") || sanitized.Contains("\n") || sanitized.Contains("\0"),
                $"Kết quả làm sạch không được chứa ký tự điều khiển độc hại. Thực tế: '{sanitized}'");

            // Chế độ từ chối nghiêm ngặt (rejectDangerous = true): phải ném ArgumentException
            Assert.Throws<ArgumentException>(() => TelemetrySecurityCore.SanitizeSpreadsheetField(payload, rejectDangerous: true),
                $"Chế độ rejectDangerous=true phải từ chối payload '{payload}'.");
        }

        // Giá trị bình thường của Telemetry phải giữ nguyên không bị biến đổi
        const string safeBreakdown = "HorizontalSlash(T:6,H:2,D:3,M:1);StrongRedAttack(T:3,H:1,D:1,M:1)";
        Assert.AreEqual(safeBreakdown, TelemetrySecurityCore.SanitizeSpreadsheetField(safeBreakdown),
            "Chuỗi thống kê hợp lệ không được bị sửa đổi ngoài ý muốn.");
    }

    [Test]
    public void VerifyRecordIntegrity_DetectsTamperedLocalLog()
    {
        const string rawMatchJson =
            "{\"timestamp\":\"26/11/2026 - 11h00p00s\",\"sessionId\":\"ab12cd34\"," +
            "\"experimentGroup\":\"GroupB_WithTelegraph\",\"matchResult\":\"Defeat\"," +
            "\"elapsedSeconds\":42.5,\"playerHP\":0,\"bossHP\":1200," +
            "\"totalBossAttacks\":8,\"totalHitsTaken\":3,\"totalDodgedIFrame\":3," +
            "\"attackBreakdown\":\"RedAttack(T:5,H:2,D:2,M:1);StrongRedAttack(T:3,H:1,D:1,M:1)\"," +
            "\"surveyAnswer\":\"PlayerSkill_Timing\"}";

        const long fixedUnix = 1795579200L;
        string signedLine = TelemetrySecurityCore.CreateSignedEnvelopeJson(rawMatchJson, fixedUnix, TestSecretKey, out string hash);

        Assert.IsFalse(string.IsNullOrEmpty(hash), "Chữ ký recordHash phải được tạo.");
        Assert.AreEqual(64, hash.Length, "Chữ ký HMAC-SHA256 dạng hex phải dài đúng 64 ký tự.");

        // 1. Bản ghi nguyên vẹn phải vượt qua xác minh
        Assert.IsTrue(
            TelemetrySecurityCore.TryVerifySignedLine(signedLine, TestSecretKey, out string validError),
            $"Bản ghi hợp lệ phải vượt qua TryVerifySignedLine. Lỗi: {validError}");

        string canonicalPayload = TelemetrySecurityCore.BuildCanonicalPayload(rawMatchJson, fixedUnix);
        Assert.IsTrue(
            TelemetrySecurityCore.VerifyRecordIntegrity(canonicalPayload, hash, TestSecretKey),
            "VerifyRecordIntegrity phải trả về true cho payload và chữ ký nguyên vẹn.");

        // 2. Sửa tay đúng 1 ký tự trong dòng log (bossHP: 1200 -> 1201) -> phải phát hiện TamperDetected
        string tamperedLine = signedLine.Replace("\"bossHP\":1200", "\"bossHP\":1201");
        Assert.AreNotEqual(signedLine, tamperedLine, "Test phải thực sự thay đổi ít nhất 1 ký tự của dòng log.");

        bool tamperedVerified = TelemetrySecurityCore.TryVerifySignedLine(tamperedLine, TestSecretKey, out string tamperError);
        Assert.IsFalse(tamperedVerified, "Dòng log bị sửa 1 ký tự phải bị từ chối bởi TryVerifySignedLine.");
        Assert.IsTrue(tamperError.Contains("TamperDetected"),
            $"Thông báo lỗi phải ghi rõ TamperDetected. Thực tế: '{tamperError}'");

        string tamperedCanonical = TelemetrySecurityCore.BuildCanonicalPayload(rawMatchJson.Replace("1200", "1201"), fixedUnix);
        Assert.IsFalse(
            TelemetrySecurityCore.VerifyRecordIntegrity(tamperedCanonical, hash, TestSecretKey),
            "VerifyRecordIntegrity phải trả về false khi payload đã bị sửa tay.");
    }

    [Test]
    public void ValidateTelemetryInvariants_RejectsInvalidCounts()
    {
        // Trường hợp hợp lệ
        Assert.IsTrue(TelemetrySecurityCore.ValidateTelemetryInvariants(10, 3, 4, 3));
        Assert.IsTrue(TelemetrySecurityCore.ValidateTelemetryInvariants(0, 0, 0, 0));

        // Trường hợp sai tổng T != H + D + M
        Assert.IsFalse(TelemetrySecurityCore.ValidateTelemetryInvariants(10, 3, 4, 2),
            "Phải từ chối khi T (10) != H + D + M (9).");
        Assert.IsFalse(TelemetrySecurityCore.ValidateTelemetryInvariants(5, 3, 3, 0),
            "Phải từ chối khi H + D (6) > T (5).");

        // Trường hợp có giá trị âm
        Assert.IsFalse(TelemetrySecurityCore.ValidateTelemetryInvariants(-1, 0, 0, 0),
            "Phải từ chối khi total < 0.");
        Assert.IsFalse(TelemetrySecurityCore.ValidateTelemetryInvariants(5, -1, 3, 3),
            "Phải từ chối khi hit < 0.");
        Assert.IsFalse(TelemetrySecurityCore.ValidateTelemetryInvariants(5, 2, -1, 4),
            "Phải từ chối khi dodged < 0.");
        Assert.IsFalse(TelemetrySecurityCore.ValidateTelemetryInvariants(5, 2, 3, -1),
            "Phải từ chối khi missed < 0.");
    }

    [Test]
    public void OfflineQueue_RetriesAndFlushesOnReplay_TC12()
    {
        // 1. Tạo TelemetryLogger và BattleLoopManager giả lập (0 HTTP thật nhờ transport override)
        GameObject managerGo = new GameObject("TestTC12Manager");
        createdObjects.Add(managerGo);

        TelemetryLogger logger = managerGo.AddComponent<TelemetryLogger>();
        SetPrivateField(logger, "googleSheetsWebhookUrl", TelemetryLogger.DefaultLocalProxyEndpoint);
        logger.SetRuntimeHmacKeyForTesting(TestSecretKey);
        logger.InitializeSession();

        BattleLoopManager battleLoop = managerGo.AddComponent<BattleLoopManager>();
        SetPrivateField(battleLoop, "telemetryLogger", logger);

        // 2. Ghi sẵn 2 bản ghi vào telemetry_unsent_queue.jsonl: 1 hợp lệ + 1 bị sửa tay sau khi ký
        const string validRawJson =
            "{\"timestamp\":\"26/11/2026 - 11h10p00s\",\"sessionId\":\"valid001\"," +
            "\"experimentGroup\":\"GroupA_NoTelegraph\",\"matchResult\":\"Defeat\"," +
            "\"elapsedSeconds\":35.2,\"playerHP\":0,\"bossHP\":1800," +
            "\"totalBossAttacks\":5,\"totalHitsTaken\":2,\"totalDodgedIFrame\":2," +
            "\"attackBreakdown\":\"RedAttack(T:5,H:2,D:2,M:1)\",\"surveyAnswer\":\"BossTelegraph_Unclear\"}";

        const string tamperedOriginRawJson =
            "{\"timestamp\":\"26/11/2026 - 11h12p00s\",\"sessionId\":\"tamper02\"," +
            "\"experimentGroup\":\"GroupA_NoTelegraph\",\"matchResult\":\"Defeat\"," +
            "\"elapsedSeconds\":28.4,\"playerHP\":0,\"bossHP\":2100," +
            "\"totalBossAttacks\":4,\"totalHitsTaken\":2,\"totalDodgedIFrame\":1," +
            "\"attackBreakdown\":\"RedAttack(T:4,H:2,D:1,M:1)\",\"surveyAnswer\":\"PlayerSkill_Timing\"}";

        string validSignedLine = TelemetrySecurityCore.CreateSignedEnvelopeJson(validRawJson, 1795579800L, TestSecretKey, out string validHash);
        string tamperedSignedLine = TelemetrySecurityCore.CreateSignedEnvelopeJson(tamperedOriginRawJson, 1795579920L, TestSecretKey, out _)
            .Replace("\"bossHP\":2100", "\"bossHP\":9999");

        string queuePath = logger.UnsentQueueFilePath;
        File.WriteAllLines(queuePath, new[] { validSignedLine, tamperedSignedLine }, Encoding.UTF8);

        // 3. Gắn mock transport để ghi nhận toàn bộ bản ghi được gửi đi (không phát sinh HTTP thật)
        List<string> sentPayloads = new List<string>();
        List<string> sentSignatures = new List<string>();
        logger.SetSyncTransportOverrideForTesting((payload, signature, timestampUnix) =>
        {
            sentPayloads.Add(payload);
            sentSignatures.Add(signature);
            return true;
        });

        // Kỳ vọng hệ thống ghi cảnh báo TamperDetected cho bản ghi giả mạo
        LogAssert.Expect(LogType.Warning, new Regex(@"\[TelemetrySecurity\] TamperDetected"));

        // 4. Mô phỏng người chơi bấm Replay (TC-12): BattleLoopManager.RestartBattle() phải tự flush hàng đợi
        battleLoop.RestartBattle();

        // 5. Xác minh: chỉ gửi đúng 1 bản ghi hợp lệ, chặn tuyệt đối bản ghi bị sửa tay, dọn sạch hàng đợi
        Assert.AreEqual(1, sentPayloads.Count,
            "Khi bấm Replay với hàng đợi gồm 1 bản ghi hợp lệ và 1 bản ghi bị sửa tay, chỉ được gửi đúng 1 bản ghi hợp lệ.");
        Assert.AreEqual(validSignedLine, sentPayloads[0],
            "Bản ghi gửi đi phải chính là bản ghi hợp lệ chưa bị sửa đổi.");
        Assert.AreEqual(validHash, sentSignatures[0],
            "Header X-DR-Signature gửi kèm phải khớp chữ ký recordHash của bản ghi hợp lệ.");
        Assert.IsFalse(File.Exists(queuePath),
            "File telemetry_unsent_queue.jsonl phải được xóa sạch sau khi đã gửi bản ghi hợp lệ và loại bỏ bản ghi giả mạo.");
    }

    [Test]
    public void CombatAudioController_TrimsSilenceAndSlicesFootsteps_WithoutBossFootsteps()
    {
        const int sampleRate = 24000;
        // 1. Tạo clip giả lập có 0.25s im lặng đầu + 0.20s xung âm thanh để kiểm tra cắt khoảng lặng
        int silenceSamples = (int)(0.25f * sampleRate);
        int activeSamples = (int)(0.20f * sampleRate);
        float[] slashData = new float[silenceSamples + activeSamples];
        for (int i = silenceSamples; i < slashData.Length; i++)
        {
            slashData[i] = 0.6f * Mathf.Sin(2f * Mathf.PI * 440f * i / sampleRate);
        }
        AudioClip rawSlashClip = AudioClip.Create("TestRawSlash", slashData.Length, 1, sampleRate, false);
        rawSlashClip.SetData(slashData, 0);

        // 2. Tạo clip bước chân giả lập dài 2.0s có 3 tiếng bước chân tách biệt tại 0.4s, 1.0s, 1.6s
        float[] footstepData = new float[sampleRate * 2];
        float[] stepTimes = { 0.4f, 1.0f, 1.6f };
        foreach (float st in stepTimes)
        {
            int center = (int)(st * sampleRate);
            int halfWidth = (int)(0.05f * sampleRate);
            for (int i = center - halfWidth; i <= center + halfWidth; i++)
            {
                footstepData[i] = 0.12f * Mathf.Sin(2f * Mathf.PI * 180f * i / sampleRate);
            }
        }
        AudioClip rawFootstepClip = AudioClip.Create("TestRawFootsteps", footstepData.Length, 1, sampleRate, false);
        rawFootstepClip.SetData(footstepData, 0);

        GameObject audioGo = new GameObject("TestCombatAudioController");
        createdObjects.Add(audioGo);

        try
        {
            CombatAudioController audioController = audioGo.AddComponent<CombatAudioController>();
            SetPrivateField(audioController, "swordSlash1Clip", rawSlashClip);
            SetPrivateField(audioController, "knightFootstepsClip", rawFootstepClip);

            MethodInfo awakeMethod = typeof(CombatAudioController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awakeMethod?.Invoke(audioController, null);

            // Kiểm tra đã cắt bỏ khoảng lặng đầu của Slash1 (ngắn hơn đáng kể so với 0.45s gốc)
            FieldInfo preparedSlash1Field = typeof(CombatAudioController).GetField("preparedSlash1", BindingFlags.Instance | BindingFlags.NonPublic);
            AudioClip preparedSlash1 = preparedSlash1Field?.GetValue(audioController) as AudioClip;
            Assert.IsNotNull(preparedSlash1, "Prepared Slash1 không được null.");
            Assert.Less(preparedSlash1.length, 0.30f, "Phải cắt bỏ được 0.25s khoảng lặng đầu clip.");

            // Kiểm tra tách đúng 3 biến thể bước chân đơn lẻ (~0.22s mỗi bước) thay vì giữ nguyên clip dài 2.0s
            Assert.AreEqual(3, audioController.FootstepVariantCount,
                "Phải trích xuất đúng 3 bước chân đơn lẻ từ clip nhiều bước.");
            FieldInfo preparedFootstepsField = typeof(CombatAudioController).GetField("preparedFootsteps", BindingFlags.Instance | BindingFlags.NonPublic);
            AudioClip[] preparedFootsteps = preparedFootstepsField?.GetValue(audioController) as AudioClip[];
            Assert.IsNotNull(preparedFootsteps);
            for (int i = 0; i < preparedFootsteps.Length; i++)
            {
                Assert.Less(preparedFootsteps[i].length, 0.35f, $"Mỗi bước chân tách ra phải ngắn gọn ({preparedFootsteps[i].length:F2}s).");
            }

            // Kiểm tra Boss KHÔNG có sự kiện hay âm thanh bước chân trên BossAttackController
            Assert.IsNull(typeof(BossAttackController).GetEvent("OnFootstepLanded"),
                "BossAttackController tuyệt đối không được có sự kiện bước chân.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rawSlashClip);
            UnityEngine.Object.DestroyImmediate(rawFootstepClip);
        }
    }

    [Test]
    public void PlayerAnimationEventRelay_BlocksFootstepsWhenIdleOrLocked()
    {
        GameObject knightGo = new GameObject("TestKnightRelay");
        createdObjects.Add(knightGo);

        PlayerMovement movement = knightGo.AddComponent<PlayerMovement>();
        PlayerDodge dodge = knightGo.AddComponent<PlayerDodge>();
        PlayerHealth health = knightGo.AddComponent<PlayerHealth>();
        PlayerAttackController attack = knightGo.AddComponent<PlayerAttackController>();
        PlayerAnimationEventRelay relay = knightGo.AddComponent<PlayerAnimationEventRelay>();

        MethodInfo relayAwake = typeof(PlayerAnimationEventRelay).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        relayAwake?.Invoke(relay, null);

        // Chặn kiểm tra CharacterController.velocity trong EditMode (khi chưa vào PlayMode velocity = 0)
        SetPrivateField(relay, "characterController", null);

        int footstepCount = 0;
        relay.OnFootstepLanded += () => footstepCount++;

        // 1. Đứng yên (CurrentInputDirection == zero) -> KHÔNG được phát tiếng bước chân
        SetPrivateField(movement, "currentInputDirection", Vector3.zero);
        relay.OnPlayerFootstep();
        Assert.AreEqual(0, footstepCount, "Khi đứng yên không được phát tiếng bước chân.");

        // 2. Có hướng di chuyển và đang ở trạng thái chạy hợp lệ -> PHÁT đúng 1 tiếng bước chân
        SetPrivateField(movement, "currentInputDirection", Vector3.forward);
        relay.OnPlayerFootstep();
        Assert.AreEqual(1, footstepCount, "Khi đang chạy hợp lệ phải phát 1 tiếng bước chân.");

        // 3. Gọi trùng ngay lập tức (trong minFootstepIntervalSeconds) -> BỊ CHẶN chống phát đúp
        relay.OnPlayerFootstep();
        Assert.AreEqual(1, footstepCount, "Không được phát đúp bước chân liên tiếp dưới ngưỡng minFootstepIntervalSeconds.");

        // 4. Đang bị khóa di chuyển (Overlay / Stun / Roll) -> KHÔNG được phát
        SetPrivateField(relay, "lastFootstepTime", -999f);
        movement.SetMovementLocked(true);
        relay.OnPlayerFootstep();
        Assert.AreEqual(1, footstepCount, "Khi IsMovementLocked=true không được phát tiếng bước chân.");

        // 5. Đang chém hoặc đang lăn -> KHÔNG được phát
        movement.SetMovementLocked(false);
        SetPrivateField(attack, "isAttacking", true);
        relay.OnPlayerFootstep();
        Assert.AreEqual(1, footstepCount, "Khi đang vung kiếm không được phát tiếng bước chân.");

        SetPrivateField(attack, "isAttacking", false);
        SetPrivateField(dodge, "isRolling", true);
        relay.OnPlayerFootstep();
        Assert.AreEqual(1, footstepCount, "Khi đang lăn né không được phát tiếng bước chân.");

        // 6. Đã chết -> KHÔNG được phát
        SetPrivateField(dodge, "isRolling", false);
        SetPrivateField(health, "isDead", true);
        relay.OnPlayerFootstep();
        Assert.AreEqual(1, footstepCount, "Khi đã chết không được phát tiếng bước chân.");
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
}
