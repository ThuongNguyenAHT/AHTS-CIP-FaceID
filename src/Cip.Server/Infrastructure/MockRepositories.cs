using System.Collections.Concurrent;
using Cip.Server.Configuration;
using Cip.Server.Domain;
using Microsoft.Extensions.Options;

namespace Cip.Server.Infrastructure;

public sealed class MockSourcePassengerRepository : SourcePassengerRepository
{
    private readonly List<SourcePassenger> _passengers = [];
    private readonly object _lock = new();
    private int _nextId = 1011;

    public MockSourcePassengerRepository(
        DatabaseConnectionFactory connectionFactory,
        IOptions<SourceDataOptions> options)
        : base(connectionFactory, options)
    {
        var today = DateTime.Today;

        _passengers.AddRange([
            new SourcePassenger(1001, "NGUYEN VAN AN", "VN", "123", today, "POS-CIP-01", "Phòng chờ Quốc tế T2", "letan01", "Nguyễn Thị Thu"),
            new SourcePassenger(1002, "TRAN THI BICH", "VJ", "456", today, "POS-CIP-01", "Phòng chờ Quốc tế T2", "letan01", "Nguyễn Thị Thu"),
            new SourcePassenger(1003, "LE HOANG NAM", "QH", "789", today, "POS-CIP-01", "Phòng chờ Quốc tế T2", "letan02", "Trần Minh Đức"),
            new SourcePassenger(1004, "PHAM THI MAI", "VN", "214", today, "POS-CIP-02", "Phòng chờ Quốc tế T2", "letan01", "Nguyễn Thị Thu"),
            new SourcePassenger(1005, "HOANG VAN DUC", "SQ", "176", today, "POS-CIP-02", "Phòng chờ Quốc tế T2", "letan02", "Trần Minh Đức"),
            new SourcePassenger(1006, "DOAN THI LAN", "VJ", "688", today, "POS-CIP-01", "Phòng chờ Quốc nội T1", "letan01", "Nguyễn Thị Thu"),
            new SourcePassenger(1007, "NGUYEN MINH TUAN", "VN", "502", today, "POS-CIP-01", "Phòng chờ Quốc nội T1", "letan02", "Trần Minh Đức"),
            new SourcePassenger(1008, "VU THI HONG", "QH", "112", today, "POS-CIP-02", "Phòng chờ Quốc nội T1", "letan01", "Nguyễn Thị Thu"),
            new SourcePassenger(1009, "DANG QUOC HUY", "VN", "720", today, "POS-CIP-01", "Phòng chờ Quốc nội T1", "letan02", "Trần Minh Đức"),
            new SourcePassenger(1010, "BUI THI THAO", "VJ", "234", today, "POS-CIP-02", "Phòng chờ Quốc nội T1", "letan01", "Nguyễn Thị Thu")
        ]);
    }

    public override Task<IReadOnlyList<SourcePassenger>> GetLatestAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<SourcePassenger>>(_passengers.OrderByDescending(p => p.Id).ToList());
        }
    }

    public override Task<SourcePassenger?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var passenger = _passengers.FirstOrDefault(p => p.Id == id);
            return Task.FromResult(passenger);
        }
    }

    public override Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public SourcePassenger SimulateNewPassengerScan()
    {
        string[] sampleNames = [
            "NGUYỄN TIẾN DŨNG",
            "LÊ THỊ THẢO NGUYÊN",
            "VŨ MINH TRÍ",
            "TRẦN HOÀNG LONG",
            "PHẠM BÍCH NGỌC",
            "ĐỖ ANH TUẤN"
        ];
        string[] airlines = ["VN", "VJ", "QH", "SQ"];

        lock (_lock)
        {
            var id = _nextId++;
            var name = sampleNames[id % sampleNames.Length];
            var airline = airlines[id % airlines.Length];
            var flightNum = $"{100 + (id * 17) % 899}";

            var newPassenger = new SourcePassenger(
                id,
                name,
                airline,
                flightNum,
                DateTime.Today,
                "POS-CIP-01",
                "Phòng chờ Quốc tế T2",
                "letan01",
                "Nguyễn Thị Thu"
            );

            _passengers.Insert(0, newPassenger);
            return newPassenger;
        }
    }
}

public sealed class MockConsentRepository : ConsentRepository
{
    private sealed record MockDpoRecord(
        int Id,
        string PassengerName,
        string OperatingCarrierDesignator,
        string FlightNumber,
        DateTime? DateOfFlight,
        string? WorkstationCode,
        string? CreateBy,
        DateTime? CreateTime,
        string AcceptContent,
        bool? Accept);

    private readonly ConcurrentDictionary<int, MockDpoRecord> _records = new();

    public MockConsentRepository(
        DatabaseConnectionFactory connectionFactory,
        IOptions<ConsentPolicyOptions> policyOptions)
        : base(connectionFactory, policyOptions)
    {
    }

    public override Task<IReadOnlyDictionary<int, DpoConsentState>> GetStatesAsync(
        IReadOnlyCollection<int> sourceIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, DpoConsentState>();
        foreach (var id in sourceIds)
        {
            if (_records.TryGetValue(id, out var record))
            {
                var status = record.Accept switch
                {
                    null => "Pending",
                    true => "Accepted",
                    false => "Declined"
                };
                result[id] = new DpoConsentState(id, status, record.CreateTime);
            }
        }
        return Task.FromResult<IReadOnlyDictionary<int, DpoConsentState>>(result);
    }

    public override Task<QueueForDisplayResult> QueueForDisplayAsync(
        SourcePassenger passenger,
        CancellationToken cancellationToken)
    {
        lock (_records)
        {
            var pending = _records.Values.FirstOrDefault(r => r.Accept is null);
            if (pending is not null)
            {
                if (pending.Id == passenger.Id)
                {
                    return Task.FromResult(QueueForDisplayResult.AlreadyPending);
                }
                return Task.FromResult(QueueForDisplayResult.AnotherPassengerPending);
            }

            if (_records.TryGetValue(passenger.Id, out var existing) && existing.Accept is not null)
            {
                return Task.FromResult(QueueForDisplayResult.AlreadyCompleted);
            }

            var normalized = FlightCodeFormatter.Normalize(passenger);
            _records[passenger.Id] = new MockDpoRecord(
                passenger.Id,
                normalized.PassengerName,
                normalized.AirlineCode,
                normalized.FlightNumber,
                passenger.DateOfFlight,
                passenger.WorkstationCode,
                passenger.CreatedByDisplay,
                null,
                _policy.Content,
                null
            );

            return Task.FromResult(QueueForDisplayResult.Queued);
        }
    }

    public override Task<ConsentDisplay?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var pending = _records.Values.FirstOrDefault(r => r.Accept is null);
        if (pending is null)
        {
            return Task.FromResult<ConsentDisplay?>(null);
        }

        var carrier = pending.OperatingCarrierDesignator.Trim().ToUpperInvariant();
        var flightNumber = pending.FlightNumber.Trim().ToUpperInvariant();

        var display = new ConsentDisplay(
            pending.Id,
            pending.PassengerName,
            carrier,
            flightNumber,
            carrier + flightNumber,
            pending.DateOfFlight,
            pending.WorkstationCode,
            pending.CreateBy ?? string.Empty,
            _policy.Version,
            _policy.Title,
            pending.AcceptContent,
            _policy.LanguageCode
        );

        return Task.FromResult<ConsentDisplay?>(display);
    }

    public override Task<DecisionOutcome> SaveDecisionAsync(
        int id,
        bool accept,
        CancellationToken cancellationToken)
    {
        lock (_records)
        {
            if (!_records.TryGetValue(id, out var record))
            {
                return Task.FromResult(new DecisionOutcome(false, false, null, null));
            }

            if (record.Accept is not null)
            {
                var existingStatus = record.Accept == true ? "Accepted" : "Declined";
                return Task.FromResult(new DecisionOutcome(true, false, existingStatus, record.CreateTime));
            }

            var confirmedAt = DateTime.Now;
            _records[id] = record with
            {
                Accept = accept,
                CreateTime = confirmedAt
            };

            var status = accept ? "Accepted" : "Declined";
            return Task.FromResult(new DecisionOutcome(true, true, status, confirmedAt));
        }
    }

    public override Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var all = _records.Values;
        var total = all.Count;
        var pending = all.Count(r => r.Accept is null);
        var accepted = all.Count(r => r.Accept == true);
        var declined = all.Count(r => r.Accept == false);

        return Task.FromResult(new DashboardSummary(total, pending, accepted, declined));
    }

    public override Task<bool> CanConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
