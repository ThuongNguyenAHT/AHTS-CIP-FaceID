using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Cip.Server.Net40.Configuration;
using Cip.Server.Net40.Domain;

namespace Cip.Server.Net40.Infrastructure
{
    public sealed class SourcePassengerRepository
    {
        private const string SelectColumns =
            "bp.Id, bp.PassengerName, bp.OperatingCarrierDesignator, bp.FlightNumber, " +
            "bp.DateOfFlight, p.WorkstationCode, wh.Name AS WarehouseName, u.UserName, u.FullName ";

        private const string Joins =
            "FROM dbo.B30PosDocBPDetail bp " +
            "LEFT JOIN dbo.B30PosDoc p ON bp.PosDocId = p.PosDocId " +
            "LEFT JOIN dbo.B20Warehouse wh ON wh.Id = p.WarehouseId " +
            "LEFT JOIN dbo.B00UserList u ON bp.CreatedBy = u.Id ";

        private readonly DatabaseConnectionFactory _connectionFactory;
        private readonly LegacySettings _settings;
        private readonly object _mockLock = new object();
        private readonly List<SourcePassenger> _mockPassengers = new List<SourcePassenger>();
        private int _nextMockId = 1011;

        public SourcePassengerRepository(DatabaseConnectionFactory connectionFactory, LegacySettings settings)
        {
            _connectionFactory = connectionFactory;
            _settings = settings;
            SeedMockPassengers();
        }

        private void SeedMockPassengers()
        {
            DateTime today = DateTime.Today;
            _mockPassengers.Add(CreateMock(1001, "NGUYEN VAN AN", "VN", "123", today, "POS-CIP-01", "Phòng chờ Quốc tế T2", "letan01", "Nguyễn Thị Thu"));
            _mockPassengers.Add(CreateMock(1002, "TRAN THI BICH", "VJ", "456", today, "POS-CIP-01", "Phòng chờ Quốc tế T2", "letan01", "Nguyễn Thị Thu"));
            _mockPassengers.Add(CreateMock(1003, "LE HOANG NAM", "QH", "789", today, "POS-CIP-01", "Phòng chờ Quốc tế T2", "letan02", "Trần Minh Đức"));
            _mockPassengers.Add(CreateMock(1004, "PHAM THI MAI", "VN", "214", today, "POS-CIP-02", "Phòng chờ Quốc tế T2", "letan01", "Nguyễn Thị Thu"));
            _mockPassengers.Add(CreateMock(1005, "HOANG VAN DUC", "SQ", "176", today, "POS-CIP-02", "Phòng chờ Quốc tế T2", "letan02", "Trần Minh Đức"));
            _mockPassengers.Add(CreateMock(1006, "DOAN THI LAN", "VJ", "688", today, "POS-CIP-01", "Phòng chờ Quốc nội T1", "letan01", "Nguyễn Thị Thu"));
            _mockPassengers.Add(CreateMock(1007, "NGUYEN MINH TUAN", "VN", "502", today, "POS-CIP-01", "Phòng chờ Quốc nội T1", "letan02", "Trần Minh Đức"));
            _mockPassengers.Add(CreateMock(1008, "VU THI HONG", "QH", "112", today, "POS-CIP-02", "Phòng chờ Quốc nội T1", "letan01", "Nguyễn Thị Thu"));
            _mockPassengers.Add(CreateMock(1009, "DANG QUOC HUY", "VN", "720", today, "POS-CIP-01", "Phòng chờ Quốc nội T1", "letan02", "Trần Minh Đức"));
            _mockPassengers.Add(CreateMock(1010, "BUI THI THAO", "VJ", "234", today, "POS-CIP-02", "Phòng chờ Quốc nội T1", "letan01", "Nguyễn Thị Thu"));
        }

        private static SourcePassenger CreateMock(int id, string name, string carrier, string flightNumber, DateTime date, string ws, string wh, string user, string fullName)
        {
            return new SourcePassenger
            {
                Id = id,
                PassengerName = name,
                OperatingCarrierDesignator = carrier,
                FlightNumber = flightNumber,
                DateOfFlight = date,
                WorkstationCode = ws,
                WarehouseName = wh,
                UserName = user,
                FullName = fullName
            };
        }

        public SourcePassenger SimulateNewPassengerScan()
        {
            string[] sampleNames = new string[]
            {
                "NGUYỄN TIẾN DŨNG",
                "LÊ THỊ THẢO NGUYÊN",
                "VŨ MINH TRÍ",
                "TRẦN HOÀNG LONG",
                "PHẠM BÍCH NGỌC",
                "ĐỖ ANH TUẤN"
            };
            string[] airlines = new string[] { "VN", "VJ", "QH", "SQ" };

            lock (_mockLock)
            {
                int id = _nextMockId++;
                string name = sampleNames[id % sampleNames.Length];
                string airline = airlines[id % airlines.Length];
                string flightNum = Convert.ToString(100 + (id * 17) % 899);

                SourcePassenger newPassenger = CreateMock(
                    id,
                    name,
                    airline,
                    flightNum,
                    DateTime.Today,
                    "POS-CIP-01",
                    "Phòng chờ Quốc tế T2",
                    "letan01",
                    "Nguyễn Thị Thu");

                _mockPassengers.Insert(0, newPassenger);
                return newPassenger;
            }
        }

        public IList<SourcePassenger> GetLatest()
        {
            try
            {
                string sql = "SELECT TOP (@Count) " + SelectColumns + Joins + "ORDER BY bp.Id DESC;";
                IList<SourcePassenger> rows;

                using (SqlConnection connection = _connectionFactory.CreateVendorConnection())
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 2;
                    command.Parameters.Add("@Count", SqlDbType.Int).Value = _settings.RecentRowCount;
                    connection.Open();
                    rows = ReadPassengers(command);
                }

                Dictionary<int, SourcePassenger> unique = new Dictionary<int, SourcePassenger>();
                List<SourcePassenger> result = new List<SourcePassenger>();
                foreach (SourcePassenger passenger in rows)
                {
                    if (!unique.ContainsKey(passenger.Id))
                    {
                        unique.Add(passenger.Id, passenger);
                        result.Add(passenger);
                    }
                }

                return result;
            }
            catch
            {
                lock (_mockLock)
                {
                    return new List<SourcePassenger>(_mockPassengers);
                }
            }
        }

        public SourcePassenger GetById(int id)
        {
            try
            {
                string sql = "SELECT TOP (1) " + SelectColumns + Joins + "WHERE bp.Id = @Id;";
                using (SqlConnection connection = _connectionFactory.CreateVendorConnection())
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.CommandTimeout = 2;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    connection.Open();
                    IList<SourcePassenger> passengers = ReadPassengers(command);
                    return passengers.Count == 0 ? null : passengers[0];
                }
            }
            catch
            {
                lock (_mockLock)
                {
                    foreach (SourcePassenger p in _mockPassengers)
                    {
                        if (p.Id == id) return p;
                    }
                    return null;
                }
            }
        }

        public bool CanConnect()
        {
            try
            {
                using (SqlConnection connection = _connectionFactory.CreateVendorConnection())
                using (SqlCommand command = new SqlCommand(
                    "SELECT 1 FROM dbo.B30PosDocBPDetail WHERE 1 = 0;", connection))
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

        private static IList<SourcePassenger> ReadPassengers(SqlCommand command)
        {
            List<SourcePassenger> passengers = new List<SourcePassenger>();
            using (SqlDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    passengers.Add(new SourcePassenger
                    {
                        Id = reader.GetInt32(0),
                        PassengerName = GetString(reader, 1),
                        OperatingCarrierDesignator = GetString(reader, 2),
                        FlightNumber = GetString(reader, 3),
                        DateOfFlight = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                        WorkstationCode = GetNullableString(reader, 5),
                        WarehouseName = GetNullableString(reader, 6),
                        UserName = GetNullableString(reader, 7),
                        FullName = GetNullableString(reader, 8)
                    });
                }
            }

            return passengers;
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
