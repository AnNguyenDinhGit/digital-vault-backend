# LegacyVault backend

ASP.NET Core .NET 8, EF Core SQL Server 8.0.30. Giữ kiến trúc API → BLL → DAL.

- API: controllers, cookie authentication, DI, rate limiting và HTTP error responses.
- BLL: DTOs, phân quyền, upload, xác minh chữ ký, mã hóa AES-256-GCM, OTP và nghiệp vụ bàn giao.
- DAL: entities database-first, DbContext, repositories và lưu file mã hóa.
- Tests: bộ kiểm tra chạy độc lập, không cần SQL Server hoặc SMTP thật.

Đã triển khai các API chủ sở hữu, người thi hành và người thụ hưởng theo ảnh `Screenshot 2026-10-04 133536.png`.

Xem [tài liệu API và cấu hình](docs/API.md) để biết endpoint, multipart fields, định dạng chữ ký, OTP và giới hạn triển khai.

```powershell
dotnet build LegacyVault.sln
dotnet run --project LegacyVault.Tests
dotnet run --project LegacyVault.API --launch-profile https
```

Cần cấu hình ConnectionStrings:LegacyVault, Security:EncryptionKey, chứng thư signer và SMTP qua User Secrets hoặc biến môi trường. Không lưu secrets vào Git. API không tự tạo user, chạy migration hay sửa schema. Chưa kiểm tra kết nối database/SMTP thật; database/schema.sql không có CREATE TABLE và không được dùng để dựng schema.
