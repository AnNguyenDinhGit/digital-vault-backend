# LegacyVault backend

ASP.NET Core .NET 8, EF Core SQL Server 8.0.30. Giữ kiến trúc API → BLL → DAL.

- API: controllers, cookie authentication, DI, rate limiting và HTTP error responses.
- BLL: DTOs, phân quyền, upload, xác minh chữ ký, mã hóa AES-256-GCM, OTP và nghiệp vụ bàn giao.
- DAL: entities database-first, DbContext, repositories và lưu file mã hóa.
- Tests: bộ kiểm tra chạy độc lập, không cần SQL Server hoặc SMTP thật.

Đã triển khai các API chủ sở hữu, người thi hành và người thụ hưởng theo ảnh `Screenshot 2026-10-04 133536.png`.

Đã có đăng ký/login mật khẩu, tạo kho/tài sản, chỉ định người thụ hưởng bằng email và đăng nhập Google → OTP email → cookie. Google nhận diện User bằng email, không thêm bảng/cột; tài khoản mới nhận Owner và không có mật khẩu giả. Email có sẵn giữ nguyên tài khoản/quyền, nhưng tài khoản nhân viên, không Active hoặc đang khóa bị chặn khỏi Google công khai.

Hướng dẫn [đăng nhập Google và test Swagger](docs/Google-login-Swagger.md), [luồng tạo kho/tài sản và chọn người thụ hưởng](docs/Luong-chinh-dang-ky-tao-assets.md), [trạng thái chức năng](docs/Trang-thai-chuc-nang.md).

Xem [tài liệu API và cấu hình](docs/API.md) để biết endpoint, multipart fields, định dạng chữ ký, OTP và giới hạn triển khai.

```powershell
dotnet build LegacyVault.sln
dotnet run --project LegacyVault.Tests
dotnet run --project LegacyVault.API --launch-profile https
```

Cần cấu hình ConnectionStrings:LegacyVault, Security:EncryptionKey, chứng thư signer và SMTP qua User Secrets hoặc biến môi trường. Google cần thêm Google:ClientId/ClientSecret. Không lưu secrets vào Git. API không tự seed user, chạy migration hay sửa schema; User được tạo khi đăng ký hoặc xác minh OTP Google thành công. database/schema.sql không có CREATE TABLE và không được dùng để dựng schema.

Kiểm thử gần nhất: build thành công, 154 kiểm tra đạt. Google dùng provider giả lập với token RSA và HTTPS loopback; dữ liệu test dùng SQLite in-memory. Chưa test Google/SMTP thật; challenge Google lưu trong bộ nhớ một instance và mất khi restart.
