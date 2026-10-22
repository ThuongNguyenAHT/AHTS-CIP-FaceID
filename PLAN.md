# PLAN — CIP Confirmation sử dụng database thật

## 1. Phạm vi

- Không xử lý việc quét Boarding Pass.
- Không polling Database số 1 ở background.
- Lễ tân chủ động tải 200 hành khách gần nhất trên trang Admin.
- Chỉ khi lễ tân chọn khách và nhấn **Hiển thị**, dữ liệu mới được insert vào `DPO_CIP` và gửi tới tablet.
- Một cổng, một tablet, một khách được hiển thị tại một thời điểm.

## 2. Nguồn dữ liệu

Database số 1 được đọc bằng join:

```text
B30PosDocBPDetail
  -> B30PosDoc qua PosDocId
  -> B20Warehouse qua WarehouseId
  -> B00UserList qua CreatedBy
```

Các trường sử dụng:

- `bp.Id`
- `bp.PassengerName`
- `bp.OperatingCarrierDesignator`
- `bp.FlightNumber`
- `bp.DateOfFlight`
- `p.WorkstationCode`
- `wh.Name`
- `u.UserName`
- `u.FullName`

Database số 1 chỉ được đọc.

## 3. Luồng Admin thủ công

1. Lễ tân quét vé bằng phần mềm bên thứ ba.
2. Phần mềm bên thứ ba insert dữ liệu nguồn.
3. Lễ tân nhấn **Làm mới danh sách** trên Admin.
4. Server chạy truy vấn `TOP 200 ... ORDER BY bp.Id DESC`.
5. Server đối chiếu các ID với `DPO_CIP` và trả trạng thái:
   - `NotSent`: chưa gửi tablet.
   - `Pending`: đang chờ khách xác nhận.
   - `Accepted`: đã đồng ý.
   - `Declined`: đã từ chối.
6. Lễ tân tick đúng một hành khách.
7. Khi nhấn **Hiển thị trên tablet**, server đọc lại hành khách theo ID để tránh tin dữ liệu từ browser.
8. Nếu tablet đang có khách khác, server từ chối thao tác.
9. Nếu hợp lệ, server insert snapshot vào `DPO_CIP` và phát SignalR.

## 4. Luồng tablet

1. Tablet ở màn hình chờ khi không có dòng `DPO_CIP.Accept IS NULL`.
2. Sau thao tác của lễ tân, tablet nhận SignalR và gọi API lấy dòng pending.
3. Tablet hiển thị tên, chuyến bay và `AcceptContent`.
4. Khách chọn Đồng ý hoặc Từ chối.
5. Server cập nhật:
   - Đồng ý: `Accept = 1`.
   - Từ chối: `Accept = 0`.
   - `CreateTime = GETDATE()` tại thời điểm xác nhận.
6. Tablet quay lại màn hình chờ.

## 5. Quy tắc `DPO_CIP`

- `ID` là ID nguồn từ `B30PosDocBPDetail`.
- Không tạo GUID hoặc correlation ID.
- `AcceptContent` lưu policy thực tế đã hiển thị.
- `CreateTime` là thời điểm xác nhận, không phải thời điểm lễ tân nhấn hiển thị.
- Không cho gửi lại ID đã `Accepted` hoặc `Declined`.
- Cho phép gửi lại SignalR nếu cùng ID vẫn đang `Pending`.
- Chỉ cho tồn tại một lượt pending trong luồng ứng dụng.

## 6. Thành phần

- `Cip.Server`: API, Admin auth, truy vấn nguồn, thao tác `DPO_CIP`, SignalR.
- `Cip.Tablet.Web`: giao diện policy và quyết định.
- `Cip.Admin.Web`: làm mới, chọn khách, gửi tablet, Dashboard.
- Không có polling Worker/BackgroundService.

## 7. Kiểm thử bắt buộc

1. Mở Admin không tự insert dữ liệu vào `DPO_CIP`.
2. Nhấn Làm mới trả đúng dữ liệu join bốn bảng.
3. Chọn khách rồi nhấn Hiển thị tạo đúng một dòng `Accept = NULL`.
4. Tablet hiển thị đúng tên và mã chuyến bay.
5. Đồng ý cập nhật `Accept = 1` và `CreateTime`.
6. Từ chối cập nhật `Accept = 0` và `CreateTime`.
7. Không cho hiển thị khách thứ hai khi khách thứ nhất chưa hoàn tất.
8. Không cho gửi lại khách đã hoàn tất.
9. Refresh Admin hiển thị đúng trạng thái từ `DPO_CIP`.
10. SignalR lỗi thì tablet vẫn lấy được dòng pending qua API fallback.

## 8. Tiêu chí hoàn thành

- Code build và unit test thành công.
- Không còn service polling Database số 1.
- Connection mặc định sử dụng schema `TESTDB2` đã cung cấp.
- Luồng Admin chọn thủ công hoạt động end-to-end.
- Code không yêu cầu thêm cột vào `DPO_CIP`.
