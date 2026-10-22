using System.Data.SqlClient;
using Cip.Server.Net40.Configuration;

namespace Cip.Server.Net40.Infrastructure
{
    public sealed class DatabaseConnectionFactory
    {
        private readonly LegacySettings _settings;

        public DatabaseConnectionFactory(LegacySettings settings)
        {
            _settings = settings;
        }

        public SqlConnection CreateVendorConnection()
        {
            return new SqlConnection(_settings.VendorConnectionString);
        }

        public SqlConnection CreateConsentConnection()
        {
            return new SqlConnection(_settings.ConsentConnectionString);
        }
    }
}
