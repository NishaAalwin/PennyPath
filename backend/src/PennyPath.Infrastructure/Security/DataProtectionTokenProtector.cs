using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using PennyPath.Application.Security;

namespace PennyPath.Infrastructure.Security;

/// <summary>
/// Encrypts secrets with ASP.NET Data Protection (authenticated encryption). Decryption only works while the key
/// ring survives, so the ring must live on persistent storage (see DataProtection:KeysPath).
/// </summary>
public class DataProtectionTokenProtector(IDataProtectionProvider provider) : ITokenProtector
{
    public const string Purpose = "PennyPath.StoredSecrets.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string secret) => _protector.Protect(secret);

    public string? Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
