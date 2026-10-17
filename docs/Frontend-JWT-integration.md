# LegacyVault: tích hợp frontend bằng JWT Bearer

API → BLL → DAL, ASP.NET Core .NET 8. Các entity/context scaffold và database schema được giữ nguyên. Frontend không được chỉnh sửa trong lần triển khai này.

## 1. Cấu hình và chạy

`LegacyVault.API/appsettings.json` là mẫu trống, không chứa credentials. Cấu hình qua User Secrets hoặc environment; không đưa secrets vào Git:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "YOUR_RANDOM_SIGNING_KEY_AT_LEAST_32_BYTES" --project LegacyVault.API
dotnet user-secrets set "ConnectionStrings:LegacyVault" "YOUR_SQL_SERVER_CONNECTION_STRING" --project LegacyVault.API
dotnet run --project LegacyVault.API --launch-profile https
```

Dùng khóa ngẫu nhiên đủ mạnh, không dùng chuỗi mẫu trên. JWT không cấu hình hợp lệ sẽ làm startup thất bại, không tự dùng khóa giả. JWT HS256, issuer/audience bắt buộc, lifetime mặc định 15 phút (cấu hình 1–30 phút), clock skew 15 giây. Cấu hình chi tiết:

| Config/environment | Ý nghĩa |
| --- | --- |
| `Jwt__SigningKey`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__LifetimeMinutes` | Khóa ký và tham số xác minh JWT |
| `Cors__Origins__0`, `Cors__Origins__1` | Origin frontend cụ thể, không path/wildcard; mặc định localhost:5173 |
| `ConnectionStrings__LegacyVault` | SQL Server hiện hữu |
| `Google__ClientId`, `Google__ClientSecret` | Google OAuth web client |
| `Google__FrontendCallbackUrl` | Tùy chọn: URL FE cố định trên origin CORS allowlist, không query/fragment; trống để test Swagger |
| `Mail__Host`, `Mail__Port`, `Mail__Username`, `Mail__Password`, `Mail__From` | SMTP TLS, mặc định port 587 |
| `Security__EncryptionKey` | Base64 của khóa AES 32 byte |
| `Security__SignerCertificates__USER_ID` | Chứng thư PEM của người ký |
| `Security__TrustedRootCertificates__0` | CA PEM được tin cậy khi dùng private CA |
| `AllowedHosts` | Host backend thực tế khi deploy |

Swagger Development: `https://localhost:7015/swagger`. Chọn **Authorize**, dán accessToken. Chứng thư HTTPS dev có thể tin bằng `dotnet dev-certs https --trust`.

## 2. Login, 2FA, Google và me

### Password login

`POST /api/auth/login`, anonymous, JSON:

```json
{"email":"owner@example.com","password":"YOUR_PASSWORD"}
```

Không có 2FA enabled → 200:

```json
{"userId":1,"roles":["Owner"],"accessToken":"JWT_VALUE","tokenType":"Bearer","expiresIn":900}
```

Có 2FA → 202, **không có accessToken**:

```json
{"challengeId":"32_HEX_CHARACTERS","method":"Email","expiresAt":"2026-10-14T12:05:00Z","requiresTwoFactor":true}
```

`POST /api/auth/2fa/verify`, anonymous, `{"challengeId":"...","code":"123456"}` → 200 TokenDto trên. Challenge không thể dùng làm Bearer. Hỗ trợ đúng `Method=Email` (OTP gửi đến User.Email) hoặc `Method=TOTP` (Secret Base32, SHA1, 6 số, 30 giây, cửa sổ ±1). Đúng một factor enabled; cấu hình/method khác trả 503, không bỏ qua. Challenge hết hạn 5 phút, tối đa 5 lần thử. `POST /api/auth/2fa/resend` với `{"challengeId":"..."}` gửi lại Email OTP sau cooldown 60 giây, trả challenge mới và hủy challenge cũ; không resend TOTP. Hết hạn/hết lượt phải login lại. Fingerprint kiểm tra email/password/factor configuration; nếu đổi thì 409. LastLoginAt và audit thành công chỉ lưu sau khi đủ factor; lỗi persistence không cấp token. TOTP reuse được chặn trong instance hiện tại.

`POST /api/auth/register` vẫn tạo Owner bằng fullName/email/password/confirmPassword/phone; không cho chọn role. Đăng ký không tự cấp token. Mật khẩu tối thiểu 12 ký tự, confirmPassword khớp; email trùng 409.

### Google

Mở `/api/auth/google/start` bằng browser navigation. Google gọi `/api/auth/google/oidc-callback`; backend xác minh OIDC, gửi OTP và chuyển tới `/api/auth/google/callback`. Trang callback cung cấp challengeId, không có OTP/token trong URL. Gọi `/api/auth/google/otp/verify` với challengeId/code → 200 TokenDto; nếu tài khoản có 2FA enabled → 202 và tiếp tục `/api/auth/2fa/verify`. Google không bỏ qua 2FA tài khoản.

Giữ cookies **chỉ cho bước Google**: frontend fetch với `credentials: "include"`, `X-Vault-Request: 1`, origin được allowlist. Cookie tạm Secure/HttpOnly/SameSite=None. Dùng HTTPS; không gửi token qua URL. `/api/auth/google/otp/resend` trả challengeId mới, cooldown 60 giây. Không có redirect tùy ý hoặc refresh token. Chưa có UI frontend riêng; có thể dùng trang callback + Swagger cùng browser.

Khi FE sẵn sàng, cấu hình `Google:FrontendCallbackUrl` để trang callback redirect tới một URL HTTPS cố định trên CORS origin allowlist (HTTP chỉ loopback). URL không có query/fragment và không chứa token/OTP/challenge. FE tiếp tục lấy challenge qua endpoint JSON bên dưới. Chi tiết luồng, lỗi và giới hạn: [Backend-login.md](Backend-login.md).

Frontend có thể mở Google trong popup/browser navigation rồi gọi **GET `/api/auth/google/challenge`** với credentials include khi callback hoàn tất để lấy `{challengeId,expiresAt}` dạng JSON. Endpoint chỉ chấp nhận cookie của browser đã bắt đầu luồng; browser khác trả 400. Không cần parse trang HTML hoặc truyền JWT/OTP qua URL. Có rate limit 10/IP/phút; tránh poll liên tục. Sau đó hiển thị form OTP và POST verify để nhận token.

### Session JWT

```http
GET /api/auth/me
Authorization: Bearer JWT_VALUE
```

200: `{"userId":1,"fullName":"Owner","email":"owner@example.com","roles":["Owner"],"status":"Active"}`. Mỗi request Bearer kiểm tra User Active, khóa mật khẩu và role từ database; token cũ không giữ role bị thu hồi. API nghiệp vụ không dùng application cookie, không cần `X-Vault-Request`. Actor UserId lấy từ principal, không gửi ownerId/userId để giả lập.

Frontend: login → OTP/Google nếu cần → lưu accessToken trong bộ nhớ → gọi me → gắn Bearer cho API → gặp 401 thì bỏ token và đăng nhập lại. 403 là thiếu quyền/OTP, không đồng nghĩa token hết hạn. Không có refresh-token persistence nên **không phát refresh token**.

Đã đọc frontend hiện tại nhưng không sửa: `src/callapi/axiosClient.js` đang dựa vào withCredentials, `authApi.js` kiểm tra session bằng owner/vaults, `AuthContext.jsx` chỉ giữ userId/roles. Đội frontend cần:

- Nhận và giữ accessToken; interceptor gắn Authorization Bearer cho nghiệp vụ, không coi cookie là login.
- Xử lý 202 thành màn hình Email/TOTP, không đặt trạng thái logged-in trước khi có token.
- Đổi checkSession sang GET auth/me để nhận profile/roles thật; không dùng 403 owner/vaults để suy ra phiên hợp lệ.
- Giữ credentials include + X-Vault-Request riêng cho Google POST; base URL HTTPS và Cors origin khớp.
- Logout/401 xóa token và user; reload khi không có token phải login lại. Phân biệt mảng list cũ với PageDto mới.

`POST /api/auth/logout`, Bearer → 204: thu hồi OTP beneficiary của session; frontend phải bỏ token. **Không revoke JWT đã phát**: JWT vẫn dùng được đến hết hạn nếu User còn hợp lệ; không có denylist persistence. Có thể request lại beneficiary OTP nếu vẫn giữ JWT.

## 3. Endpoint nghiệp vụ

Các path dưới đây đều cần Bearer. Quyền được đọc lại từ DB; target ID không thay quyền actor.

### Owner

| Method | Path sau `/api/owner` | Request / kết quả |
| --- | --- | --- |
| POST | `/vaults` | `{name,description}` → 201 VaultDto + Location; description ≤500 |
| GET | `/vaults`, `/vaults/{id}` | Mảng / VaultDto của mình |
| PUT | `/vaults/{id}` | `{name,description}` → VaultDto |
| POST | `/vaults/{id}/close` | 204; dùng Status, không xóa |
| POST | `/assets` | `{vaultId,name,type,description}` → 201 AssetDto + Location |
| GET | `/assets`, `/assets/{id}` | Mảng / AssetDto; documents chỉ metadata Active |
| PUT | `/assets/{id}` | `{name,type,description}` → AssetDto; không chuyển vault |
| POST | `/assets/{id}/revoke` | 204, không xóa dữ liệu |
| POST | `/assets/{id}/beneficiaries` | `{email,allocation}` → 201 BeneficiaryDto |
| GET | `/beneficiaries?assetId=...` | Mảng BeneficiaryDto; assetId tùy chọn |
| GET | `/assets/{id}/allocations` | Mảng `{assignmentId,assetId,beneficiaryId,fullName,allocation,status}` |
| PUT | `/assets/{id}/allocations/{assignmentId}` | `{allocation}` → AssignmentDto |
| POST | `/assets/{id}/allocations/{assignmentId}/revoke` | AssignmentDto, Status Revoked |
| POST | `/assets/{id}/documents` | multipart bên dưới → DocumentDto |
| GET | `/assets/{id}/documents` | Metadata gồm status; không storage path |
| GET | `/assets/{id}/documents/{documentId}/content` | Bytes giải mã; kiểm tra ownership |
| POST | `/assets/{id}/documents/{documentId}/revoke` | 204, giữ bytes/hồ sơ |
| PUT | `/vaults/{id}/executor` | `{executorId}` → 204; Executor Active, độc lập |
| POST | `/vaults/{id}/handovers` | 201 HandoverDto; RequestType cố định Inheritance |
| GET | `/handovers?page=1&pageSize=20&status=Draft` | PageDto |
| GET | `/handovers/{requestId}` | HandoverDto của mình |
| POST | `/handovers/{requestId}/cancel` | 204, chỉ Draft hoặc Rejected |

Asset name ≤100, type ASCII ≤50, description ≤4000. Phân bổ >0, ≤100, tối đa 2 chữ số thập phân, tổng Active ≤100; recipient đã đăng ký và Active, không tự thụ hưởng/trùng phân bổ. Concurrency trên asset/vault chặn cập nhật vượt tổng.

Mọi mutation kho/tài sản/phân bổ/giấy tờ bị chặn khi có hồ sơ chưa Cancelled, kể cả Completed. Tạo bàn giao yêu cầu mỗi asset Active có giấy tờ và đúng 100% allocation; dùng Executor hợp lệ hiện tại hoặc random Executor Active/độc lập. Không có người phù hợp → 503, không dùng ID giả. Một hồ sơ đang xử lý/hoàn tất không được tạo lặp; Draft/Rejected có thể cancel để sửa tài liệu và tạo lại. Giữ lịch sử pháp lý, không xóa bản ghi.

### Executor

| Method | Path | Contract |
| --- | --- | --- |
| GET | `/api/executor/assignments` | Phân công Active/AutoAssigned của mình |
| POST | `/api/executor/assignments/{vaultId}/handovers` | Tạo Draft cho kho đã được phân công hợp lệ |
| POST | `/api/executor/handovers/{requestId}/death-certificate` | multipart File, Signature tùy chọn; chỉ Executor được phân công |
| GET | `/api/executor/handovers` | Mảng HandoverDto của mình; Active/AutoAssigned thống nhất |
| GET | `/api/executor/handovers/{requestId}` | HandoverDto được phân công |
| GET | `/api/executor/handovers/{requestId}/documents/{documentId}/content` | Bytes; X-Signature-Valid |
| POST | `/api/executor/handovers/{requestId}/documents` | multipart File, Signature tùy chọn → DocumentDto |
| POST | `/api/executor/handovers/{requestId}/legal-verifications` | multipart VerifierId, Comment, Evidence → `{verificationId}` |
| POST | `/api/executor/handovers/{requestId}/start` | 204, chỉ Approved → In_Progress |
| POST | `/api/executor/handovers/{requestId}/complete` | multipart File + Signature bắt buộc → 204 |

Không còn role/nghiệp vụ Staff. Chỉ Executor Active được phân công hợp lệ được upload giấy chứng tử/bằng chứng vào hồ sơ Draft/Rejected/Additional_Info. Giấy chứng tử bắt buộc trước khi nộp và duyệt pháp lý, không tự chứng minh tính hợp lệ hoặc hoàn tất bàn giao. Xem [contract upload và phân công](Executor-evidence-update.md).

### LegalVerifier

| Method | Path sau `/api/legal/handovers` | Kết quả |
| --- | --- | --- |
| GET | `?page=1&pageSize=20&status=Pending_Legal` | PageDto được giao |
| GET | `/{requestId}` | `{request:HandoverDto,verification:ReviewDto}` |
| GET | `/{requestId}/documents/{documentId}/content` | Nội dung evidence được phép |
| GET | `/{requestId}/asset-documents/{documentId}/content` | Nội dung giấy tờ asset thuộc hồ sơ |
| POST | `/{requestId}/approve` | `{comment}` → ReviewDto |
| POST | `/{requestId}/reject` | `{comment}` → ReviewDto |
| POST | `/{requestId}/request-additional-information` | `{comment}` → ReviewDto |

Comment bắt buộc, ≤2000. ReviewDto có verificationId/requestId/verifierId/status/comment/submittedAt/reviewedAt. Chỉ verifier được giao, độc lập Owner/Executor/beneficiary được xử lý. Pending_Legal → Approved/Rejected/Additional_Info. Evidence bổ sung phải được operator nộp lại trước review; không ghi đè quyết định đã chốt. Approve kiểm tra hồ sơ, cases, documents và giải mã integrity; giấy tờ Active của Owner còn sống phải còn chữ ký hợp lệ. Approve mở cases In_Progress và giấy tờ Pending_Legal thành Active.

### Beneficiary và notifications

| Method | Path | Contract |
| --- | --- | --- |
| POST | `/api/beneficiary/otp/request` | OtpChallenge; email từ DB |
| POST | `/api/beneficiary/otp/verify` | `{challengeId,code}` → `{verified,validForSeconds:900}` |
| GET | `/api/beneficiary/assets`, `/assets/{id}` cùng prefix | Mảng / InheritanceDto sau OTP và legal approval |
| GET | `/api/beneficiary/assets/{id}/documents/{documentId}/content` | Bytes chỉ của di sản mình |
| GET | `/api/beneficiary/handovers?page=1&pageSize=20` | PageDto CaseDto của mình |
| GET | `/api/beneficiary/handovers/{handoverId}` | CaseDto |
| GET | `/api/beneficiary/handovers/{handoverId}/documents` | Mảng DocumentDto |
| GET | `/api/beneficiary/handovers/{handoverId}/documents/{documentId}/content` | Bytes handover thuộc case mình |
| POST | `/api/beneficiary/handovers/{handoverId}/receipt` | `{note}` → ReceiptDto, chỉ Completed chưa nhận |
| GET | `/api/beneficiary/handovers/{handoverId}/receipt` | ReceiptDto của mình |
| GET | `/api/notifications?page=1&pageSize=20` | PageDto của User hiện tại |
| POST | `/api/notifications/{notificationId}/read` | 204, không đánh dấu hộ người khác |

ReceiptDto có receiptId/handoverId/beneficiaryId/status/note/confirmedAt. Confirm dùng BENEFICIARY_RECEIPTS và chuyển case Received atomically; lặp 409. Đây là xác nhận nhận tài sản, **không phải xác minh chữ ký số**. Di sản đã Received vẫn truy cập được. OTP beneficiary riêng, gắn JWT vault_session, không được thay bằng OTP Google/2FA. Metadata/thông báo/audit không chứa JWT/OTP/secret/file bytes/storage path.

## 4. Multipart, lỗi và pagination

Owner upload fields: **File**, **Signature** bắt buộc; **BeneficiaryId** tùy chọn, phải là phân bổ Active hiện có. Không gửi OwnerAlive/DeathCertificate. Giấy chứng tử dùng endpoint riêng của Executor và HANDOVER_DOCUMENTS. Mỗi file ≤10 MiB. MIME hỗ trợ PDF/PNG/JPEG/text UTF-8, kiểm tra nội dung/header thực tế; không tin MIME/filename. Filename được chuẩn hóa ở API; storage GUID nằm ngoài web root. Chữ ký rời RSA-SHA256 PKCS#1 v1.5, RSA ≥2048, cert tin cậy/chưa hết hạn; chưa hỗ trợ PDF embedded/CMS. AES-256-GCM mã hóa **tại backend**, không E2EE/client-side encryption. Download có Content-Disposition và X-Signature-Valid, không lộ đường dẫn.

PageDto: `{"page":1,"pageSize":20,"items":[]}`, page ≥1, pageSize 1–100; chưa trả total. Handover status filter nhận Draft/Cancelled/Pending_Legal/Additional_Info/Rejected/Approved/In_Progress/Completed. Các list cũ giữ mảng và chưa có pagination; frontend không tự giả định list đó là PageDto.

| Status | Ý nghĩa |
| --- | --- |
| 400 | Input/MIME/OTP/challenge không hợp lệ hoặc thiếu header Google |
| 401 | Thiếu/sai/hết hạn JWT hoặc User bị khóa/không Active |
| 403 | Sai role, xung đột quyền hoặc cần beneficiary OTP |
| 404 | Không tồn tại hoặc resource không thuộc phạm vi người gọi |
| 409 | Trùng, concurrent update, trạng thái không cho phép, allocation vượt tổng |
| 413 | File vượt 10 MiB |
| 429 | Rate limit/cooldown |
| 503 | Cấu hình/2FA/provider SMTP chưa hỗ trợ hoặc không có Executor phù hợp |
| 500 | Lỗi persistence/storage không dự kiến; không trả thành công giả |

Lỗi nghiệp vụ có `{status,detail}`; validation MVC có ValidationProblemDetails; lỗi Bearer có thể trả 401 không body. Không tự retry POST tạo hồ sơ khi 409; reload trước.

## 5. Giới hạn schema và kiểm chứng

Không có invitation/expiry/acceptance hoặc refresh-token persistence phù hợp: chỉ phân bổ cho tài khoản đã đăng ký/Active, không tạo user giả và không nhét token vào cột khác. Không migration/reset/seed test trên SQL Server thật.

OTP/challenge và TOTP replay tracking hiện trong bộ nhớ một instance; restart làm mất challenge/grant, deployment nhiều instance cần thiết kế shared store/sticky routing. JWT ký bằng khóa ổn định vẫn hợp lệ khi restart. Tài khoản Google nhận diện theo email, không lưu provider subject; đổi email không tự nối User cũ.

SELECT audit đã đối chiếu 25 bảng không thiếu cột; ExecutorId bắt buộc int, Description kho nvarchar(500). Đây chưa phải xác minh mọi kiểu dữ liệu/index/constraints hoặc concurrency SQL Server. Test dùng SQLite relational và external doubles, Google OIDC in-process không import chứng thư Windows. Google/SMTP thật và cert production chưa được kiểm chứng.

Chạy `dotnet run --project LegacyVault.Tests` (console harness, không dotnet test). Các luồng chính có [LegacyVault.http](../LegacyVault.http).

Build/test cuối: 0 lỗi/0 cảnh báo, **256 kiểm tra đạt**. Xem [báo cáo bàn giao](Backend-JWT-handover.md). Tham khảo thuật toán: [ASP.NET Core JWT validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication), [RFC 6238 TOTP](https://www.rfc-editor.org/rfc/rfc6238).
