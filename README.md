# LegacyVault / DigitalVault backend

ASP.NET Core .NET 8, EF Core SQL Server, API → BLL → DAL. API nghiệp vụ dùng JWT Bearer; Google chỉ giữ cookies tạm để xác minh Google/OTP.

## Chức năng

- Đăng ký Owner, password login có 2FA Email/TOTP, Google OIDC + OTP, JWT và `/api/auth/me`.
- Owner tạo/xem/sửa/đóng kho; tạo/xem/sửa/thu hồi tài sản; phân bổ/create/update/revoke và giấy tờ mã hóa/upload/list/download/revoke.
- Tạo/cancel hồ sơ bàn giao, chỉ định hoặc tự chọn Executor độc lập. Staff tiếp nhận kho đủ tiêu chí atomically, không thay Executor hợp lệ.
- Executor/Staff xử lý đúng hồ sơ được giao; LegalVerifier xem tài liệu và approve/reject/request-additional-information; Beneficiary xác minh OTP, xem di sản và confirm receipt.
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
- [Contract login hoàn thiện và giới hạn triển khai](docs/Backend-login.md)
- [HTTP collection](LegacyVault.http)
- [Trạng thái và giới hạn](docs/Trang-thai-chuc-nang.md)
- [Báo cáo bàn giao và 256 kiểm tra](docs/Backend-JWT-handover.md)

Không có refresh token hoặc invitation persistence phù hợp; JWT hết hạn phải login lại. Logout chỉ kết thúc OTP grant và yêu cầu client bỏ token, không tuyên bố revoke JWT. Google nhận diện theo email, không lưu sub. Challenge/OTP/replay tracking nằm trong bộ nhớ một instance.

Database SELECT audit đã đối chiếu tên cột của 25 bảng; ExecutorId bắt buộc int và Description kho tối đa 500. Đây chưa xác minh toàn bộ schema/SQL Server concurrency. Tests dùng SQLite relational và provider/SMTP giả lập; Google/SMTP thật, chứng thư production chưa được kiểm chứng. Role Staff chưa được thêm vào database thật: script `database/ensure-staff-role.sql` dành cho quản trị viên chạy rõ ràng, không tự gán User.

File người dùng `docs/Danh-sach-API-da-lam.md` được giữ nguyên; tham khảo contract JWT mới ở trên để tránh dùng hướng dẫn cookie cũ.
