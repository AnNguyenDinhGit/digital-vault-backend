# LegacyVault API

Contract hiện tại: **JWT Bearer**, API → BLL → DAL. Danh sách method/path/role, DTO, multipart, status codes, pagination và giới hạn được cập nhật đầy đủ tại [Frontend-JWT-integration.md](Frontend-JWT-integration.md).

## Authentication

- POST `/api/auth/register`: tạo Owner bằng email/mật khẩu, không tự login.
- POST `/api/auth/login`: 200 TokenDto hoặc 202 LoginChallengeDto khi 2FA enabled.
- POST `/api/auth/2fa/verify`: hoàn thành Email/TOTP, trả TokenDto.
- GET `/api/auth/me`: Bearer, profile công khai và roles hiện tại từ DB.
- POST `/api/auth/logout`: Bearer, thu hồi beneficiary OTP grant; client bỏ token, JWT không được revoke.
- GET `/api/auth/google/start`: browser navigation; middleware callback `/api/auth/google/oidc-callback` → trang `/api/auth/google/callback`.
- POST `/api/auth/google/otp/verify`: 200 TokenDto hoặc 202 nếu tài khoản còn 2FA; không cấp application cookie.
- POST `/api/auth/google/otp/resend`: cookie tạm Google + X-Vault-Request: 1, challengeId mới.
- GET `/api/auth/google/challenge`: lấy challengeId/expiry dạng JSON cho frontend, cookie-bound; không token trong URL.

## Nghiệp vụ

Owner có kho/tài sản/phân bổ/giấy tờ create/list/detail/update/close hoặc revoke, chỉ định Executor, tạo/cancel bàn giao. Executor và Staff chỉ xử lý hồ sơ được giao; Staff intake không thay Executor. LegalVerifier có list/detail/download/approve/reject/additional information. Beneficiary có OTP, di sản, hồ sơ, documents và receipt. Notifications chỉ của User hiện tại.

Các endpoint cũ được tái sử dụng; list cũ giữ mảng, list workflow mới dùng PageDto. API nghiệp vụ không cần X-Vault-Request; chỉ Google cookie-bound POST cần header. Swagger hỗ trợ Authorize Bearer.

Mỗi file upload ≤10 MiB, backend xác minh nội dung/MIME, mã hóa AES-256-GCM; không E2EE. Giấy tờ và hồ sơ được thu hồi bằng Status, không xóa lịch sử. Các mutation bị bảo vệ khi đang/đã bàn giao; concurrency vault/asset/request/legal/case ngăn ghi đè.

Không có invitation/refresh token persistence đúng mục đích, không tạo User giả. Chưa có API cấp tài khoản nhân viên. Xem [collection HTTP](../LegacyVault.http), [Swagger](Huong-dan-test-API-Swagger.md) và [Google](Google-login-Swagger.md).
