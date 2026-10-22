using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using Cip.Server.Net40.Configuration;
using Cip.Server.Net40.Domain;

namespace Cip.Server.Net40.Infrastructure
{
    public sealed class ConsentRepository
    {
        private readonly DatabaseConnectionFactory _connectionFactory;
        private readonly LegacySettings _settings;

        public ConsentRepository(DatabaseConnectionFactory connectionFactory, LegacySettings settings)
        {
            _connectionFactory = connectionFactory;
            _settings = settings;
        }

        public IDictionary<int, DpoConsentState> GetStates(IList<int> sourceIds)
        {
            Dictionary<int, DpoConsentState> states = new Dictionary<int, DpoConsentState>();
            if (sourceIds.Count == 0)
            {
                return states;
            }

            List<string> parameterNames = new List<string>();
            for (int index = 0; index < sourceIds.Count; index++)
            {
                parameterNames.Add("@id" + index);
            }

            string sql =
                "WITH Ranked AS (" +
                " SELECT ID, Accept, CreateTime, ROW_NUMBER() OVER (" +
                " PARTITION BY ID ORDER BY CASE WHEN Accept IS NULL THEN 0 ELSE 1 END, CreateTime DESC" +
                " ) AS RowNumber FROM dbo.DPO_CIP WHERE ID IN (" + string.Join(", ", parameterNames.ToArray()) + ")" +
                ") SELECT ID, CASE WHEN Accept IS NULL THEN 'Pending' WHEN Accept = 1 THEN 'Accepted' " +
                "ELSE 'Declined' END AS ConsentStatus, CreateTime FROM Ranked WHERE RowNumber = 1;";

            using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _settings.CommandTimeoutSeconds;
                for (int index = 0; index < sourceIds.Count; index++)
                {
                    command.Parameters.Add(parameterNames[index], SqlDbType.Int).Value = sourceIds[index];
                }

                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DpoConsentState state = new DpoConsentState
                        {
                            Id = reader.GetInt32(0),
                            Status = reader.GetString(1),
                            ConfirmedAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2)
                        };
                        states[state.Id] = state;
                    }
                }
            }

            return states;
        }

        public QueueForDisplayResult QueueForDisplay(SourcePassenger passenger)
        {
            NormalizedPassenger normalized = FlightCodeFormatter.Normalize(passenger);
            const string sql =
                "SET XACT_ABORT ON; SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; BEGIN TRANSACTION; " +
                "DECLARE @PendingId int = (SELECT TOP (1) ID FROM dbo.DPO_CIP WITH (TABLOCKX, HOLDLOCK) " +
                "WHERE Accept IS NULL AND ID IS NOT NULL ORDER BY ID DESC); DECLARE @Result int; " +
                "IF @PendingId IS NOT NULL BEGIN SET @Result = CASE WHEN @PendingId = @Id THEN 1 ELSE 3 END; END " +
                "ELSE IF EXISTS (SELECT 1 FROM dbo.DPO_CIP WHERE ID = @Id) BEGIN SET @Result = 2; END " +
                "ELSE BEGIN INSERT INTO dbo.DPO_CIP " +
                "(ID, PassengerName, OperatingCarrierDesignator, FlightNumber, DateOfFlight, WorkstationCode, " +
                "CreateBy, CreateTime, AcceptContent, Accept) VALUES " +
                "(@Id, @PassengerName, @OperatingCarrierDesignator, @FlightNumber, @DateOfFlight, @WorkstationCode, " +
                "@CreateBy, NULL, @AcceptContent, NULL); SET @Result = 0; END; " +
                "COMMIT TRANSACTION; SELECT @Result;";

            using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _settings.CommandTimeoutSeconds;
                command.Parameters.Add("@Id", SqlDbType.Int).Value = passenger.Id;
                command.Parameters.Add("@PassengerName", SqlDbType.NVarChar, 254).Value = normalized.PassengerName;
                command.Parameters.Add("@OperatingCarrierDesignator", SqlDbType.NVarChar, 24).Value = normalized.AirlineCode;
                command.Parameters.Add("@FlightNumber", SqlDbType.NVarChar, 24).Value = normalized.FlightNumber;
                command.Parameters.Add("@DateOfFlight", SqlDbType.SmallDateTime).Value =
                    passenger.DateOfFlight.HasValue ? (object)passenger.DateOfFlight.Value : DBNull.Value;
                command.Parameters.Add("@WorkstationCode", SqlDbType.NVarChar, 24).Value =
                    ToDatabaseValue(Truncate(passenger.WorkstationCode, 24));
                command.Parameters.Add("@CreateBy", SqlDbType.NVarChar, 256).Value =
                    ToDatabaseValue(Truncate(passenger.CreatedByDisplay, 256));
                command.Parameters.Add("@AcceptContent", SqlDbType.NVarChar, -1).Value = _settings.PolicyContent;
                connection.Open();
                return (QueueForDisplayResult)Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public ConsentDisplay GetCurrent()
        {
            const string sql =
                "SELECT TOP (1) ID, PassengerName, OperatingCarrierDesignator, FlightNumber, DateOfFlight, " +
                "WorkstationCode, CreateBy, AcceptContent FROM dbo.DPO_CIP " +
                "WHERE Accept IS NULL AND ID IS NOT NULL ORDER BY ID DESC;";

            using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _settings.CommandTimeoutSeconds;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    string carrier = GetString(reader, 2).Trim().ToUpperInvariant();
                    string flightNumber = GetString(reader, 3).Trim().ToUpperInvariant();
                    string storedContent = GetNullableString(reader, 7);
                    return new ConsentDisplay
                    {
                        Id = reader.GetInt32(0),
                        PassengerName = GetString(reader, 1),
                        OperatingCarrierDesignator = carrier,
                        FlightNumber = flightNumber,
                        FlightCode = carrier + flightNumber,
                        DateOfFlight = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                        WorkstationCode = GetNullableString(reader, 5),
                        CreatedBy = GetNullableString(reader, 6) ?? string.Empty,
                        PolicyVersion = _settings.PolicyVersion,
                        PolicyTitle = _settings.PolicyTitle,
                        PolicyContent = string.IsNullOrWhiteSpace(storedContent) ? _settings.PolicyContent : storedContent,
                        LanguageCode = _settings.PolicyLanguageCode
                    };
                }
            }
        }

        public DecisionOutcome SaveDecision(int id, bool accept)
        {
            const string sql =
                "SET XACT_ABORT ON; BEGIN TRANSACTION; " +
                ";WITH Target AS (SELECT TOP (1) * FROM dbo.DPO_CIP WITH (UPDLOCK, ROWLOCK) " +
                "WHERE ID = @Id AND Accept IS NULL) UPDATE Target SET Accept = @Accept, CreateTime = GETDATE(); " +
                "DECLARE @WasApplied bit = CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END; " +
                "SELECT TOP (1) @WasApplied AS WasApplied, Accept, CreateTime FROM dbo.DPO_CIP WHERE ID = @Id " +
                "ORDER BY CASE WHEN Accept IS NULL THEN 0 ELSE 1 END, CreateTime DESC; COMMIT TRANSACTION;";

            using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _settings.CommandTimeoutSeconds;
                command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                command.Parameters.Add("@Accept", SqlDbType.Bit).Value = accept;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return new DecisionOutcome { Found = false, Applied = false };
                    }

                    bool? storedAccept = reader.IsDBNull(1) ? (bool?)null : reader.GetBoolean(1);
                    string status = !storedAccept.HasValue
                        ? "Pending"
                        : (storedAccept.Value ? "Accepted" : "Declined");
                    return new DecisionOutcome
                    {
                        Found = true,
                        Applied = reader.GetBoolean(0) || storedAccept == accept,
                        Status = status,
                        ConfirmedAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2)
                    };
                }
            }
        }

        public DashboardSummary GetDashboard()
        {
            const string sql =
                "SELECT COUNT(*) AS Total, COALESCE(SUM(CASE WHEN Accept IS NULL THEN 1 ELSE 0 END), 0) AS Pending, " +
                "COALESCE(SUM(CASE WHEN Accept = 1 THEN 1 ELSE 0 END), 0) AS Accepted, " +
                "COALESCE(SUM(CASE WHEN Accept = 0 THEN 1 ELSE 0 END), 0) AS Declined " +
                "FROM dbo.DPO_CIP WHERE ID IS NOT NULL;";

            using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _settings.CommandTimeoutSeconds;
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    reader.Read();
                    return new DashboardSummary
                    {
                        Total = reader.GetInt32(0),
                        Pending = reader.GetInt32(1),
                        Accepted = reader.GetInt32(2),
                        Declined = reader.GetInt32(3)
                    };
                }
            }
        }

        public bool CanConnect()
        {
            try
            {
                using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
                using (SqlCommand command = new SqlCommand("SELECT 1 FROM dbo.DPO_CIP WHERE 1 = 0;", connection))
                {
                    command.CommandTimeout = _settings.CommandTimeoutSeconds;
                    connection.Open();
                    command.ExecuteNonQuery();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static object ToDatabaseValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DBNull.Value : (object)value;
        }

        private static string Truncate(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            return trimmed.Length <= maximumLength ? trimmed : trimmed.Substring(0, maximumLength);
        }

        private static string GetString(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static string GetNullableString(SqlDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }
}
