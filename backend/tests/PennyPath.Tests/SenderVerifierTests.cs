using PennyPath.Application.Gmail;

namespace PennyPath.Tests;

public class SenderVerifierTests
{
    private static SenderVerifier.Verdict Check(string? from, params string[] auth) =>
        SenderVerifier.Check(from, auth, GmailQuery.SenderDomains);

    private const string Good = "mx.google.com; dkim=pass header.i=@hdfcbank.net header.s=s header.b=x; spf=pass smtp.mailfrom=hdfcbank.net; dmarc=pass (p=REJECT) header.from=hdfcbank.net";

    [Fact]
    public void A_signed_message_from_a_known_bank_is_verified()
    {
        Assert.Equal(SenderVerifier.Verdict.Verified, Check("HDFC Bank <alerts@hdfcbank.net>", Good));
        Assert.Equal(SenderVerifier.Verdict.Verified, Check("alerts@hdfcbank.net", Good));
    }

    [Fact]
    public void A_subdomain_of_a_known_bank_signed_by_the_bank_is_verified()
    {
        Assert.Equal(SenderVerifier.Verdict.Verified, Check("a@alerts.hdfcbank.net",
            "mx.google.com; dkim=pass header.d=hdfcbank.net header.s=s; spf=pass smtp.mailfrom=alerts.hdfcbank.net"));
    }

    [Fact]
    public void A_dmarc_pass_alone_is_enough()
    {
        Assert.Equal(SenderVerifier.Verdict.Verified, Check("a@icicibank.com", "mx.google.com; dmarc=pass (p=QUARANTINE) header.from=icicibank.com"));
    }

    [Theory]
    [InlineData("a@hdfcbank.net.evil.example")]   // the bank's name as a prefix of someone else's domain
    [InlineData("a@evilhdfcbank.net")]            // the bank's name glued onto another word
    [InlineData("a@gmail.com")]
    [InlineData("hdfcbank.net")]                  // no address at all
    [InlineData("")]
    [InlineData(null)]
    public void Look_alike_and_unknown_senders_are_never_trusted_even_with_a_pass(string? from)
    {
        Assert.NotEqual(SenderVerifier.Verdict.Verified, Check(from, Good));
    }

    [Fact]
    public void Gmail_must_have_verified_the_signature()
    {
        Assert.Equal(SenderVerifier.Verdict.NotAuthenticated, Check("a@hdfcbank.net"));
        Assert.Equal(SenderVerifier.Verdict.NotAuthenticated, Check("a@hdfcbank.net", "mx.google.com; dkim=fail header.i=@hdfcbank.net; spf=fail smtp.mailfrom=hdfcbank.net"));
        Assert.Equal(SenderVerifier.Verdict.NotAuthenticated, Check("a@hdfcbank.net", "mx.google.com; dkim=neutral header.i=@hdfcbank.net; dmarc=none header.from=hdfcbank.net"));
    }

    [Fact]
    public void A_header_written_by_the_sender_instead_of_gmail_is_ignored()
    {
        // An attacker can put any text in a header; only the one that names Gmail's own server counts.
        Assert.Equal(SenderVerifier.Verdict.NotAuthenticated, Check("a@hdfcbank.net",
            "attacker.example; dkim=pass header.i=@hdfcbank.net; dmarc=pass header.from=hdfcbank.net"));
    }

    [Fact]
    public void A_pass_for_a_different_domain_does_not_vouch_for_the_banks_address()
    {
        // The message is signed, but by the attacker's own domain, not by the bank it claims to come from.
        Assert.Equal(SenderVerifier.Verdict.NotAuthenticated, Check("a@hdfcbank.net",
            "mx.google.com; dkim=pass header.i=@evil.example header.s=s; spf=pass smtp.mailfrom=evil.example; dmarc=pass header.from=evil.example"));
    }

    [Fact]
    public void A_signature_by_another_known_bank_does_not_vouch_for_this_one()
    {
        Assert.Equal(SenderVerifier.Verdict.NotAuthenticated, Check("a@hdfcbank.net",
            "mx.google.com; dkim=pass header.i=@icicibank.com header.s=s"));
    }
}
