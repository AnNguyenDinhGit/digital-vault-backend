# Trạng thái LegacyVault — JWT Bearer

## Đã triển khai

- JWT HS256, issuer/audience/expiry; /auth/me và kiểm tra User Active/locked/roles từ DB ở mỗi request.
- Password login tôn trọng Email/TOTP 2FA; Google OIDC/OTP trả JWT và không bỏ qua 2FA tài khoản.
- Owner kho/tài sản/phân bổ/giấy tờ create/list/detail/update/close/revoke, ownership và allocation concurrency.
- Tạo/cancel hồ sơ, chọn Executor hoặc random độc lập, Staff intake assignment/request/cases cùng transaction; không thay Executor.
- Executor/Staff chỉ thao tác hồ sơ được giao, upload/start/complete; LegalVerifier review độc lập, approve/reject/additional information.
- Beneficiary OTP gate, tài sản/hồ sơ/tài liệu của mình, confirm/read receipt; notifications và audit persistence.
- Swagger Bearer, CORS origin allowlist, cấu hình mẫu không credentials, collection HTTP và contract frontend.
- BeneficiaryService placeholder được thay bằng wrapper qua ManagementService/repository, được DI và dùng ở controller; không trả EF entities.

## Giới hạn rõ ràng

Không invitation/refresh-token persistence phù hợp, không API cấp tài khoản nhân viên hoặc đặt mật khẩu Google-only. OTP/challenge/TOTP replay tracking trong bộ nhớ một instance. Logout thu hồi OTP grant, không revoke JWT. Không client-side encryption/E2EE hoặc chữ ký số trên receipt chỉ có note/status.

ExecutorId bắt buộc int nên không tạo request unassigned. Staff dùng USERS/ROLES/USER_ROLES và UserId tại ExecutorId. Script ensure-staff-role.sql được chuẩn bị, không chạy tự động hoặc gán cho user nào.

## Xác minh

Kết quả cuối: build 0 lỗi/0 cảnh báo, console harness **256 kiểm tra đạt**. [Báo cáo lệnh chạy và file sửa](Backend-JWT-handover.md).

Build/test console bằng SQLite relational và Google/SMTP doubles, không seed test trên SQL Server. Google test dùng TestServer, không import chứng thư Windows. SELECT audit kết nối được, so sánh 25 bảng không thiếu cột, xác nhận ExecutorId NOT NULL và mô tả kho nvarchar(500). Audit không chứng minh toàn bộ schema, constraints/index hoặc SQL Server concurrency. Google/SMTP thật và chứng thư production chưa được kiểm chứng.

[Contract và danh sách API hiện tại](Frontend-JWT-integration.md), [Swagger](Huong-dan-test-API-Swagger.md), [HTTP collection](../LegacyVault.http). File người dùng Danh-sach-API-da-lam.md được giữ nguyên và có thể mô tả contract cũ.
