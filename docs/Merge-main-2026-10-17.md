# Kết hợp main vào dung — 17/10/2026

Nguồn main: `82a3e61`; dung trước merge: `b29d70d`.

## Conflict đã xử lý

- `VaultService.cs`: giữ Owner upload File + Signature, BeneficiaryId tùy chọn; giữ purpose trong envelope mã hóa và giấy chứng tử chỉ cho Executor được phân công. Kết hợp phần refactor không xung đột từ main. Không khôi phục nhánh OwnerAlive/DeathCertificate hoặc Staff.
- `UploadDocumentInput.cs`: tiếp tục loại bỏ DTO cùng UploadDocumentAsync không còn caller. Endpoint Owner vẫn chỉ có một action, không tạo route trùng.

Giữ MappingExtensions, các cập nhật model và refresh token từ main. Migration đi kèm chỉ được giữ trong Git, không chạy trên database thật.

## Contract auth sau merge

Login mật khẩu, Google OTP và 2FA vẫn bắt buộc hoàn tất xác thực trước khi nhận TokenDto. Ngoài accessToken/tokenType/expiresIn/userId/roles, response bổ sung refreshToken và refreshTokenExpiresAt. Refresh token có hạn 7 ngày, được lưu theo model RefreshToken từ main.

```http
POST /api/auth/refresh-token
Content-Type: application/json

{"refreshToken":"TOKEN_FROM_LOGIN"}
```

200 trả TokenDto với cặp token mới; token cũ bị revoke và lưu ReplacedByToken. Token không tồn tại/revoked/hết hạn hoặc User inactive/locked trả 401. Role lấy từ DB. IsRevoked có optimistic concurrency token qua partial model để hai lượt rotation không cùng ghi thành công; không thêm cột.

Logout vẫn cần Bearer và chấp nhận body trống, `{}` hoặc `{"refreshToken":"..."}`; nếu có refresh token thì thu hồi, vẫn kết thúc beneficiary OTP grant. Không tuyên bố revoke access JWT đã phát. Refresh endpoint dùng rate limit login. Không gửi token qua URL.

Các tài liệu trước ngày merge nói chưa có refresh token là mô tả phiên bản cũ. Database triển khai phải có schema RefreshToken tương ứng main; chưa kiểm chứng schema SQL Server thật hoặc tự chạy migration trong lần xử lý conflict này. Không giả lập persistence để che thiếu bảng/cột.

## Kiểm chứng

Build 0 lỗi/0 cảnh báo; console harness **337 kiểm tra đạt**, gồm upload Owner/Executor, loại bỏ Staff, JWT/Google/2FA, refresh rotation/replay/lock/logout và HTTP swagger.json. `git diff --check` sạch, không còn conflict marker.

```powershell
dotnet build LegacyVault.sln --no-restore -p:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/main-conflict/
dotnet run --project LegacyVault.Tests --no-build --no-restore --property:OutDir=D:/SWP/Project/digital-vault-backend/.artifacts/main-conflict/
```

Tests dùng SQLite relational và doubles Google/SMTP. Không sửa frontend, không chạy migration, không ghi dữ liệu thật. Merge được giải quyết và stage tại local; chưa tự commit/push.
