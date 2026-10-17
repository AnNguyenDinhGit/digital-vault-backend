> Cập nhật Google login: email cũ không cần OTP đăng ký; email mới vẫn cần OTP. Xem [luồng Google hiện tại](Google-login-direct.md). Các mô tả Google luôn yêu cầu OTP bên dưới thuộc phiên bản trước.

> Cập nhật 17/10/2026: main đã bổ sung refresh token; xem [contract auth sau merge](Merge-main-2026-10-17.md). Các mô tả chưa có refresh token bên dưới thuộc phiên bản trước.

> Cập nhật contract 15/10/2026: giấy tờ Owner và bằng chứng Executor đã tách riêng.

[Upload Owner / giấy chứng tử Executor / loại bỏ Staff](Executor-evidence-update.md)

# Backend login: contract và kiểm chứng

Backend dùng JWT Bearer, API → BLL → DAL. Không sửa frontend hoặc thêm bảng/cột/chạy migration. `JwtTokenIssuer` là implementation cấp JWT duy nhất; bỏ service JWT cũ không có caller vì dùng cấu hình khác.

## Endpoint

| Method/path | Input | Kết quả |
| --- | --- | --- |
| POST `/api/auth/register` | fullName, email, password, confirmPassword, phone; identityNumber tùy chọn | 201 Owner, chưa đăng nhập |
| POST `/api/auth/login` | email, password | 200 TokenDto hoặc 202 challenge 2FA |
| POST `/api/auth/2fa/verify` | challengeId, code 6 số | 200 TokenDto |
| POST `/api/auth/2fa/resend` | challengeId | 200 challenge mới, chỉ Email; chưa cấp JWT |
| GET `/api/auth/google/start` | browser navigation | Redirect tới Google, chọn tài khoản |
| GET `/api/auth/google/oidc-callback` | Google callback | Xác minh OIDC, gửi Email OTP |
| GET `/api/auth/google/callback` | cookie Google tạm | Trang Swagger hoặc redirect frontend cố định |
| GET `/api/auth/google/challenge` | cookie Google tạm | challengeId, expiresAt |
| POST `/api/auth/google/complete` | challengeId, cookie Google tạm | Email đã có User: token hoặc 2FA, không OTP đăng ký |
| POST `/api/auth/google/otp/resend` | challengeId và cookie tạm | challenge mới |
| POST `/api/auth/google/otp/verify` | challengeId, code và cookie tạm | 200 TokenDto hoặc 202 nếu tài khoản còn 2FA |
| GET `/api/auth/me` | Bearer | userId, fullName, email, roles, status từ DB |
| POST `/api/auth/logout` | Bearer | 204, thu hồi beneficiary OTP grant; client bỏ JWT |

Google POST cần `X-Vault-Request: 1` và credentials include. API nghiệp vụ dùng `Authorization: Bearer <accessToken>`, không application cookie. Verify/resend không nhận userId, roles, email đích hoặc password.

## Response và luồng frontend

200 login/verify:

```json
{"userId":1,"roles":["Owner"],"accessToken":"JWT_VALUE","tokenType":"Bearer","expiresIn":900}
```

202 login/Google verify:

```json
{"challengeId":"32_HEX_CHARACTERS","method":"Email","expiresAt":"2026-10-14T12:05:00Z","requiresTwoFactor":true}
```

Frontend chỉ đặt logged-in khi có accessToken. Với 202, hiển thị Email OTP/TOTP rồi verify. Sau đó gọi me, gắn Bearer cho API; 401 thì xóa token và đăng nhập lại. Không có endpoint refresh-token.

| Status | Trường hợp |
| --- | --- |
| 400 | Input/code/challenge không hợp lệ, hết hạn/hết lượt, replay, resend TOTP |
| 401 | Sai mật khẩu, account inactive/locked, JWT thiếu/sai/hết hạn |
| 403 | Tài khoản đặc quyền dùng Google công khai hoặc thiếu quyền API |
| 409 | Email đăng ký trùng, account/factor đổi trong lúc chờ OTP, cập nhật đồng thời |
| 429 | Cooldown/rate limit |
| 503 | Thiếu hoặc lỗi SMTP, SMTP timeout, cấu hình 2FA chưa hỗ trợ |
| 500 | Lỗi persistence khác; không cấp token khi lưu thất bại |

Lỗi nghiệp vụ trả `{status,detail}`. Validation tự động của ASP.NET trả ValidationProblemDetails. Google provider failure/cancellation chuyển tới `/api/auth/google/error` (400).

## Quy tắc xác thực

- JWT HS256, kiểm tra chữ ký/issuer/audience/expiration/algorithm. Mặc định 15 phút, cấu hình 1–30 phút. `Jwt:SigningKey` tối thiểu 32 byte UTF-8 ngẫu nhiên, không có khóa mặc định; không dùng `Jwt:Key` cũ.
- Mật khẩu PBKDF2-SHA256. Sai 5 lần khóa 15 phút, lưu trong AUTHENTICATIONS. Account Active và mọi password record không bị khóa mới được đăng nhập.
- Đúng một factor enabled: Email hoặc TOTP Base32/SHA1/6 số/30 giây, cửa sổ ±1. Method khác/nhiều factor enabled trả 503; không bỏ qua.
- Challenge 5 phút, tối đa 5 lần thử, dùng một lần. Resend Email chờ 60 giây, hủy challenge cũ. TOTP từ authenticator, không resend. Hết hạn/hết lượt phải login lại.
- Challenge gắn với email, password records và factor configuration; thay đổi khiến verify/resend trả 409. Đọc lại Active/locked và quyền Google trước khi cấp token.
- Google xác minh state, nonce, PKCE, chữ ký RS256, issuer, audience, expiration, email_verified. OTP email bắt buộc trước khi tạo Owner mới; không tạo password giả. Admin/Executor/LegalVerifier không dùng Google công khai, kể cả vừa được cấp role trong lúc chờ 2FA.
- LastLoginAt và AUDIT_LOGS thành công chỉ lưu sau khi đủ factor, trước khi cấp token. Không lưu password/OTP/JWT trong audit. SMTP thất bại/timeout 30 giây trả 503, không báo thành công.
- Mỗi Bearer request đọc lại trạng thái, khóa và roles; JWT cũ không giữ role bị thu hồi.

## Callback Google và cấu hình

Test Swagger: giữ `Google:FrontendCallbackUrl` trống. Khi FE sẵn sàng, đặt URL cố định, ví dụ `https://frontend.example.com/auth/google`; origin phải có trong `Cors:Origins`. Backend từ chối query/fragment/user info/origin khác. HTTPS bắt buộc, trừ HTTP loopback cho phát triển.

Đây là trang hoàn tất FE, khác Google Authorized redirect URI `/api/auth/google/oidc-callback`. Sau redirect, FE GET `/api/auth/google/challenge` với cookie tạm; URL không có JWT/OTP/challengeId. Không nhận returnUrl tùy ý từ client.

Secrets/configuration: [Frontend-JWT-integration.md](Frontend-JWT-integration.md). Mẫu request: [LegacyVault.http](../LegacyVault.http). Google/SMTP: [Google-login-Swagger.md](Google-login-Swagger.md).

## Giới hạn

Challenge/OTP/cooldown/TOTP replay tracking trong bộ nhớ một instance; restart mất challenge. Chạy một backend instance; nhiều replica cần store phân tán có atomic consume/rate limits/replay protection, chưa triển khai.

Logout không revoke JWT đã cấp. Model RefreshToken và migration từ main vẫn giữ nhưng không được dùng/tự chạy; persistence thật chưa xác minh. Google liên kết theo verified email, chưa lưu sub. Chưa có API quên mật khẩu, đặt password cho Google-only user hoặc bật/tắt 2FA; không tự bật 2FA cho mọi account.

## Kiểm chứng

```powershell
dotnet build LegacyVault.sln --no-restore
dotnet run --project LegacyVault.Tests
git diff --check
```

Console harness dùng SQLite relational cô lập và test doubles Google/SMTP. Bao gồm OTP resend/replay/concurrency, đổi email/password/factor, disable/lock/role changes, audit rollback, callback redirect và workflow hiện có. Google/SMTP thật, SQL Server thật chưa được kiểm chứng trong lần sửa này. Không ghi dữ liệu/migration trên database thật.

Kết quả ngày 14/10/2026: build 0 lỗi/0 cảnh báo, **301 kiểm tra đạt**. Thực tế dùng output riêng để tránh DLL bị khóa bởi API đang chạy:

```powershell
dotnet build LegacyVault.sln --no-restore -p:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/login-final/
dotnet run --project LegacyVault.Tests --no-build --no-restore --property:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/login-final/
```
