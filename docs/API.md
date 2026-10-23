> Cập nhật 17/10/2026: main đã bổ sung refresh token; xem [contract auth sau merge](Merge-main-2026-10-17.md). Các mô tả chưa có refresh token bên dưới thuộc phiên bản trước.

> Cập nhật contract 15/10/2026: giấy tờ Owner và bằng chứng Executor đã tách riêng.

[Upload Owner / giấy chứng tử Executor / loại bỏ Staff](Executor-evidence-update.md)

# LegacyVault API

## Owner xem Executor và lý do từ chối, thu hồi phân công

POST `/api/owner/vaults/{vaultId}/handovers`, GET `/api/owner/handovers`, GET `/api/owner/handovers/{requestId}` bổ sung executorName/declineReason/declinedAt, giữ executorId và phân trang. Chưa từ chối hoặc thiếu dữ liệu lịch sử thì null. GET `/api/owner/vaults/{vaultId}/executor` trả vaultId/executorId/fullName/status/assignedAt (ưu tiên hiệu lực, nếu không có thì lịch sử gần nhất). POST `/api/owner/vaults/{vaultId}/executor/revoke` không body, chỉ trả 204 sau khi lưu Status=Revoked; cancel đơn Pending_Executor, chặn hồ sơ đã nhận/xử lý. Xem phần 8 trong [hướng dẫn tổng hợp](Huong-dan-test-toan-bo-API.md).

**Hướng dẫn test tổng hợp hiện tại:** [Huong-dan-test-toan-bo-API.md](Huong-dan-test-toan-bo-API.md). File này bao phủ đủ 93 controller operations cùng callback OIDC/SignalR, có input, quyền, response, trạng thái, flow test và giới hạn thực tế. Dùng file tổng hợp khi kiểm thử thay cho các mô tả lịch sử chưa được đồng bộ bên dưới.

## Tiếp nhận bàn giao và chi tiết Executor

Đã bổ sung GET `/api/owner/executors/{executorId}`, POST `/api/executor/handovers/{requestId}/accept` và `/decline`. Đơn Owner tạo mới ở `Pending_Executor`; accept mở Draft, decline thành Declined. Xem [contract và hướng dẫn frontend](Executor-intake-and-profile.md). Phân trang vẫn giữ nguyên.

## Dropdown Executor dành cho Owner

GET `/api/owner/executors?page=1&pageSize=20&search=...` trả PageDto gồm `userId`, `fullName` cho dropdown trước khi tạo kho. Hỗ trợ `vaultId` tùy chọn để lọc xung đột thụ hưởng trong kho của Owner. Xem [contract frontend và test](Owner-executor-dropdown.md).

> Contract sau merge 21/10/2026: các list từ main đã áp dụng phân trang `{ page, pageSize, items }`. Xem [danh sách endpoint thay đổi](Merge-pagination-2026-10-21.md); mô tả mảng trực tiếp ở tài liệu cũ không còn áp dụng cho các endpoint này.

## Owner xác nhận còn sống (20/10/2026)

GET/POST `/api/owner/life-confirmation`: đọc lần gần nhất/bấm xác nhận bằng JWT Owner; POST không body. Response có `ownerId`, `lastConfirmedAliveAt` (UTC hoặc null). Chi tiết frontend, persistence và test: [Owner-life-confirmation.md](Owner-life-confirmation.md).

## Executor và LegalVerifier (19/10/2026)

Xem [contract và hướng dẫn test đầy đủ](Executor-Legal-workflow.md): tổng quan tất cả đơn kèm Owner, chi tiết theo phân công, giấy chứng tử, lịch sử pháp lý, tài liệu và trạng thái Owner có nguồn bằng chứng. Endpoint cũ được giữ; `GET /api/executor/handovers/overview` là danh sách tổng quan phân trang mới.

Contract hiện tại: **JWT Bearer**, API → BLL → DAL. Danh sách method/path/role, DTO, multipart, status codes, pagination và giới hạn được cập nhật đầy đủ tại [Frontend-JWT-integration.md](Frontend-JWT-integration.md).

## Authentication

- POST `/api/auth/register`: tạo Owner bằng email/mật khẩu, không tự login.
- POST `/api/auth/login`: 200 TokenDto hoặc 202 LoginChallengeDto khi 2FA enabled.
- POST `/api/auth/2fa/verify`: hoàn thành Email/TOTP, trả TokenDto.
- POST `/api/auth/2fa/resend`: gửi lại Email OTP, cooldown 60 giây; chưa cấp token, không hỗ trợ resend TOTP.
- GET `/api/auth/me`: Bearer, profile công khai và roles hiện tại từ DB.
- POST `/api/auth/logout`: Bearer, thu hồi beneficiary OTP grant; client bỏ token, JWT không được revoke.
- GET `/api/auth/google/start`: browser navigation; middleware callback `/api/auth/google/oidc-callback` → trang `/api/auth/google/callback`.
- POST `/api/auth/google/otp/verify`: 200 TokenDto hoặc 202 nếu tài khoản còn 2FA; không cấp application cookie.
- POST `/api/auth/google/otp/resend`: cookie tạm Google + X-Vault-Request: 1, challengeId mới.
- GET `/api/auth/google/challenge`: lấy challengeId/expiry dạng JSON cho frontend, cookie-bound; không token trong URL.
- POST `/api/auth/google/complete`: email đã có User, dùng challengeId + cookie sau OIDC, không gửi OTP đăng ký; email mới vẫn dùng otp/verify. Xem [Google login mới](Google-login-direct.md).

## Tài liệu Owner và hồ sơ Executor

- POST `/api/owner/assets/{assetId}/documents`: File + Signature, BeneficiaryId tùy chọn; không gửi OwnerAlive/DeathCertificate.
- GET `/api/executor/assignments`: phân công hiện có của Executor.
- POST `/api/executor/assignments/{vaultId}/handovers`: Executor được phân công khởi tạo Draft nếu chưa có hồ sơ bảo vệ.
- POST `/api/executor/handovers/{requestId}/death-certificate`: chỉ assigned Executor, File + Signature tùy chọn; lưu HANDOVER_DOCUMENTS trước khi nộp pháp lý.

## Nghiệp vụ

Owner có kho/tài sản/phân bổ/giấy tờ create/list/detail/update/close hoặc revoke, chỉ định Executor và tạo/cancel bàn giao. Chỉ Executor được phân công xử lý hồ sơ; không còn intake hoặc fallback Staff. LegalVerifier xem/tải và thẩm định hồ sơ được giao. Beneficiary có OTP, di sản và receipt; notifications chỉ của chính mình. Xem [contract mới](Executor-evidence-update.md).

Các endpoint cũ được tái sử dụng; list cũ giữ mảng, list workflow mới dùng PageDto. API nghiệp vụ không cần X-Vault-Request; chỉ Google cookie-bound POST cần header. Swagger hỗ trợ Authorize Bearer.

Mỗi file upload ≤10 MiB, backend xác minh nội dung/MIME, mã hóa AES-256-GCM; không E2EE. Giấy tờ và hồ sơ được thu hồi bằng Status, không xóa lịch sử. Các mutation bị bảo vệ khi đang/đã bàn giao; concurrency vault/asset/request/legal/case ngăn ghi đè.

Không có invitation/refresh token persistence đúng mục đích, không tạo User giả. Chưa có API cấp tài khoản nhân viên. Xem [collection HTTP](../LegacyVault.http), [Swagger](Huong-dan-test-API-Swagger.md) và [Google](Google-login-Swagger.md).
