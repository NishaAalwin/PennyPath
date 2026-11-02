namespace PennyPath.Infrastructure.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "PennyPath";
    public string Audience { get; set; } = "PennyPath";
    public int ExpiryMinutes { get; set; } = 60;
}
