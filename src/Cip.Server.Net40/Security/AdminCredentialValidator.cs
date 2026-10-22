using System.Text;
using Cip.Server.Net40.Configuration;

namespace Cip.Server.Net40.Security
{
    public sealed class AdminCredentialValidator
    {
        private readonly LegacySettings _settings;

        public AdminCredentialValidator(LegacySettings settings)
        {
            _settings = settings;
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_settings.AdminPassword); }
        }

        public string Username
        {
            get { return _settings.AdminUsername; }
        }

        public bool Validate(string username, string password)
        {
            if (!IsConfigured || username == null || password == null)
            {
                return false;
            }

            return FixedTimeEquals(username.Trim(), _settings.AdminUsername)
                   && FixedTimeEquals(password, _settings.AdminPassword);
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);
            int difference = leftBytes.Length ^ rightBytes.Length;
            int maximumLength = leftBytes.Length > rightBytes.Length ? leftBytes.Length : rightBytes.Length;

            for (int index = 0; index < maximumLength; index++)
            {
                byte leftByte = index < leftBytes.Length ? leftBytes[index] : (byte)0;
                byte rightByte = index < rightBytes.Length ? rightBytes[index] : (byte)0;
                difference |= leftByte ^ rightByte;
            }

            return difference == 0;
        }
    }
}
