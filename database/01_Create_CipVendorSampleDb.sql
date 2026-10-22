-- ============================================================================
-- 01_Create_CipVendorSampleDb.sql
-- Giả lập Database số 1 (Bravo ERP / POS Boarding Pass System)
-- ============================================================================

IF DB_ID('CipVendorSampleDb') IS NULL
BEGIN
    CREATE DATABASE CipVendorSampleDb;
END
GO

USE CipVendorSampleDb;
GO

-- 1. Bảng nhân viên lễ tân / người dùng Bravo (B00UserList)
IF OBJECT_ID('dbo.B00UserList', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.B00UserList (
        Id INT NOT NULL PRIMARY KEY,
        UserName NVARCHAR(50) NOT NULL,
        FullName NVARCHAR(100) NOT NULL
    );
END
GO

-- 2. Bảng kho / khu vực phòng chờ CIP (B20Warehouse)
IF OBJECT_ID('dbo.B20Warehouse', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.B20Warehouse (
        Id INT NOT NULL PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL
    );
END
GO

-- 3. Bảng chứng từ POS tại quầy (B30PosDoc)
IF OBJECT_ID('dbo.B30PosDoc', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.B30PosDoc (
        PosDocId INT NOT NULL PRIMARY KEY,
        WarehouseId INT NOT NULL,
        WorkstationCode NVARCHAR(50) NOT NULL,
        DocDate DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_PosDoc_Warehouse FOREIGN KEY (WarehouseId) REFERENCES dbo.B20Warehouse(Id)
    );
END
GO

-- 4. Bảng chi tiết Boarding Pass quét tại quầy (B30PosDocBPDetail)
IF OBJECT_ID('dbo.B30PosDocBPDetail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.B30PosDocBPDetail (
        Id INT IDENTITY(1001, 1) NOT NULL PRIMARY KEY,
        PosDocId INT NOT NULL,
        PassengerName NVARCHAR(254) NOT NULL,
        OperatingCarrierDesignator NVARCHAR(24) NOT NULL,
        FlightNumber NVARCHAR(24) NOT NULL,
        DateOfFlight SMALLDATETIME NULL,
        CreatedBy INT NULL,
        CreatedTime DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_BPDetail_PosDoc FOREIGN KEY (PosDocId) REFERENCES dbo.B30PosDoc(PosDocId)
    );
END
GO

-- 5. Nạp dữ liệu mẫu ban đầu
-- Người dùng lễ tân
IF NOT EXISTS (SELECT 1 FROM dbo.B00UserList WHERE Id = 1)
    INSERT INTO dbo.B00UserList (Id, UserName, FullName) VALUES (1, 'letan01', N'Nguyễn Thị Thu');
IF NOT EXISTS (SELECT 1 FROM dbo.B00UserList WHERE Id = 2)
    INSERT INTO dbo.B00UserList (Id, UserName, FullName) VALUES (2, 'letan02', N'Trần Minh Đức');

-- Phòng chờ CIP
IF NOT EXISTS (SELECT 1 FROM dbo.B20Warehouse WHERE Id = 1)
    INSERT INTO dbo.B20Warehouse (Id, Name) VALUES (1, N'Phòng chờ Quốc tế CIP Lounge - T2');
IF NOT EXISTS (SELECT 1 FROM dbo.B20Warehouse WHERE Id = 2)
    INSERT INTO dbo.B20Warehouse (Id, Name) VALUES (2, N'Phòng chờ Quốc nội CIP Lounge - T1');

-- Quầy POS
IF NOT EXISTS (SELECT 1 FROM dbo.B30PosDoc WHERE PosDocId = 101)
    INSERT INTO dbo.B30PosDoc (PosDocId, WarehouseId, WorkstationCode) VALUES (101, 1, 'POS-CIP-01');
IF NOT EXISTS (SELECT 1 FROM dbo.B30PosDoc WHERE PosDocId = 102)
    INSERT INTO dbo.B30PosDoc (PosDocId, WarehouseId, WorkstationCode) VALUES (102, 1, 'POS-CIP-02');

-- Dữ liệu hành khách mẫu
IF NOT EXISTS (SELECT 1 FROM dbo.B30PosDocBPDetail)
BEGIN
    SET IDENTITY_INSERT dbo.B30PosDocBPDetail ON;

    INSERT INTO dbo.B30PosDocBPDetail (Id, PosDocId, PassengerName, OperatingCarrierDesignator, FlightNumber, DateOfFlight, CreatedBy)
    VALUES
    (1001, 101, N'NGUYEN VAN AN', 'VN', '123', CAST(GETDATE() AS DATE), 1),
    (1002, 101, N'TRAN THI BICH', 'VJ', '456', CAST(GETDATE() AS DATE), 1),
    (1003, 101, N'LE HOANG NAM', 'QH', '789', CAST(GETDATE() AS DATE), 2),
    (1004, 102, N'PHAM THI MAI', 'VN', '214', CAST(GETDATE() AS DATE), 1),
    (1005, 102, N'HOANG VAN DUC', 'SQ', '176', CAST(GETDATE() AS DATE), 2),
    (1006, 101, N'DOAN THI LAN', 'VJ', '688', CAST(GETDATE() AS DATE), 1),
    (1007, 101, N'NGUYEN MINH TUAN', 'VN', '502', CAST(GETDATE() AS DATE), 2),
    (1008, 102, N'VU THI HONG', 'QH', '112', CAST(GETDATE() AS DATE), 1),
    (1009, 101, N'DANG QUOC HUY', 'VN', '720', CAST(GETDATE() AS DATE), 2),
    (1010, 102, N'BUI THI THAO', 'VJ', '234', CAST(GETDATE() AS DATE), 1);

    SET IDENTITY_INSERT dbo.B30PosDocBPDetail OFF;
END
GO

PRINT N'Cơ sở dữ liệu mẫu CipVendorSampleDb (Bravo Mock) đã tạo thành công!';
