-- ============================================================================
-- 03_Recommended_DPO_CIP_Index.sql
-- Tối ưu chỉ mục truy vấn cho bảng DPO_CIP
-- ============================================================================

USE CipConsentDb; -- hoặc đổi sang TESTDB2 nếu dùng chung database
GO

-- Index phục vụ kiểm tra trạng thái đang chờ (Pending) tức thời cho Tablet
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_DPO_CIP_Pending' 
      AND object_id = OBJECT_ID('dbo.DPO_CIP')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_DPO_CIP_Pending
    ON dbo.DPO_CIP (ID DESC)
    INCLUDE (PassengerName, OperatingCarrierDesignator, FlightNumber, DateOfFlight, WorkstationCode, CreateBy, AcceptContent)
    WHERE Accept IS NULL;
    
    PRINT N'Index IX_DPO_CIP_Pending đã được tạo thành công!';
END
ELSE
BEGIN
    PRINT N'Index IX_DPO_CIP_Pending đã tồn tại.';
END
GO
