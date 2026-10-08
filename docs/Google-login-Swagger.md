# Đăng nhập Google theo email và OTP

Luồng: chọn tài khoản Google → backend xác minh Google → gửi OTP → nhập OTP trên Swagger → tạo/tìm User và cấp cookie. Không thêm bảng/cột, không migration hoặc sửa entities scaffold. Email là định danh của ứng dụng; Google `sub` không được lưu.

## 1. Cấu hình Google

Trong Google Cloud Console, cấu hình màn hình consent và tạo OAuth Client loại **Web application**. Nếu ứng dụng đang ở chế độ Testing, thêm email thử nghiệm vào test users. Đây là cấu hình OAuth, không sử dụng Firebase/Auth0 hoặc database cloud.

Thêm Authorized redirect URI chính xác:

```text
https://localhost:7015/api/auth/google/oidc-callback
```

Middleware xử lý URL này; `/api/auth/google/callback` chỉ là trang hướng dẫn nhập OTP. Khi đổi host/port, cập nhật redirect URI tương ứng.

Lưu credentials trên máy, không commit vào appsettings:

```powershell
dotnet user-secrets set "Google:ClientId" "YOUR_CLIENT_ID" --project LegacyVault.API
dotnet user-secrets set "Google:ClientSecret" "YOUR_CLIENT_SECRET" --project LegacyVault.API
```

Thiếu credentials: start trả 503; các API đăng nhập mật khẩu vẫn hoạt động.

## 2. SMTP và chạy backend

Cấu hình connection string như hiện tại và SMTP có TLS qua User Secrets hoặc biến môi trường:

```powershell
dotnet user-secrets set "Mail:Host" "YOUR_SMTP_HOST" --project LegacyVault.API
dotnet user-secrets set "Mail:Port" "587" --project LegacyVault.API
dotnet user-secrets set "Mail:Username" "YOUR_SMTP_USERNAME" --project LegacyVault.API
dotnet user-secrets set "Mail:Password" "YOUR_SMTP_PASSWORD" --project LegacyVault.API
dotnet user-secrets set "Mail:From" "YOUR_SENDER_EMAIL" --project LegacyVault.API
dotnet run --project LegacyVault.API --launch-profile https
```

SMTP hiện gửi thật, gồm cả OTP người thụ hưởng. Nhà cung cấp có thể yêu cầu app password. Nếu chứng thư localhost chưa được tin cậy, chạy `dotnet dev-certs https --trust`.

## 3. Test khi chưa có frontend

1. Nếu đang login tài khoản khác, gọi `/api/auth/logout` trước để dễ theo dõi kết quả.
2. Mở trực tiếp `https://localhost:7015/api/auth/google/start` trên thanh địa chỉ trình duyệt. Không dùng Execute trong Swagger cho bước redirect này.
3. Chọn tài khoản Google. Backend gửi OTP; trang callback hiển thị `challengeId`. Chưa tạo User hoặc cấp phiên đăng nhập mới.
4. Mở `https://localhost:7015/swagger` trong **cùng trình duyệt và cùng host/port**. Gọi `POST /api/auth/google/otp/verify`:

```json
{
  "challengeId": "COPY_FROM_CALLBACK_PAGE",
  "code": "123456"
}
```

Thay code bằng OTP nhận được. Kỳ vọng 200 với `userId`, `roles`; trình duyệt lưu cookie phiên. Tiếp tục test tạo kho/tài sản bằng API owner.

5. Gửi lại: gọi `POST /api/auth/google/otp/resend` với `{"challengeId":"CURRENT_CHALLENGE_ID"}`. Chờ tối thiểu 60 giây từ lần gửi trước; dùng **challengeId mới** trong response và OTP mới. Có thể mở lại trang callback để xem challenge mới.

Swagger tự gửi `X-Vault-Request: 1`. Client khác phải gửi header này cho POST và giữ cookie đã bắt đầu luồng Google. ChallengeId và OTP không đủ nếu thiếu cookie tương ứng.

## 4. Quy tắc và giới hạn

- Email lấy từ Google đã xác minh và kiểm tra thêm bằng OTP; không nhận email/role từ body xác minh.
- Email chưa có: tạo User Active và Owner trong cùng transaction, không tạo mật khẩu giả.
- Email có sẵn: giữ UserId, tên, mật khẩu và role. Không lưu liên kết provider lâu dài.
- User không Active, đang khóa đăng nhập mật khẩu hoặc có Admin/Executor/LegalVerifier: trả 403.
- OTP hết hạn sau 5 phút, tối đa 5 lần thử, chỉ dùng một lần. OTP Google không thay thế OTP người thụ hưởng.
- Email Google thay đổi: không tự nhận ra tài khoản cũ. Google-only User không đăng nhập bằng mật khẩu; hiện chưa có API thiết lập mật khẩu.
- Challenge lưu trong bộ nhớ một instance; restart làm mất challenge. Chưa hỗ trợ nhiều instance không có sticky routing/shared store.
- Tạo email đồng thời có thể trả 409; bắt đầu lại Google. OTP đã dùng không được dùng lại kể cả khi lưu database thất bại.

## 5. Kiểm thử tự động

```powershell
dotnet build LegacyVault.sln
dotnet run --project LegacyVault.Tests
```

Google tests dùng discovery/token/JWKS giả lập, token RSA, HTTPS loopback và SQLite in-memory. Không tạo tài khoản ở SQL Server thật. Chứng thư TLS test chỉ dùng trong test; production vẫn xác minh TLS. Test Google và SMTP thực tế cần credentials hợp lệ của bạn.

Tham khảo: [Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect), [ASP.NET Core OpenID Connect](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication).
