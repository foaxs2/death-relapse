using System;
using NUnit.Framework;

namespace DeathRelapse.CoreTests
{
    /// <summary>
    /// Bộ kiểm thử NUnit .NET 8 chạy trực tiếp trên mã nguồn sản phẩm Assets/Scripts/Core/TelemetrySecurityCore.cs
    /// trong GitHub Actions CI (không sao chép mã nguồn trùng lặp).
    /// </summary>
    [TestFixture]
    public class TelemetrySecurityCoreTests
    {
        private const string TestSecretKey = "death-relapse-ci-core-hmac-secret-v09";

        private const string CrossLangRawJson =
            "{\"timestamp\":\"26/11/2026 - 12h00p00s\",\"sessionId\":\"sync0001\"," +
            "\"experimentGroup\":\"GroupA_NoTelegraph\",\"matchResult\":\"Defeat\"," +
            "\"elapsedSeconds\":30.5,\"playerHP\":0,\"bossHP\":1500," +
            "\"totalBossAttacks\":6,\"totalHitsTaken\":2,\"totalDodgedIFrame\":3," +
            "\"attackBreakdown\":\"RedAttack(T:6,H:2,D:3,M:1)\",\"surveyAnswer\":\"BossTelegraph_Unclear\"}";

        private const long CrossLangTimestampUnix = 1795600000L;
        private const string CrossLangSecretKey = "cross-lang-vector-secret-v09";

        // Vector vàng đối chiếu chéo 100% với telemetry-service/tests/test_security_api.py (Python FastAPI)
        private const string ExpectedCrossLangHmacHex =
            "583e1fc7e0d44bb978bf08a83f1d05314125dcfbdb32e59b00ca1ef4cf07d1c9";

        [Test]
        public void CrossLanguageHmacVector_MatchesPythonTelemetryProxyExactly()
        {
            string canonical = TelemetrySecurityCore.BuildCanonicalPayload(CrossLangRawJson, CrossLangTimestampUnix);
            string computed = TelemetrySecurityCore.ComputeRecordHmacSha256(canonical, CrossLangSecretKey);

            Assert.That(computed, Is.EqualTo(ExpectedCrossLangHmacHex),
                "Công thức ký HMAC-SHA256 bên C# phải khớp 100% từng ký tự với Python telemetry-service/app/security.py.");

            string signedLine = TelemetrySecurityCore.CreateSignedEnvelopeJson(
                CrossLangRawJson, CrossLangTimestampUnix, CrossLangSecretKey, out string outHash);
            Assert.That(outHash, Is.EqualTo(ExpectedCrossLangHmacHex));
            Assert.That(TelemetrySecurityCore.TryVerifySignedLine(signedLine, CrossLangSecretKey, out string err), Is.True, err);
        }

        [Test]
        public void ComputeAndVerifyHmac_ValidSignatureReturnsTrue_WrongKeyOrSignatureReturnsFalse()
        {
            string canonical = TelemetrySecurityCore.BuildCanonicalPayload(CrossLangRawJson, CrossLangTimestampUnix);
            string sig = TelemetrySecurityCore.ComputeRecordHmacSha256(canonical, TestSecretKey);

            Assert.That(sig.Length, Is.EqualTo(64));
            Assert.That(TelemetrySecurityCore.VerifyRecordIntegrity(canonical, sig, TestSecretKey), Is.True);
            Assert.That(TelemetrySecurityCore.VerifyRecordIntegrity(canonical, sig.ToUpperInvariant(), TestSecretKey), Is.True);
            Assert.That(TelemetrySecurityCore.VerifyRecordIntegrity(canonical, sig, "wrong-secret-key"), Is.False);
            Assert.That(TelemetrySecurityCore.VerifyRecordIntegrity(canonical, "deadbeef", TestSecretKey), Is.False);
            Assert.That(TelemetrySecurityCore.VerifyRecordIntegrity(canonical, string.Empty, TestSecretKey), Is.False);
        }

        [Test]
        public void TryVerifySignedLine_DetectsSingleCharacterTamperingInLocalLog()
        {
            string signedLine = TelemetrySecurityCore.CreateSignedEnvelopeJson(
                CrossLangRawJson, CrossLangTimestampUnix, TestSecretKey);

            string tamperedPayload = signedLine.Replace("\"bossHP\":1500", "\"bossHP\":1501");
            bool okPayload = TelemetrySecurityCore.TryVerifySignedLine(tamperedPayload, TestSecretKey, out string errPayload);
            Assert.That(okPayload, Is.False);
            Assert.That(errPayload, Does.Contain("TamperDetected"));

            string tamperedTimestamp = signedLine.Replace("1795600000", "1795600001");
            bool okTimestamp = TelemetrySecurityCore.TryVerifySignedLine(tamperedTimestamp, TestSecretKey, out string errTs);
            Assert.That(okTimestamp, Is.False);
            Assert.That(errTs, Does.Contain("TamperDetected"));
        }

        [Test]
        public void TryVerifySignedLine_RejectsMissingSecurityEnvelopeFields()
        {
            bool ok = TelemetrySecurityCore.TryVerifySignedLine(CrossLangRawJson, TestSecretKey, out string err);
            Assert.That(ok, Is.False);
            Assert.That(err, Does.Contain("TamperDetected"));
        }

        [TestCase("=HYPERLINK(\"http://evil.example\",\"Click\")")]
        [TestCase("+cmd|' /C calc'!A0")]
        [TestCase("-1+1")]
        [TestCase("@SUM(A1:A10)")]
        [TestCase("\t=1+2")]
        [TestCase("\r=3+4")]
        [TestCase("   =IMPORTXML(\"http://evil\",\"//a\")")]
        public void SanitizeSpreadsheetField_BlocksAndPrefixesFormulaInjection(string dangerousInput)
        {
            Assert.That(TelemetrySecurityCore.IsDangerousSpreadsheetField(dangerousInput), Is.True);

            string sanitized = TelemetrySecurityCore.SanitizeSpreadsheetField(dangerousInput);
            Assert.That(sanitized, Does.StartWith("'"));
            Assert.That(sanitized.IndexOf('\t'), Is.EqualTo(-1));
            Assert.That(sanitized.IndexOf('\r'), Is.EqualTo(-1));
            Assert.That(sanitized.IndexOf('\n'), Is.EqualTo(-1));
            Assert.That(sanitized.IndexOf('\0'), Is.EqualTo(-1));

            Assert.Throws<ArgumentException>(() =>
                TelemetrySecurityCore.SanitizeSpreadsheetField(dangerousInput, rejectDangerous: true));
        }

        [Test]
        public void SanitizeSpreadsheetField_PreservesNormalTelemetryStringsAndStripsControlChars()
        {
            const string safeBreakdown = "RedAttack(T:6,H:2,D:3,M:1);StrongRedAttack(T:3,H:1,D:1,M:1)";
            Assert.That(TelemetrySecurityCore.IsDangerousSpreadsheetField(safeBreakdown), Is.False);
            Assert.That(TelemetrySecurityCore.SanitizeSpreadsheetField(safeBreakdown), Is.EqualTo(safeBreakdown));

            string withControlChar = "PlayerSkill\0_Timing\n";
            Assert.That(TelemetrySecurityCore.IsDangerousSpreadsheetField(withControlChar), Is.True);
            Assert.That(TelemetrySecurityCore.SanitizeSpreadsheetField(withControlChar), Is.EqualTo("PlayerSkill_Timing"));
        }

        [Test]
        public void ValidateTelemetryInvariants_AcceptsValidConservedCounts()
        {
            Assert.That(TelemetrySecurityCore.ValidateTelemetryInvariants(0, 0, 0, 0), Is.True);
            Assert.That(TelemetrySecurityCore.ValidateTelemetryInvariants(12, 4, 5, 3), Is.True);
            Assert.That(TelemetrySecurityCore.ValidateTelemetryInvariants(500, 100, 200, 200), Is.True);
        }

        [TestCase(10, 4, 4, 1)]
        [TestCase(5, 3, 3, 0)]
        [TestCase(-1, 0, 0, 0)]
        [TestCase(5, -1, 3, 3)]
        [TestCase(5, 2, -1, 4)]
        [TestCase(5, 2, 3, -1)]
        [TestCase(int.MaxValue, int.MaxValue, 1, 0)]
        public void ValidateTelemetryInvariants_RejectsInvalidNegativesAndOverflow(int total, int hit, int dodged, int missed)
        {
            bool valid = TelemetrySecurityCore.ValidateTelemetryInvariants(total, hit, dodged, missed, out string reason);
            Assert.That(valid, Is.False);
            Assert.That(reason, Does.Contain("InvariantViolation"));
        }

        [Test]
        public void TryVerifySignedLine_RejectsSignedPayloadWithBrokenEmbeddedCounters()
        {
            const string brokenCountersJson =
                "{\"timestamp\":\"26/11/2026 - 12h00p00s\",\"sessionId\":\"bad00001\"," +
                "\"experimentGroup\":\"GroupA_NoTelegraph\",\"matchResult\":\"Defeat\"," +
                "\"elapsedSeconds\":20.0,\"playerHP\":0,\"bossHP\":2000," +
                "\"totalBossAttacks\":5,\"totalHitsTaken\":4,\"totalDodgedIFrame\":3," +
                "\"attackBreakdown\":\"RedAttack(T:5,H:4,D:3,M:0)\",\"surveyAnswer\":\"PlayerSkill_Timing\"}";

            string signedLine = TelemetrySecurityCore.CreateSignedEnvelopeJson(
                brokenCountersJson, CrossLangTimestampUnix, TestSecretKey);

            bool ok = TelemetrySecurityCore.TryVerifySignedLine(signedLine, TestSecretKey, out string err);
            Assert.That(ok, Is.False);
            Assert.That(err, Does.Contain("InvariantViolation"));
        }

        [Test]
        public void KeylessLocalFallbackSha256_StillDetectsTamperingDeterministically()
        {
            string signed = TelemetrySecurityCore.CreateSignedEnvelopeJson(
                CrossLangRawJson, CrossLangTimestampUnix, string.Empty, out string shaHex);

            Assert.That(shaHex.Length, Is.EqualTo(64));
            Assert.That(TelemetrySecurityCore.TryVerifySignedLine(signed, string.Empty, out _), Is.True);

            string tampered = signed.Replace("Defeat", "Victory");
            Assert.That(TelemetrySecurityCore.TryVerifySignedLine(tampered, string.Empty, out string err), Is.False);
            Assert.That(err, Does.Contain("TamperDetected"));
        }
    }
}
