> Cập nhật Google login: email cũ không cần OTP đăng ký; email mới vẫn cần OTP. Xem [luồng Google hiện tại](Google-login-direct.md). Các mô tả Google luôn yêu cầu OTP bên dưới thuộc phiên bản trước.

> Cập nhật contract 15/10/2026: giấy tờ Owner và bằng chứng Executor đã tách riêng.

[Upload Owner / giấy chứng tử Executor / loại bỏ Staff](Executor-evidence-update.md)

# Test Google login: tài khoản cũ authorize, email mới OTP

Không thay schema. Backend xác minh Google OIDC; email có User hợp lệ không cần OTP đăng ký, email mới cần OTP trước khi tạo Owner. Không tạo mật khẩu giả hoặc lưu Google sub; email thay đổi không tự nhận ra User cũ.

## Cấu hình

Google OAuth Client loại Web application, consent/test users phù hợp. Authorized redirect URI:

```text
https://vault-owner-be.huydevops.id.vn/api/auth/google/oidc-callback
```

For local development, add `https://localhost:7015/api/auth/google/oidc-callback` as an additional authorized redirect URI.

```powershell
dotnet user-secrets set "Google:ClientId" "YOUR_CLIENT_ID" --project LegacyVault.API
dotnet user-secrets set "Google:ClientSecret" "YOUR_CLIENT_SECRET" --project LegacyVault.API
dotnet user-secrets set "Mail:Host" "YOUR_SMTP_HOST" --project LegacyVault.API
dotnet user-secrets set "Mail:Port" "587" --project LegacyVault.API
dotnet user-secrets set "Mail:Username" "YOUR_SMTP_USERNAME" --project LegacyVault.API
dotnet user-secrets set "Mail:Password" "YOUR_SMTP_PASSWORD" --project LegacyVault.API
dotnet user-secrets set "Mail:From" "YOUR_SENDER_EMAIL" --project LegacyVault.API
```

Cần JWT signing key và connection string như [contract frontend](Frontend-JWT-integration.md). Credentials mẫu trên không dùng thật; không commit secrets.

## Luồng browser + Swagger

1. Chạy backend HTTPS. Mở `/api/auth/google/start` trên thanh địa chỉ, không Execute Swagger.
2. Chọn Google. Middleware xác minh state/signature/issuer/audience/expiration/nonce/email_verified; chưa cấp token hoặc application cookie.
3. GET `/api/auth/google/challenge` trong cùng browser: nhận challengeId/expiresAt/requiresOtp; không đưa OTP/token vào URL.
4. Nếu requiresOtp=false, POST `/api/auth/google/complete` với `{"challengeId":"COPY_FROM_PAGE"}`, không nhập OTP. Nếu requiresOtp=true (email mới), POST `/api/auth/google/otp/verify`:

```json
{"challengeId":"COPY_FROM_PAGE","code":"123456"}
```

5. 200 → TokenDto accessToken/tokenType/expiresIn/userId/roles; **Authorize bằng token** rồi GET `/api/auth/me`. Nếu 202, tài khoản có 2FA enabled; hoàn tất `/api/auth/2fa/verify` trước.
6. Gửi lại: POST `/api/auth/google/otp/resend`, body challengeId, chờ 60 giây, dùng challengeId và mã mới. OTP 5 phút, 5 lần thử, chỉ dùng một lần.

Cookie tạm Secure/HttpOnly/SameSite=None chỉ phục vụ Google challenge. Frontend cross-origin dùng HTTPS, origin trong Cors:Origins, credentials include và X-Vault-Request: 1 cho Google POST. Các API Bearer không cần cookie hoặc header này. Không có frontend redirect tùy ý; trang callback hiện tại phục vụ test khi chưa có frontend.

Frontend có thể GET `/api/auth/google/challenge` (credentials include) sau callback để nhận challengeId/expiry dạng JSON, thay vì đọc HTML. Không gọi/poll quá 10 lần/IP/phút.

`Google:FrontendCallbackUrl` mặc định trống để dùng trang Swagger. Khi FE sẵn sàng, đặt URL HTTPS cố định trên `Cors:Origins`, ví dụ `https://frontend.example.com/auth/google`; không query/fragment và không nhận returnUrl tùy ý. Callback redirect không mang token/OTP/challengeId; FE đọc challenge qua GET ở trên. Không thay Google Authorized redirect URI `/api/auth/google/oidc-callback` bằng URL frontend này.

Nếu nhận 202, Email 2FA có thể gửi lại qua POST `/api/auth/2fa/resend` với challengeId, chờ 60 giây và dùng challenge mới. TOTP không resend. Tham khảo [contract login](Backend-login.md).

## Quy tắc và giới hạn

Email mới tạo User + Owner atomically sau OTP. Email cũ giữ tên/mật khẩu/roles; User không Active/đang khóa hoặc có Admin/Executor/LegalVerifier bị chặn khỏi Google công khai. Google không bỏ qua 2FA tài khoản; OTP beneficiary riêng vẫn bắt buộc khi xem di sản.

Không refresh token, JWT hết hạn phải login lại. Logout không revoke JWT. OTP/challenge trong bộ nhớ một instance, restart mất challenge; Google-only User chưa có API đặt mật khẩu.

Tests dùng Google discovery/token/JWKS giả lập và TestServer, không certificate import UserKeySet hoặc TLS bypass production. Google/SMTP thật cần credentials hợp lệ, chưa được kiểm chứng trong lần triển khai này.

## Production callback URLs

Google Cloud authorized redirect URI: `https://vault-owner-be.huydevops.id.vn/api/auth/google/oidc-callback`.

Frontend sign-in starts at `https://vault-owner-be.huydevops.id.vn/api/auth/google/start` and returns to `https://digitalvault-zeta.vercel.app/auth/google/callback`. Configure `Google:ClientId` and `Google:ClientSecret` as backend environment variables; do not commit the client secret.
If the backend host terminates TLS at a reverse proxy, configure `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` in the hosting service so OIDC generates the HTTPS callback URI. Only enable this when requests reach the app through the trusted hosting proxy.
