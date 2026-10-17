> Cập nhật contract 15/10/2026: giấy tờ Owner và bằng chứng Executor đã tách riêng.

[Upload Owner / giấy chứng tử Executor / loại bỏ Staff](Executor-evidence-update.md)

# Luồng chính: đăng ký → JWT → kho → tài sản → người thụ hưởng

## 1. Tài khoản

POST `/api/auth/register` nhận fullName/email/password/confirmPassword/phone; luôn Owner. POST `/api/auth/login` nhận email/password, trả 200 JWT hoặc 202 challenge khi 2FA enabled. Hoàn tất POST `/api/auth/2fa/verify` nếu cần. Google + OTP cũng trả cùng token contract, xem [Google](Google-login-Swagger.md).

Dán accessToken vào Swagger Authorize, gọi GET `/api/auth/me`. Không dùng application cookie cho API nghiệp vụ.

## 2. Tạo kho và tài sản

POST `/api/owner/vaults` với name/description (≤500), lấy vaultId. POST `/api/owner/assets` với vaultId/name/type/description, lấy assetId. Kho phải Active và của người gọi. Không gửi ownerId/status; server quyết định. Name ≤100, type ASCII ≤50, mô tả tài sản ≤4000.

GET list/detail để kiểm tra. PUT vault/asset cập nhật; POST close/revoke giữ lịch sử và bị chặn khi có hồ sơ bàn giao bảo vệ.

## 3. Chọn người thụ hưởng

Tài khoản B đăng ký bình thường. Login A bằng JWT, POST `/api/owner/assets/{assetId}/beneficiaries`:

```json
{"email":"beneficiary@example.com","allocation":100}
```

201 trả beneficiaryId; B giữ Owner và nhận thêm Beneficiary. Email phải đăng ký/Active, không tự thụ hưởng/trùng; tổng ≤100, 2 chữ số thập phân. GET allocations trả assignmentId; PUT allocation hoặc POST revoke để thay đổi trước khi bàn giao. Không có lời mời email chưa đăng ký vì schema không có invitation persistence.

## 4. Giấy tờ và bàn giao

Owner upload multipart File/Signature (BeneficiaryId tùy chọn, phải là phân bổ Active hiện có). Backend AES-256-GCM, tối đa 10 MiB/file. Owner luôn cần chữ ký rời. Giấy chứng tử do Executor được phân công nộp vào hồ sơ bàn giao, không gửi qua upload asset. GET documents/content và POST revoke có ownership checks.

Chỉ Executor được phân công xử lý hồ sơ; không còn intake hoặc fallback Staff. Xem [contract mới](Executor-evidence-update.md).

Login B bằng JWT, xác minh OTP beneficiary riêng để xem di sản. Chỉ định B không tự mở tài sản. Confirm receipt chỉ sau Completed, không thể confirm thay người khác/lặp.

Toàn bộ method/path/DTO, state machine, status codes và pagination: [Frontend-JWT-integration.md](Frontend-JWT-integration.md). Test console: `dotnet run --project LegacyVault.Tests`; dùng SQLite relational và external doubles, không database thật.