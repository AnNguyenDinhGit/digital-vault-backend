> Cập nhật contract 15/10/2026: giấy tờ Owner và bằng chứng Executor đã tách riêng.

[Upload Owner / giấy chứng tử Executor / loại bỏ Staff](Executor-evidence-update.md)

# Trạng thái LegacyVault — JWT Bearer

## Đã triển khai

- JWT HS256, issuer/audience/expiry; /auth/me và kiểm tra User Active/locked/roles từ DB ở mỗi request.
- Password login tôn trọng Email/TOTP 2FA; Google OIDC/OTP trả JWT và không bỏ qua 2FA tài khoản.
- Owner kho/tài sản/phân bổ/giấy tờ create/list/detail/update/close/revoke, ownership và allocation concurrency.
Chỉ Executor được phân công xử lý hồ sơ; không còn intake hoặc fallback Staff. Xem [contract mới](Executor-evidence-update.md).
- Executor chỉ thao tác hồ sơ được giao, upload/start/complete; LegalVerifier review độc lập, approve/reject/additional information.
- Beneficiary OTP gate, tài sản/hồ sơ/tài liệu của mình, confirm/read receipt; notifications và audit persistence.
- Swagger Bearer, CORS origin allowlist, cấu hình mẫu không credentials, collection HTTP và contract frontend.
- BeneficiaryService placeholder được thay bằng wrapper qua ManagementService/repository, được DI và dùng ở controller; không trả EF entities.

## Giới hạn rõ ràng

Không invitation/refresh-token persistence phù hợp, không API cấp tài khoản nhân viên hoặc đặt mật khẩu Google-only. OTP/challenge/TOTP replay tracking trong bộ nhớ một instance. Logout thu hồi OTP grant, không revoke JWT. Không client-side encryption/E2EE hoặc chữ ký số trên receipt chỉ có note/status.

Role Staff đã loại khỏi nghiệp vụ; không seed/xóa/chuyển dữ liệu thật. Xem [audit Staff cũ](../database/review-legacy-staff.sql).

## Xác minh

Kết quả ngày 15/10/2026: build 0 lỗi/0 cảnh báo, console harness **330 kiểm tra đạt**, Swagger Kestrel trả HTTP 200. [Báo cáo lệnh chạy và file sửa](Executor-evidence-update.md).

Build/test console bằng SQLite relational và Google/SMTP doubles, không seed test trên SQL Server. Google test dùng TestServer, không import chứng thư Windows. SELECT audit kết nối được, so sánh 25 bảng không thiếu cột, xác nhận ExecutorId NOT NULL và mô tả kho nvarchar(500). Audit không chứng minh toàn bộ schema, constraints/index hoặc SQL Server concurrency. Google/SMTP thật và chứng thư production chưa được kiểm chứng.

[Contract và danh sách API hiện tại](Frontend-JWT-integration.md), [Swagger](Huong-dan-test-API-Swagger.md), [HTTP collection](../LegacyVault.http). File người dùng Danh-sach-API-da-lam.md được giữ nguyên và có thể mô tả contract cũ.
