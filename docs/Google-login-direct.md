# Google login: OTP chỉ khi email mới

## Quy tắc

- Google xác thực OIDC thành công, email đã có User hợp lệ: không gửi OTP đăng ký, không phụ thuộc SMTP để đăng nhập thông thường.
- Email chưa có User: gửi OTP email và chỉ tạo Owner sau khi OTP đúng. Chưa xác minh không có User/JWT/refresh token.
- 2FA đã bật là lớp xác thực riêng và vẫn được giữ: tài khoản bật Email/TOTP có thể nhận 202 sau xác thực Google. Không tự bật/tắt 2FA trong thay đổi này.
- Giữ kiểm tra User Active, khóa tài khoản, role được phép, verified email, state/nonce/PKCE/chữ ký RS256/issuer/audience/expiration. Không cho Admin/Executor/LegalVerifier hoặc role không hỗ trợ dùng Google công khai.

Phạm vi cập nhật này là Google login; đăng ký/đăng nhập mật khẩu và API nghiệp vụ khác không đổi.

## Luồng backend và frontend

1. Điều hướng browser tới GET `/api/auth/google/start`, chọn tài khoản Google. Không gửi email/password/role từ FE.
2. Google gọi `/api/auth/google/oidc-callback`. Backend tra email đã xác minh, lưu kết quả tạm và chuyển tới `/api/auth/google/callback` hoặc URL frontend cố định đã cấu hình. Không có JWT/refresh token/OTP trong URL.
3. Trong cùng browser, GET `/api/auth/google/challenge` với credentials include:

```json
{"challengeId":"32_HEX_CHARACTERS","expiresAt":"2026-10-17T12:05:00Z","requiresOtp":false}
```

4. `requiresOtp=false`: FE tự gọi POST `/api/auth/google/complete` với cookie tạm, `X-Vault-Request: 1`, JSON:

```json
{"challengeId":"32_HEX_CHARACTERS"}
```

Không có màn hình OTP đăng ký cho tài khoản cũ. Endpoint POST chỉ để nhận token an toàn qua JSON sau OIDC, không bắt người dùng nhập thêm thông tin.

5. `requiresOtp=true`: hiển thị form OTP đăng ký, POST `/api/auth/google/otp/verify` với challengeId/code và cùng cookie/header. Gửi lại dùng `/api/auth/google/otp/resend`, cooldown 60 giây.
6. 200 trả TokenDto hiện có: userId, roles, accessToken, tokenType, expiresIn, refreshToken, refreshTokenExpiresAt. Nếu 202, tiếp tục `/api/auth/2fa/verify` cho factor đã bật.
7. Gắn Bearer khi gọi `/api/auth/me` và API nghiệp vụ; không dùng Google cookie như application login.

## Test Swagger

Email mới: start bằng thanh địa chỉ → challenge.requiresOtp=true → complete bị từ chối 400 → OTP verify đúng tạo tài khoản và trả token.

Email đã đăng ký: start lại → challenge.requiresOtp=false → POST complete chỉ với challengeId → nhận token, không có email OTP đăng ký. Phải dùng cùng browser/host để giữ cookie; POST Google cần X-Vault-Request. Swagger interceptor đã thêm header.

Thử browser khác hoặc gọi lại complete: 400. Dùng challengeId làm Bearer: 401. Authorization hết hạn sau 5 phút; cần start lại. Nếu email/account thay đổi sau OIDC: 409/403, không tự tạo account thay thế. Email mới không thể dùng complete để bỏ qua OTP.

## Bảo toàn và giới hạn

Verified identity tạm lưu trong một instance, browser-bound, hết hạn 5 phút và consume một lần bằng lock. Không lưu token ứng dụng trong URL hoặc store tạm này. Callback không tạo tài khoản cũ/mới ngoài quy tắc trên. Consume thất bại persistence yêu cầu bắt đầu lại, không replay.

Không thêm bảng/cột, chạy migration hoặc sửa frontend. Không tự commit/push. Google/SMTP thật chưa kiểm chứng; test dùng OIDC/SMTP doubles, SQLite relational và chứng thư RSA test. Không tuyên bố đã xác minh nhà cung cấp thật.

Kết quả: build 0 lỗi/0 cảnh báo, **356 kiểm tra đạt**, git diff --check sạch. Lệnh kiểm chứng dùng output riêng:

```powershell
dotnet build LegacyVault.sln --no-restore -p:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/google-direct/
dotnet run --project LegacyVault.Tests --no-build --no-restore --property:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/google-direct/
```
