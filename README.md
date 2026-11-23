# LegacyVault / Digital Vault Backend

Hệ thống Backend cho nền tảng **Lưu trữ và Bàn giao Tài sản Số** (**Digital Vault**), được xây dựng bằng **ASP.NET Core (.NET 8)** và **Entity Framework Core** với **Microsoft SQL Server** theo kiến trúc phân tầng **API → BLL → DAL**.

## Kiến trúc & Cấu trúc giải pháp

`LegacyVault.sln` gồm 4 project chính:

- **`LegacyVault.API/`**: ASP.NET Core Web API controllers, JWT Bearer authentication, middleware xử lý lỗi, SignalR hubs, cấu hình DI và Swagger/OpenAPI.
- **`LegacyVault.BLL/`**: Business services, DTOs, validation, OTP/2FA, mã hóa tài liệu (`AES-GCM`), xác thực chữ ký số (`RSA`), xử lý nghiệp vụ bàn giao & thanh toán VNPay.
- **`LegacyVault.DAL/`**: Entities, `LegacyVaultDbContext`, repositories, xử lý concurrency và lưu trữ tệp tin.
- **`LegacyVault.Tests/`**: Console test harness chạy integration tests trên SQLite in-memory và các test doubles cho dịch vụ ngoài.
- **`docs/`**: Tài liệu hướng dẫn kiểm thử API/Swagger, tích hợp Frontend JWT và cấu hình VNPay.
- **`database/`**: Scripts cơ sở dữ liệu.

## Công nghệ sử dụng

- **Framework & Ngôn ngữ**: C#, .NET 8 (SDK pinned tại `global.json`), ASP.NET Core Web API
- **ORM & Database**: Entity Framework Core, Microsoft SQL Server, SQLite (in-memory cho kiểm thử)
- **Xác thực & Bảo mật**: JWT Bearer (kèm Refresh Token), Google OIDC + OTP, 2FA (Email/TOTP), mã hóa tệp `AES-GCM`, xác thực chữ ký số `RSA` (SHA-256)
- **Tích hợp khác**: SignalR (real-time chat/notifications), VNPay Payment Gateway, Swagger/OpenAPI

## Chức năng chính

- **Xác thực & Tài khoản**: Đăng ký Owner, đăng nhập mật khẩu với 2FA (Email/TOTP), đăng nhập Google OIDC + OTP, làm mới token (Refresh Token) và quản lý hồ sơ/nhật ký hoạt động (`/api/auth/me`, `/api/owner/*`).
- **Quản lý Kho & Tài sản số**: Owner tạo/sửa/đóng kho; tạo/sửa/thu hồi tài sản; phân bổ người thụ hưởng; tải lên/tải xuống/thu hồi tài liệu đã mã hóa `AES-GCM` kèm chữ ký số `RSA`.
- **Quy trình Bàn giao & Thẩm định Pháp lý**:
  - Owner hoặc Executor khởi tạo hồ sơ bàn giao (hỗ trợ giữ Executor đã chỉ định hoặc tự động chọn Executor đủ điều kiện).
  - Executor tiếp nhận hồ sơ, nộp bằng chứng/giấy chứng tử và gửi thẩm định pháp lý.
  - Tự động khóa các thao tác thay đổi dữ liệu trên tài khoản Owner khi đã có hồ sơ bàn giao đính kèm giấy chứng tử (kể cả sau khi hoàn tất).
  - LegalVerifier xem xét tài liệu và thực hiện duyệt (`Approve`), từ chối (`Reject`) hoặc yêu cầu bổ sung thông tin (`Request Additional Information`).
- **Thụ hưởng & Xác nhận nhận tài sản**: Beneficiary xác minh OTP theo phiên JWT, xem/tải tài sản được bàn giao và xác nhận đã nhận tài sản bằng chữ ký số.
- **Thanh toán & Thời gian thực**: Quản lý gói dịch vụ, báo giá, thanh toán VNPay (Checkout, Return, IPN), thông báo và chat real-time qua SignalR.

## Cài đặt & Chạy dự án

Yêu cầu **.NET SDK** theo `global.json` (khuyến nghị .NET 8/9 SDK tương thích).

```powershell
dotnet restore LegacyVault.sln
dotnet build LegacyVault.sln --no-restore
dotnet run --project LegacyVault.Tests
dotnet run --project LegacyVault.API --launch-profile https
```

- **Swagger UI** (môi trường Development): `https://localhost:7015/swagger`
- Nếu cần tin cậy chứng chỉ HTTPS local:
  ```powershell
  dotnet dev-certs https --trust
  ```
- **Kiểm thử**: Dự án dùng console test runner riêng, chạy bằng `dotnet run --project LegacyVault.Tests` (không dùng `dotnet test`).
- **Cấu hình & Bảo mật**: `appsettings.json` chỉ chứa cấu hình mẫu; chuỗi kết nối SQL Server, JWT signing key, SMTP, khóa mã hóa, chứng chỉ xác thực chữ ký số và thông số VNPay cần được cấu hình qua **User Secrets** hoặc **biến môi trường**. Không tự động chạy migration hay seed dữ liệu thật khi khởi động.

## Tài liệu tham khảo (`docs/`)

- [Hướng dẫn test toàn bộ API](docs/Huong-dan-test-toan-bo-API.md)
- [Contract Frontend JWT và toàn bộ endpoint](docs/Frontend-JWT-integration.md)
- [Hướng dẫn test API qua Swagger](docs/Huong-dan-test-API-Swagger.md)
- [Cấu hình và luồng thanh toán VNPay](docs/VNPay.md)
- [Luồng Google login hiện tại](docs/Google-login-direct.md)
- [Contract Auth sau cập nhật Refresh Token](docs/Merge-main-2026-10-17.md)
- [Cập nhật Upload Owner & Bằng chứng Executor](docs/Executor-evidence-update.md)
- [Báo cáo bàn giao Backend](docs/Backend-JWT-handover.md)
- [HTTP Collection](LegacyVault.http)
