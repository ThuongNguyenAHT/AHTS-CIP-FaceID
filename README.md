# CIP Confirmation

Ứng dụng ASP.NET Core cho quy trình xác nhận thủ công tại quầy CIP:

1. Phần mềm bên thứ ba ghi Boarding Pass vào các bảng nguồn.
2. Lễ tân mở Admin và nhấn **Làm mới danh sách**.
3. Lễ tân tick một hành khách rồi nhấn **Hiển thị trên tablet**.
4. Server insert snapshot vào `DPO_CIP` với `Accept = NULL`.
5. Tablet hiển thị policy; khách chọn Đồng ý hoặc Từ chối.
6. Server cập nhật `Accept` và `CreateTime` trong `DPO_CIP`.

Ứng dụng không polling Database số 1 ở background và không can thiệp phần mềm quét vé.

## Database thật

Cấu hình mặc định kết nối `TESTDB2`, đúng schema trong `hi.sql`:

- Database số 1: `B30PosDocBPDetail`, `B30PosDoc`, `B20Warehouse`, `B00UserList`.
- Database số 2: bảng `DPO_CIP`.

Truy vấn nguồn nằm tại [SourcePassengerRepository.cs](src/Cip.Server/Infrastructure/SourcePassengerRepository.cs) và tương đương:

```sql
SELECT TOP (200)
    bp.Id,
    bp.PassengerName,
    bp.OperatingCarrierDesignator,
    bp.FlightNumber,
    bp.DateOfFlight,
    p.WorkstationCode,
    wh.Name,
    u.UserName,
    u.FullName
FROM B30PosDocBPDetail bp
LEFT JOIN B30PosDoc p ON bp.PosDocId = p.PosDocId
LEFT JOIN B20Warehouse wh ON wh.Id = p.WarehouseId
LEFT JOIN B00UserList u ON bp.CreatedBy = u.Id
ORDER BY bp.Id DESC;
```

Hai connection string vẫn tách riêng trong cấu hình để sau này Database số 1 và bảng `DPO_CIP` có thể nằm ở hai database/server khác nhau.

## Mapping `DPO_CIP`

| Cột | Giá trị |
|---|---|
| `ID` | `B30PosDocBPDetail.Id` |
| `PassengerName` | Tên hành khách |
| `OperatingCarrierDesignator` | Mã hãng |
| `FlightNumber` | Số hiệu chuyến bay |
| `DateOfFlight` | Ngày bay |
| `WorkstationCode` | Workstation từ `B30PosDoc` |
| `CreateBy` | `FullName`, fallback sang `UserName` |
| `AcceptContent` | Nội dung policy đã hiển thị |
| `Accept` | `NULL` đang chờ, `1` đồng ý, `0` từ chối |
| `CreateTime` | Để `NULL` khi gửi tablet; cập nhật lúc khách xác nhận |

Hệ thống chỉ cho một dòng `Accept IS NULL` tại một thời điểm vì chỉ có một tablet.

## Database development mẫu

Nếu không muốn dùng `TESTDB2`, chạy:

1. [01_Create_CipVendorSampleDb.sql](database/01_Create_CipVendorSampleDb.sql)
2. [02_Create_CipConsentDb.sql](database/02_Create_CipConsentDb.sql)

```powershell
sqlcmd -S localhost -E -f 65001 -i ".\database\01_Create_CipVendorSampleDb.sql"
sqlcmd -S localhost -E -f 65001 -i ".\database\02_Create_CipConsentDb.sql"
```

Sau đó đặt connection string:

```powershell
$env:ConnectionStrings__VendorDatabase = 'Server=localhost;Database=CipVendorSampleDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False'
$env:ConnectionStrings__ConsentDatabase = 'Server=localhost;Database=CipConsentDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False'
```

Script [03_Recommended_DPO_CIP_Index.sql](database/03_Recommended_DPO_CIP_Index.sql) là tùy chọn cho database thật. Nó kiểm tra duplicate trước khi thêm unique filtered index trên `DPO_CIP.ID`.

## Cấu hình Admin và policy

Tài khoản mặc định để chạy ngay:

- Username: `admin`
- Password: `Cip@123456`

Có thể đổi trực tiếp trong phần `Admin` của `appsettings.json` hoặc ghi đè bằng biến môi trường `Admin__Username` và `Admin__Password` khi triển khai.

Nội dung policy nằm trong `ConsentPolicy` tại [appsettings.json](src/Cip.Server/appsettings.json). Phải thay policy mẫu bằng nội dung đã được phê duyệt trước khi chạy thật.

## Chạy ứng dụng

```powershell
dotnet restore .\CipConfirmation.slnx
dotnet run --project .\src\Cip.Server\Cip.Server.csproj
```

- Tablet: `http://localhost:5188/tablet`
- Admin: `http://localhost:5188/admin`
- Health check: `http://localhost:5188/health/ready`

### Bản IIS legacy .NET Framework 4.0

Bản viết lại cho IIS/.NET Framework 4.0 nằm tại
[src/Cip.Server.Net40](src/Cip.Server.Net40/README.md). Bản này build và triển khai độc lập,
dùng polling Database số 2 thay cho SignalR và không thay đổi ứng dụng .NET 10 hiện tại.

Tại Admin:

1. Sau khi lễ tân quét vé, nhấn **Làm mới danh sách**.
2. Tick hành khách có trạng thái **Chưa hiển thị**.
3. Nhấn **Hiển thị trên tablet**.
4. Chờ khách hoàn tất trước khi gửi người tiếp theo.

## Kiểm thử

```powershell
dotnet test .\CipConfirmation.slnx
```

## Triển khai

- Production phải dùng HTTPS.
- Tài khoản Database số 1 chỉ cần quyền `SELECT` trên bốn bảng nguồn.
- Tài khoản Database số 2 cần `SELECT`, `INSERT`, `UPDATE` trên `DPO_CIP`.
- Nếu host bằng IIS, bật Application Initialization/Always On.
- Tablet vẫn gọi `/api/tablet/current` mỗi giây làm fallback nếu mất SignalR; việc này chỉ đọc `DPO_CIP`, không polling Database số 1.
