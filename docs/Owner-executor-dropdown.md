# Dropdown Executor khi Owner tạo kho

## Lấy danh sách

Nút xem chi tiết đã có GET `/api/owner/executors/{executorId}`. Luồng Executor chấp nhận/từ chối đơn và contract đầy đủ xem [Executor-intake-and-profile.md](Executor-intake-and-profile.md).

```http
GET /api/owner/executors?page=1&pageSize=20&search=Nguyen
Authorization: Bearer <ownerAccessToken>
```

Query: `page` mặc định 1, `pageSize` mặc định 20 (1–100), `search` tùy chọn tìm theo họ tên tối đa 100 ký tự, `vaultId` tùy chọn khi kho đã tồn tại. Search bỏ khoảng trắng đầu/cuối, không phân biệt hoa/thường; không cam kết bỏ dấu tiếng Việt.

Response 200:

```json
{
  "page": 1,
  "pageSize": 20,
  "items": [
    { "userId": 15, "fullName": "Nguyen Van A" }
  ]
}
```

Chỉ trả tài khoản có role Executor, Status Active, không có authentication đang khóa và không phải Owner đang gọi. Không trả email, số điện thoại hoặc dữ liệu xác thực. Danh sách sắp theo họ tên rồi UserId, phân trang trong truy vấn database. Không có kết quả trả `items: []`, không tạo Executor thay thế.

Khi truyền `vaultId`, Owner phải sở hữu kho đó; API còn loại người có phân bổ thụ hưởng Active trong kho, đúng điều kiện độc lập của API gán Executor. Trước khi tạo kho chưa có vaultId, các xung đột phát sinh sau đó vẫn được kiểm tra lại khi gán.

## Flow frontend

1. Mở form tạo kho: gọi GET trên, dùng `fullName` làm label, `userId` làm value của dropdown. Search/load trang tiếp khi cần; không dùng tên làm định danh.
2. Owner bấm lưu: gọi `POST /api/owner/vaults` theo contract hiện có với name/description, nhận `vaultId`.
3. Nếu đã chọn Executor, gọi:

```http
PUT /api/owner/vaults/123/executor
Authorization: Bearer <ownerAccessToken>
Content-Type: application/json

{ "executorId": 15 }
```

Thành công gán trả 204. Đây là hai request riêng, không phải transaction tạo kho + gán: nếu gán thất bại, kho vẫn đã tạo; frontend báo lỗi gán, tải lại options theo vaultId và cho chọn lại. Không POST tạo lại kho chỉ vì PUT thất bại. Trạng thái/role có thể thay đổi sau khi lấy dropdown nên PUT luôn kiểm tra lại quyền và eligibility.

Nếu không chọn, frontend không gọi PUT; cơ chế tự chọn Executor đủ điều kiện khi tạo yêu cầu bàn giao giữ nguyên. Endpoint tạo kho không nhận thêm field executorId trong thay đổi này.

## Quyền và lỗi

- 401: thiếu/hết hạn JWT, tài khoản khóa/inactive.
- 403: không có Owner role hiện tại.
- 400: page/pageSize/search/vaultId không hợp lệ.
- 404: vaultId không tồn tại hoặc thuộc Owner khác.

Chỉ thêm API đọc; không sửa frontend, không đổi schema hoặc seed tài khoản trên database thật. Test HTTP dùng SQLite cô lập, bao gồm lọc role/Active/khóa, Owner nhiều role, phân trang, search, giới hạn dữ liệu và ownership/vai trò thụ hưởng của vault. SQL Server thật chưa được chạy thử.
