# Phân tích entities scaffold và kiểm thử

Đã đối chiếu `LegacyVault.DAL/Entities` và mapping trong `LegacyVaultDbContext` với database được cấu hình qua User Secrets. Có **24 entity CLR và 1 bảng liên kết USER_ROLES**, tổng cộng 25 bảng. Lệnh kiểm tra SQL chỉ SELECT danh sách role và metadata; không đổi schema hoặc tạo tài khoản trong database hiện tại. Đối chiếu tên cột không phát hiện cột bị thiếu; đây không phải kiểm chứng toàn bộ kiểu dữ liệu/index/constraint trên server.

## Nguyên nhân lỗi đăng ký 503

Code trước đây tìm `Vault Owner`, nhưng dữ liệu ROLES thực tế là:

| RoleId | RoleName | Ý nghĩa |
| --- | --- | --- |
| 1 | Admin | Quản trị hệ thống |
| 2 | Owner | Chủ sở hữu |
| 3 | Executor | Người thi hành |
| 4 | Beneficiary | Người thụ hưởng |
| 5 | LegalVerifier | Người xác minh pháp lý |

Tên role là dữ liệu, không phải enum hoặc giá trị được định nghĩa trong entity scaffold. Các hằng số kiểm tra quyền đã được sửa thành `Owner`, `Executor`, `Beneficiary`, `LegalVerifier`, `Admin`; không hard-code RoleId. Đăng ký công khai chỉ gán Owner, không nhận role trong request. Database hiện có Owner nên đăng ký dùng lại role hiện có; database mới thiếu Owner sẽ tạo duy nhất role mặc định này trong transaction đăng ký.

## Cấu trúc đăng ký đúng với database

| Entity / mapping | Vai trò trong đăng ký | Ràng buộc từ scaffold |
| --- | --- | --- |
| User → USERS | Hồ sơ người dùng | UserId là PK; FullName ≤100; Email ≤150, unique; Phone nullable, ≤20; Status ≤20; CreatedAt/UpdatedAt |
| Authentication → AUTHENTICATIONS | Thông tin mật khẩu và đăng nhập | AuthId là PK; UserId là FK; PasswordHash ≤255; FailedLoginCount, LockedUntil, LastLoginAt |
| Role → ROLES | Danh mục quyền | RoleId là PK; RoleName ≤50, unique |
| User.Roles ↔ Role.Users → USER_ROLES | Gắn user với role | PK kép (UserId, RoleId), hai FK tương ứng; EF dùng shared entity Dictionary, không cần tạo entity mới |

User không có trường Password, RoleId, ConfirmPassword hoặc IsAlive. ConfirmPassword chỉ là dữ liệu kiểm tra request, không lưu. Mật khẩu được hash PBKDF2-SHA256 600.000 vòng và lưu vào Authentication; không thêm cột lên User. Quan hệ User.Authentications là collection: scaffold không cho phép giả định User và Authentication là quan hệ 1–1, dù đăng ký hiện tạo một Authentication.

Repository đăng ký dùng navigation của các entity hiện có, lưu role (nếu thiếu), user, authentication và USER_ROLES trong cùng transaction. Khi thất bại, transaction rollback cả đồ thị. SQL Server dùng khóa UPDLOCK/HOLDLOCK khi tìm role để bảo vệ đăng ký đồng thời; retry tối đa hai lần khi SQL Server trả deadlock 1205. Email trùng được xử lý theo unique index và trả 409. Không tự chạy migration/scaffold lại.

## Nhóm entity phục vụ các API

| Nhóm | Entity / bảng | Quan hệ và lưu ý |
| --- | --- | --- |
| Danh tính | User, Role, Authentication, TwoFactorAuth | User.Roles nhiều–nhiều; TwoFactorAuth có Secret/Method/IsEnabled, chưa có challenge/expiry/attempt dành cho OTP email tạm |
| Kho và tài sản | DigitalVault, DigitalAsset | DigitalVault.OwnerId → User; DigitalAsset.VaultId → DigitalVault; kiểm tra quyền theo owner của vault |
| Giấy tờ | AssetDocument | AssetId và UploadedBy là FK; lưu FileName/FileType/StoragePath/IsEncrypted/Status, không có cột nội dung bytes |
| Mã hóa | EncryptionKey | Khóa gắn theo AssetId, có Algorithm/KeyVersion/Status; cơ chế hiện tại dùng khóa cấu hình và envelope mã hóa, chưa tích hợp rotation theo bảng này |
| Người thụ hưởng | BeneficiaryAssignment | AssetId/BeneficiaryId là FK; Allocation decimal(5,2), Status; không tự cấp quyền chỉ vì user nhập ID |
| Người thi hành | ExecutorAssignment | VaultId/ExecutorId là FK; mặc định Status=Pending; API nghiệp vụ cần assignment Active |
| Bàn giao | HandoverRequest, HandoverCase | Request có VaultId/ExecutorId; Case nối Request với BeneficiaryAssignment. Đơn và case là hai thực thể khác nhau |
| Chứng cứ bàn giao | HandoverDocument | RequestId/UploadedBy là FK; nội dung file ở storage ngoài database |
| Pháp lý | LegalVerification, DigitalSignature | Verification nối Request/Verifier; DigitalSignature.VerificationId bắt buộc, nên không dùng như bản ghi chữ ký chung cho mọi AssetDocument |
| Xác nhận nhận tài sản | BeneficiaryReceipt | HandoverId/BeneficiaryId là FK; entity sẵn có, chưa có endpoint nhận bàn giao trong phạm vi API hiện tại |
| Kiểm tra còn sống | ProofOfLife | UserId, lịch ping, MissedCount/Status; không có boolean IsAlive hoặc ngày tử vong |
| Trao đổi | Conversation, Message | Message thuộc Conversation và Sender User; chưa có entity thành viên conversation trong scaffold |
| Thông báo và nhật ký | Notification, AuditLog | Notification gắn User và có thể gắn HandoverCase; AuditLog.Result tối đa 20 ký tự |
| Gói dịch vụ | SubscriptionPlan, UserSubscription, PaymentTransaction | SubscriptionPlan → UserSubscription → PaymentTransaction; chưa thuộc luồng API được yêu cầu |

Đã sửa AuditLog.Result của nhánh upload khai báo đã mất thành `Pending_Legal` để vừa cột 20 ký tự; giá trị cũ `Deceased_PendingLegal` dài 21 ký tự. OwnerAlive hiện chỉ là thông tin request và được phản ánh vào trạng thái giấy tờ/audit, không thêm cột giả vào entity.

## Kiểm thử

```powershell
dotnet build LegacyVault.sln
dotnet run --project LegacyVault.Tests
```

Test nghiệp vụ bao gồm phân quyền, chữ ký RSA, AES-GCM, OTP, đăng ký/login, email trùng và không cho truyền role. Test quan hệ chạy qua **VaultRepository và DbContext thật** trên SQLite in-memory, không phải EF InMemory provider: kiểm tra FK Authentication.UserId, USER_ROLES, tạo/tái sử dụng Owner, đọc lại dữ liệu, login và rollback sau khi SQL đã ghi nhưng trước khi commit.

SQLite test không khẳng định hành vi lock/deadlock hoặc mọi kiểu dữ liệu SQL Server. Tên bảng/cột, index và độ dài được kiểm tra thêm từ model SQL Server. Không chạy EnsureCreated trên SQL Server hiện tại; EnsureCreated chỉ dùng cho database SQLite tạm trong test.

Đối chiếu database SQL Server đang cấu hình bằng lệnh chỉ đọc:

```powershell
dotnet run --project LegacyVault.Tests -- --database-audit
```

Lệnh đọc cấu hình API/User Secrets/biến môi trường, in danh sách role, so sánh tên cột của 25 bảng và đọc CHECK constraints; không in connection string, password hash hoặc hồ sơ người dùng. API đang chạy có thể khóa DLL khi build: dừng bằng Ctrl+C rồi build/run lại, hoặc build với OutDir riêng để kiểm thử mà không dừng phiên hiện tại.

## Hướng Google + OTP về sau

Scaffold hiện chưa có entity lưu external provider identity (provider + subject Google). Authentication.PasswordHash bắt buộc và gắn user. Luồng Google/OTP cần thiết kế lưu định danh ngoài và challenge đăng ký riêng; không tạo tài khoản trước khi OTP hợp lệ, không tự ghép tài khoản nội bộ chỉ dựa vào email và không tái sử dụng OTP beneficiary để đăng ký. Luồng đó chưa được triển khai trong thay đổi này.
