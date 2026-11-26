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
