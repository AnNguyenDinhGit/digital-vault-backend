# Owner chọn Executor và Executor tiếp nhận đơn

## Owner: danh sách và chi tiết Executor

Tất cả request cần Bearer JWT. Owner dùng GET `/api/owner/executors?page=1&pageSize=20&search=Nguyen` để lấy dropdown. Response `items` chỉ có `userId`, `fullName`; frontend hiển thị tên, lưu ID. Giữ phân trang/search và điều kiện Active, không khóa, có Executor role, không phải chính Owner. Có thể truyền `vaultId` để lọc thêm xung đột người thụ hưởng của kho đang sở hữu.

Nút “Xem chi tiết” gọi GET `/api/owner/executors/{executorId}` (cũng hỗ trợ `vaultId` tùy chọn):

```json
{
  "userId": 15,
  "fullName": "Nguyen Van A",
  "role": "Executor",
  "status": "Active",
  "joinedAt": "2026-10-01T02:00:00Z"
}
```

Chỉ xem được Executor đủ điều kiện; trả 404 nếu không tồn tại, sai role, inactive, khóa, chính Owner hoặc xung đột vault. Không trả CCCD, email, điện thoại, mật khẩu/secret. Schema chưa có hồ sơ chuyên môn/tiểu sử/ảnh đại diện Executor nên API không tạo dữ liệu thay thế.

Owner gán người đã chọn qua PUT `/api/owner/vaults/{vaultId}/executor` với `{ "executorId": 15 }`. Bước này ghi phân công và thông báo cho Executor; GET `/api/executor/assignments` hiển thị ngay phân công, gồm thêm `vaultName` và `owner: { userId, fullName }`.

Gán phân công và tạo đơn bàn giao là hai bước riêng: kho vừa tạo có thể chưa có tài sản/tài liệu/phân bổ hợp lệ để lập đơn. Khi đủ điều kiện, Owner POST `/api/owner/vaults/{vaultId}/handovers`; đơn mới ở **Pending_Executor**. Trước khi tạo đơn, Owner vẫn có thể dùng dropdown chọn Executor ở bước chuẩn bị bàn giao. Không đổi executorId trong một đơn đang xử lý.

## Executor: danh sách và quyết định

- GET `/api/executor/assignments?page=1&pageSize=20`: các kho đang được phân công.
- GET `/api/executor/handovers?page=1&pageSize=20`: đơn của mình, gồm đơn chờ và lịch sử đã từ chối.
- GET `/api/executor/handovers/overview?assignedOnly=true&status=Pending_Executor&page=1&pageSize=20`: danh sách đơn chờ tiếp nhận. `assignedOnly=false` là tổng quan mọi đơn; quyền tổng quan không cho phép quyết định đơn của người khác.
- GET `/api/executor/handovers/{requestId}`: chi tiết và Owner trong đơn.

Nút **Chấp nhận**:

```http
POST /api/executor/handovers/123/accept
Authorization: Bearer <assignedExecutorToken>
```

Không body. Response 200 HandoverDto với `status: "Draft"`. Kiểm tra lại kho/tài sản/phân bổ/tài liệu và phân công còn hợp lệ. Chỉ mở bước chuẩn bị bằng chứng, không duyệt pháp lý.

Nút **Từ chối tiếp nhận**:

```http
POST /api/executor/handovers/123/decline
Authorization: Bearer <assignedExecutorToken>
Content-Type: application/json

{ "reason": "Hiện tại không thể tiếp nhận hồ sơ này" }
```

Reason bắt buộc, không chỉ khoảng trắng, tối đa 2000 ký tự. Response 200 HandoverDto với `status: "Declined"`. Trong cùng lần lưu transaction: đóng các case thành Cancelled, thu hồi phân công, ghi audit và thông báo cho Owner kèm mã đơn/lý do. Đơn cũ vẫn lưu và Executor đã từ chối có quyền đọc lịch sử của chính đơn đó; không upload/khôi phục/xử lý tiếp. Owner nhận lý do qua API notifications và xem status qua API handovers của mình.

Sau từ chối, Owner được chọn Executor khác rồi tạo **đơn mới**, giữ lịch sử đơn cũ. Không tự chuyển đơn sang người khác hoặc random lại khi GET. Nếu không chỉ định người mới, cơ chế random hợp lệ hiện có chỉ chạy khi Owner tạo đơn mới.

Chỉ Pending_Executor được accept/decline; gọi lặp hoặc đổi ý sau quyết định trả 409. Sai role trả 403; không có phân công trả 404; JWT không hợp lệ trả 401; input sai trả 400. Hai quyết định cạnh tranh không được cùng commit, bên thua trả 409.

## State machine và tương thích

`Pending_Executor → Draft → Pending_Legal → Approved → In_Progress → Completed`.

Nhánh từ chối tiếp nhận: `Pending_Executor → Declined`. Owner cũng có thể cancel đơn Pending_Executor trước quyết định; sau cancel Executor không accept/decline được.

Nhánh pháp lý giữ nguyên: `Pending_Legal → Rejected` hoặc `Additional_Info`, do LegalVerifier được giao quyết định. **Declined của Executor khác Rejected của pháp lý**. Không được upload giấy chứng tử, nộp xác minh, start/complete khi Pending_Executor hoặc Declined.

Hồ sơ cũ Draft được coi là đã mở bước chuẩn bị, không tự chuyển ngược Pending_Executor; các hồ sơ pháp lý/hoàn tất cũ không thay đổi. Nếu Executor chủ động tạo hồ sơ từ phân công qua POST `/api/executor/assignments/{vaultId}/handovers`, hành động đó bao gồm chấp nhận và tạo Draft; không cần gọi accept lần nữa. Tài sản/tài liệu/phân bổ vẫn phải đủ điều kiện trước khi tạo đơn.

Frontend cần thêm nhãn Pending_Executor/Declined; chỉ hiện hai nút quyết định trên đơn Pending_Executor thuộc mình, khóa nút trong khi gửi, tải lại khi gặp 409. Danh sách vẫn dùng `{ page, pageSize, items }`. Quyền không dựa vào việc frontend ẩn nút; backend luôn kiểm tra JWT, role hiện tại, phân công và trạng thái.

## Persistence và kiểm chứng

Tận dụng HANDOVER_REQUESTS.Status, EXECUTOR_ASSIGNMENTS.Status, HANDOVER_CASES, AUDIT_LOGS, NOTIFICATIONS hiện có. Không thêm bảng/cột, không sửa file entity/context scaffold, không migration/seed database thật. Hai status mới vừa giới hạn 20 ký tự trong scaffold; schema SQL Server thật chưa được audit, nếu DB có CHECK constraint giới hạn danh sách status thì cần người quản trị review/cập nhật constraint đó trước khi triển khai.

Build đã thành công; console harness đạt 495 kiểm tra, bao gồm HTTP Swagger JSON, dropdown/detail, nhận/từ chối, quyền, luồng pháp lý, JWT/Google/2FA, rollback thật và hai quyết định cạnh tranh qua DbContext SQLite cô lập. Chưa kiểm chứng SQL Server concurrency, SMTP/Google thật hoặc chứng thư production. Test không ghi database thật. Collection mẫu: [LegacyVault.http](../LegacyVault.http).
