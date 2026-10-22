using System.Security.Cryptography;
using System.Text;
using Cip.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Cip.Server.Services;

public sealed class AdminCredentialValidator(IOptions<AdminOptions> options)
{
    private readonly AdminOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Password);

    public bool Validate(string username, string password)
    {
        if (!IsConfigured)
        {
            return false;
        }

        return FixedTimeEquals(username.Trim(), _options.Username)
               && FixedTimeEquals(password, _options.Password);
    }

    public string Username => _options.Username;

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
