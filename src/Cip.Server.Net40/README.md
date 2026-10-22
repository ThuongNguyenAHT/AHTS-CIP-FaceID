# CIP Confirmation — .NET Framework 4.0

Đây là bản legacy độc lập của `Cip.Server`, dành cho IIS chỉ chạy được .NET Framework 4.0.
Bản ASP.NET Core/.NET 10 trong `src/Cip.Server` được giữ nguyên.

## Khác biệt bắt buộc

- Server dùng ASP.NET `System.Web` và C# 4.
- Truy cập SQL Server bằng `System.Data.SqlClient` đồng bộ.
- Admin dùng cookie đã ký bằng `FormsAuthentication` API, nhưng tự kiểm tra để API trả đúng HTTP 401.
- Không dùng SignalR. Tablet đọc `DPO_CIP` qua `/api/tablet/current` theo chu kỳ mặc định 1 giây.
- HTML, CSS và JavaScript được nhúng vào DLL từ UI của bản .NET 10. Khi phục vụ `tablet.js`, lời gọi SignalR được tắt và trạng thái được đổi thành polling.

Polling chỉ đọc Database số 2 (`DPO_CIP`), không polling các bảng nguồn trong Database số 1.

## Build

Từ thư mục gốc repository:

```powershell
dotnet build .\CipConfirmation.Net40.sln -c Release
```

Website sẵn sàng để copy lên IIS được tạo tại:

```text
src\Cip.Server.Net40\bin\Release\net40\site
```

Thư mục này chỉ cần chứa:

```text
site/
├── bin/
│   ├── Cip.Server.Net40.dll
│   └── Cip.Server.Net40.pdb
└── web.config
```

Các trang và static assets nằm trong DLL, không cần copy từ project .NET 10 lên server.
Route được đăng ký bằng bootstrapper đã compile trong DLL, không dùng `Global.asax` và không sinh
`App_global.asax.*.dll` trong Temporary ASP.NET Files. Thiết kế này tương thích với máy bật chính sách
Application Control chặn dynamic assembly trong thư mục Temp.

## IIS

1. Cài/bật IIS cùng các role service ASP.NET 4.x, .NET Extensibility 4.x, ISAPI Extensions và ISAPI Filters.
2. Tạo Application Pool dùng `.NET CLR Version v4.0`, pipeline `Integrated`.
3. Tạo IIS Site trỏ Physical Path tới thư mục `site` đã build.
4. Host ứng dụng ở site root vì UI hiện dùng URL tuyệt đối như `/api/...`, `/css/...` và `/js/...`.
5. Cấp quyền Read & Execute thư mục site cho Application Pool identity.
6. Cấp quyền SQL Server cho Application Pool identity hoặc đổi connection string sang tài khoản triển khai riêng.
7. Bật HTTPS trước khi sử dụng thật.

Không chọn `No Managed Code`: tùy chọn đó dành cho ASP.NET Core; bản này là ứng dụng `System.Web` chạy trên CLR v4.0.

## Cấu hình

Chỉnh trực tiếp trong `web.config` của thư mục deploy:

- `VendorDatabase`: chỉ cần quyền `SELECT` trên bốn bảng nguồn.
- `ConsentDatabase`: cần `SELECT`, `INSERT`, `UPDATE` trên `DPO_CIP`.
- `Admin.Username` và `Admin.Password`: tài khoản lễ tân.
- `ConsentPolicy.*`: policy thực tế đã được phê duyệt.
- `Tablet.FallbackPollingMilliseconds`: chu kỳ polling, từ 500 đến 10000 ms.

Mặc định hai connection string dùng `Trusted_Connection=True`. Khi đó cần cấp quyền database cho identity chạy Application Pool. Với SQL Server ở máy khác, nên dùng domain/service account.

## URL

- Tablet: `/tablet`
- Admin: `/admin`
- Đăng nhập Admin: `/admin/login`
- Liveness: `/health/live`
- Database readiness: `/health/ready`

## Smoke test end-to-end

Bộ test dùng ASP.NET in-process host nên có thể kiểm tra pipeline `System.Web` ngay cả khi máy phát triển
chưa cài IIS/IIS Express:

```powershell
dotnet build .\CipConfirmation.Net40.sln -c Release
.\tests\Cip.Server.Net40.SmokeHost\Run-SmokeTests.ps1 -Configuration Release
```

Test kiểm tra UI, authentication, 200 hành khách nguồn, quy tắc một pending, accept, decline,
chặn gửi lại và dashboard. Nó chỉ chọn các ID chưa tồn tại trong `DPO_CIP`, tạo hai dòng test rồi
xóa đúng hai ID đã xác minh trong khối cleanup. Cuối test, số dòng `DPO_CIP` phải bằng trước test.

Smoke host được đóng gói vào site test riêng dưới `tests/.../bin`; DLL test không được copy vào
thư mục deploy production `src/Cip.Server.Net40/bin/.../site`.

## Giới hạn hỗ trợ

.NET Framework 4.0 đã hết vòng đời hỗ trợ của Microsoft. Bản này chỉ nên dùng khi hạ tầng legacy là ràng buộc bắt buộc. Thử nghiệm bản legacy bằng IIS/IIS Express; `dotnet run` không host được ứng dụng `System.Web`.
