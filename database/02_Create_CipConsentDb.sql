-- ============================================================================
-- 02_Create_CipConsentDb.sql
-- Tạo Database số 2 (Bảng DPO_CIP lưu quyết định xác nhận của hành khách)
-- ============================================================================

IF DB_ID('CipConsentDb') IS NULL
BEGIN
    CREATE DATABASE CipConsentDb;
END
GO

USE CipConsentDb;
GO

IF OBJECT_ID('dbo.DPO_CIP', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DPO_CIP (
        ID INT NOT NULL PRIMARY KEY,
        PassengerName NVARCHAR(254) NOT NULL,
        OperatingCarrierDesignator NVARCHAR(24) NOT NULL,
        FlightNumber NVARCHAR(24) NOT NULL,
        DateOfFlight SMALLDATETIME NULL,
        WorkstationCode NVARCHAR(24) NULL,
        CreateBy NVARCHAR(256) NULL,
        CreateTime DATETIME NULL,
        AcceptContent NVARCHAR(MAX) NULL,
        Accept BIT NULL
    );
END
GO

PRINT N'Bảng DPO_CIP trong CipConsentDb đã được khởi tạo thành công!';
