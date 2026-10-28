> Cập nhật Google login: email cũ không cần OTP đăng ký; email mới vẫn cần OTP. Xem [luồng Google hiện tại](docs/Google-login-direct.md). Các mô tả Google luôn yêu cầu OTP bên dưới thuộc phiên bản trước.

> Cập nhật 17/10/2026: main đã bổ sung refresh token; xem [contract auth sau merge](docs/Merge-main-2026-10-17.md). Các mô tả chưa có refresh token bên dưới thuộc phiên bản trước.

> Cập nhật contract 15/10/2026: giấy tờ Owner và bằng chứng Executor đã tách riêng.

[Upload Owner / giấy chứng tử Executor / loại bỏ Staff](docs/Executor-evidence-update.md)

# LegacyVault / DigitalVault backend

Hướng dẫn test chung cho toàn bộ API: [Huong-dan-test-toan-bo-API.md](docs/Huong-dan-test-toan-bo-API.md) — 97 controller operations, auth/Google/2FA, Owner, Executor, LegalVerifier, Beneficiary, Admin, notifications, chat và mã hóa trường. Các hướng dẫn theo đợt cập nhật bên dưới là tài liệu tham khảo lịch sử.

ASP.NET Core .NET 8, EF Core SQL Server, API → BLL → DAL. API nghiệp vụ dùng JWT Bearer; Google chỉ giữ cookies tạm để xác minh Google/OTP.

## Chức năng

- Đăng ký Owner, password login có 2FA Email/TOTP, Google OIDC + OTP, JWT và `/api/auth/me`.
- Owner tạo/xem/sửa/đóng kho; tạo/xem/sửa/thu hồi tài sản; phân bổ/create/update/revoke và giấy tờ mã hóa/upload/list/download/revoke.
- Tạo/cancel hồ sơ bàn giao; giữ Executor chỉ định hoặc random đúng tập Executor đủ điều kiện khi tạo Draft.
- Executor xử lý đúng hồ sơ được giao; LegalVerifier xem tài liệu và approve/reject/request-additional-information; Beneficiary xác minh OTP, xem di sản và confirm receipt.
- Notifications/audit lưu trong bảng hiện có; không trả EF entities, storage path hoặc secrets.

## Chạy và tích hợp

```powershell
dotnet restore LegacyVault.sln
dotnet build LegacyVault.sln
dotnet run --project LegacyVault.Tests
dotnet run --project LegacyVault.API --launch-profile https
```

Console test harness, không `dotnet test`. `appsettings.json` là cấu hình mẫu trống; JWT signing key và các credentials phải đặt bằng environment/User Secrets. Không tự seed user/migration hoặc thay schema khi startup.

- [Contract frontend JWT và toàn bộ endpoint](docs/Frontend-JWT-integration.md)
- [Test Swagger](docs/Huong-dan-test-API-Swagger.md)
- [Google login](docs/Google-login-Swagger.md)
- [VNPay payment setup and flow](docs/VNPay.md)
- [Contract login hoàn thiện và giới hạn triển khai](docs/Backend-login.md)
- [HTTP collection](LegacyVault.http)
- [Trạng thái và giới hạn](docs/Trang-thai-chuc-nang.md)
- [Báo cáo bàn giao backend](docs/Backend-JWT-handover.md)

Không có refresh token hoặc invitation persistence phù hợp; JWT hết hạn phải login lại. Logout chỉ kết thúc OTP grant và yêu cầu client bỏ token, không tuyên bố revoke JWT. Google nhận diện theo email, không lưu sub. Challenge/OTP/replay tracking nằm trong bộ nhớ một instance.

Role Staff đã loại khỏi nghiệp vụ; không seed/xóa/chuyển dữ liệu thật. Xem [audit Staff cũ](database/review-legacy-staff.sql).

File người dùng `docs/Danh-sach-API-da-lam.md` được giữ nguyên; tham khảo contract JWT mới ở trên để tránh dùng hướng dẫn cookie cũ.
