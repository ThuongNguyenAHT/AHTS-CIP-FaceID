using System;

namespace Cip.Server.Net40.Domain
{
    public sealed class SourcePassenger
    {
        public int Id { get; set; }
        public string PassengerName { get; set; }
        public string OperatingCarrierDesignator { get; set; }
        public string FlightNumber { get; set; }
        public DateTime? DateOfFlight { get; set; }
        public string WorkstationCode { get; set; }
        public string WarehouseName { get; set; }
        public string UserName { get; set; }
        public string FullName { get; set; }

        public string FlightCode
        {
            get { return OperatingCarrierDesignator.Trim().ToUpperInvariant() + FlightNumber.Trim().ToUpperInvariant(); }
        }

        public string CreatedByDisplay
        {
            get
            {
                return !string.IsNullOrWhiteSpace(FullName)
                    ? FullName.Trim()
                    : (UserName == null ? string.Empty : UserName.Trim());
            }
        }
    }

    public sealed class DpoConsentState
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public DateTime? ConfirmedAt { get; set; }
    }

    public sealed class ConsentDisplay
    {
        public int Id { get; set; }
        public string PassengerName { get; set; }
        public string OperatingCarrierDesignator { get; set; }
        public string FlightNumber { get; set; }
        public string FlightCode { get; set; }
        public DateTime? DateOfFlight { get; set; }
        public string WorkstationCode { get; set; }
        public string CreatedBy { get; set; }
        public string PolicyVersion { get; set; }
        public string PolicyTitle { get; set; }
        public string PolicyContent { get; set; }
        public string LanguageCode { get; set; }
    }

    public enum QueueForDisplayResult
    {
        Queued = 0,
        AlreadyPending = 1,
        AlreadyCompleted = 2,
        AnotherPassengerPending = 3
    }

    public sealed class DecisionOutcome
    {
        public bool Found { get; set; }
        public bool Applied { get; set; }
        public string Status { get; set; }
        public DateTime? ConfirmedAt { get; set; }
    }

    public sealed class DashboardSummary
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Accepted { get; set; }
        public int Declined { get; set; }
    }

    public sealed class NormalizedPassenger
    {
        public string PassengerName { get; set; }
        public string AirlineCode { get; set; }
        public string FlightNumber { get; set; }
    }
}
