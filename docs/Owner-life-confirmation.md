# Owner: xác nhận “Tôi vẫn còn sống”

## Contract frontend

Hai API cần `Authorization: Bearer <accessToken>` và role Owner đang Active, không khóa. Không cần `X-Vault-Request`.

| Method | Path | Mục đích |
| --- | --- | --- |
| GET | `/api/owner/life-confirmation` | Đọc lần xác nhận của chính Owner đăng nhập |
| POST | `/api/owner/life-confirmation` | Bấm nút xác nhận; không có request body |

Cả hai trả HTTP 200:

```json
{
  "ownerId": 12,
  "lastConfirmedAliveAt": "2026-10-20T02:30:00Z"
}
```

Nếu chưa có bản ghi PROOF_OF_LIFE, GET trả `lastConfirmedAliveAt: null`. Backend lấy UserId từ JWT và thời gian từ server; không nhận OwnerId, timestamp hoặc status do client chỉ định. POST trả kết quả sau khi transaction lưu thành công.

Frontend tải GET khi mở trang. Khi bấm “Tôi vẫn còn sống”, gọi POST, khóa nút trong khi chờ và cập nhật thời gian bằng response thành công. Hiển thị null là “Bạn chưa xác nhận còn sống”; đổi UTC sang `Asia/Ho_Chi_Minh` để hiển thị giờ Việt Nam. Không dùng giờ máy client thay cho response, không hiển thị thành công nếu request lỗi.

## Persistence và nghiệp vụ

Tận dụng bảng scaffold `PROOF_OF_LIFE`, không thêm bảng/cột. Lần đầu tạo record với FrequencyDays=30, GracePeriodDays=14 theo defaults hiện có. Các lần sau cập nhật record có LastPingAt mới nhất (CheckId phân định khi trùng giờ), giữ lịch FrequencyDays/GracePeriodDays hiện có, đặt LastPingAt/UpdatedAt theo UTC, NextPingDue theo lịch, MissedCount=0, Status=Active. Mỗi lần bấm ghi thêm audit `ConfirmAlive` trong cùng transaction.

SQL Server dùng transaction Serializable và khóa cập nhật trên dòng USERS của Owner để tuần tự hóa các lần bấm, kể cả tạo record đầu tiên. Không sửa entity/context scaffold hoặc USERS.Status. Lần xác nhận là lời khai của người dùng, không phải xác minh danh tính sinh trắc hay kết luận pháp lý. Chưa triển khai job nhắc lịch hoặc tự suy luận đã mất khi quá NextPingDue.

Nếu có giấy chứng tử trong hồ sơ chưa bị hủy/từ chối, hoặc hồ sơ đã duyệt pháp lý còn hiệu lực, POST trả **409**. Không thay đổi lần xác nhận, hủy hồ sơ hoặc tự đảo kết quả pháp lý. GET vẫn trả lịch sử gần nhất. Kiểm tra xung đột không phải cơ chế khóa chéo với upload giấy chứng tử; sự kiện lịch sử và hồ sơ pháp lý luôn được giữ riêng, xác nhận không cấp quyền bỏ qua xác minh.

Executor được phân công và LegalVerifier được giao đọc thêm field `lastConfirmedAliveAt` trong GET `/api/executor/handovers/{requestId}/owner-status` và `/api/legal/handovers/{requestId}/owner-status`. Các trạng thái khai báo/xác nhận đã mất vẫn giữ nguyên, không bị thay bằng “Alive”. API tổng quan toàn bộ đơn không mở thêm quyền đọc này.

## Test Swagger

1. Authorize bằng token Owner; GET lấy thời gian (null nếu chưa có record).
2. POST không body; kiểm tra giờ UTC có hậu tố Z. GET lại phải giữ thời gian vừa lưu.
3. Bấm lần nữa; giờ mới được lưu, audit cũ còn nguyên. Owner khác không xem/sửa được record của bạn bằng cách gửi OwnerId.
4. Thử không token: 401; token Executor/LegalVerifier/Beneficiary không có Owner: 403. Tài khoản khóa/inactive: 401; Owner role bị thu hồi: 403.
5. Với Owner có giấy chứng tử đang được xử lý hoặc kết quả pháp lý đã duyệt, POST trả 409 và giữ nguyên dữ liệu cũ.
6. Đổi token Executor/LegalVerifier được phân công để kiểm tra field bổ sung trong owner-status. Không được phân công vẫn không có quyền đọc chi tiết.

Collection: [LegacyVault.http](../LegacyVault.http). Console harness: `dotnet run --project LegacyVault.Tests`; kiểm tra HTTP, persistence SQLite, lịch mặc định/tùy chỉnh, giới hạn quyền và xung đột pháp lý. Chưa kiểm chứng khóa/concurrency trên SQL Server thật; không chạy migration hoặc seed database thật để test tính năng.
