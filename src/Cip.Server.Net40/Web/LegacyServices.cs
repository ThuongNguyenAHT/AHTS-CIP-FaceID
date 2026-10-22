using Cip.Server.Net40.Configuration;
using Cip.Server.Net40.Infrastructure;
using Cip.Server.Net40.Security;

namespace Cip.Server.Net40.Web
{
    internal static class LegacyServices
    {
        static LegacyServices()
        {
            Settings = new LegacySettings();
            DatabaseConnectionFactory factory = new DatabaseConnectionFactory(Settings);
            SourcePassengers = new SourcePassengerRepository(factory, Settings);
            Consents = new ConsentRepository(factory, Settings);
            Credentials = new AdminCredentialValidator(Settings);
        }

        public static LegacySettings Settings { get; private set; }
        public static SourcePassengerRepository SourcePassengers { get; private set; }
        public static ConsentRepository Consents { get; private set; }
        public static AdminCredentialValidator Credentials { get; private set; }
    }
}
