using System.Data;
using Cip.Server.Configuration;
using Cip.Server.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Cip.Server.Infrastructure;

public class SourcePassengerRepository(
    DatabaseConnectionFactory connectionFactory,
    IOptions<SourceDataOptions> options)
{
    private readonly DatabaseConnectionFactory _connectionFactory = connectionFactory;
    private readonly SourceDataOptions _options = options.Value;

    private const string SelectColumns = """
        bp.Id,
        bp.PassengerName,
        bp.OperatingCarrierDesignator,
        bp.FlightNumber,
        bp.DateOfFlight,
        p.WorkstationCode,
        wh.Name AS WarehouseName,
        u.UserName,
        u.FullName
        """;

    private const string Joins = """
        FROM dbo.B30PosDocBPDetail bp
        LEFT JOIN dbo.B30PosDoc p ON bp.PosDocId = p.PosDocId
        LEFT JOIN dbo.B20Warehouse wh ON wh.Id = p.WarehouseId
        LEFT JOIN dbo.B00UserList u ON bp.CreatedBy = u.Id
        """;

    public virtual async Task<IReadOnlyList<SourcePassenger>> GetLatestAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT TOP (@Count) {SelectColumns}
            {Joins}
            ORDER BY bp.Id DESC;
            """;

        await using var connection = _connectionFactory.CreateVendorConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.Add("@Count", SqlDbType.Int).Value = _options.RecentRowCount;

        return (await ReadPassengersAsync(command, cancellationToken))
            .GroupBy(passenger => passenger.Id)
            .Select(group => group.First())
            .ToArray();
    }

    public virtual async Task<SourcePassenger?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT TOP (1) {SelectColumns}
            {Joins}
            WHERE bp.Id = @Id;
            """;

        await using var connection = _connectionFactory.CreateVendorConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _options.CommandTimeoutSeconds
        };
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

        return (await ReadPassengersAsync(command, cancellationToken)).FirstOrDefault();
    }

    public virtual async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = _connectionFactory.CreateVendorConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1 FROM dbo.B30PosDocBPDetail WHERE 1 = 0;", connection)
            {
                CommandTimeout = _options.CommandTimeoutSeconds
            };
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<SourcePassenger>> ReadPassengersAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var passengers = new List<SourcePassenger>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            passengers.Add(new SourcePassenger(
                reader.GetInt32(0),
                GetString(reader, 1),
                GetString(reader, 2),
                GetString(reader, 3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                GetNullableString(reader, 5),
                GetNullableString(reader, 6),
                GetNullableString(reader, 7),
                GetNullableString(reader, 8)));
        }

        return passengers;
    }

    private static string GetString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static string? GetNullableString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
