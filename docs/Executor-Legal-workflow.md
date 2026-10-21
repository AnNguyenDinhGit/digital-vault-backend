# Hướng dẫn Executor và LegalVerifier

Mọi endpoint bên dưới dùng `Authorization: Bearer <accessToken>`. Định danh và quyền lấy từ JWT kết hợp tài khoản/role hiện tại trong database. Không gửi `UploadedBy` hoặc tự nhận phân công bằng `ExecutorId`.

## Tổng quan tất cả đơn

`GET /api/executor/handovers/overview?page=1&pageSize=20&assignedOnly=false` trả `{ page, pageSize, items }`; mỗi item có `requestId`, `vaultId`, `vaultName`, `owner: { userId, fullName }`, `executorId`, `executorName`, `requestType`, `status`, các mốc thời gian và `isAssignedToMe`.

Lọc thêm `ownerId`, `status` hoặc `assignedOnly=true`. Page bắt đầu từ 1, pageSize 1–100. API này cho Executor xem trạng thái tất cả đơn, không trả tài liệu, danh sách thụ hưởng hoặc thông tin liên lạc riêng tư. Sau merge main 21/10/2026, `GET /api/executor/handovers` cũng trả `{ page, pageSize, items }` và chỉ trả đơn được phân công. `GET /api/executor/assignments` dùng cùng contract phân trang.

## Luồng người thi hành

1. `GET /api/executor/assignments`; nếu cần, `POST /api/executor/assignments/{vaultId}/handovers` tạo Draft theo phân công đã lưu.
2. `GET /api/executor/handovers/{requestId}` xem chi tiết, có thêm `owner`, `executorId`, `submittedAt`, `completedAt`. Chỉ Executor được phân công mới có quyền đọc hoặc thao tác.
3. `POST /api/executor/handovers/{requestId}/death-certificate`: multipart `File`, `Signature` tùy chọn. Chỉ Executor được phân công; Owner và LegalVerifier không được nộp giấy chứng tử.
4. `GET /api/executor/handovers/{requestId}/legal-verifiers` lấy danh sách người thẩm định Active, không khóa, không xung đột lợi ích. Gửi multipart `VerifierId`, `Evidence` (file bắt buộc), `Comment` tùy chọn vào `POST /api/executor/handovers/{requestId}/legal-verifications`.
5. Khi yêu cầu bổ sung, dùng `POST /api/executor/handovers/{requestId}/documents` với `File`, `Signature` tùy chọn; sau đó nộp lại xác minh. Không sửa hồ sơ đang chờ pháp lý hoặc đã khóa.
6. Chỉ sau khi Approved mới `POST /api/executor/handovers/{requestId}/start`, rồi `/complete` với multipart `File` và `Signature` bắt buộc. File hoàn tất phải có chữ ký hợp lệ của Executor.

File tối đa **10 MiB**, kiểm tra MIME/nội dung và mã hóa backend AES-256-GCM. Chữ ký là detached signature theo cấu hình chứng thư hiện có. Không coi việc tải lên là xác minh tính xác thực pháp lý của giấy chứng tử.

## Xác minh pháp lý

Base path `/api/legal/handovers`. GET danh sách hỗ trợ `page`, `pageSize`, `status`; chỉ trả hồ sơ được giao. GET `/{requestId}` trả request và verification. POST `/{requestId}/approve`, `/reject`, `/request-additional-information` nhận JSON `{ "comment": "Kết quả thẩm định" }`.

Luồng chính: `Draft → Pending_Legal → Approved → In_Progress → Completed`. Nhánh bổ sung: `Pending_Legal → Additional_Info → Pending_Legal`. Từ chối: `Pending_Legal → Rejected`. Không truyền status tùy ý. Duyệt cần giấy chứng tử, phân công còn hiệu lực, tài sản/phân bổ/thụ hưởng hợp lệ và tài liệu chữ ký Owner hợp lệ. Quyết định cạnh tranh trả 409; không ghi một phần audit hoặc trạng thái case.

## Tài liệu, lịch sử và tình trạng Owner

Cả hai base path `/api/executor/handovers` và `/api/legal/handovers` có GET:

| Suffix | Kết quả |
| --- | --- |
| `/{requestId}/documents` | Metadata bằng chứng; có `purpose`, `signatureValid`, không có storage path |
| `/{requestId}/documents/{documentId}/content` | Nội dung file giải mã sau kiểm tra quyền và integrity |
| `/{requestId}/history` | Các lần xác minh: người thẩm định, quyết định, comment, thời gian |
| `/{requestId}/owner-status` | Tình trạng khai báo/xác minh Owner và nguồn bằng chứng |

LegalVerifier tải tài liệu tài sản qua `/{requestId}/asset-documents/{documentId}/content`. Chỉ đọc/thẩm định hồ sơ được giao, không upload bằng chứng.

Owner status: `NotConfirmed` (chưa đủ bằng chứng, không có nghĩa còn sống), `ReportedDeceased` (đã nộp giấy), `Rejected` (khai báo trong hồ sơ bị từ chối), `ConfirmedDeceased` (đã được pháp lý duyệt). Response có `ownerId`, `fullName`, `requestId`, `evidenceRequestId`, `deathCertificateDocumentId`, `verifierId`, `verifiedAt`.

Không có cột alive/deceased trong schema hiện tại: trạng thái này được suy ra từ hồ sơ đã lưu và bằng chứng mã hóa, **không ghi đè `USERS.Status`** vốn là trạng thái tài khoản. Xác nhận từ kho khác của cùng Owner có thể xuất hiện với nguồn hồ sơ tương ứng; hồ sơ mới vẫn phải qua xác minh riêng. Không có endpoint tùy ý sửa trạng thái Owner.

## Test bằng Swagger

Authorize với token Executor; gọi overview rồi thử detail một đơn `isAssignedToMe=false` (404). Với đơn được giao, chạy luồng trên và kiểm tra owner-status trước/sau upload/sau duyệt. Đổi token LegalVerifier được giao để xem, tải, yêu cầu bổ sung rồi duyệt. Kiểm tra từ chối, tài khoản sai role (403), truy cập ngoài phân công (404), trạng thái không hợp lệ hoặc file bị sửa (409), input sai (400), file lớn (413), storage không sẵn sàng (503). Token thiếu/hết hạn trả 401.

Collection mẫu: [LegacyVault.http](../LegacyVault.http). Test tự động: `dotnet run --project LegacyVault.Tests`; dùng SQLite cô lập và test doubles cho dịch vụ ngoài. Đã chạy 398 kiểm tra, gồm HTTP Swagger JSON, JWT/Google/2FA, quyền, luồng ký hoàn tất thật trong test, tài liệu bị sửa và hai quyết định pháp lý cạnh tranh bằng hai DbContext. Đây không phải kiểm chứng concurrency SQL Server thật, SMTP/Google thật hay chứng thư production.
