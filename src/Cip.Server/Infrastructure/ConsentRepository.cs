using System.Collections.Concurrent;
using System.Data;
using Cip.Server.Configuration;
using Cip.Server.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Cip.Server.Infrastructure;

public class ConsentRepository(
    DatabaseConnectionFactory connectionFactory,
    IOptions<ConsentPolicyOptions> policyOptions)
{
    protected readonly ConsentPolicyOptions _policy = policyOptions.Value;
    protected readonly ConcurrentDictionary<int, string> _faceImages = new();

    public virtual void SaveFaceImage(int id, string? faceImageUrl)
    {
        if (!string.IsNullOrWhiteSpace(faceImageUrl))
        {
            _faceImages[id] = faceImageUrl;
        }
    }

    public virtual string? GetFaceImage(int id) =>
        _faceImages.TryGetValue(id, out var img) ? img : null;

    public virtual async Task<IReadOnlyDictionary<int, DpoConsentState>> GetStatesAsync(
        IReadOnlyCollection<int> sourceIds,
        CancellationToken cancellationToken)
    {
        if (sourceIds.Count == 0)
        {
            return new Dictionary<int, DpoConsentState>();
        }

        var parameterNames = sourceIds.Select((_, index) => $"@id{index}").ToArray();
        var sql = $"""
            WITH Ranked AS
            (
                SELECT
                    ID,
                    Accept,
                    CreateTime,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY ID
                        ORDER BY
                            CASE WHEN Accept IS NULL THEN 0 ELSE 1 END,
                            CreateTime DESC
                    ) AS RowNumber
                FROM dbo.DPO_CIP
                WHERE ID IN ({string.Join(", ", parameterNames)})
            )
            SELECT
                ID,
                CASE
                    WHEN Accept IS NULL THEN 'Pending'
                    WHEN Accept = 1 THEN 'Accepted'
                    ELSE 'Declined'
                END AS ConsentStatus,
                CreateTime
            FROM Ranked
            WHERE RowNumber = 1;
            """;

        await using var connection = connectionFactory.CreateConsentConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);

        var index = 0;
        foreach (var sourceId in sourceIds)
        {
            command.Parameters.Add(parameterNames[index++], SqlDbType.Int).Value = sourceId;
        }

        var states = new Dictionary<int, DpoConsentState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rowId = reader.GetInt32(0);
            var state = new DpoConsentState(
                rowId,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                GetFaceImage(rowId));
            states[state.Id] = state;
        }

        return states;
    }

    public virtual async Task<QueueForDisplayResult> QueueForDisplayAsync(
        SourcePassenger passenger,
        CancellationToken cancellationToken)
    {
        var normalized = FlightCodeFormatter.Normalize(passenger);

        const string sql = """
            SET XACT_ABORT ON;
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DECLARE @PendingId int =
            (
                SELECT TOP (1) ID
                FROM dbo.DPO_CIP WITH (TABLOCKX, HOLDLOCK)
                WHERE Accept IS NULL AND ID IS NOT NULL
                ORDER BY ID DESC
            );

            DECLARE @Result int;

            IF @PendingId IS NOT NULL
            BEGIN
                SET @Result = CASE WHEN @PendingId = @Id THEN 1 ELSE 3 END;
            END
            ELSE IF EXISTS (SELECT 1 FROM dbo.DPO_CIP WHERE ID = @Id)
            BEGIN
                SET @Result = 2;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.DPO_CIP
                (
                    ID,
                    PassengerName,
                    OperatingCarrierDesignator,
                    FlightNumber,
                    DateOfFlight,
                    WorkstationCode,
                    CreateBy,
                    CreateTime,
                    AcceptContent,
                    Accept
                )
                VALUES
                (
                    @Id,
                    @PassengerName,
                    @OperatingCarrierDesignator,
                    @FlightNumber,
                    @DateOfFlight,
                    @WorkstationCode,
                    @CreateBy,
                    NULL,
                    @AcceptContent,
                    NULL
                );

                SET @Result = 0;
            END;

            COMMIT TRANSACTION;
            SELECT @Result;
            """;

        await using var connection = connectionFactory.CreateConsentConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = passenger.Id;
        command.Parameters.Add("@PassengerName", SqlDbType.NVarChar, 254).Value = normalized.PassengerName;
        command.Parameters.Add("@OperatingCarrierDesignator", SqlDbType.NVarChar, 24).Value = normalized.AirlineCode;
        command.Parameters.Add("@FlightNumber", SqlDbType.NVarChar, 24).Value = normalized.FlightNumber;
        command.Parameters.Add("@DateOfFlight", SqlDbType.SmallDateTime).Value =
            passenger.DateOfFlight is null ? DBNull.Value : passenger.DateOfFlight.Value;
        command.Parameters.Add("@WorkstationCode", SqlDbType.NVarChar, 24).Value =
            string.IsNullOrWhiteSpace(passenger.WorkstationCode)
                ? DBNull.Value
                : passenger.WorkstationCode.Trim()[..Math.Min(passenger.WorkstationCode.Trim().Length, 24)];
        command.Parameters.Add("@CreateBy", SqlDbType.NVarChar, 256).Value =
            string.IsNullOrWhiteSpace(passenger.CreatedByDisplay)
                ? DBNull.Value
                : passenger.CreatedByDisplay[..Math.Min(passenger.CreatedByDisplay.Length, 256)];
        command.Parameters.Add("@AcceptContent", SqlDbType.NVarChar, -1).Value = _policy.Content;

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return (QueueForDisplayResult)result;
    }

    public virtual async Task<ConsentDisplay?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1)
                ID,
                PassengerName,
                OperatingCarrierDesignator,
                FlightNumber,
                DateOfFlight,
                WorkstationCode,
                CreateBy,
                AcceptContent
            FROM dbo.DPO_CIP
            WHERE Accept IS NULL AND ID IS NOT NULL
            ORDER BY ID DESC;
            """;

        await using var connection = connectionFactory.CreateConsentConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var carrier = GetString(reader, 2).Trim().ToUpperInvariant();
        var flightNumber = GetString(reader, 3).Trim().ToUpperInvariant();
        var storedContent = GetNullableString(reader, 7);

        return new ConsentDisplay(
            reader.GetInt32(0),
            GetString(reader, 1),
            carrier,
            flightNumber,
            carrier + flightNumber,
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            GetNullableString(reader, 5),
            GetNullableString(reader, 6) ?? string.Empty,
            _policy.Version,
            _policy.Title,
            string.IsNullOrWhiteSpace(storedContent) ? _policy.Content : storedContent,
            _policy.LanguageCode);
    }

    public virtual async Task<DecisionOutcome> SaveDecisionAsync(
        int id,
        bool accept,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            ;WITH Target AS
            (
                SELECT TOP (1) *
                FROM dbo.DPO_CIP WITH (UPDLOCK, ROWLOCK)
                WHERE ID = @Id AND Accept IS NULL
            )
            UPDATE Target
            SET Accept = @Accept,
                CreateTime = GETDATE();

            DECLARE @WasApplied bit = CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END;

            SELECT TOP (1)
                @WasApplied AS WasApplied,
                Accept,
                CreateTime
            FROM dbo.DPO_CIP
            WHERE ID = @Id
            ORDER BY
                CASE WHEN Accept IS NULL THEN 0 ELSE 1 END,
                CreateTime DESC;

            COMMIT TRANSACTION;
            """;

        await using var connection = connectionFactory.CreateConsentConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        command.Parameters.Add("@Accept", SqlDbType.Bit).Value = accept;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DecisionOutcome(false, false, null, null);
        }

        var storedAccept = reader.IsDBNull(1) ? (bool?)null : reader.GetBoolean(1);
        var status = storedAccept switch
        {
            true => "Accepted",
            false => "Declined",
            null => "Pending"
        };
        DateTime? confirmedAt = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
        var applied = reader.GetBoolean(0) || storedAccept == accept;

        return new DecisionOutcome(true, applied, status, confirmedAt);
    }

    public virtual async Task<DecisionOutcome> CancelPendingAsync(
        int id,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            DECLARE @Existed bit = CASE WHEN EXISTS (SELECT 1 FROM dbo.DPO_CIP WHERE ID = @Id) THEN 1 ELSE 0 END;

            DELETE FROM dbo.DPO_CIP
            WHERE ID = @Id AND Accept IS NULL;

            DECLARE @WasDeleted bit = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;

            SELECT @Existed AS Existed, @WasDeleted AS WasDeleted;

            COMMIT TRANSACTION;
            """;

        await using var connection = connectionFactory.CreateConsentConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DecisionOutcome(false, false, null, null);
        }

        var existed = reader.GetBoolean(0);
        var wasDeleted = reader.GetBoolean(1);
        if (wasDeleted)
        {
            _faceImages.TryRemove(id, out _);
        }
        return new DecisionOutcome(existed || wasDeleted, wasDeleted, "NotSent", null);
    }

    public virtual async Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                COUNT(*) AS Total,
                COALESCE(SUM(CASE WHEN Accept IS NULL THEN 1 ELSE 0 END), 0) AS Pending,
                COALESCE(SUM(CASE WHEN Accept = 1 THEN 1 ELSE 0 END), 0) AS Accepted,
                COALESCE(SUM(CASE WHEN Accept = 0 THEN 1 ELSE 0 END), 0) AS Declined
            FROM dbo.DPO_CIP
            WHERE ID IS NOT NULL;
            """;

        await using var connection = connectionFactory.CreateConsentConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new DashboardSummary(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }

    public virtual async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = connectionFactory.CreateConsentConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1 FROM dbo.DPO_CIP WHERE 1 = 0;", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static string? GetNullableString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
