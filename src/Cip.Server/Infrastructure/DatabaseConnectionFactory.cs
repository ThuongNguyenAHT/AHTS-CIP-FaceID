using Microsoft.Data.SqlClient;

namespace Cip.Server.Infrastructure;

public sealed class DatabaseConnectionFactory(IConfiguration configuration)
{
    private readonly string _vendorConnectionString =
        configuration.GetConnectionString("VendorDatabase")
        ?? throw new InvalidOperationException("Thiếu ConnectionStrings:VendorDatabase.");

    private readonly string _consentConnectionString =
        configuration.GetConnectionString("ConsentDatabase")
        ?? throw new InvalidOperationException("Thiếu ConnectionStrings:ConsentDatabase.");

    public SqlConnection CreateVendorConnection() => new(_vendorConnectionString);

    public SqlConnection CreateConsentConnection() => new(_consentConnectionString);
}
