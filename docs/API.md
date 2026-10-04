# LegacyVault API

Đã đọc `Screenshot 2026-10-04 133536.png`. Luồng code: controller API → service BLL → repository/storage DAL. Controller không truy vấn DbContext; DAL không quyết định nghiệp vụ. Entities và mapping database-first được giữ nguyên; cấu hình concurrency dùng partial DbContext.

## Chạy và cấu hình

```powershell
dotnet build LegacyVault.sln
dotnet run --project LegacyVault.Tests
dotnet run --project LegacyVault.API --launch-profile https
```

Đặt secrets bằng User Secrets hoặc biến môi trường. Không đưa mật khẩu, khóa mã hóa hay khóa riêng vào Git.

| Cấu hình | Giá trị cần cung cấp |
| --- | --- |
| `ConnectionStrings__LegacyVault` | Connection string SQL Server |
| `Security__EncryptionKey` | Khóa AES 32 byte được encode base64; giữ ổn định để đọc lại file |
| `Security__SignerCertificates__<userId>` | Chứng thư X.509 PEM của người ký, đã được quản trị viên đối chiếu danh tính |
| `Security__TrustedRootCertificates__0` | Tùy chọn: chứng thư CA gốc PEM của PKI nội bộ; bỏ trống để dùng trust store hệ điều hành |
| `Mail__Host`, `Mail__Port` | SMTP có TLS, mặc định cổng 587 |
| `Mail__Username`, `Mail__Password`, `Mail__From` | Tài khoản SMTP và địa chỉ gửi |

File được lưu ở `LegacyVault.API/App_Data/documents`, ngoài static web root, tên ngẫu nhiên; nội dung, metadata gốc và chữ ký nằm trong envelope AES-256-GCM. Khóa cookie được lưu tại `App_Data/auth-keys`, bảo vệ bằng DPAPI trên Windows. Thư mục App_Data đã được bỏ qua trong Git. Cần sao lưu cả file và khóa mã hóa; bảo vệ quyền truy cập thư mục khóa cookie khi triển khai trên hệ điều hành khác. Phiên bản hiện tại dùng một khóa cấu hình, chưa có luân chuyển khóa.

RoleName đã đối chiếu với database: `Owner`, `Executor`, `Beneficiary`, `LegalVerifier`, `Admin`. User và assignment phải có `Status = Active`. Không seed user, không chạy migration hay thay đổi schema tự động. Đăng ký dùng lại role Owner, hoặc tạo role mặc định này nếu database chưa có, trong cùng transaction tạo user.

## Xác thực

### Đăng ký tài khoản

`POST /api/auth/register` không yêu cầu đăng nhập, nhận JSON:

```json
{
  "fullName": "Nguyen Van A",
  "email": "owner@example.test",
  "password": "Registration123!",
  "confirmPassword": "Registration123!",
  "phone": "0901234567"
}
```

Request chỉ nhận 5 trường fullName, email, password, confirmPassword, phone; phone tùy chọn. Không có trường role trên Swagger. Mật khẩu dài 12–1.024 ký tự, confirmPassword phải khớp. fullName tối đa 100, email 150, phone tối đa 20 ký tự. Email được trim và lưu chữ thường, kiểm tra trùng không phân biệt hoa/thường. Server luôn gán `Owner` cho tài khoản đăng ký công khai; dùng lại role này hoặc tạo trong transaction nếu thiếu. Gửi thêm role hoặc trường ngoài hợp đồng trả 400.

Thành công trả **201** với userId, fullName, email và roles. User, Authentication, liên kết UserRole và role Owner mới (nếu cần) được lưu trong cùng transaction; lỗi sẽ rollback. Mật khẩu được hash PBKDF2-SHA256 600.000 vòng với salt ngẫu nhiên. Email trùng (kể cả khi đăng ký đồng thời) trả 409; input không hợp lệ trả 400. Swagger tự thêm `X-Vault-Request: 1`. Endpoint dùng chung giới hạn 10 request/IP/phút với login.

Đăng ký không tự login, tạo vault/tài sản/assignment hoặc xác minh email. Sau khi nhận 201, gọi `/api/auth/login` với email và mật khẩu vừa đăng ký. Tài khoản executor, pháp lý và admin được cấp email/mật khẩu và role riêng qua quy trình quản trị, không dùng đăng ký công khai. Hiện chưa có API cấp tài khoản nội bộ. Người thụ hưởng đăng ký bình thường với role Owner; khi được chủ sở hữu chọn bằng email, server cấp thêm Beneficiary và tạo assignment cho tài sản đó, giữ nguyên các role hiện có.

Hướng mở rộng sau này (chưa triển khai): bấm nút Google → chọn tài khoản → gửi OTP tới email Google đã xác thực → nhập OTP hợp lệ → tạo tài khoản mới và đăng nhập. Không tạo tài khoản trước khi OTP được xác nhận; tài khoản mới vẫn nhận quyền mặc định do server gán. API hiện tại vẫn đăng ký bằng email/mật khẩu và chưa có đăng nhập Google hoặc OTP đăng ký; OTP beneficiary hiện chỉ dùng để mở quyền xem tài sản thừa kế.

### Test bằng Swagger

Chạy `dotnet run --project LegacyVault.API --launch-profile https`, rồi mở **https://localhost:7015/swagger**. Swagger chỉ bật trong môi trường Development. Chọn `POST /api/auth/login` → **Try it out**, nhập email/password của user trong database → **Execute**. Trình duyệt lưu cookie và tự dùng cookie này cho những request tiếp theo; không cần nút Authorize hoặc nhập token. Swagger tự thêm header `X-Vault-Request: 1`.

Sau khi đăng nhập, chọn API đúng role → Try it out → Execute. Endpoint multipart có nút chọn file cho giấy tờ, chữ ký rời hoặc evidence. Beneficiary phải gọi `/api/beneficiary/otp/request` và `/api/beneficiary/otp/verify` trước khi xem tài sản. Dùng `/api/auth/logout` trước khi đổi tài khoản. Nếu HTTPS localhost chưa được tin cậy, chạy `dotnet dev-certs https --trust`. Swagger mở được khi chưa có database; các API đọc/ghi dữ liệu vẫn cần cấu hình và tài khoản thật.

`POST /api/auth/login` nhận JSON `{ "email": "...", "password": "..." }`, trả thông tin user/roles và cookie HTTPS `LegacyVault.Session`. Cookie có HttpOnly, SameSite=Strict, thời hạn 30 phút. UserId lấy từ cookie đã ký, không lấy từ body/query/header của người gọi. Quyền và trạng thái user được kiểm tra lại từ database khi xử lý nghiệp vụ.

Hash mật khẩu được hỗ trợ: `PBKDF2-SHA256$iterations$base64Salt$base64Hash`, 100.000–1.000.000 vòng, salt tối thiểu 16 byte, hash 32 byte. API đăng ký tạo hash đúng định dạng này. Chưa có endpoint reset mật khẩu. Hash có định dạng khác sẽ bị từ chối; cần tích hợp verifier tương ứng khi đã xác định định dạng đang dùng. Sau 5 lần sai mật khẩu, tài khoản khóa 15 phút.

`POST /api/auth/logout` xóa cookie và thu hồi quyền OTP của phiên hiện tại. Mọi POST phải có header `X-Vault-Request: 1`, kể cả login. Header tùy chỉnh cùng SameSite=Strict ngăn submit form từ site khác; hiện chưa bật CORS. Dùng cùng origin hoặc proxy cùng origin cho frontend. Không truyền userId để giả lập đăng nhập.

## Chủ sở hữu

Luồng tạo dữ liệu từ tài khoản mới: đăng ký → đăng nhập → `POST /api/owner/vaults` → `POST /api/owner/assets` → GET danh sách/chi tiết tài sản. Xem [hướng dẫn đầy đủ với JSON mẫu](Luong-chinh-dang-ky-tao-assets.md).

Tạo kho nhận JSON `{ "name": "Kho tai san", "description": "..." }`, trả 201 với vaultId và Location. Tạo tài sản nhận `{ "vaultId": 10, "name": "Tai khoan", "type": "BankAccount", "description": "..." }`, trả 201 với assetId và Location. Name tối đa 100, description tùy chọn tối đa 4.000; type là chuỗi ASCII bắt buộc tối đa 50. OwnerId và status do server xác định; tài sản chỉ được tạo trong kho Active thuộc user. Chưa áp dụng hạn mức subscription hoặc tự gán beneficiary khi tạo tài sản.

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/owner/vaults` | Tạo kho của user đang đăng nhập |
| GET | `/api/owner/vaults` | Danh sách kho của user |
| GET | `/api/owner/vaults/{vaultId}` | Chi tiết kho thuộc user |
| POST | `/api/owner/assets` | Tạo tài sản trong kho Active của user |
| GET | `/api/owner/assets` | Danh sách tài sản của user đang đăng nhập |
| GET | `/api/owner/assets/{assetId}` | Chi tiết tài sản và metadata giấy tờ |
| POST | `/api/owner/assets/{assetId}/beneficiaries` | Chỉ định tài khoản đã đăng ký bằng email, cấp thêm Beneficiary |
| GET | `/api/owner/beneficiaries?assetId=10` | Danh sách người thụ hưởng; bỏ assetId để lấy trên mọi tài sản thuộc user |
| POST | `/api/owner/assets/{assetId}/documents` | Upload file mã hóa và chọn người thụ hưởng |

Chỉ định người thụ hưởng dùng JSON `{"email":"beneficiary@example.com","allocation":100}`; trả 201 với `beneficiaryId`. Chỉ chủ tài sản được chỉ định, kho và tài sản phải Active. Email phải thuộc user Active (404 nếu không có); không chọn chính mình (400), không trùng assignment Active (409). Tỷ lệ 0,01–100, tối đa hai chữ số thập phân; tổng assignment Active không vượt 100% (409). Hiện chưa có API sửa/xóa phân bổ hoặc mời email chưa đăng ký. Chỉ định không tự mở tài sản; vẫn cần điều kiện bàn giao và OTP. Xem [luồng hai tài khoản](Luong-chinh-dang-ky-tao-assets.md#6-chọn-người-thụ-hưởng-bằng-email).

Upload dùng `multipart/form-data`:

| Field | Ý nghĩa |
| --- | --- |
| `file` | File giấy tờ, bắt buộc |
| `beneficiaryId` | User có role Beneficiary, bắt buộc |
| `ownerAlive` | Boolean bắt buộc, không có giá trị mặc định ngầm |
| `signature` | File chữ ký rời dạng binary, bắt buộc nếu ownerAlive=true |
| `deathCertificate` | Giấy chứng tử, bắt buộc nếu ownerAlive=false |

Nhánh còn sống kiểm tra chữ ký của chủ sở hữu. Nhánh đã mất lưu cả file và giấy chứng tử với trạng thái `Pending_Legal`; lựa chọn này chỉ là khai báo, không chứng minh người đó đã mất và không tự mở tài sản. Theo phạm vi API chủ sở hữu được yêu cầu, endpoint vẫn cần phiên đăng nhập chủ sở hữu. Việc nộp chứng cứ tử vong thay mặt chủ sở hữu thuộc endpoint gửi xác minh pháp lý của người thi hành.

Nếu tài sản chưa có người thụ hưởng đang hoạt động, tạo assignment 100%. Nếu đã có, phải chọn một người trong danh sách hiện tại; không tự thay đổi phân bổ hoặc ghi đè người khác. File giấy tờ có thể tối đa 10 MB mỗi file; hỗ trợ PDF, PNG, JPEG, text/plain và kiểm tra header nội dung tương ứng.

## Người thi hành

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/executor/handovers` | Danh sách đơn của executor đang có assignment Active |
| GET | `/api/executor/handovers/{requestId}` | Chi tiết đơn, cases, documents và SignatureValid của từng file |
| GET | `/api/executor/handovers/{requestId}/documents/{documentId}/content` | Toàn bộ bytes file sau giải mã |
| POST | `/api/executor/handovers/{requestId}/complete` | Xác nhận hoàn thành bằng file có chữ ký executor |
| POST | `/api/executor/handovers/{requestId}/legal-verifications` | Gửi chứng cứ và yêu cầu xác minh pháp lý |

Endpoint content trả Content-Type, filename và header `X-Signature-Valid: true/false`. Frontend dùng bytes để hiển thị/tải file; không có trích xuất OCR/text từ PDF trong backend. Chi tiết đơn kiểm tra lại chữ ký của file đang lưu; file chứng cứ không được ký trả false.

Complete nhận multipart fields `file`, `signature`. Chỉ nhận đơn `Approved` hoặc `In_Progress`, có ít nhất một LegalVerification `Approved` và có handover cases; lưu file ký rồi cập nhật đơn/cases sang `Completed` trong cùng SaveChanges. Gọi lại trả 409. Không nhận chữ ký của người khác.

Legal verification nhận multipart fields `verifierId`, `comment` (tối đa 2.000 ký tự), `evidence` (bắt buộc). Verifier phải có role LegalVerifier, khác executor và owner. Chỉ nhận đơn `Draft` hoặc `Rejected`; tạo verification `Pending`, lưu evidence mã hóa và cập nhật đơn thành `Pending_Legal`. Duyệt/từ chối pháp lý và tạo đơn mới chưa thuộc API được yêu cầu trong lượt này.

## Người thụ hưởng

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/beneficiary/otp/request` | Gửi OTP tới email user, trả challengeId và expiresAt; không trả OTP |
| POST | `/api/beneficiary/otp/verify` | JSON `{ "challengeId": "...", "code": "123456" }` |
| GET | `/api/beneficiary/assets` | Danh sách tài sản được mở bàn giao sau OTP |
| GET | `/api/beneficiary/assets/{assetId}` | Chi tiết tài sản được thừa kế và tỷ lệ phân bổ |
| GET | `/api/beneficiary/assets/{assetId}/documents/{documentId}/content` | File giấy tờ Active, sau kiểm tra OTP và assignment |

OTP hết hạn sau 5 phút, tối đa 5 lần thử, gửi lại cách nhau ít nhất 60 giây. OTP chỉ dùng một lần, gắn với user và phiên đăng nhập; xác minh cho phép xem tài sản trong 15 phút. Đổi phiên hoặc logout phải xác minh lại. Endpoint login/OTP có giới hạn 10 request/IP/phút.

Tài sản chỉ được trả khi beneficiary assignment Active, có handover case In_Progress/Completed và yêu cầu tương ứng có xác minh pháp lý Approved. Giấy tờ Pending_Legal chưa được tải bởi beneficiary. OTP chứng minh quyền truy cập email, không phải eKYC/đối chiếu giấy tờ tùy thân.

## Chữ ký và giới hạn triển khai

Chữ ký được hỗ trợ là RSA-SHA256 PKCS#1 v1.5, key tối thiểu 2048 bit, ký trên toàn bộ bytes nguyên bản của file; trường signature gửi binary signature, không phải base64 text. Chứng thư của signer lấy từ cấu hình quản trị, không nhận chứng thư tự khai báo trong upload. Kiểm tra hạn chứng thư, DigitalSignature key usage nếu có, chain trust và revocation online. Không hỗ trợ chữ ký PDF nhúng PAdES, CMS/.p7s hoặc XMLDSig ở phiên bản này.

OTP/challenge/grant hiện lưu trong RAM của một process; restart làm mất trạng thái. Chạy nhiều instance cần thay bằng kho dùng chung và rate limiter phân tán. File cũ từ database chưa được chuyển sang envelope mới sẽ chưa đọc được bằng storage này; cần chuyển đổi dữ liệu riêng sau khi đã xác định format cũ. Danh sách hiện chưa phân trang.

Mã lỗi: 400 input/chữ ký/OTP không hợp lệ; 401 chưa đăng nhập; 403 thiếu role/OTP; 404 không tìm thấy hoặc không có quyền tài nguyên; 409 sai trạng thái/xung đột đồng thời; 429 giới hạn request; 503 thiếu cấu hình mã hóa/email. Không trả entities hoặc đường dẫn lưu trữ/khóa/hash mật khẩu trong DTO.

## Kiểm chứng

`LegacyVault.Tests` là executable kiểm tra nghiệp vụ, không dùng xUnit và không chạy qua `dotnet test`. Chạy bằng `dotnet run --project LegacyVault.Tests`. Kiểm tra AES round trip/tampering, RSA signature, OTP, phân quyền, upload và chuyển trạng thái. Có thêm test qua repository EF thật trên SQLite in-memory để kiểm tra đăng ký, các khóa ngoại, USER_ROLES, tái sử dụng role và rollback; SMTP/storage của test nghiệp vụ vẫn dùng test doubles. Chưa kiểm thử SMTP thật hoặc toàn bộ hành vi lock/deadlock của SQL Server.

Đã đối chiếu role và tên cột của 25 bảng với SQL Server đang cấu hình bằng SELECT chỉ đọc. Chạy lại bằng `dotnet run --project LegacyVault.Tests -- --database-audit`. Xem [phân tích entities và kiểm thử](Entities-analysis.md).

`database/schema.sql` hiện chỉ có cấu hình database, không có CREATE TABLE. Không chạy script này để tạo schema. Cần database đã scaffold đúng entities hiện tại và đối chiếu role/status/check constraints trước kiểm thử tích hợp. Không mở connection hay đổi schema ở startup.
