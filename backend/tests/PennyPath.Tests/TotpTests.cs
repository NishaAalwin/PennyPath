using System.Text;
using PennyPath.Application.Account;
using PennyPath.Infrastructure.Account;

namespace PennyPath.Tests;

public class TotpTests
{
    // RFC 6238 test secret "12345678901234567890" (SHA-1). The RFC lists 8-digit codes; ours are the last 6 digits.
    private static readonly byte[] Rfc = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void It_produces_the_codes_in_the_rfc_6238_test_vectors(long unixSeconds, string expected)
    {
        Assert.Equal(expected, Totp.CodeFor(Rfc, Totp.StepOf(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
    }

    [Fact]
    public void Base32_round_trips_and_matches_the_known_encoding()
    {
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32(Rfc));
        Assert.Equal(Rfc, Totp.FromBase32("gezd gnbv".Replace(" ", "") + "GY3TQOJQGEZDGNBVGY3TQOJQ"));
        var random = Totp.NewSecret();
        Assert.Equal(random, Totp.FromBase32(Totp.ToBase32(random)));
        Assert.Null(Totp.FromBase32("not base32 !"));
    }

    [Fact]
    public void New_secrets_are_20_random_bytes()
    {
        var a = Totp.NewSecret();
        Assert.Equal(20, a.Length);
        Assert.NotEqual(a, Totp.NewSecret());
    }

    [Fact]
    public void The_current_code_and_one_step_either_side_are_accepted_but_not_further()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepOf(now);

        Assert.Equal(step, Totp.Verify(Rfc, Totp.CodeFor(Rfc, step), now, 0));
        Assert.Equal(step - 1, Totp.Verify(Rfc, Totp.CodeFor(Rfc, step - 1), now, 0));
        Assert.Equal(step + 1, Totp.Verify(Rfc, Totp.CodeFor(Rfc, step + 1), now, 0));
        Assert.Null(Totp.Verify(Rfc, Totp.CodeFor(Rfc, step - 2), now, 0));
        Assert.Null(Totp.Verify(Rfc, Totp.CodeFor(Rfc, step + 2), now, 0));
    }

    [Fact]
    public void A_code_that_was_already_accepted_cannot_be_used_again()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepOf(now);
        var used = Totp.Verify(Rfc, Totp.CodeFor(Rfc, step), now, 0)!.Value;

        Assert.Null(Totp.Verify(Rfc, Totp.CodeFor(Rfc, step), now, used));
        Assert.Null(Totp.Verify(Rfc, Totp.CodeFor(Rfc, step - 1), now, used)); // an older code cannot rewind it either
        Assert.Equal(step + 1, Totp.Verify(Rfc, Totp.CodeFor(Rfc, step + 1), now, used));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("      ")]
    public void Malformed_codes_are_refused(string? code)
    {
        Assert.Null(Totp.Verify(Rfc, code, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), 0));
    }

    [Fact]
    public void A_wrong_code_is_refused()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var right = Totp.CodeFor(Rfc, Totp.StepOf(now));
        var wrong = (int.Parse(right) + 1) % 1_000_000;

        Assert.Null(Totp.Verify(Rfc, wrong.ToString("D6"), now, 0));
    }

    [Fact]
    public void The_authenticator_address_names_the_app_and_the_secret_with_standard_settings()
    {
        var uri = Totp.OtpAuthUri("PennyPath", "asha@example.com", Rfc);

        Assert.StartsWith("otpauth://totp/PennyPath:asha%40example.com?", uri);
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri);
        Assert.Contains("issuer=PennyPath", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void Recovery_codes_are_random_formatted_and_unique()
    {
        var codes = MfaVerifier.NewRecoveryCodes();

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[a-hj-km-np-z2-9]{5}-[a-hj-km-np-z2-9]{5}$", c));
    }

    [Fact]
    public void A_recovery_code_matches_however_it_is_typed()
    {
        var code = MfaVerifier.NewRecoveryCodes(1)[0];
        var hash = MfaVerifier.HashRecoveryCode(MfaVerifier.Normalize(code));

        Assert.Equal(hash, MfaVerifier.HashRecoveryCode(MfaVerifier.Normalize(" " + code.ToUpperInvariant().Replace("-", " ") + " ")));
        Assert.NotEqual(code.Replace("-", ""), hash); // stored as a hash, not the code
    }
}
