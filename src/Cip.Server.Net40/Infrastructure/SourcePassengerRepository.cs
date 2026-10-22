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

        public SourcePassengerRepository(DatabaseConnectionFactory connectionFactory, LegacySettings settings)
        {
            _connectionFactory = connectionFactory;
            _settings = settings;
        }

        public IList<SourcePassenger> GetLatest()
        {
            string sql = "SELECT TOP (@Count) " + SelectColumns + Joins + "ORDER BY bp.Id DESC;";
            IList<SourcePassenger> rows;

            using (SqlConnection connection = _connectionFactory.CreateVendorConnection())
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _settings.CommandTimeoutSeconds;
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

        public SourcePassenger GetById(int id)
        {
            string sql = "SELECT TOP (1) " + SelectColumns + Joins + "WHERE bp.Id = @Id;";
            using (SqlConnection connection = _connectionFactory.CreateVendorConnection())
            using (SqlCommand command = new SqlCommand(sql, connection))
            {
                command.CommandTimeout = _settings.CommandTimeoutSeconds;
                command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                connection.Open();
                IList<SourcePassenger> passengers = ReadPassengers(command);
                return passengers.Count == 0 ? null : passengers[0];
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
