-- ============================================================================
-- 04_Simulate_New_Scan.sql
-- Giả lập quét 1 vé máy bay mới tại quầy CIP từ phần mềm Bravo
-- Chạy script này, sau đó quay lại trang Admin nhấn "Làm mới danh sách"
-- ============================================================================

USE CipVendorSampleDb; -- hoặc TESTDB2
GO

DECLARE @RandomFlightNum INT = 100 + ABS(CHECKSUM(NEWID())) % 900;
DECLARE @Airlines TABLE (Code VARCHAR(3));
INSERT INTO @Airlines VALUES ('VN'), ('VJ'), ('QH'), ('SQ'), ('EK');

DECLARE @Airline VARCHAR(3) = (SELECT TOP 1 Code FROM @Airlines ORDER BY NEWID());

DECLARE @Names TABLE (FullName NVARCHAR(100));
INSERT INTO @Names VALUES 
(N'NGUYỄN TIẾN DŨNG'), 
(N'LÊ THỊ THẢO NGUYÊN'), 
(N'VŨ MINH TRÍ'), 
(N'TRẦN HOÀNG LONG'), 
(N'PHẠM THỊ BÍCH NGỌC');

DECLARE @PassengerName NVARCHAR(100) = (SELECT TOP 1 FullName FROM @Names ORDER BY NEWID());

INSERT INTO dbo.B30PosDocBPDetail 
(
    PosDocId,
    PassengerName,
    OperatingCarrierDesignator,
    FlightNumber,
    DateOfFlight,
    CreatedBy
)
VALUES
(
    101, -- POS-CIP-01
    @PassengerName,
    @Airline,
    CAST(@RandomFlightNum AS NVARCHAR(24)),
    CAST(GETDATE() AS DATE),
    1    -- Nhân viên lễ tân 1
);

SELECT TOP 1 
    Id AS NewPassengerId,
    PassengerName,
    OperatingCarrierDesignator + FlightNumber AS Flight,
    DateOfFlight,
    CreatedTime
FROM dbo.B30PosDocBPDetail
ORDER BY Id DESC;

PRINT N'>> Đã giả lập quét vé thành công! Hãy vào trang Admin và nhấn "Làm mới danh sách".';
GO
