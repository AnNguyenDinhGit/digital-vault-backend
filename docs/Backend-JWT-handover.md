# Báo cáo bàn giao backend JWT — 14/10/2026

## Kết quả

Đã sửa code trực tiếp trong repository. Giữ API → BLL → DAL, các entity/context scaffold và schema. API nghiệp vụ dùng JWT Bearer, không application cookie. Không commit/push và không chỉnh sửa frontend.

## API hoàn thành

- Auth: register, password login với 2FA Email/TOTP, verify factor, Google OIDC + OTP + JSON challenge, JWT, me và logout theo giới hạn JWT.
- Owner: create/list/detail/update/close kho; create/list/detail/update/revoke tài sản; create/list/update/revoke phân bổ; upload/list/download/revoke giấy tờ; chọn Executor; create/list/detail/cancel bàn giao.
- Executor/Staff: hồ sơ được phân công, file content/upload, nộp legal evidence, start và signed completion. Staff khám phá metadata kho đủ tiêu chí và intake assignment/request/cases atomically, không thay Executor đã được phân công.
- LegalVerifier: list/detail scoped, handover/asset document content chỉ trong cases của hồ sơ; approve/reject/request-additional-information, comment/reviewer/review time và kiểm tra trạng thái/xung đột.
- Beneficiary: OTP riêng; tài sản/hồ sơ/tài liệu của mình; confirm/read receipt chỉ sau Completed, không lặp. Notifications list/read của User hiện tại và audit các mutation quan trọng.

Toàn bộ method/path/request/response/status/filter/pagination/multipart: [Frontend-JWT-integration.md](Frontend-JWT-integration.md). [LegacyVault.http](../LegacyVault.http) chứa các luồng chính và variables không secrets.

## File code chính

| Tầng | File thêm/sửa |
| --- | --- |
| API auth/security | Authentication/JwtAuthentication.cs, FrontendSecurity.cs, GoogleAuthentication.cs; Program.cs; Swagger/MutationHeaderFilter.cs; appsettings.json; LegacyVault.API.csproj |
| Controllers | VaultControllers.cs, GoogleAuthController.cs, ManagementControllers.cs |
| BLL auth | DTOs/AuthDtos.cs; Security/LoginFactorStore.cs; Services/IdentityService.cs, PasswordAuthenticationService.cs |
| BLL workflow | DTOs/AssetCreationDtos.cs, ManagementDtos.cs; Services/VaultService.cs, ManagementService.cs, RecordVersion.cs, BeneficiaryService.cs, IBeneficiaryService.cs |
| DAL | Repositories/VaultRepository.cs, WorkflowRepository.cs; Context/WorkflowConcurrency.cs (partial) |
| Tests | JwtWorkflowTests.cs, TestAuthentication.cs, MainFlowTests.cs, GoogleLoginTests.cs, BeneficiaryAssignmentTests.cs, DatabaseAudit.cs, Program.cs, LegacyVault.Tests.csproj |
| Tài liệu/script | README.md; docs/API.md, Google-login-Swagger.md, Huong-dan-test-API-Swagger.md, Luong-chinh-dang-ky-tao-assets.md, Entities-analysis.md, Trang-thai-chuc-nang.md; các tài liệu JWT mới; LegacyVault.http; database/ensure-staff-role.sql |

BeneficiaryService placeholder đã được thay bằng DTO/repository-backed wrapper có OTP gate và nối DI/controller. Google tests chuyển sang in-process TestServer với HTTPS scheme, không import private key bằng UserKeySet/EphemeralKeySet và không tắt xác minh production.

## Build và test thực tế

```powershell
dotnet build LegacyVault.sln --no-restore -p:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/jwt-console/
dotnet run --project LegacyVault.Tests --no-restore --property:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/jwt-console/
```

Build thành công **0 lỗi, 0 cảnh báo**. Console harness kết thúc exit 0, **256 kiểm tra đạt**, không tính skip/chưa chạy. OutDir riêng tránh ghi đè DLL của phiên API đang chạy. Lệnh thông thường vẫn là `dotnet run --project LegacyVault.Tests`, không dotnet test.

Bao gồm JWT sai chữ ký/issuer/audience/algorithm/expiry, me, account inactive/locked, role revocation; Email/TOTP 2FA, Google validation/OTP/replay/browser binding; ownership và CRUD/revoke; allocation vượt tổng/concurrent update; upload MIME/>10 MiB và download scope; Staff intake repeat/race/SQL rollback; AutoAssigned consistency; legal review states và receipt gate/cross-user/repeat; CORS và Swagger.

Google/SMTP dùng doubles trong test, persistence dùng SQLite relational cô lập. Không dùng fake persistence/external services trong production.

## Audit database thật và bảo toàn workspace

Chỉ SELECT, không ghi dữ liệu. Kết nối audit được thực hiện ngoài sandbox sau lỗi SQL 5 trong sandbox, với quyền được cấp. Kết quả: 5 role hiện hữu, 25 bảng không thiếu cột so với scaffold; DIGITAL_VAULTS.Description nvarchar(500) nullable, HANDOVER_REQUESTS.ExecutorId int NOT NULL. Audit chưa kiểm tra toàn bộ kiểu dữ liệu/index/constraints hoặc concurrency SQL Server. Không migration/reset/EnsureCreated/seed trên SQL Server thật.

Role Staff chưa có trong database thật. Script ensure-staff-role.sql được chuẩn bị để quản trị chạy rõ ràng, kiểm tra RoleId identity và chạy lại an toàn; không chạy script và không cấp role cho user nào trong công việc này.

Audit bổ sung xác nhận UpdatedAt của USERS/AUTHENTICATIONS/DIGITAL_VAULTS/DIGITAL_ASSETS là datetime2 precision 7. RecordVersion đảm bảo version tăng kể cả hai mutation trong cùng clock tick; không đổi schema. Nếu deploy database khác, phải kiểm tra precision này trước khi dựa vào token timestamp.

Frontend Git status trước/sau đều sạch; không tạo/sửa/xóa/format file frontend. File người dùng docs/Danh-sach-API-da-lam.md giữ nguyên SHA256 `CC963F0E6283F3E5741FD21D1C9AF6989C138BC5B983215B4EEA18BF41D6F131`. Entity files và LegacyVaultDbContext.cs scaffold không đổi. `git diff --check` cuối cùng exit 0.

## Giới hạn và việc đội frontend cần làm

- Cấu hình JWT signing key ngẫu nhiên, issuer/audience; CORS origin chính xác, database, Google/SMTP/encryption/certificates qua environment/User Secrets. Không có key mặc định để che thiếu cấu hình. Credentials đã được bỏ khỏi appsettings.json.
- Frontend nhận TokenDto, gắn Bearer; 202 bắt buộc OTP/TOTP, dùng me thay kiểm tra cookie/owner-vaults. Google cookie tạm cần credentials include + X-Vault-Request; có endpoint JSON challenge. Xử lý 401 bằng bỏ token/login lại; 403 không tự coi là hết hạn.
- Không có refresh-token/invitation persistence phù hợp, không tạo tài khoản giả. JWT hết hạn login lại. Logout chỉ thu hồi beneficiary OTP grant; không revoke access token đã phát.
- OTP/challenge/TOTP replay tracking ở một instance, restart làm mất challenge/grant; Google nhận diện bằng email, không lưu sub. Chưa có API cấp tài khoản nhân viên hoặc đặt mật khẩu Google-only.
- ExecutorId bắt buộc nên không có request unassigned; Staff intake là luồng khả thi có tiêu chí và assignment cùng transaction. Không tự chuyển hồ sơ đang xử lý sang Staff hoặc thay Executor hợp lệ.
- Receipt note/status không chứng minh chữ ký số. Asset files mã hóa backend AES-256-GCM, không E2EE. HandoverDocument không có Status nên chưa có revoke endpoint cho tài liệu bàn giao; asset documents có revoke.
- Google/SMTP thật, cert production và transaction concurrency SQL Server chưa được kiểm thử; không gọi chúng là đã pass.
