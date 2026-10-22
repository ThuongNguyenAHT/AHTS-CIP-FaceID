using System.Text.RegularExpressions;

namespace Cip.Server.Domain;

public static partial class FlightCodeFormatter
{
    public static (string PassengerName, string AirlineCode, string FlightNumber, string FlightCode)
        Normalize(SourcePassenger passenger)
    {
        ArgumentNullException.ThrowIfNull(passenger);

        var passengerName = CollapseWhitespace(passenger.PassengerName);
        var airlineCode = passenger.OperatingCarrierDesignator.Trim().ToUpperInvariant();
        var flightNumber = passenger.FlightNumber.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(passengerName) || passengerName.Length > 254)
        {
            throw new InvalidDataException("Tên hành khách không hợp lệ.");
        }

        if (!AirlineCodePattern().IsMatch(airlineCode))
        {
            throw new InvalidDataException("Mã hãng bay không hợp lệ.");
        }

        if (!FlightNumberPattern().IsMatch(flightNumber))
        {
            throw new InvalidDataException("Số hiệu chuyến bay không hợp lệ.");
        }

        return (passengerName, airlineCode, flightNumber, airlineCode + flightNumber);
    }

    private static string CollapseWhitespace(string value) =>
        WhitespacePattern().Replace(value.Trim(), " ");

    [GeneratedRegex("^[A-Z0-9]{2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex AirlineCodePattern();

    [GeneratedRegex("^[A-Z0-9]{1,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex FlightNumberPattern();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
