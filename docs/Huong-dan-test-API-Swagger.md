> Cập nhật Google login: email cũ không cần OTP đăng ký; email mới vẫn cần OTP. Xem [luồng Google hiện tại](Google-login-direct.md). Các mô tả Google luôn yêu cầu OTP bên dưới thuộc phiên bản trước.

> Cập nhật contract 15/10/2026: giấy tờ Owner và bằng chứng Executor đã tách riêng.

[Upload Owner / giấy chứng tử Executor / loại bỏ Staff](Executor-evidence-update.md)

# Hướng dẫn test LegacyVault bằng Swagger — JWT

## 1. Chuẩn bị

Cấu hình JWT key/issuer/audience, SQL Server, SMTP và encryption/certificates cho luồng cần dùng. Không dùng database production để tạo dữ liệu test. Hướng dẫn cấu hình chi tiết: [Frontend-JWT-integration.md](Frontend-JWT-integration.md).

```powershell
dotnet run --project LegacyVault.API --launch-profile https
```

Mở `https://localhost:7015/swagger`. API nghiệp vụ dùng Bearer, không tự nhận session cookie.

## 2. Đăng ký và login

Gọi POST `/api/auth/register` với fullName/email/password/confirmPassword/phone; mật khẩu ít nhất 12 ký tự; không gửi role. Sau 201, gọi POST `/api/auth/login` với email/password.

- 200: lấy accessToken, bấm **Authorize** và dán JWT.
- 202: lấy challengeId/method; nhận OTP email hoặc mã TOTP, gọi POST `/api/auth/2fa/verify`; lấy token rồi Authorize.
- Email OTP chưa đến: chờ 60 giây, POST `/api/auth/2fa/resend` với `{"challengeId":"..."}`. Dùng challengeId mới; challenge cũ mất hiệu lực. TOTP không gửi lại qua email.
- Method 2FA không hỗ trợ trả 503; không được bỏ qua.

Gọi GET `/api/auth/me` để kiểm tra UserId/roles. Khi đổi tài khoản, logout rồi xóa Bearer ở Authorize và login tài khoản khác. JWT logout không revoke token cũ; client phải bỏ token.

## 3. Owner

1. POST `/api/owner/vaults`: name, description tối đa 500; lấy vaultId.
2. POST `/api/owner/assets`: vaultId/name/type/description; lấy assetId.
3. Tạo tài khoản người thụ hưởng nếu chưa có. POST `/api/owner/assets/{assetId}/beneficiaries`: email và allocation, tổng Active ≤100.
4. GET `/api/owner/assets/{assetId}/allocations`: lấy assignmentId để PUT tỷ lệ hoặc POST revoke.
5. Upload documents bằng multipart File/Signature (BeneficiaryId tùy chọn, phải là phân bổ Active hiện có). Mỗi file ≤10 MiB; signature là chữ ký rời RSA-SHA256, không PDF ký nhúng.
6. GET documents và content để kiểm tra download; PUT vault/asset và POST close/revoke khi chưa có hồ sơ bảo vệ.
7. PUT `/api/owner/vaults/{vaultId}/executor` nếu chọn Executor. POST `/api/owner/vaults/{vaultId}/handovers` yêu cầu mỗi asset Active đủ 100% phân bổ và có documents.

## 4. Executor → Legal → Beneficiary

Không còn Staff. Executor xem GET /api/executor/assignments; tạo hồ sơ cho kho đã được phân công qua POST /api/executor/assignments/{vaultId}/handovers hoặc tiếp tục hồ sơ Owner đã tạo.

Login Executor, dùng Bearer của người được phân công. Xem detail và documents, POST `/api/executor/handovers/{requestId}/death-certificate` với File trước; sau đó nộp legal-verifications với VerifierId/Comment/Evidence. LegalVerifier được giao login riêng, chỉ xem/tải và approve/reject/request-additional-information. Additional information phải nộp lại trước review; approve mới mở di sản.

Executor start hoặc complete sau approve; complete cần File và Signature hợp lệ. Không được bỏ qua pháp lý.

Login Beneficiary, POST otp/request rồi otp/verify bằng JWT hiện tại. GET assets/handovers/documents sau legal approval. POST receipt chỉ khi case/request Completed và chưa confirm; receipt là xác nhận trạng thái, không chứng minh chữ ký số.

## 5. Google và lỗi cần thử

[Google-login-Swagger.md](Google-login-Swagger.md): start mở trực tiếp trong browser; chỉ bước Google giữ cookie tạm. GET challenge: requiresOtp=false thì POST complete chỉ với challengeId; requiresOtp=true thì nhập OTP email mới và POST otp/verify. Nhận JWT hoặc challenge 2FA đã bật; Authorize JWT cho nghiệp vụ.

Thử 401 token sai/hết hạn, 403 role/OTP sai, 404 dữ liệu người khác, 409 duplicate/trạng thái/concurrency, 413 file quá lớn, 429 cooldown, 503 cấu hình không hỗ trợ. X-Vault-Request chỉ bắt buộc cho Google POST; Swagger tự thêm.

List workflow mới trả `{page,pageSize,items}`; pageSize 1–100. Endpoint cũ giữ mảng. Toàn bộ path và DTO: [contract JWT](Frontend-JWT-integration.md).

## 6. Test tự động

```powershell
dotnet run --project LegacyVault.Tests
```

SQLite relational cô lập và external doubles; không seed test trên SQL Server thật. Google test chạy TestServer với scheme HTTPS, token RSA, không import private key vào Windows và không tắt TLS production. Đây không thay thế Google/SMTP/certificates thật.
# Bổ sung test Executor và LegalVerifier — 19/10/2026

Làm theo [luồng Swagger Executor/LegalVerifier](Executor-Legal-workflow.md). Có hướng dẫn xem tất cả đơn kèm Owner, giới hạn phân công, nộp giấy chứng tử, bổ sung/duyệt/từ chối, bắt đầu/hoàn tất và kiểm tra trạng thái Owner. Request mẫu mới ở cuối [LegacyVault.http](../LegacyVault.http).
# Test nút “Tôi vẫn còn sống”

Xem [hướng dẫn Owner life confirmation](Owner-life-confirmation.md): GET/POST `/api/owner/life-confirmation`, xác minh thời gian UTC được lưu, giới hạn quyền và lỗi 409 khi xung đột hồ sơ pháp lý. POST không cần body.
# Cập nhật phân trang 21/10/2026

Khi test list kho/tài sản/người thụ hưởng/phân bổ/tài liệu Owner, phân công/đơn Executor và tài sản thừa kế: gửi `page`, `pageSize` và đọc `items` trong response. Xem [contract sau merge](Merge-pagination-2026-10-21.md). Các kiểm tra quyền và OTP giữ nguyên.
# Test dropdown Executor của Owner

Authorize bằng JWT Owner; gọi GET `/api/owner/executors?page=1&pageSize=20`. Chọn `items[].userId` rồi tạo kho và PUT `/api/owner/vaults/{vaultId}/executor` với `{ "executorId": 15 }`. Thử search, chuyển trang, tài khoản khóa và vaultId của Owner khác theo [hướng dẫn đầy đủ](Owner-executor-dropdown.md).
# Test Executor nhận/từ chối và xem chi tiết

Thực hiện [luồng tiếp nhận bàn giao](Executor-intake-and-profile.md): Owner mở dropdown, xem detail, gán và tạo đơn Pending_Executor; Executor đúng phân công accept (không body) hoặc decline (reason bắt buộc). Thử sai role/phân công, gọi lặp, upload trước accept, rồi đi tiếp luồng pháp lý sau accept. Decline phải giữ lịch sử, báo Owner và cho Owner chọn lại người khác.
