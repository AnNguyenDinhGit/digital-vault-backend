# Hướng dẫn test toàn bộ API LegacyVault / DigitalVault

Cập nhật theo mã nguồn ngày 24/10/2026. Đây là tài liệu test tổng hợp: **95 operation trong controller**, callback OIDC và SignalR. Có thể thực hiện luồng chính chỉ với tài liệu này và Swagger, không cần đọc các hướng dẫn lịch sử. “Có endpoint” không đồng nghĩa mọi nhóm đã được test production; xem giới hạn ở cuối.

## 1. Chuẩn bị môi trường và Swagger

Backend .NET 8, API → BLL → DAL. Dùng SDK tương thích `global.json` hiện tại (`8.0.100`, rollForward `latestFeature`); AGENTS.md có mô tả SDK cũ. Console harness không phải xUnit/NUnit.

```powershell
dotnet restore LegacyVault.sln
dotnet build LegacyVault.sln --no-restore
dotnet run --project LegacyVault.Tests
dotnet run --project LegacyVault.API --launch-profile https
```

**Trước khi chạy API:** trỏ connection string sang database test riêng. `Program.cs` hiện gọi `EnsureCreatedAsync` và `DbInitializer.SeedAsync` khi khởi động; không khởi chạy với connection production để thử API. Không dùng tài khoản/dữ liệu thật cho các bước tạo user, đổi role, khóa user hoặc bàn giao.

Swagger: `https://localhost:7015/swagger`; OpenAPI: `https://localhost:7015/swagger/v1/swagger.json`. HTTP profile là `http://localhost:5131`. Có thể cần `dotnet dev-certs https --trust` trên máy phát triển. Trên server khác dùng base URL do môi trường đó cung cấp, không sửa localhost trong OAuth một cách tùy ý.

Đặt cấu hình qua environment hoặc User Secrets, không ghi secret vào Markdown/Git:

| Nhóm cấu hình | Nội dung cần chuẩn bị |
| --- | --- |
| `ConnectionStrings:LegacyVault` | SQL Server test, schema tương thích entity scaffold |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`, `Jwt:LifetimeMinutes` | Key ngẫu nhiên ít nhất 32 byte; lifetime 1–30 phút, mặc định 15 |
| `Security:EncryptionKey` | Base64 của key AES 32 byte, giữ ổn định để đọc file cũ |
| `Security:SignerCertificates:<id>` | Main hiện dùng **giá trị PEM đầu tiên** làm public key chung cho mọi user; hỗ trợ chuỗi newline escaped |
| `Security:TrustedRootCertificates:<index>` | Cấu hình có sẵn nhưng Verify hiện tại trên main không kiểm tra certificate chain |
| `Google:ClientId`, `Google:ClientSecret`, `Google:FrontendCallbackUrl` | OAuth client; frontend callback tùy chọn, URL cố định được cấu hình cho phép |
| `MailOptions:Host`, `Port`, `Username`, `Password`, `From` | SMTP gửi OTP; code cũng có fallback section `Mail` |
| `Cors:Origins:<index>` | Origin frontend cụ thể, không wildcard; CORS hỗ trợ Authorization/multipart |

Ví dụ biến environment dùng dấu `__`: `Jwt__SigningKey`, `MailOptions__Host`, `Cors__Origins__0`. Không copy secret vào request URL hoặc chụp màn hình chứa JWT/OTP/private key.

Chuẩn bị tài khoản test: Owner A, Owner B (cũng có thể là người thụ hưởng), Executor E1/E2, LegalVerifier L1/L2, Admin. Owner đăng ký công khai; role đặc quyền do Admin cấp bằng API có sẵn hoặc quản trị môi trường test cấp trước. Không có role Staff trong nghiệp vụ. Cần ít nhất một Admin test đã được cấp hợp lệ để test phần Admin.

## 2. Quy ước dùng chung

Trong tất cả bảng, đường dẫn là đầy đủ từ `/api`; thay `{...}` bằng ID thật, không giữ dấu ngoặc. POST không body được ghi “không body”. JSON dùng `Content-Type: application/json`; upload dùng multipart/form-data do Swagger tự tạo boundary.

API được bảo vệ cần `Authorization: Bearer <accessToken>`. Swagger Authorize: nhập token theo ô Bearer của UI; kiểm tra request sinh ra chỉ có một tiền tố `Bearer`. Khi đổi vai trò test, thay token bằng tài khoản tương ứng; cùng một tài khoản có thể nhiều role nhưng vẫn bị kiểm tra quyền trên tài nguyên.

API nghiệp vụ không dùng cookie để login. Chỉ luồng Google dùng cookie tạm; mọi POST dưới `/api/auth/google` cần **`X-Vault-Request: 1`**, cùng browser/cookie đã bắt đầu OIDC. Header này không thay JWT hoặc phân quyền. Swagger đã có interceptor cho luồng cookie.

Phân trang: `page=1`, `pageSize=20` mặc định; page 1–100000, pageSize 1–100. Response:

```json
{ "page": 1, "pageSize": 20, "items": [] }
```

Không có `totalCount`/`totalPages` trong PageDto hiện tại. Không đổi mọi list sang PageDto: bảng bên dưới ghi rõ các endpoint metadata/lịch sử vẫn trả mảng. Thời gian xác nhận còn sống là UTC (`Z`); frontend đổi sang giờ Việt Nam khi hiển thị.

Lưu riêng: `vaultId`, `assetId`, `assignmentId` (phân bổ), `executorId` (UserId), `requestId` (đơn), `handoverId` (case của beneficiary), `documentId`, `notificationId`. Không dùng lẫn `requestId` và `handoverId`. ID tài liệu tài sản khác ID tài liệu bàn giao dù có thể cùng giá trị số.

| HTTP | Cách hiểu khi test |
| --- | --- |
| 200 / 201 / 204 | Có response / tạo mới / không body response |
| 202 | Đang chờ 2FA, chưa được dùng API nghiệp vụ |
| 400 | Validation, MIME/chữ ký/OTP hoặc input sai; đọc detail/errors |
| 401 | Thiếu/sai/hết hạn JWT, tài khoản không khả dụng, refresh không hợp lệ |
| 403 | Thiếu role, beneficiary OTP chưa đạt hoặc phạm vi nghiệp vụ bị cấm |
| 404 | Không tồn tại hoặc không thuộc phạm vi được phép |
| 409 | Trùng, vượt phân bổ, sai trạng thái hoặc xung đột concurrent update |
| 413 | File vượt 10 MiB hoặc request vượt giới hạn server |
| 429 | Rate limit/cooldown; đợi rồi gửi lại, không gọi liên tục |
| 503 | Thiếu cấu hình/dịch vụ/chứng thực hỗ trợ hoặc không có Executor đủ điều kiện |
| 500 | Lỗi chưa xử lý/persistence; không coi là test pass, lưu lỗi đã che secret |

Workflow thường trả `{ "status": 409, "detail": "..." }`; lỗi model binding có thể là ValidationProblemDetails (`errors`), API mã hóa có thể trả chuỗi. Không giả định mọi lỗi có cùng shape.

## 3. Đăng ký, mật khẩu, JWT, refresh và 2FA

| Method | Endpoint | Input / kỳ vọng |
| --- | --- | --- |
| POST | `/api/auth/register` | JSON đăng ký bên dưới → 201 RegistrationDto, không tự login |
| POST | `/api/auth/login` | `{ "email": "owner.a@example.test", "password": "Registration123!" }` → 200 TokenDto hoặc 202 challenge |
| POST | `/api/auth/2fa/verify` | `{ "challengeId": "<32 ký tự>", "code": "<6 chữ số>" }` → 200 TokenDto |
| POST | `/api/auth/2fa/resend` | `{ "challengeId": "..." }` → 200 challenge mới; chỉ Email, không resend TOTP |
| GET | `/api/auth/me` | Bearer → 200 `{ userId, fullName, email, roles, status }` |
| POST | `/api/auth/refresh-token` | `{ "refreshToken": "<token hiện tại>" }` → 200 cặp token mới |
| POST | `/api/auth/logout` | Bearer, không body hoặc `{ "refreshToken": "..." }` → 204 |

Đăng ký:

```json
{
  "fullName": "Owner A",
  "email": "owner.a@example.test",
  "password": "Registration123!",
  "confirmPassword": "Registration123!",
  "phone": "0901234567"
}
```

fullName tối đa 100, email 150, password 12–1024 ký tự; confirmPassword phải khớp. Phone và identityNumber tùy chọn, tối đa 20 ký tự. Không gửi role/ownerId/userId. Email trùng → 409. **Đăng ký email/mật khẩu hiện tạo Owner trực tiếp, không có bước OTP đăng ký riêng cho endpoint này.** Không nhầm với OTP tạo tài khoản Google mới.

TokenDto gồm `userId`, `roles`, `accessToken`, `tokenType`, `expiresIn`, `refreshToken`, `refreshTokenExpiresAt`. Sau login thành công, copy accessToken vào Authorize rồi gọi me. JWT xác minh chữ ký/issuer/audience/expiry và tài khoản/role hiện tại; token cũ không giữ role đã thu hồi.

Nếu 202: response có `challengeId`, `method`, `expiresAt`, `requiresTwoFactor: true`; không có accessToken dùng nghiệp vụ. Với Email lấy OTP qua SMTP; với TOTP lấy từ authenticator đã đăng ký secret. Hiện hỗ trợ đúng một factor enabled với Method Email hoặc TOTP; phương thức khác/cấu hình không phù hợp báo 503, không bỏ qua. Chưa có controller enroll/bật/tắt 2FA; cần tài khoản test đã được cấu hình hợp lệ. Không tự thay Secret trong database thật để test.

Refresh có persistence, xoay token; token cũ bị từ chối khi dùng lại. Token refresh mới có hạn theo response (code hiện đặt 7 ngày). Logout với refreshToken thu hồi token đó; **accessToken chưa hết hạn không được tuyên bố đã revoke**. Client phải bỏ token; logout cũng thu hồi beneficiary OTP grant của session. Sau refresh cần kiểm tra lại beneficiary OTP vì accessToken mới có session mới.

Test lỗi: sai mật khẩu, 5 lần sai có thể khóa 15 phút; sai challenge/code, replay OTP, resend quá nhanh; dùng JWT sai issuer/audience/signature/hết hạn; Admin khóa hoặc thu hồi role rồi gọi lại bằng JWT cũ. Giữ tài khoản test phụ để tránh tự khóa tài khoản đang dùng cho toàn luồng.

## 4. Google OIDC và OTP tài khoản mới

| Method | Endpoint | Cách test |
| --- | --- | --- |
| GET | `/api/auth/google/start` | Mở bằng thanh địa chỉ browser → redirect Google chọn tài khoản |
| GET | `/api/auth/google/callback` | Trang kết quả sau OIDC; có thể redirect tới frontend callback cố định |
| GET | `/api/auth/google/challenge` | Cùng browser → `{ challengeId, expiresAt, requiresOtp }` |
| POST | `/api/auth/google/complete` | Header X-Vault-Request, `{ "challengeId": "..." }` cho email đã có tài khoản → 200/202 |
| POST | `/api/auth/google/otp/verify` | Header X-Vault-Request, `{ "challengeId": "...", "code": "..." }` cho email mới → 200/202 |
| POST | `/api/auth/google/otp/resend` | Header X-Vault-Request, `{ "challengeId": "..." }` → challenge gửi lại |
| GET | `/api/auth/google/error` | 400 thông báo lỗi/hủy Google; endpoint thông báo, không login |

Ngoài controller: `/api/auth/google/oidc-callback` do middleware xử lý. Đăng ký chính xác redirect URI HTTPS này trong Google OAuth client; không gọi tay để tạo token. OIDC dùng authorization code/PKCE và validation Google; không gửi email tùy ý để đăng nhập.

Thực hiện: mở start → chọn tài khoản/authorize → quay về callback → GET challenge trong browser đó. Nếu `requiresOtp=false`, POST complete, không gửi OTP đăng ký. Nếu `true`, nhận OTP email rồi POST otp/verify; resend tạo challenge mới, dùng challenge mới nhất. Challenge tạm có hạn ngắn (5 phút), cookie-bound, dùng một lần; browser khác/replay không được cấp token.

200 trả TokenDto như password login; 202 vẫn cần `/api/auth/2fa/verify` nếu tài khoản bật 2FA. Không truyền JWT/refresh/OTP vào URL. Tài khoản đặc quyền Admin/Executor/LegalVerifier không dùng public Google sign-in. Test Swagger phải giữ cookie browser; Postman khác cookie không thể tiếp quản challenge từ browser. Thiếu Google/SMTP cấu hình thì không tính test thật là pass.

## 5. Owner: kho và tài sản

Tất cả endpoint phần 5–8 cần Owner Active và sở hữu tài nguyên.

| Method | Endpoint | Input / response |
| --- | --- | --- |
| POST | `/api/owner/vaults` | `{ "name": "Kho A", "description": "Kho kiểm thử" }` → 201 VaultDto + Location |
| GET | `/api/owner/vaults` | page/pageSize → PageDto<VaultDto> |
| GET | `/api/owner/vaults/{vaultId}` | 200 VaultDto |
| PUT | `/api/owner/vaults/{vaultId}` | Cùng body tạo kho → 200 VaultDto |
| POST | `/api/owner/vaults/{vaultId}/close` | Không body → 204, đóng mềm |
| POST | `/api/owner/assets` | Body dưới → 201 AssetDto + Location |
| GET | `/api/owner/assets` | page/pageSize → PageDto<AssetDto> |
| GET | `/api/owner/assets/{assetId}` | 200 AssetDto |
| PUT | `/api/owner/assets/{assetId}` | name/type/description, không vaultId → 200 AssetDto |
| POST | `/api/owner/assets/{assetId}/revoke` | Không body → 204, thu hồi mềm |

Tạo tài sản:

```json
{ "vaultId": 123, "name": "Tài khoản tiết kiệm", "type": "BankAccount", "description": "Tài sản kiểm thử" }
```

Vault name tối đa 100, description 500. Asset name 100, type ASCII in được tối đa 50, description 4000. VaultDto: `vaultId,name,description,status`; AssetDto: `assetId,name,type,description,status,documents`. Không gửi OwnerAlive/DeathCertificate để tạo kho/tài sản.

Test CRUD/close/revoke trên kho riêng **chưa có hồ sơ đang bảo vệ**. Kho có đơn Pending_Executor/Draft/đang pháp lý/đã hoàn tất không được sửa tùy ý (409). Cancelled/Declined giải phóng gate nếu không còn đơn khác bảo vệ. Dùng Owner B truy cập/sửa/download tài sản A → 404, không thấy dữ liệu A trong list.

## 6. Owner: người thụ hưởng và phân bổ

| Method | Endpoint | Input / response |
| --- | --- | --- |
| GET | `/api/owner/beneficiaries` | assetId tùy chọn, page/pageSize → PageDto<BeneficiaryDto>; là danh sách phân bổ, không phải danh bạ mọi user |
| POST | `/api/owner/assets/{assetId}/beneficiaries` | `{ "email": "owner.b@example.test", "allocation": 100 }` → 201 BeneficiaryDto |
| GET | `/api/owner/assets/{assetId}/allocations` | page/pageSize → PageDto<AssignmentDto> |
| PUT | `/api/owner/assets/{assetId}/allocations/{assignmentId}` | `{ "allocation": 60 }` → 200 AssignmentDto |
| POST | `/api/owner/assets/{assetId}/allocations/{assignmentId}/revoke` | Không body → 200 AssignmentDto |

Đăng ký Owner B trước bằng email thật của môi trường test; A chọn B theo email. User được chọn vẫn giữ Owner role và được bổ sung Beneficiary theo nghiệp vụ. Không cho tự thụ hưởng, chọn user inactive, trùng phân bổ Active hoặc tổng Active >100%. Tỷ lệ từ 0.01 tới 100, tối đa 2 số thập phân. Lập hồ sơ bàn giao yêu cầu tổng Active **đúng 100% trên từng tài sản Active**.

AssignmentDto có `assignmentId,assetId,beneficiaryId,fullName,allocation,status`. Ghi lại assignmentId để PUT/revoke. Không có invitation cho người chưa đăng ký: email chưa có user không được tạo tài khoản giả, không tự gửi link mời. Test 60%+40% thành công, thêm 1% hoặc update vượt tổng → 409; 0 hoặc 0.001 → 400.

## 7. Upload, chữ ký và tài liệu Owner

Mỗi file nội dung tối đa **10 MiB = 10,485,760 byte**, không rỗng. Định dạng cho phép: application/pdf, image/png, image/jpeg, text/plain UTF-8. Backend kiểm tra header/nội dung tương ứng, không chỉ extension; không phải công cụ thẩm định pháp lý hay antivirus đầy đủ. Đổi đuôi file text thành PNG không làm file hợp lệ. Signature là **file nhị phân detached signature**, không phải ảnh chữ ký hoặc chuỗi base64.

| Method | Endpoint | Multipart / response |
| --- | --- | --- |
| POST | `/api/owner/assets/{assetId}/documents` | File + Signature bắt buộc, BeneficiaryId tùy chọn đã có phân bổ Active → 200 DocumentDto |
| GET | `/api/owner/assets/{assetId}/documents` | page/pageSize → PageDto<ManagedDocumentDto> gồm trạng thái |
| GET | `/api/owner/assets/{assetId}/documents/{documentId}/content` | Download nội dung sau kiểm tra ownership |
| POST | `/api/owner/assets/{assetId}/documents/{documentId}/revoke` | Không body → 204; file đã thu hồi không download được |

Owner chỉ gửi các multipart field trên; OwnerAlive/DeathCertificate/UploadedBy là không hợp lệ trong endpoint upload Owner. Không dùng endpoint Owner để nộp giấy chứng tử. DocumentDto có `documentId,fileName,fileType,isEncrypted,signatureValid,purpose` (hai trường cuối có thể null ở response upload cũ).

Theo main mới (`635b589`), Verify lấy **public PEM đầu tiên trong SignerCertificates làm key chung**, không chọn chứng thư theo signerId; không kiểm tra chain, hạn chứng thư, key usage hoặc ràng buộc chứng thư với user. Private key tương ứng do môi trường ký quản lý, không gửi trong upload. Cơ chế kiểm tra bytes vẫn là SHA-256 + RSA PKCS#1 v1.5, chữ ký từ key khác hoặc file bị sửa bị từ chối. Có thể tạo detached signature bằng private PEM tương ứng của môi trường test:

```text
openssl dgst -sha256 -sign owner-private.pem -out instructions.sig instructions.pdf
```

Upload chính xác bytes đã ký. Owner upload và Executor completion đều phải dùng chữ ký khớp **key chung** hiện tại; có quyền Executor không làm chữ ký từ key khác hợp lệ. JWT/role/phân công vẫn được backend kiểm tra riêng. Verify hiện không chứng minh danh tính pháp lý của người ký; không ghi “đã xác minh chứng thư riêng của Executor” từ cơ chế này. Cách hoạt động này đến từ main, không phải thay đổi bảo mật trong API Owner/revoke.

File được backend mã hóa AES-256-GCM khi lưu, giải mã khi download sau authorization; **không E2EE/client-side encryption**. Download trả binary theo MIME, Content-Disposition filename, Cache-Control no-store, X-Content-Type-Options nosniff, `X-Signature-Valid: true|false`. File không ký hợp lệ có thể false trong bằng chứng tùy chọn; không suy ra giấy tờ đã được pháp lý xác nhận từ header này. Không có storage path trong DTO.

## 8. Owner: chọn Executor, xác nhận còn sống, tạo/cancel đơn

| Method | Endpoint | Input / response |
| --- | --- | --- |
| GET | `/api/owner/executors` | page/pageSize, search tên ≤100, vaultId tùy chọn → PageDto `{userId,fullName}` |
| GET | `/api/owner/executors/{executorId}` | vaultId tùy chọn → `{userId,fullName,role,status,joinedAt}` |
| PUT | `/api/owner/vaults/{vaultId}/executor` | `{ "executorId": 15 }` → 204 |
| GET | `/api/owner/vaults/{vaultId}/executor` | 200 `{vaultId,executorId,fullName,status,assignedAt}`; ưu tiên phân công Active/AutoAssigned, nếu không có trả phân công gần nhất kể cả Revoked; chưa từng gán → 404 |
| POST | `/api/owner/vaults/{vaultId}/executor/revoke` | Không body → 204 **sau khi** lưu EXECUTOR_ASSIGNMENTS.Status = Revoked; chỉ trước tiếp nhận/xử lý |
| GET | `/api/owner/life-confirmation` | 200 `{ownerId,lastConfirmedAliveAt}`; null nếu chưa có record |
| POST | `/api/owner/life-confirmation` | Không body → 200 thời điểm server UTC đã lưu |
| POST | `/api/owner/vaults/{vaultId}/handovers` | Không body → 201 HandoverDto, status Pending_Executor |
| GET | `/api/owner/handovers` | page/pageSize/status → PageDto<HandoverDto> |
| GET | `/api/owner/handovers/{requestId}` | 200 HandoverDto |
| POST | `/api/owner/handovers/{requestId}/cancel` | Không body → 204, chỉ Pending_Executor/Draft/Rejected |

Dropdown chỉ trả Executor role Active, không khóa, không phải Owner; vaultId còn loại người thụ hưởng Active của kho sở hữu. Không có email/CCCD/phone trong public detail, chưa có profile chuyên môn/ảnh Executor. Frontend label=fullName, value=userId; tạo kho rồi PUT executor là **hai request riêng**. Nếu PUT thất bại kho vẫn tồn tại, không tạo lặp kho để retry. Gán phân công không đồng nghĩa tạo hồ sơ: Executor thấy kho trong assignments ngay, đơn chỉ xuất hiện khi Owner/Executor gọi API tạo handover lúc đủ điều kiện.

Lập đơn cần kho Active, tài sản Active có tài liệu Active và phân bổ đúng 100%, beneficiary Active. Giữ Executor chỉ định nếu hợp lệ; chưa chỉ định thì random đều bằng RandomNumberGenerator trong tập Executor đủ điều kiện, độc lập với Owner/beneficiary. Lưu assignment cùng request/cases/audit/notification, không random khi GET. Không ứng viên → 503, không tạo hồ sơ nửa chừng; retry tạo đơn đang tồn tại → 409. Không có fallback Staff.

Nút “Tôi vẫn còn sống” lưu PROOF_OF_LIFE và audit, giữ lịch hiện có; không ghi đè USERS.Status. Lần đầu mặc định 30 ngày, grace 14 ngày; chưa có job tự nhắc/đánh dấu mất. Không bấm không đồng nghĩa đã mất. Có giấy chứng tử đang xử lý hoặc hồ sơ pháp lý đã duyệt → POST 409, không hủy pháp lý; GET vẫn đọc lịch sử. Đây là lời tự xác nhận, không phải xác minh y tế/pháp lý.

HandoverDto: `requestId,vaultId,requestType,status,initiatedAt,documents,cases,owner,executorId,executorName,declineReason,declinedAt,submittedAt,completedAt`. Cases có `handoverId,assetId,beneficiaryId,status`; lưu handoverId dành cho bước receipt.

Ba API Owner POST tạo handover, GET list và GET detail đều trả `executorId`/`executorName` theo Executor gắn với **đơn đó**, không bị thay bằng người vừa được gán cho kho sau này. GET Owner list/detail có thêm `declineReason`/`declinedAt` cho Declined. Không thêm cột: reason đọc từ thông báo `Executor declined handover` của đúng Owner và mã đơn, thời gian từ audit `DeclineHandover`. Chưa từ chối hoặc dữ liệu cũ thiếu bằng chứng tương ứng thì null; không lấy comment Rejected của LegalVerifier làm lý do Executor từ chối. `declinedAt` trả UTC. POST tạo đơn mới chưa có quyết định nên hai trường này null.

Ví dụ các field trong item đơn bị từ chối:

```json
{
  "requestId": 321,
  "executorId": 15,
  "executorName": "Nguyen Van A",
  "status": "Declined",
  "declineReason": "Không thể tiếp nhận",
  "declinedAt": "2026-10-23T02:30:00Z"
}
```

GET executor theo vault cho Owner xem phân công hiện tại và trạng thái thu hồi. `fullName` lấy từ USERS; `status`/`assignedAt` lấy từ EXECUTOR_ASSIGNMENTS, không phải trạng thái tài khoản. Không có phân công Active/AutoAssigned thì lấy lịch sử gần nhất (assignedAt, rồi assignment ID), có thể Revoked; frontend không hiển thị Revoked thành người đang giữ kho.

Revoke: kho Active và chưa có đơn được nhận/xử lý; chưa tạo đơn thì thu hồi phân công, có Pending_Executor thì cancel đơn chờ cùng các case và revoke trong cùng lần lưu. Draft đã tiếp nhận/Rejected/Additional_Info/Pending_Legal/Approved/In_Progress/Completed bị chặn 409. Phân công đang Active/AutoAssigned mới bị chuyển sang **Revoked**, không xóa lịch sử; gửi thông báo Executor và audit. Không có phân công còn hiệu lực → 404, nhiều phân công hiệu lực bất thường → 409. Owner khác không được GET/revoke (404). Sau 204 GET phải thấy Revoked nếu chưa gán người mới; Executor cũ không thể accept đơn đã bị hủy. Không trả 204 khi persistence thất bại.

## 9. Executor: màn hình đơn và tiếp nhận

| Method | Endpoint | Input / response |
| --- | --- | --- |
| GET | `/api/executor/assignments` | page/pageSize → PageDto gồm vaultId, executorId, status, assignedAt, vaultName, owner |
| POST | `/api/executor/assignments/{vaultId}/handovers` | Không body → 201 Draft nếu tự khởi tạo từ phân công hợp lệ, chưa có đơn bảo vệ |
| GET | `/api/executor/handovers` | page/pageSize → PageDto đơn của chính Executor; giữ lịch sử Declined |
| GET | `/api/executor/handovers/overview` | page/pageSize/status/ownerId/assignedOnly → PageDto tổng quan |
| GET | `/api/executor/handovers/completed` | page/pageSize → PageDto<HandoverOverviewDto> mọi đơn Completed; Executor role, tương tự overview filter Completed |
| GET | `/api/executor/handovers/{requestId}` | Chi tiết đơn mình được giao |
| POST | `/api/executor/handovers/{requestId}/accept` | Không body → 200 HandoverDto status Draft |
| POST | `/api/executor/handovers/{requestId}/decline` | `{ "reason": "Không thể tiếp nhận" }` → 200 status Declined |
| GET | `/api/executor/handovers/{requestId}/owner-status` | Tình trạng Owner có nguồn bằng chứng + lastConfirmedAliveAt |
| GET | `/api/executor/handovers/{requestId}/history` | Mảng lịch sử xác minh pháp lý |
| GET | `/api/executor/handovers/{requestId}/legal-verifiers` | Mảng `{userId,fullName}` người xác minh hợp lệ |

Màn hình tất cả đơn dùng overview mặc định `assignedOnly=false`: trả Owner `{userId,fullName}`, vaultName, Executor, trạng thái/thời gian, `isAssignedToMe`; không trả file/contact/danh sách case riêng tư. Inbox chờ quyết định dùng `?assignedOnly=true&status=Pending_Executor`. Quyền tổng quan không cho nhận/từ chối/đọc chi tiết đơn của người khác.

Chỉ Pending_Executor được accept/decline. Accept kiểm tra lại điều kiện, không bypass pháp lý. Decline bắt buộc reason không trắng ≤2000, đóng case Cancelled, thu hồi assignment, lưu audit và thông báo cho Owner cùng mã đơn/lý do. Owner đọc lý do trong notifications, chọn Executor khác rồi tạo **đơn mới**. Executor giữ quyền đọc lịch sử Declined của chính đơn, không sửa/upload/accept lại. Hai quyết định cạnh tranh không cùng commit, bên thua 409; gọi lặp 409.

Hồ sơ cũ Draft được coi đã vào bước chuẩn bị, không chuyển ngược. Executor tự POST tạo Draft từ phân công đã có cũng bao gồm nhận việc; không accept lần nữa. Không truyền ExecutorId/UploadedBy để tự nhận đơn.

Owner status: NotConfirmed (không đủ bằng chứng, không có nghĩa còn sống), ReportedDeceased (đã nộp), Rejected (khai báo bị pháp lý từ chối), ConfirmedDeceased (đã duyệt). Có `evidenceRequestId,deathCertificateDocumentId,verifierId,verifiedAt,lastConfirmedAliveAt`. Bằng chứng từ đơn khác cùng Owner có thể được dẫn nguồn; không tự duyệt đơn mới.

## 10. Executor: bằng chứng, gửi pháp lý và hoàn tất

| Method | Endpoint | Input / response |
| --- | --- | --- |
| POST | `/api/executor/handovers/{requestId}/death-certificate` | Multipart File bắt buộc, Signature tùy chọn → 200 DocumentDto |
| POST | `/api/executor/handovers/{requestId}/documents` | Multipart File bắt buộc, Signature tùy chọn → 200 DocumentDto |
| GET | `/api/executor/handovers/{requestId}/documents` | Mảng DocumentDto metadata, purpose phân biệt DeathCertificate |
| GET | `/api/executor/handovers/{requestId}/documents/{documentId}/content` | Download file bàn giao có quyền |
| POST | `/api/executor/handovers/{requestId}/legal-verifications` | Multipart VerifierId + Evidence bắt buộc, Comment ≤2000 tùy chọn → 200 `{verificationId}` |
| POST | `/api/executor/handovers/{requestId}/start` | Không body → 204, Approved sang In_Progress |
| POST | `/api/executor/handovers/{requestId}/complete` | Multipart File + Signature Executor bắt buộc → 204, Completed |

Chỉ Executor được phân công có quyền nộp giấy chứng tử. Owner/Beneficiary/LegalVerifier/Staff không được nộp qua bất kỳ endpoint nghiệp vụ nào. Backend dùng UserId từ principal; không tin field định danh client. Mỗi file áp dụng giới hạn ở phần 7.

Upload và gửi pháp lý chỉ ở Draft/Rejected/Additional_Info. Pending_Executor phải accept trước; Pending_Legal/Approved/In_Progress/Completed/Declined không sửa chứng cứ. Chọn verifier từ legal-verifiers, không phải nhập tên; người xác minh phải độc lập, Active, đúng role.

Trình tự: upload death-certificate → chọn verifier → POST legal-verifications với **Evidence**, không phải File → trạng thái Pending_Legal. Nộp giấy chứng tử không tự xác nhận đã mất và không hoàn tất đơn. RequestLegal tạo review Pending và thông báo người thẩm định. start/complete cần pháp lý Approved; code hiện cho complete từ Approved hoặc In_Progress, nên test chuẩn vẫn start trước. Executor được phân công nộp file complete có chữ ký hợp lệ theo key chung; completion ghi case Completed và thông báo beneficiary.

## 11. LegalVerifier: xem hồ sơ và quyết định

Tất cả endpoint chỉ cho người xác minh được giao, không phải mọi user có LegalVerifier role; không xung đột Owner/Executor/beneficiary.

| Method | Endpoint | Input / response |
| --- | --- | --- |
| GET | `/api/legal/handovers` | page/pageSize/status → PageDto<HandoverDto> được giao |
| GET | `/api/legal/handovers/{requestId}` | 200 `{ request: HandoverDto, verification: ReviewDto }` |
| GET | `/api/legal/handovers/{requestId}/assets` | page/pageSize → PageDto<LegalAssetDto> tài sản thuộc case trong hồ sơ được giao, kèm metadata tài liệu chưa thu hồi |
| GET | `/api/legal/handovers/{requestId}/owner-status` | OwnerLifeStatusDto có nguồn bằng chứng |
| GET | `/api/legal/handovers/{requestId}/history` | Mảng review các lần nộp/bổ sung/duyệt |
| GET | `/api/legal/handovers/{requestId}/documents` | Mảng metadata bằng chứng |
| GET | `/api/legal/handovers/{requestId}/documents/{documentId}/content` | Download bằng chứng HANDOVER_DOCUMENTS |
| GET | `/api/legal/handovers/{requestId}/asset-documents/{documentId}/content` | Download tài liệu tài sản có trong case hợp lệ |
| POST | `/api/legal/handovers/{requestId}/approve` | `{ "comment": "Hồ sơ đủ điều kiện" }` → 200 ReviewDto |
| POST | `/api/legal/handovers/{requestId}/reject` | `{ "comment": "Bằng chứng chưa đủ" }` → 200 ReviewDto |
| POST | `/api/legal/handovers/{requestId}/request-additional-information` | `{ "comment": "Cần bổ sung giấy tờ" }` → 200 ReviewDto |

Comment bắt buộc không trắng, tối đa 2000. Review chỉ khi request Pending_Legal và review Pending của mình. Approve cần giấy chứng tử đúng nguồn Executor, phân công còn hiệu lực, kho/tài sản/phân bổ/thụ hưởng hợp lệ và tài liệu Owner có chữ ký hợp lệ. File bị sửa/tài liệu thiếu không được duyệt. Lưu verifier/comment/review time; cạnh tranh review trả 409, không ghi một phần trạng thái/audit.

### Danh sách tài sản để hiển thị cho pháp lý

```http
GET /api/legal/handovers/123/assets?page=1&pageSize=20
Authorization: Bearer <legalVerifierToken>
```

Response 200:

```json
{
  "page": 1,
  "pageSize": 20,
  "items": [
    {
      "assetId": 45,
      "name": "Tài khoản tiết kiệm",
      "type": "BankAccount",
      "description": "Tài sản trong hồ sơ bàn giao",
      "status": "Active",
      "documents": [
        {
          "documentId": 67,
          "fileName": "account.pdf",
          "fileType": "application/pdf",
          "isEncrypted": true,
          "status": "Active"
        }
      ]
    }
  ]
}
```

Chỉ trả tài sản có trong HANDOVER_CASES của đúng hồ sơ và đúng vault. Một tài sản có nhiều người thụ hưởng vẫn chỉ xuất hiện một lần. Sắp theo assetId tăng dần, page mặc định 1, pageSize mặc định 20, tối đa 100; trang hết dữ liệu trả items rỗng. Đây là phân trang trong BLL trên graph hồ sơ đã đọc qua repository, không tuyên bố đã tối ưu SQL pagination riêng cho endpoint này.

Metadata tài liệu dùng đúng điều kiện download hiện tại: loại Status=Revoked, giữ các trạng thái chưa thu hồi và hiển thị status để người thẩm định biết. Không tự ẩn tài sản nằm trong case chỉ vì trạng thái của tài sản đã đổi; không lấy tài sản khác cùng vault nhưng ngoài case. Documents có thể rỗng; không trả storage path, encryption key hoặc nội dung file trong API list. Metadata không đồng nghĩa chữ ký đã được kiểm chứng: tải/đọc bằng GET `/api/legal/handovers/{requestId}/asset-documents/{documentId}/content` có quyền để kiểm tra nội dung và header chữ ký hiện có.

Test: dùng token LegalVerifier được giao, kiểm tra tên/type/description/status/documentId; dùng hai asset và một asset có hai beneficiary để kiểm tra không trùng qua các trang. Thu hồi một tài liệu trên fixture trước tạo đơn thì list không được hiển thị tài liệu đó, download trả 404. Asset ngoài case cùng vault không xuất hiện. Không token → 401; sai role/xung đột lợi ích → 403; người xác minh không được giao hoặc request không tồn tại → 404; page=0, pageSize=0/101 → 400. Có thể đọc sau khi đã quyết định pháp lý theo quyền lịch sử hiện có, không chỉ giới hạn Pending_Legal.

ReviewDto: `verificationId,requestId,verifierId,status,comment,submittedAt,reviewedAt`. LegalVerifier được xem/tải, **không có endpoint upload**. Additional_Info: Executor xem comment/lịch sử, upload bổ sung, gửi legal-verifications lại; review mới Pending trước khi duyệt. Rejected pháp lý khác Declined tiếp nhận. Không gửi JSON status tùy ý.

## 12. Beneficiary: OTP, di sản và nhận bàn giao

| Method | Endpoint | Input / response |
| --- | --- | --- |
| POST | `/api/beneficiary/otp/request` | Bearer Beneficiary, không body → OtpChallenge có challengeId/expiry |
| POST | `/api/beneficiary/otp/verify` | `{ "challengeId": "<32 ký tự>", "code": "<6 chữ số>" }` → `{verified:true,validForSeconds:900}` |
| GET | `/api/beneficiary/assets` | page/pageSize → PageDto<InheritanceDto> sau OTP |
| GET | `/api/beneficiary/assets/{assetId}` | 200 InheritanceDto của mình |
| GET | `/api/beneficiary/assets/{assetId}/documents/{documentId}/content` | Download tài liệu tài sản được thừa kế |
| GET | `/api/beneficiary/handovers` | page/pageSize → PageDto<CaseDto> của mình |
| GET | `/api/beneficiary/handovers/{handoverId}` | 200 CaseDto |
| GET | `/api/beneficiary/handovers/{handoverId}/documents` | Mảng metadata tài liệu bàn giao |
| GET | `/api/beneficiary/handovers/{handoverId}/documents/{documentId}/content` | Download bằng chứng bàn giao khi đủ quyền |
| POST | `/api/beneficiary/handovers/{handoverId}/receipt` | `{ "note": "Đã nhận" }`, note tùy chọn ≤2000 → ReceiptDto |
| GET | `/api/beneficiary/handovers/{handoverId}/receipt` | 200 receipt đã lưu; chưa có → 404 |

Login B bằng tài khoản đã được A phân bổ; sau đó request OTP và lấy mã SMTP. OTP này khác OTP đăng nhập/Google; cần trên session Bearer hiện tại. Grant truy cập có hạn 15 phút; sai/expired/replay bị từ chối. Chưa pháp lý Approved thì list không tự mở tài sản chỉ vì được chỉ định.

InheritanceDto: `handoverId,allocation,asset`; CaseDto: `handoverId,assetId,beneficiaryId,status`. Dùng **handoverId của case**, không requestId. Receipt chỉ sau request/case hoàn tất, không xác nhận thay người khác, không xác nhận lặp (409). ReceiptDto: `receiptId,handoverId,beneficiaryId,status,note,confirmedAt`. Receipt là ghi nhận nhận tài sản, không tuyên bố đã kiểm chứng chữ ký số của beneficiary.

## 13. Admin: cấp tài khoản, role, trạng thái và audit

| Method | Endpoint | Input / response |
| --- | --- | --- |
| GET | `/api/admin/users` | page/pageSize/role/status → PageDto<UserAdminDto> |
| POST | `/api/admin/users` | Body tạo user bên dưới → 201 UserAdminDto |
| PUT | `/api/admin/users/{userId}/roles` | `{ "roles": ["Executor"] }` → 204, thay toàn bộ roles |
| PUT | `/api/admin/users/{userId}/status` | `{ "status": "Disabled" }` → 204; cho Active/Disabled/Locked |
| GET | `/api/admin/audit-logs` | page/pageSize/action → PageDto<AuditLogDto> |

Tạo Executor trên **database test** bằng JWT Admin đã có:

```json
{
  "fullName": "Executor E1",
  "email": "executor.e1@example.test",
  "password": "Registration123!",
  "phone": "0901234567",
  "roles": ["Executor"]
}
```

fullName ≤100, email ≤150, password 12–1024, phone/identityNumber tùy chọn ≤20; không có confirmPassword trong Admin create. Lặp với LegalVerifier L1 bằng role LegalVerifier. Role phải có sẵn, đúng tên; **code Admin hiện bỏ qua tên role không tìm thấy**, vì vậy đọc roles trong response sau create/update, không coi 2xx là bằng chứng role mong muốn đã được cấp. Không test tự xóa role Admin duy nhất hoặc tự khóa Admin đang thao tác.

UserAdminDto có email/phone/identityNumber/status/roles/timestamps chỉ dành Admin, không password hash. AuditDto có logId/userId/userEmail/action/entityType/entityId/result/ipaddress/timestamp. Xem audit sau accept/decline, legal review, receipt, life-confirmation. Staff cũ trong DB không có quyền nghiệp vụ; không tự xóa/chuyển dữ liệu Staff bằng các bước test này.

## 14. Thông báo

| Method | Endpoint | Input / response |
| --- | --- | --- |
| GET | `/api/notifications` | Bearer bất kỳ tài khoản hợp lệ, page/pageSize → PageDto của chính mình |
| POST | `/api/notifications/{notificationId}/read` | Không body → 204, chỉ notification của mình |

DTO có `notificationId,type,title,message,isRead,createdAt`. Kiểm tra Executor nhận phân công, Owner nhận quyết định tiếp nhận/lý do từ chối, các bên nhận cập nhật pháp lý/hoàn tất/receipt. Dùng token tài khoản khác đánh dấu notification của mình → 404. Thông báo lưu database không đồng nghĩa SMTP đã được gửi cho mọi sự kiện; SMTP OTP là luồng riêng.

## 15. Chat REST và SignalR — có giới hạn hiện tại

| Method | Endpoint | Input / response |
| --- | --- | --- |
| GET | `/api/handovers/{requestId}/messages` | Executor/LegalVerifier, page/pageSize → PageDto<MessageDto> |
| POST | `/api/handovers/{requestId}/messages` | `{ "content": "Cần bổ sung giấy tờ", "messageType": "Text" }` → 200 MessageDto |

Content bắt buộc 1–4000 ký tự; dùng Text cho messageType. MessageDto có `messageId,conversationId,senderId,senderName,senderRole,content,messageType,sentAt`. GET sắp mới nhất trước; lần đầu GET có thể tạo Conversation nếu chưa có. POST lưu DB rồi broadcast ReceiveMessage; lỗi broadcast sau save có thể khiến client thấy lỗi dù tin đã lưu, nên kiểm tra GET trước khi gửi lại trùng.

**Giới hạn đã đọc từ ChatService:** role cho phép Executor/LegalVerifier nhưng assignment hiện chỉ so `HandoverRequest.ExecutorId == userId`; chưa nối phân công LegalVerifications cho LegalVerifier, chưa kiểm tra live assignment/status request như workflow. Vì vậy L1 được giao review vẫn có thể nhận 403, còn Executor cũ có thể còn được truy cập chat theo ExecutorId. Ghi nhận là lỗi còn tồn tại, không sửa role để vượt qua hoặc đánh dấu chat hai chiều đã pass.

Hub `/hubs/chat`, không phải REST và không có form Try it out trong Swagger. SignalR client kết nối bằng JWT; sau negotiate cần gọi JoinHandoverChat để vào group và nghe ReceiveMessage; rời bằng LeaveHandoverChat. Ví dụ khởi tạo listener với @microsoft/signalr trên môi trường test:

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl(baseUrl + "/hubs/chat", { accessTokenFactory: () => accessToken })
  .build();
connection.on("ReceiveMessage", message => console.log(message));
await connection.start();
// Cần join group thành công trước khi kiểm tra nhận broadcast.
// Gửi tin qua REST POST /api/handovers/{requestId}/messages sau khi join.
```

Chữ ký method hiện là `JoinHandoverChat(int requestId, CancellationToken ct)`, còn leave là `LeaveHandoverChat(int requestId)`. CancellationToken công khai có thể ảnh hưởng binding; chưa có mẫu invoke join được kiểm chứng cho client JavaScript hiện tại. Nếu lỗi argument/binding, ghi nhận riêng, không báo realtime pass từ REST 200. Kiểm tra hai connection thật và quyền group. Chỉ negotiate được không chứng minh đã nhận broadcast.

## 16. Mã hóa trường dữ liệu tài khoản

| Method | Endpoint | Input / response |
| --- | --- | --- |
| POST | `/api/account-security/encrypt-field` | Bearer hợp lệ, `{ "plainText": "Du lieu kiem thu" }` → `{cipherText:"<base64>"}` |
| POST | `/api/account-security/decrypt-field` | Bearer hợp lệ, `{ "cipherText": "<kết quả vừa nhận>" }` → `{plainText:"Du lieu kiem thu"}` |

Test round-trip, plaintext rỗng → 400; sửa ciphertext/base64 sai → 400. Endpoint dùng AES-GCM theo cấu hình backend; không có ID tài nguyên hoặc policy Owner riêng. Đây là tiện ích encrypt/decrypt được authorize chung, **không coi là cơ chế phân quyền dữ liệu giữa người dùng**. Code có fallback SHA-256 khi key config không phải Base64; test chuẩn vẫn phải dùng key Base64 32 byte và không thay key để giả lập persistence. Không dùng dữ liệu nhạy cảm thật để thử.

## 17. Kịch bản xuyên suốt và các nhánh bắt buộc

### Luồng thành công

1. Chuẩn bị database test, SMTP và public key chung/chữ ký tương ứng theo main hiện tại. Có Admin/E1/L1 hợp lệ; đăng ký A/B, login A và B, lưu token riêng.
2. A GET/POST life-confirmation, kiểm tra thời gian đã lưu. A lấy dropdown, xem detail E1, tạo kho và PUT gán E1. E1 GET assignments thấy tên kho/Owner.
3. A tạo asset, phân bổ B 100%, upload File + chữ ký Owner. Kiểm tra list/detail/download. Lưu assetId/assignmentId/documentId.
4. A POST tạo handover → Pending_Executor. E1 xem inbox/detail, accept → Draft. Chưa accept thử upload phải 409.
5. E1 upload death-certificate → owner-status ReportedDeceased, không ConfirmedDeceased. A bấm life-confirmation lúc này phải 409.
6. E1 lấy legal-verifiers, gửi VerifierId L1 + Evidence. L1 thấy đơn Pending_Legal, xem nội dung asset/bằng chứng, approve với comment.
7. Owner-status ConfirmedDeceased có reviewer/time/nguồn bằng chứng. E1 start, complete với File + chữ ký khớp public key chung của môi trường. JWT gửi request phải là E1 được phân công, dù Verify chữ ký không phân biệt user bằng chứng thư.
8. B request/verify beneficiary OTP, xem tài sản/case/tài liệu; sau Completed POST receipt một lần, GET đọc lại. Kiểm tra notification và audit bằng đúng vai trò.

### Nhánh riêng trên hồ sơ test khác

- **Decline:** tạo đơn mới, E1 decline có reason → Declined, case Cancelled, assignment Revoked; A nhận thông báo, gán E2 và tạo đơn mới. Lịch sử cũ còn nguyên. Accept sau decline hoặc decline sau accept → 409.
- **Owner đọc quyết định/revoke:** kiểm tra executorName ngay POST tạo đơn; sau decline, GET list/detail phải có cùng reason/time đã lưu. Sau gán E2, đơn cũ vẫn chỉ E1; GET executor theo vault chỉ E2. Trên kho test khác, revoke trước nhận đơn → 204, query test DB thấy Status=Revoked, request/case chờ thành Cancelled; thử revoke sau accept phải 409. Không có reason/audit cũ thì null, không bịa dữ liệu.
- **Bổ sung:** L1 request-additional-information → Additional_Info; E1 đọc comment, upload bổ sung, gửi legal-verifications lại rồi L1 mới approve.
- **Từ chối pháp lý:** L1 reject → Rejected; A có thể cancel để sửa hồ sơ hoặc E1 bổ sung/nộp lại theo state machine. Không dùng endpoint decline tiếp nhận để thay quyết định pháp lý.
- **Không chỉ định:** A tạo đơn khi đủ điều kiện mà không PUT executor; kiểm tra random đúng role/Active/không khóa/độc lập, assignment lưu một lần. Không ứng viên → 503, DB không có nửa hồ sơ.
- **Close/revoke:** dùng kho riêng chưa bàn giao để test đóng kho, thu hồi asset/allocation/document. Không làm hỏng fixture luồng thành công.
- **Refresh/logout:** refresh → dùng token mới gọi me, token refresh cũ không replay; logout với refresh mới, thử refresh lại phải thất bại.
- **Google:** thử email cũ không OTP đăng ký, email mới có OTP, tài khoản bật 2FA vẫn cần factor; browser khác và challenge replay bị chặn.

State machine chính: `Pending_Executor → Draft → Pending_Legal → Approved → In_Progress → Completed`; nhánh tiếp nhận `Pending_Executor → Declined`; nhánh pháp lý `Pending_Legal → Additional_Info/Rejected`. Chỉ endpoint chuyên biệt được chuyển trạng thái, không PUT status tự do.

## 18. Checklist lỗi, coverage và giới hạn kiểm chứng

- [ ] Mọi list phân trang đọc items, chuyển page không trùng dữ liệu; 0/101 pageSize không hợp lệ; list metadata mảng không bị parse thành PageDto.
- [ ] Không token/sai role/Owner khác/Executor khác/Legal khác/beneficiary khác đều bị chặn theo 401/403/404.
- [ ] File >10 MiB, rỗng, MIME sai, chữ ký sai key/bytes bị sửa, download trái phép và tài liệu revoked không được thành công. Không kỳ vọng main hiện tại tự chặn chứng thư hết hạn hoặc signerId không được map riêng.
- [ ] Pending_Executor chưa upload; accept/decline lặp và cạnh tranh không thay hồ sơ đã xử lý; fail persistence không báo success.
- [ ] Tổng phân bổ >100%, hơn 2 chữ số thập phân, beneficiary inactive/trùng/tự thụ hưởng bị chặn.
- [ ] Bổ sung/nộp lại/approve/reject đúng trạng thái; file bị sửa không được duyệt; không bỏ qua pháp lý khi complete.
- [ ] Receipt trước Completed, receipt lặp hoặc nhận thay người khác bị chặn.
- [ ] OTP/challenge/code không replay, không cấp token trước khi đủ yếu tố; khóa/role thu hồi ảnh hưởng JWT đang còn hạn.
- [ ] Swagger JSON trả 200, không trùng method+route; CORS chỉ origin cấu hình; download expose headers cần thiết.
- [ ] Admin role sai/tên role không tồn tại được ghi nhận đúng; chat LegalVerifier và realtime được ghi nhận theo giới hạn thực tế.

Sau pull/integrate main `635b589`, build thành công và console harness **531 kiểm tra đạt**. Fixture và assertion chữ ký được cập nhật công khai theo contract key chung của main, vẫn kiểm tra chữ ký sai key/bytes bị sửa; code Verify production được giữ nguyên. Có HTTP Swagger JSON, completed overview mới của main, JWT/Google/2FA, SQL truy vấn lý do từ chối, SQLite relational rollback và cạnh tranh revoke/accept qua nhiều DbContext. Full build có 3 warning có sẵn (mailSender chưa dùng, using trùng); lần build tăng dần cuối không có lỗi. **Không đồng nghĩa 95 endpoint đã được test thủ công hay mọi nhóm Admin/chat/encryption có integration coverage đầy đủ.** Không gọi Google/SMTP/SQL Server thật trong các đợt test cô lập này.

Bổ sung API danh sách tài sản LegalVerifier ngày 24/10/2026: build thành công (0 lỗi, 3 warning có sẵn), console harness **554 kiểm tra đạt**. Test HTTP/SQLite bao gồm đúng/sai role, không được phân công, request không tồn tại, page không hợp lệ, hai trang riêng biệt, nhiều case cùng một asset, asset ngoài case, metadata tài liệu và tài liệu bị thu hồi. Swagger JSON cũng được gọi thực tế qua TestServer; không khởi động Program.cs để seed database thật.

Chưa xác minh schema/concurrency SQL Server thật, chứng thư production, Google/SMTP thật và realtime hai phía. Hai status mới Pending_Executor/Declined vừa varchar(20) theo scaffold; nếu database thật có CHECK constraint giới hạn enum thì cần review tương thích trước deploy, không tự migration theo hướng dẫn này. OTP/challenge tạm nằm trong memory của instance; restart/multi-instance cần đánh giá vận hành, không coi như đã có distributed store.

Chưa có API public invitation người chưa đăng ký, enroll 2FA, reset/change password hoặc update profile dù có DTO/service rời rạc; không tự suy ra endpoint từ tên class. Swagger/OpenAPI và `/hubs/chat/negotiate` là endpoint framework, không tính vào 95 controller operations. Không có API Staff/intake Staff.

Request mẫu tiện chạy: `LegacyVault.http` ở root; tài liệu này vẫn là hướng dẫn đầy đủ độc lập. Khi thêm/xóa endpoint, cập nhật bảng tương ứng và đối chiếu toàn bộ controller/Swagger để không thiếu operation.
