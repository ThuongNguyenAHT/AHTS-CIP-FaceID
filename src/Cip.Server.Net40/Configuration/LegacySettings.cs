using System;
using System.Configuration;

namespace Cip.Server.Net40.Configuration
{
    public sealed class LegacySettings
    {
        public LegacySettings()
        {
            VendorConnectionString = ReadConnectionString("VendorDatabase");
            ConsentConnectionString = ReadConnectionString("ConsentDatabase");
            RecentRowCount = ReadInt("SourceData.RecentRowCount", 200, 1, 1000);
            CommandTimeoutSeconds = ReadInt("SourceData.CommandTimeoutSeconds", 10, 1, 120);
            FallbackPollingMilliseconds = ReadInt("Tablet.FallbackPollingMilliseconds", 1000, 500, 10000);
            PolicyVersion = ReadRequired("ConsentPolicy.Version");
            PolicyTitle = ReadRequired("ConsentPolicy.Title");
            PolicyContent = ReadRequired("ConsentPolicy.Content");
            PolicyLanguageCode = ReadRequired("ConsentPolicy.LanguageCode");
            AdminUsername = ReadRequired("Admin.Username");
            AdminPassword = ConfigurationManager.AppSettings["Admin.Password"] ?? string.Empty;
        }

        public string VendorConnectionString { get; private set; }
        public string ConsentConnectionString { get; private set; }
        public int RecentRowCount { get; private set; }
        public int CommandTimeoutSeconds { get; private set; }
        public int FallbackPollingMilliseconds { get; private set; }
        public string PolicyVersion { get; private set; }
        public string PolicyTitle { get; private set; }
        public string PolicyContent { get; private set; }
        public string PolicyLanguageCode { get; private set; }
        public string AdminUsername { get; private set; }
        public string AdminPassword { get; private set; }

        private static string ReadConnectionString(string name)
        {
            ConnectionStringSettings setting = ConfigurationManager.ConnectionStrings[name];
            if (setting == null || string.IsNullOrWhiteSpace(setting.ConnectionString))
            {
                throw new ConfigurationErrorsException("Thiếu connection string " + name + ".");
            }

            return setting.ConnectionString;
        }

        private static string ReadRequired(string key)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationErrorsException("Thiếu appSetting " + key + ".");
            }

            return value;
        }

        private static int ReadInt(string key, int fallback, int minimum, int maximum)
        {
            string raw = ConfigurationManager.AppSettings[key];
            int value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (!int.TryParse(raw, out value) || value < minimum || value > maximum)
            {
                throw new ConfigurationErrorsException("Giá trị " + key + " không hợp lệ.");
            }

            return value;
        }
    }
}
