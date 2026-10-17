# Upload tài sản và giấy chứng tử — 15/10/2026

## API hiện tại

| Method/path | Người gọi | Multipart / kết quả |
| --- | --- | --- |
| POST `/api/owner/assets/{assetId}/documents` | Owner sở hữu tài sản | File + Signature bắt buộc; BeneficiaryId tùy chọn, phải thuộc phân bổ Active hiện có; 200 DocumentDto |
| GET `/api/owner/assets/{assetId}/documents/{documentId}/content` | Owner sở hữu tài sản | Bytes giải mã, kiểm tra chữ ký |
| PUT `/api/owner/vaults/{vaultId}/executor` | Owner sở hữu kho | JSON `{executorId}`; tùy chọn trước khi tạo hồ sơ |
| POST `/api/owner/vaults/{vaultId}/handovers` | Owner sở hữu kho | Tạo hồ sơ Draft và phân công; 201 HandoverDto |
| GET `/api/executor/assignments` | Executor Active, không bị khóa | Danh sách phân công Active/AutoAssigned: vaultId, executorId, status, assignedAt |
| POST `/api/executor/assignments/{vaultId}/handovers` | Executor đã được phân công kho | Khởi tạo Draft khi chưa có hồ sơ bảo vệ; không tự nhận kho |
| GET `/api/executor/handovers` | Executor | Hồ sơ được phân công; không random lại |
| POST `/api/executor/handovers/{requestId}/death-certificate` | Executor được phân công hợp lệ | File bắt buộc, Signature tùy chọn; 200 DocumentDto |
| POST `/api/executor/handovers/{requestId}/documents` | Executor được phân công hợp lệ | File, Signature tùy chọn; bằng chứng bổ sung |
| POST `/api/executor/handovers/{requestId}/legal-verifications` | Executor được phân công hợp lệ | VerifierId, Comment, Evidence; cần giấy chứng tử trước khi nộp |

Các endpoint dùng Bearer. Actor lấy từ JWT và kiểm tra lại DB; không nhận UploadedBy/ExecutorId để xác lập người nộp. File tối đa 10 MiB, MIME PDF/PNG/JPEG/text UTF-8 phải khớp nội dung. Mã hóa backend AES-256-GCM. Chữ ký rời RSA-SHA256 theo chứng thư tin cậy hiện có; không hỗ trợ PDF/CMS nhúng.

Owner không gửi OwnerAlive hoặc DeathCertificate; endpoint từ chối field ngoài File/Signature/BeneficiaryId. Upload không tạo phân bổ ngầm và không yêu cầu có beneficiary. Kho/tài sản/upload thông thường không yêu cầu giấy chứng tử. Giấy tờ Owner lưu ASSET_DOCUMENTS với chữ ký hợp lệ và Status Active. Download kiểm tra ownership.

Giấy chứng tử lưu HANDOVER_DOCUMENTS, RequestId và UploadedBy của Executor. Do scaffold không có DocumentType, purpose `DeathCertificate` được lưu trong envelope JSON đã mã hóa/xác thực GCM của chính file; không sửa schema. Envelope cũ không có purpose vẫn giải mã được nhưng không tự được coi là giấy chứng tử hợp lệ cho bước nộp pháp lý mới.

Việc xác định nội dung thật của một PDF là giấy chứng tử cần thẩm định: backend không có OCR/kiểm tra cơ sở dữ liệu hộ tịch. Endpoint Owner không có nghiệp vụ nộp giấy chứng tử; đổi tên/che giấu nội dung trong một file tài sản không thể được phát hiện chắc chắn bằng MIME hoặc chữ ký. File đó không thể trở thành bằng chứng tử vong của hồ sơ qua API Owner.

## Phân công và state machine

Owner tạo hồ sơ khi tài sản Active đã có giấy tờ Active và tổng phân bổ Active đúng 100%. Không cần giấy chứng tử để khởi tạo Draft, tránh vòng lặp phân công/upload.

Nếu đã có đúng một assignment Active/AutoAssigned tới Executor hợp lệ thì giữ nguyên. Nếu chưa có, lấy tập USERS có role Executor, Active, không khóa, độc lập Owner và mọi beneficiary Active của kho. Chọn đều bằng `RandomNumberGenerator.GetInt32(count)`; không chọn từ toàn bộ USERS, không nâng role User/Staff cũ thành Executor. Không có ứng viên: 503 và không tạo assignment/request dở dang. Assignment không hợp lệ/nhiều assignment: lỗi 403/409, không âm thầm random thay thế.

Lưu EXECUTOR_ASSIGNMENTS, HANDOVER_REQUESTS, cases, audit và notification trong một SaveChanges; EF relational transaction và optimistic concurrency DigitalVault.UpdatedAt bảo vệ các thay đổi cùng nhau. Retry khi đã có hồ sơ bảo vệ trả 409; GET không tạo hoặc thay phân công. Assignment + request đã lưu cho phép Executor xem phân công và tiếp tục hồ sơ.

Chỉ Executor có role hiện tại và assignment Active/AutoAssigned khớp Request.ExecutorId được nộp bằng chứng. Tài khoản nhiều role vẫn phải đáp ứng quyền Executor và độc lập Owner/beneficiary. Owner/Beneficiary/LegalVerifier/Staff-only hoặc Executor khác bị từ chối. Người có role LegalVerifier đồng thời là assigned Executor chỉ được dùng quyền Executor; không được tự thẩm định hồ sơ xung đột.

Upload chỉ trong Draft/Rejected/Additional_Info. Pending_Legal/Approved/In_Progress/Completed/Cancelled khóa upload; không có endpoint sửa/xóa/thay bằng chứng cũ. Additional_Info/Rejected cho phép thêm bằng chứng theo state machine hiện có. Upload không tự duyệt hoặc hoàn tất. LegalVerifier chỉ xem/download trong phạm vi phân công và approve/reject/request-additional-information. Khi nộp và approve, backend kiểm tra có giấy chứng tử do Executor hiện tại nộp, giải mã integrity và giữ kiểm tra chữ ký Owner.

## Staff đã bị loại bỏ

Đã bỏ StaffController, `/api/staff/*`, StaffRole constant, intake service/repository/DTO và script seed role. Không có fallback Staff hoặc tạo/gán tài khoản Staff. Role Staff còn trong JWT/DB không cấp quyền Executor; request luôn kiểm tra role hiện tại và assignment thực tế.

Không truy cập/xóa/chuyển đổi dữ liệu thật trong lần sửa này. Chạy SELECT trong [review-legacy-staff.sql](../database/review-legacy-staff.sql) để xác định USER_ROLES, EXECUTOR_ASSIGNMENTS và HANDOVER_REQUESTS cũ cần quản trị xem xét. Giữ người dùng và lịch sử; không tự đổi Staff thành Executor. Hồ sơ cũ đang khóa phải được xử lý theo quy trình quản trị/pháp lý riêng; hồ sơ có tài liệu Owner Pending_Legal cũ không tự được diễn giải thành bằng chứng Executor.

Có [script chuyển phân công riêng](../database/reassign-legacy-staff.sql), mặc định `@Apply=0` không ghi dữ liệu. Quản trị phải kiểm tra ID, cấu hình `@Apply=1` và tự chạy sau review; chỉ chuyển assignment Staff-only tới tài khoản Executor đã tồn tại, Active/unlocked/độc lập và hồ sơ còn editable. Script khóa transaction, giữ user/cases/file/lịch sử, không nâng role Staff hoặc sửa hồ sơ đang khóa/Completed. Script chưa chạy hoặc kiểm chứng trên SQL Server thật.

## Swagger và tương thích FE

Đã kiểm tra bản đầu: chỉ OwnerController có POST upload asset; không có POST trùng ở OwnerManagementController. Giữ một action, bỏ UploadDocumentAsync/UploadDocumentInput không có caller. Test quét method + normalized route và gọi swagger.json qua HTTP, không chỉ build.

FE cần bỏ OwnerAlive/DeathCertificate khỏi form Owner; gửi File + Signature, BeneficiaryId chỉ để chọn allocation hiện có. Màn hình giấy chứng tử chuyển sang Executor sau khi hồ sơ Draft được tạo. Bỏ toàn bộ màn hình/api Staff. Các path tài sản và JWT/Google/OTP/2FA không đổi. Ví dụ request: [LegacyVault.http](../LegacyVault.http).

## Kiểm chứng và giới hạn

Kết quả bản cuối: build **0 lỗi/0 cảnh báo**, console harness **330 kiểm tra đạt**, `git diff --check` sạch. Lệnh thực tế dùng output riêng để tránh DLL bị API khác khóa:

```powershell
dotnet build LegacyVault.sln --no-restore -p:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/executor-evidence/
dotnet run --project LegacyVault.Tests --no-build --no-restore --property:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/executor-evidence/
```

Build .NET và console tests dùng SQLite relational cô lập, chứng thư test RSA và test doubles SMTP/Google. Test có SQL failure trigger và interleaving hai DbContext để kiểm tra rollback/optimistic concurrency; đây không phải kiểm chứng concurrency trên SQL Server thật hoặc stress test nhiều máy.

Ứng dụng API thực tế chạy môi trường Testing trên Kestrel `http://127.0.0.1:5189`; GET `/swagger/v1/swagger.json` trả 200, có Owner upload và Executor death-certificate, không có Staff path. Cấu hình connection string kiểm tra trỏ cổng local không dùng, không kết nối DB thật. Windows sandbox thiếu quyền DPAPI/Event Log ở lần khởi chạy đầu; chạy lại ngoài sandbox, Event Log bị tắt riêng qua biến môi trường kiểm tra, production signature validation giữ nguyên.

Google/SMTP thật, SQL Server thật, chứng thư production và dữ liệu Staff hiện tại chưa kiểm chứng. Không sửa frontend, không chạy migration hoặc seed DB thật, chưa commit/push.

## File thay đổi

- API: `Controllers/VaultControllers.cs`, `Controllers/ManagementControllers.cs`.
- BLL: `Services/VaultService.cs`, `Services/ManagementService.cs`, `Services/IdentityService.cs`, `DTOs/ManagementDtos.cs`, `Security/DocumentProtection.cs`, thêm `Security/HandoverEvidence.cs`; bỏ `DTOs/UploadDocumentInput.cs`.
- DAL: `Repositories/WorkflowRepository.cs`; không sửa entities/context scaffold.
- Tests: `LegacyVault.Tests/Program.cs`, `LegacyVault.Tests/JwtWorkflowTests.cs`.
- SQL: bỏ `database/ensure-staff-role.sql`; thêm `review-legacy-staff.sql`, `reassign-legacy-staff.sql`, chưa chạy.
- Contract/mẫu: `LegacyVault.http`, `README.md`, `docs/API.md`, `Frontend-JWT-integration.md`, `Huong-dan-test-API-Swagger.md`, `Luong-chinh-dang-ky-tao-assets.md`, `Trang-thai-chuc-nang.md`, `Backend-JWT-handover.md`, `Entities-analysis.md`, `Backend-login.md`, `Google-login-Swagger.md` và báo cáo này.
- Giữ nguyên file chưa commit của người dùng `docs/Danh-sach-API-da-lam.md`.
