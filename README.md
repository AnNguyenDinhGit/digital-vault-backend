# LegacyVault backend

Thiết lập ASP.NET Core Web API, target .NET 8, dùng SDK 9.0.315 đã cài.
EF Core SQL Server và dotnet-ef cùng phiên bản 8.0.30.

## Cấu trúc

```text
LegacyVault.sln
LegacyVault.API/                 Presentation, cấu hình DI và kết nối
LegacyVault.BLL/DTOs/            Chờ DTO từ entities thực tế
LegacyVault.DAL/Context/         LegacyVaultDbContext (khung chờ scaffold)
LegacyVault.DAL/Entities/        Chờ entities từ database
database/schema.sql             Script gốc, chưa chạy
```

References: API → BLL → DAL. API cũng tham chiếu DAL để đăng ký DbContext
tại composition root. Chưa có controller, service, CRUD, authentication hoặc frontend.

## Kết nối bảo mật

Tên cấu hình: `ConnectionStrings:LegacyVault`.
Đặt connection string thật bằng biến môi trường `ConnectionStrings__LegacyVault`
hoặc .NET User Secrets của project API trong môi trường Development.
UserSecretsId đã được khởi tạo; không lưu mật khẩu trong repository.

Ví dụ cấu trúc connection string (các giá trị trong ngoặc là placeholder):

```text
Server=<server,port hoặc server\instance>;Database=<database>;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;
```

Nếu dùng SQL authentication, lưu toàn bộ connection string gồm tài khoản và
mật khẩu trong biến môi trường hoặc User Secrets. Không gửi mật khẩu qua chat.
User Secrets dùng cho phát triển và không mã hóa dữ liệu trên đĩa.

Ứng dụng không mở kết nối ở startup. DbContext được đăng ký với `UseSqlServer`;
nếu resolve DbContext khi thiếu cấu hình, thông báo lỗi chỉ nêu tên cấu hình.
Không bật sensitive data logging. Không có `EnsureCreated`, `Migrate` hoặc migrations.

## Trạng thái và blocker

Chưa kiểm tra kết nối: chưa có connection string cho database phát triển/test
được cho phép truy cập. `database/schema.sql` chỉ tạo/cấu hình `DigitalVaultDB`,
không có `CREATE TABLE`, PK, FK hoặc định nghĩa cột. Không chạy script này.

Số bảng trong script: 0. Số bảng trên server: chưa xác định.
Entities: 0. DTOs: 0. Chưa có mapping entity → DTO để kiểm chứng.
DbContext hiện là placeholder, không đại diện cho schema thực tế.

Đã chạy `dotnet build LegacyVault.sln`: cả ba project thành công,
0 warnings, 0 errors. Đã xác minh `dotnet ef --version`: 8.0.30.
Chưa mở kết nối SQL Server và chưa thực thi SQL; không thay đổi dữ liệu/schema.

Cần server/instance và port nếu cần, tên database, phương thức xác thực,
connection string được cấu hình bảo mật, và xác nhận đây là database phát triển/test.
Tài khoản cần quyền kết nối và đọc metadata schema (ví dụ `VIEW DEFINITION`).
Schema thực tế phải có bảng; có thể bổ sung script schema đầy đủ để đối chiếu.

## Tiếp tục sau khi có kết nối được cho phép

Kiểm tra kết nối bằng SqlClient, chỉ đọc metadata số bảng và schema; không ghi dữ liệu.
Sau khi kết nối được xác minh, dùng Database First:

```powershell
dotnet tool restore
dotnet ef dbcontext scaffold "Name=ConnectionStrings:LegacyVault" Microsoft.EntityFrameworkCore.SqlServer --project LegacyVault.DAL --startup-project LegacyVault.API --context LegacyVaultDbContext --context-dir Context --output-dir Entities --namespace LegacyVault.DAL.Entities --context-namespace LegacyVault.DAL.Context --no-onconfiguring --force
dotnet build LegacyVault.sln
```

`--force` thay khung DbContext hiện tại bằng code sinh từ database; khi đã có
entities cần kiểm tra các file trước khi scaffold lại. Lệnh scaffold đọc schema,
không tạo migration hoặc thay đổi database. Không truyền connection string thật
trên command line; `Name=` đọc cấu hình qua startup project. Với User Secrets,
đặt `ASPNETCORE_ENVIRONMENT=Development` và `DOTNET_ENVIRONMENT=Development`.

Sau scaffold: đối chiếu bảng, PK/FK, navigation, kiểu cột, nullability và mappings.
Tạo DTO chỉ từ các trường thực tế, bỏ password hashes và các trường nhạy cảm khác.
Ghi bảng mapping entity → DTO rồi build lại. Chưa triển khai mapper hoặc services.
