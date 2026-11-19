using System.Security.Cryptography;
using System.Text;

namespace PennyPath.Application.Account;

/// <summary>Time-based one-time passwords (RFC 6238, HMAC-SHA1, 6 digits, 30-second steps), as authenticator apps expect.</summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepOf(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string CodeFor(byte[] secret, long step)
    {
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Checks a code against the current step and one either side (clock drift). A step at or before
    /// <paramref name="lastUsedStep"/> is refused, so a code that was already accepted cannot be replayed.
    /// </summary>
    /// <returns>The step the code belongs to, or null when it is wrong or already used.</returns>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit)) return null;
        var current = StepOf(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastUsedStep) continue;
            // Compared in constant time so the check does not leak how many digits were right.
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeFor(secret, step)), Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    public static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[]? FromBase32(string text)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in text.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var v = Base32Alphabet.IndexOf(c);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }

    /// <summary>The address an authenticator app imports (shown as text; typing the key by hand works too).</summary>
    public static string OtpAuthUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}
