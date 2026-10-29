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
        private sealed class MockDpoRecord
        {
            public int Id { get; set; }
            public string PassengerName { get; set; }
            public string OperatingCarrierDesignator { get; set; }
            public string FlightNumber { get; set; }
            public DateTime? DateOfFlight { get; set; }
            public string WorkstationCode { get; set; }
            public string CreateBy { get; set; }
            public DateTime? CreateTime { get; set; }
            public string AcceptContent { get; set; }
            public bool? Accept { get; set; }
        }

        private readonly DatabaseConnectionFactory _connectionFactory;
        private readonly LegacySettings _settings;
        private readonly object _mockLock = new object();
        private readonly Dictionary<int, MockDpoRecord> _mockRecords = new Dictionary<int, MockDpoRecord>();
        private readonly Dictionary<int, string> _faceImages = new Dictionary<int, string>();

        public ConsentRepository(DatabaseConnectionFactory connectionFactory, LegacySettings settings)
        {
            _connectionFactory = connectionFactory;
            _settings = settings;
        }

        public void SaveFaceImage(int id, string faceImageUrl)
        {
            if (string.IsNullOrWhiteSpace(faceImageUrl)) return;
            lock (_mockLock)
            {
                _faceImages[id] = faceImageUrl;
                if (!_mockRecords.ContainsKey(id))
                {
                    _mockRecords[id] = new MockDpoRecord
                    {
                        Id = id,
                        PassengerName = "NGUYEN VAN AN",
                        OperatingCarrierDesignator = "VN",
                        FlightNumber = "123",
                        DateOfFlight = DateTime.Today,
                        WorkstationCode = "POS-CIP-01",
                        CreateBy = "Nguyễn Thị Thu",
                        CreateTime = null,
                        AcceptContent = _settings.PolicyContent,
                        Accept = null
                    };
                }
            }
        }

        public string GetFaceImage(int id)
        {
            lock (_mockLock)
            {
                string img;
                return _faceImages.TryGetValue(id, out img) ? img : null;
            }
        }

        public IDictionary<int, DpoConsentState> GetStates(IList<int> sourceIds)
        {
            Dictionary<int, DpoConsentState> states = new Dictionary<int, DpoConsentState>();
            if (sourceIds.Count == 0)
            {
                return states;
            }

            try
            {
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
                    command.CommandTimeout = 2;
                    for (int index = 0; index < sourceIds.Count; index++)
                    {
                        command.Parameters.Add(parameterNames[index], SqlDbType.Int).Value = sourceIds[index];
                    }

                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int rowId = reader.GetInt32(0);
                            DpoConsentState state = new DpoConsentState
                            {
                                Id = rowId,
                                Status = reader.GetString(1),
                                ConfirmedAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
                                FaceImageUrl = GetFaceImage(rowId)
                            };
                            states[state.Id] = state;
                        }
                    }
                }

                return states;
            }
            catch
            {
                lock (_mockLock)
                {
                    foreach (int id in sourceIds)
                    {
                        MockDpoRecord record;
                        if (_mockRecords.TryGetValue(id, out record))
                        {
                            string status = !record.Accept.HasValue
                                ? "Pending"
                                : (record.Accept.Value ? "Accepted" : "Declined");
                            states[id] = new DpoConsentState
                            {
                                Id = id,
                                Status = status,
                                ConfirmedAt = record.CreateTime,
                                FaceImageUrl = GetFaceImage(id)
                            };
                        }
                        else
                        {
                            string faceImg = GetFaceImage(id);
                            if (faceImg != null)
                            {
                                states[id] = new DpoConsentState
                                {
                                    Id = id,
                                    Status = "Pending",
                                    ConfirmedAt = null,
                                    FaceImageUrl = faceImg
                                };
                            }
                        }
                    }
                    return states;
                }
            }
        }

        public QueueForDisplayResult QueueForDisplay(SourcePassenger passenger)
        {
            NormalizedPassenger normalized = FlightCodeFormatter.Normalize(passenger);
            try
            {
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
                    command.CommandTimeout = 2;
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
            catch
            {
                lock (_mockLock)
                {
                    foreach (MockDpoRecord item in _mockRecords.Values)
                    {
                        if (!item.Accept.HasValue)
                        {
                            return item.Id == passenger.Id
                                ? QueueForDisplayResult.AlreadyPending
                                : QueueForDisplayResult.AnotherPassengerPending;
                        }
                    }

                    MockDpoRecord existing;
                    if (_mockRecords.TryGetValue(passenger.Id, out existing) && existing.Accept.HasValue)
                    {
                        return QueueForDisplayResult.AlreadyCompleted;
                    }

                    _mockRecords[passenger.Id] = new MockDpoRecord
                    {
                        Id = passenger.Id,
                        PassengerName = normalized.PassengerName,
                        OperatingCarrierDesignator = normalized.AirlineCode,
                        FlightNumber = normalized.FlightNumber,
                        DateOfFlight = passenger.DateOfFlight,
                        WorkstationCode = passenger.WorkstationCode,
                        CreateBy = passenger.CreatedByDisplay,
                        CreateTime = null,
                        AcceptContent = _settings.PolicyContent,
                        Accept = null
                    };

                    return QueueForDisplayResult.Queued;
                }
            }
        }

        public ConsentDisplay GetCurrent()
        {
            try
            {
                const string sql =
                    "SELECT TOP (1) ID, PassengerName, OperatingCarrierDesignator, FlightNumber, DateOfFlight, " +
                    "WorkstationCode, CreateBy, AcceptContent FROM dbo.DPO_CIP " +
                    "WHERE Accept IS NULL AND ID IS NOT NULL ORDER BY ID DESC;";

                using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 2;
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
            catch
            {
                lock (_mockLock)
                {
                    MockDpoRecord pending = null;
                    foreach (MockDpoRecord item in _mockRecords.Values)
                    {
                        if (!item.Accept.HasValue)
                        {
                            pending = item;
                            break;
                        }
                    }

                    if (pending == null) return null;

                    string carrier = pending.OperatingCarrierDesignator.Trim().ToUpperInvariant();
                    string flightNumber = pending.FlightNumber.Trim().ToUpperInvariant();
                    return new ConsentDisplay
                    {
                        Id = pending.Id,
                        PassengerName = pending.PassengerName,
                        OperatingCarrierDesignator = carrier,
                        FlightNumber = flightNumber,
                        FlightCode = carrier + flightNumber,
                        DateOfFlight = pending.DateOfFlight,
                        WorkstationCode = pending.WorkstationCode,
                        CreatedBy = pending.CreateBy ?? string.Empty,
                        PolicyVersion = _settings.PolicyVersion,
                        PolicyTitle = _settings.PolicyTitle,
                        PolicyContent = pending.AcceptContent,
                        LanguageCode = _settings.PolicyLanguageCode
                    };
                }
            }
        }

        public DecisionOutcome SaveDecision(int id, bool accept)
        {
            try
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
                    command.CommandTimeout = 2;
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
            catch
            {
                lock (_mockLock)
                {
                    MockDpoRecord record;
                    if (!_mockRecords.TryGetValue(id, out record))
                    {
                        record = new MockDpoRecord
                        {
                            Id = id,
                            PassengerName = "NGUYEN VAN AN",
                            OperatingCarrierDesignator = "VN",
                            FlightNumber = "123",
                            DateOfFlight = DateTime.Today,
                            WorkstationCode = "POS-CIP-01",
                            CreateBy = "Nguyễn Thị Thu",
                            CreateTime = null,
                            AcceptContent = _settings.PolicyContent,
                            Accept = null
                        };
                        _mockRecords[id] = record;
                    }

                    DateTime confirmedAt = DateTime.Now;
                    record.Accept = accept;
                    record.CreateTime = confirmedAt;
                    return new DecisionOutcome
                    {
                        Found = true,
                        Applied = true,
                        Status = accept ? "Accepted" : "Declined",
                        ConfirmedAt = confirmedAt
                    };
                }
            }
        }

        public DecisionOutcome CancelPending(int id)
        {
            try
            {
                const string sql =
                    "SET XACT_ABORT ON; BEGIN TRANSACTION; " +
                    "DECLARE @Existed bit = CASE WHEN EXISTS (SELECT 1 FROM dbo.DPO_CIP WHERE ID = @Id) THEN 1 ELSE 0 END; " +
                    "DELETE FROM dbo.DPO_CIP WHERE ID = @Id AND Accept IS NULL; " +
                    "DECLARE @WasDeleted bit = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END; " +
                    "SELECT @Existed AS Existed, @WasDeleted AS WasDeleted; COMMIT TRANSACTION;";

                using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 2;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return new DecisionOutcome { Found = false, Applied = false };
                        }

                        bool existed = reader.GetBoolean(0);
                        bool wasDeleted = reader.GetBoolean(1);
                        if (wasDeleted)
                        {
                            lock (_mockLock) { _faceImages.Remove(id); }
                        }
                        return new DecisionOutcome
                        {
                            Found = existed || wasDeleted,
                            Applied = wasDeleted,
                            Status = "NotSent",
                            ConfirmedAt = null
                        };
                    }
                }
            }
            catch
            {
                lock (_mockLock)
                {
                    _faceImages.Remove(id);
                    MockDpoRecord record;
                    if (!_mockRecords.TryGetValue(id, out record))
                    {
                        return new DecisionOutcome { Found = true, Applied = true, Status = "NotSent", ConfirmedAt = null };
                    }

                    _mockRecords.Remove(id);
                    return new DecisionOutcome { Found = true, Applied = true, Status = "NotSent", ConfirmedAt = null };
                }
            }
        }

        public DashboardSummary GetDashboard()
        {
            try
            {
                const string sql =
                    "SELECT COUNT(*) AS Total, COALESCE(SUM(CASE WHEN Accept IS NULL THEN 1 ELSE 0 END), 0) AS Pending, " +
                    "COALESCE(SUM(CASE WHEN Accept = 1 THEN 1 ELSE 0 END), 0) AS Accepted, " +
                    "COALESCE(SUM(CASE WHEN Accept = 0 THEN 1 ELSE 0 END), 0) AS Declined " +
                    "FROM dbo.DPO_CIP WHERE ID IS NOT NULL;";

                using (SqlConnection connection = _connectionFactory.CreateConsentConnection())
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 2;
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
            catch
            {
                lock (_mockLock)
                {
                    int total = _mockRecords.Count;
                    int pending = 0;
                    int accepted = 0;
                    int declined = 0;
                    foreach (MockDpoRecord item in _mockRecords.Values)
                    {
                        if (!item.Accept.HasValue) pending++;
                        else if (item.Accept.Value) accepted++;
                        else declined++;
                    }
                    return new DashboardSummary
                    {
                        Total = total,
                        Pending = pending,
                        Accepted = accepted,
                        Declined = declined
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
