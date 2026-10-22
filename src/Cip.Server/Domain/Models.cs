namespace Cip.Server.Domain;

public sealed record SourcePassenger(
    int Id,
    string PassengerName,
    string OperatingCarrierDesignator,
    string FlightNumber,
    DateTime? DateOfFlight,
    string? WorkstationCode,
    string? WarehouseName,
    string? UserName,
    string? FullName)
{
    public string FlightCode =>
        OperatingCarrierDesignator.Trim().ToUpperInvariant()
        + FlightNumber.Trim().ToUpperInvariant();

    public string CreatedByDisplay =>
        !string.IsNullOrWhiteSpace(FullName)
            ? FullName.Trim()
            : UserName?.Trim() ?? string.Empty;
}

public sealed record SourcePassengerWithConsent(
    int Id,
    string PassengerName,
    string OperatingCarrierDesignator,
    string FlightNumber,
    string FlightCode,
    DateTime? DateOfFlight,
    string? WorkstationCode,
    string? WarehouseName,
    string? UserName,
    string? FullName,
    string ConsentStatus,
    DateTime? ConfirmedAt);

public sealed record ConsentDisplay(
    int Id,
    string PassengerName,
    string OperatingCarrierDesignator,
    string FlightNumber,
    string FlightCode,
    DateTime? DateOfFlight,
    string? WorkstationCode,
    string CreatedBy,
    string PolicyVersion,
    string PolicyTitle,
    string PolicyContent,
    string LanguageCode);

public sealed record DpoConsentState(
    int Id,
    string Status,
    DateTime? ConfirmedAt);

public enum QueueForDisplayResult
{
    Queued,
    AlreadyPending,
    AlreadyCompleted,
    AnotherPassengerPending
}

public sealed record DecisionOutcome(
    bool Found,
    bool Applied,
    string? Status,
    DateTime? ConfirmedAt);

public sealed record DashboardSummary(
    int Total,
    int Pending,
    int Accepted,
    int Declined);
