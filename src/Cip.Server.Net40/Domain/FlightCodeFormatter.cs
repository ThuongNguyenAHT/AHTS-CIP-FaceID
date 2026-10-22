using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Cip.Server.Net40.Domain
{
    public static class FlightCodeFormatter
    {
        private static readonly Regex AirlineCodePattern =
            new Regex("^[A-Z0-9]{2,3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FlightNumberPattern =
            new Regex("^[A-Z0-9]{1,24}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex WhitespacePattern =
            new Regex("\\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static NormalizedPassenger Normalize(SourcePassenger passenger)
        {
            if (passenger == null)
            {
                throw new ArgumentNullException("passenger");
            }

            string passengerName = WhitespacePattern.Replace(passenger.PassengerName.Trim(), " ");
            string airlineCode = passenger.OperatingCarrierDesignator.Trim().ToUpperInvariant();
            string flightNumber = passenger.FlightNumber.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(passengerName) || passengerName.Length > 254)
            {
                throw new InvalidDataException("Tên hành khách không hợp lệ.");
            }

            if (!AirlineCodePattern.IsMatch(airlineCode))
            {
                throw new InvalidDataException("Mã hãng bay không hợp lệ.");
            }

            if (!FlightNumberPattern.IsMatch(flightNumber))
            {
                throw new InvalidDataException("Số hiệu chuyến bay không hợp lệ.");
            }

            return new NormalizedPassenger
            {
                PassengerName = passengerName,
                AirlineCode = airlineCode,
                FlightNumber = flightNumber
            };
        }
    }
}
