using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Lõi C# thuần xử lý bảo mật và toàn vẹn dữ liệu Telemetry (không phụ thuộc UnityEngine).
/// Dùng chung cho cả Unity EditMode Tests và pipeline .NET CI (dotnet test).
/// </summary>
public static class TelemetrySecurityCore
{
    public const string ClientVersion = "v0.9";

    // Khớp phần đuôi chữ ký được gắn vào cuối đối tượng JSON phẳng
    private static readonly Regex SignedSuffixRegex = new Regex(
        ",\"timestampUnix\":(-?\\d+),\"recordHash\":\"([0-9a-fA-F]{64})\"\\}$",
        RegexOptions.Compiled);

    private static readonly Regex EmptySignedObjectRegex = new Regex(
        "^\\{\"timestampUnix\":(-?\\d+),\"recordHash\":\"([0-9a-fA-F]{64})\"\\}$",
        RegexOptions.Compiled);

    // Đọc các trường đếm nguyên trong payload báo cáo trận đấu để kiểm tra bất biến T = H + D + M
    private static readonly Regex TotalAttacksRegex = new Regex("\"totalBossAttacks\":(-?\\d+)", RegexOptions.Compiled);
    private static readonly Regex HitsTakenRegex = new Regex("\"totalHitsTaken\":(-?\\d+)", RegexOptions.Compiled);
    private static readonly Regex DodgedRegex = new Regex("\"totalDodgedIFrame\":(-?\\d+)", RegexOptions.Compiled);
    private static readonly Regex BreakdownEntryRegex = new Regex("\\(T:(-?\\d+),H:(-?\\d+),D:(-?\\d+),M:(-?\\d+)\\)", RegexOptions.Compiled);

    /// <summary>
    /// Làm sạch giá trị chuỗi trước khi xuất CSV/Google Sheets nhằm chặn CSV/Formula Injection (OWASP A03).
    /// Mặc định tiền tố hóa dấu nháy đơn (') nếu có nguy cơ kích hoạt công thức và loại bỏ ký tự điều khiển độc hại.
    /// </summary>
    public static string SanitizeSpreadsheetField(string value)
    {
        return SanitizeSpreadsheetField(value, false);
    }

    /// <summary>
    /// Làm sạch hoặc từ chối (ném ArgumentException khi rejectDangerous = true) chuỗi có nguy cơ Formula Injection
    /// hoặc chứa ký tự điều khiển độc hại.
    /// </summary>
    public static string SanitizeSpreadsheetField(string value, bool rejectDangerous)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        bool hasLeadingFormulaTrigger = IsFormulaTriggerChar(value[0]);
        bool hasControlChars = false;

        // Kiểm tra thêm trường hợp kẻ tấn công chèn khoảng trắng đầu dòng rồi mới đặt ký tự công thức
        int firstNonSpaceIndex = -1;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (IsDangerousControlChar(c))
            {
                hasControlChars = true;
            }

            if (firstNonSpaceIndex < 0 && c != ' ')
            {
                firstNonSpaceIndex = i;
            }
        }

        if (firstNonSpaceIndex >= 0 && IsFormulaTriggerChar(value[firstNonSpaceIndex]))
        {
            hasLeadingFormulaTrigger = true;
        }

        if (rejectDangerous && (hasLeadingFormulaTrigger || hasControlChars))
        {
            throw new ArgumentException("Phát hiện chuỗi đầu vào có nguy cơ CSV/Formula Injection hoặc chứa ký tự điều khiển độc hại.", nameof(value));
        }

        // Lọc bỏ toàn bộ ký tự điều khiển độc hại (\0, \r, \n, \t và các mã ASCII điều khiển)
        StringBuilder cleaned = new StringBuilder(value.Length + 1);
        if (hasLeadingFormulaTrigger)
        {
            cleaned.Append('\'');
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (IsDangerousControlChar(c))
            {
                continue;
            }
            cleaned.Append(c);
        }

        // Phòng trường hợp sau khi lọc ký tự điều khiển đầu chuỗi lại lộ ra ký tự công thức (=, +, -, @)
        if (!hasLeadingFormulaTrigger && cleaned.Length > 0 && IsFormulaTriggerChar(cleaned[0]))
        {
            cleaned.Insert(0, '\'');
        }

        return cleaned.ToString();
    }

    /// <summary>
    /// Kiểm tra nhanh chuỗi có chứa dấu hiệu Formula Injection hoặc ký tự điều khiển độc hại hay không.
    /// </summary>
    public static bool IsDangerousSpreadsheetField(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (IsFormulaTriggerChar(value[0]))
        {
            return true;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (IsDangerousControlChar(value[i]))
            {
                return true;
            }
        }

        string trimmedStart = value.TrimStart(' ');
        return trimmedStart.Length > 0 && IsFormulaTriggerChar(trimmedStart[0]);
    }

    /// <summary>
    /// Xác minh mọi bộ đếm đều không âm và bảo toàn bất biến T == H + D + M.
    /// Dùng kiểu long khi cộng để tránh tràn số nguyên (integer overflow).
    /// </summary>
    public static bool ValidateTelemetryInvariants(int total, int hit, int dodged, int missed)
    {
        return ValidateTelemetryInvariants(total, hit, dodged, missed, out _);
    }

    /// <summary>
    /// Xác minh bất biến T == H + D + M và trả về lý do chi tiết nếu vi phạm.
    /// </summary>
    public static bool ValidateTelemetryInvariants(int total, int hit, int dodged, int missed, out string errorReason)
    {
        if (total < 0 || hit < 0 || dodged < 0 || missed < 0)
        {
            errorReason = $"InvariantViolation: Giá trị thống kê không được âm (T={total}, H={hit}, D={dodged}, M={missed}).";
            return false;
        }

        long sum = (long)hit + dodged + missed;
        if (sum != total)
        {
            errorReason = $"InvariantViolation: Tổng T ({total}) != H + D + M ({hit} + {dodged} + {missed} = {sum}).";
            return false;
        }

        errorReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Tính chữ ký HMAC-SHA256 dạng hex chữ thường cho bản ghi Telemetry.
    /// Nếu không có secretKey (chế độ dev cục bộ chưa cấu hình khóa), fallback sang SHA-256 để vẫn phát hiện sửa file tay.
    /// </summary>
    public static string ComputeRecordHmacSha256(string canonicalPayload, string secretKey)
    {
        byte[] payloadBytes = Encoding.UTF8.GetBytes(canonicalPayload ?? string.Empty);
        byte[] hashBytes;

        if (!string.IsNullOrEmpty(secretKey))
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(secretKey);
            using (HMACSHA256 hmac = new HMACSHA256(keyBytes))
            {
                hashBytes = hmac.ComputeHash(payloadBytes);
            }
        }
        else
        {
            using (SHA256 sha = SHA256.Create())
            {
                hashBytes = sha.ComputeHash(payloadBytes);
            }
        }

        return ToLowerHex(hashBytes);
    }

    /// <summary>
    /// Xác minh toàn vẹn bản ghi bằng phép so sánh thời gian hằng số (fixed-time comparison)
    /// để tránh rò rỉ thông tin qua timing attack và phát hiện hành vi sửa file cục bộ.
    /// </summary>
    public static bool VerifyRecordIntegrity(string canonicalPayload, string expectedSignature, string secretKey)
    {
        if (canonicalPayload == null || string.IsNullOrWhiteSpace(expectedSignature))
        {
            return false;
        }

        string computedSignature = ComputeRecordHmacSha256(canonicalPayload, secretKey);
        string normalizedExpected = expectedSignature.Trim().ToLowerInvariant();
        return FixedTimeEquals(computedSignature, normalizedExpected);
    }

    /// <summary>
    /// Tạo chuỗi chuẩn hóa kết hợp timestampUnix và phần JSON gốc để ký số.
    /// </summary>
    public static string BuildCanonicalPayload(string coreJsonObject, long timestampUnix)
    {
        return string.Concat(timestampUnix.ToString(CultureInfo.InvariantCulture), ".", coreJsonObject ?? string.Empty);
    }

    /// <summary>
    /// Gắn timestampUnix và recordHash vào đối tượng JSON phẳng mà vẫn giữ nguyên các trường cấp 1
    /// để tương thích ngược với JsonUtility và Google Apps Script.
    /// </summary>
    public static string CreateSignedEnvelopeJson(string rawJsonObject, string secretKey)
    {
        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return CreateSignedEnvelopeJson(rawJsonObject, nowUnix, secretKey, out _);
    }

    /// <summary>
    /// Gắn timestampUnix chỉ định và recordHash vào đối tượng JSON phẳng.
    /// </summary>
    public static string CreateSignedEnvelopeJson(string rawJsonObject, long timestampUnix, string secretKey)
    {
        return CreateSignedEnvelopeJson(rawJsonObject, timestampUnix, secretKey, out _);
    }

    /// <summary>
    /// Gắn timestampUnix và recordHash vào cuối đối tượng JSON, đồng thời trả về mã chữ ký qua tham số out.
    /// </summary>
    public static string CreateSignedEnvelopeJson(string rawJsonObject, long timestampUnix, string secretKey, out string recordHash)
    {
        if (string.IsNullOrWhiteSpace(rawJsonObject))
        {
            throw new ArgumentException("JSON đầu vào không được rỗng.", nameof(rawJsonObject));
        }

        string trimmed = rawJsonObject.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
        {
            throw new ArgumentException("JSON đầu vào phải là một đối tượng JSON hợp lệ bọc trong dấu { }.", nameof(rawJsonObject));
        }

        string canonicalPayload = BuildCanonicalPayload(trimmed, timestampUnix);
        recordHash = ComputeRecordHmacSha256(canonicalPayload, secretKey);

        string unixStr = timestampUnix.ToString(CultureInfo.InvariantCulture);
        if (trimmed == "{}")
        {
            return "{\"timestampUnix\":" + unixStr + ",\"recordHash\":\"" + recordHash + "\"}";
        }

        return trimmed.Substring(0, trimmed.Length - 1)
               + ",\"timestampUnix\":" + unixStr
               + ",\"recordHash\":\"" + recordHash + "\"}";
    }

    /// <summary>
    /// Xác minh một dòng JSONL đã ký: kiểm tra chữ ký recordHash và các bất biến thống kê nếu có.
    /// </summary>
    public static bool TryVerifySignedLine(string jsonLine, string secretKey, out string errorReason)
    {
        return TryVerifySignedLine(jsonLine, secretKey, out errorReason, out _, out _);
    }

    /// <summary>
    /// Xác minh một dòng JSONL đã ký và trích xuất recordHash, timestampUnix để gắn vào HTTP header.
    /// </summary>
    public static bool TryVerifySignedLine(
        string jsonLine,
        string secretKey,
        out string errorReason,
        out string extractedHash,
        out long extractedTimestampUnix)
    {
        extractedHash = string.Empty;
        extractedTimestampUnix = 0;

        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            errorReason = "TamperDetected: Dòng log rỗng.";
            return false;
        }

        string trimmed = jsonLine.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
        {
            errorReason = "TamperDetected: Định dạng đối tượng JSON không hợp lệ.";
            return false;
        }

        string coreJson;
        Match suffixMatch = SignedSuffixRegex.Match(trimmed);
        if (suffixMatch.Success)
        {
            if (!long.TryParse(suffixMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out extractedTimestampUnix))
            {
                errorReason = "TamperDetected: Trường timestampUnix không hợp lệ.";
                return false;
            }
            extractedHash = suffixMatch.Groups[2].Value;
            coreJson = trimmed.Substring(0, suffixMatch.Index) + "}";
        }
        else
        {
            Match emptyMatch = EmptySignedObjectRegex.Match(trimmed);
            if (!emptyMatch.Success)
            {
                errorReason = "TamperDetected: Thiếu trường bảo mật timestampUnix hoặc recordHash.";
                return false;
            }

            if (!long.TryParse(emptyMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out extractedTimestampUnix))
            {
                errorReason = "TamperDetected: Trường timestampUnix không hợp lệ.";
                return false;
            }
            extractedHash = emptyMatch.Groups[2].Value;
            coreJson = "{}";
        }

        string canonicalPayload = BuildCanonicalPayload(coreJson, extractedTimestampUnix);
        if (!VerifyRecordIntegrity(canonicalPayload, extractedHash, secretKey))
        {
            errorReason = "TamperDetected: Chữ ký toàn vẹn recordHash không khớp (bản ghi đã bị sửa đổi).";
            return false;
        }

        // Nếu bản ghi là báo cáo cuối trận có các trường đếm đòn đánh, xác minh thêm bất biến T = H + D + M
        if (!ValidateEmbeddedMatchCounters(coreJson, out string invariantError))
        {
            errorReason = "TamperDetected: " + invariantError;
            return false;
        }

        errorReason = string.Empty;
        return true;
    }

    private static bool ValidateEmbeddedMatchCounters(string coreJson, out string errorReason)
    {
        errorReason = string.Empty;

        Match totalMatch = TotalAttacksRegex.Match(coreJson);
        Match hitsMatch = HitsTakenRegex.Match(coreJson);
        Match dodgedMatch = DodgedRegex.Match(coreJson);

        if (totalMatch.Success || hitsMatch.Success || dodgedMatch.Success)
        {
            if (!totalMatch.Success || !hitsMatch.Success || !dodgedMatch.Success)
            {
                errorReason = "Thiếu một trong các trường đếm totalBossAttacks/totalHitsTaken/totalDodgedIFrame.";
                return false;
            }

            if (!int.TryParse(totalMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total) ||
                !int.TryParse(hitsMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hits) ||
                !int.TryParse(dodgedMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dodged))
            {
                errorReason = "Giá trị bộ đếm không phải số nguyên hợp lệ.";
                return false;
            }

            int missed = total - hits - dodged;
            if (!ValidateTelemetryInvariants(total, hits, dodged, missed, out errorReason))
            {
                return false;
            }
        }

        MatchCollection breakdownMatches = BreakdownEntryRegex.Matches(coreJson);
        for (int i = 0; i < breakdownMatches.Count; i++)
        {
            Match m = breakdownMatches[i];
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) ||
                !int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ||
                !int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) ||
                !int.TryParse(m.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int miss))
            {
                errorReason = "Không đọc được giá trị thống kê trong attackBreakdown.";
                return false;
            }

            if (!ValidateTelemetryInvariants(t, h, d, miss, out errorReason))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFormulaTriggerChar(char c)
    {
        return c == '=' || c == '+' || c == '-' || c == '@' || c == '\t' || c == '\r';
    }

    private static bool IsDangerousControlChar(char c)
    {
        return c < 0x20 || c == 0x7F;
    }

    // So sánh thời gian hằng số tự cài đặt để tương thích mọi phiên bản .NET Standard 2.1 / .NET 8
    private static bool FixedTimeEquals(string left, string right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] b = Encoding.UTF8.GetBytes(right ?? string.Empty);

        int diff = a.Length ^ b.Length;
        int minLength = a.Length < b.Length ? a.Length : b.Length;
        for (int i = 0; i < minLength; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }

    private static string ToLowerHex(byte[] bytes)
    {
        const string HexChars = "0123456789abcdef";
        char[] chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            byte value = bytes[i];
            chars[i * 2] = HexChars[value >> 4];
            chars[i * 2 + 1] = HexChars[value & 0x0F];
        }
        return new string(chars);
    }
}
