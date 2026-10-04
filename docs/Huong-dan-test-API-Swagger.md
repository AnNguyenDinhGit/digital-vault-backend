# LEGACYVAULT

## Hướng dẫn test API bằng Swagger

Tài liệu dành cho người kiểm thử • Ngày 04/10/2026

Thực hiện trực tiếp trên Swagger UI của backend hiện tại. API giữ kiến trúc API → BLL → DAL. Các bước POST có thể ghi dữ liệu; dùng database phát triển/test và dữ liệu có thể tạo lại.

## 1. Bắt đầu nhanh

Để chạy từ tài khoản chưa có dữ liệu, xem [luồng đăng ký → đăng nhập → tạo kho → tạo tài sản](Luong-chinh-dang-ky-tao-assets.md). Đã có POST `/api/owner/vaults` và POST `/api/owner/assets`, không cần tạo hai bản ghi này thủ công trong database.

1. Mở terminal tại D:\SWP\Project\digital-vault-backend.

```powershell
dotnet build LegacyVault.sln
dotnet run --project LegacyVault.API --launch-profile https
```

2. Mở trình duyệt: https://localhost:7015/swagger

3. Mở nhóm Auth, chọn POST /api/auth/login → Try it out → nhập email/password thật → Execute.

4. Đọc Server response, kiểm tra HTTP status và Response body. Sau đăng nhập, chọn API theo vai trò và tiếp tục Try it out → Execute.

5. Khi đổi tài khoản, gọi POST /api/auth/logout trước, sau đó login tài khoản mới. Swagger tự gửi cookie và header X-Vault-Request: 1; không cần nhập token hoặc nút Authorize.

### Dữ liệu cần chuẩn bị trước khi test

| Đối tượng | Điều kiện |
| --- | --- |
| Chủ sở hữu | User Active, role Owner; vault và tài sản Active, thuộc user này. |
| Người thi hành | User Active, role Executor; ExecutorAssignment Active cho vault; có HandoverRequest được gán cho user. |
| Người thụ hưởng | User Active, role Beneficiary; email nhận OTP được; có assignment Active. |
| Người xác minh | User Active, role LegalVerifier; khác chủ sở hữu và executor. |
| Đơn để gửi pháp lý | Có một đơn Draft hoặc Rejected. |
| Đơn để hoàn thành | Có một đơn Approved/In_Progress, ít nhất một LegalVerification Approved và có HandoverCase. |

Không có tài khoản/ID mẫu được seed sẵn. Mọi email, mật khẩu và ID trong tài liệu là ví dụ; thay bằng dữ liệu thật của database test. Có thể lấy assetId/requestId/documentId từ API danh sách/chi tiết; beneficiaryId lấy khi chỉ định bằng email hoặc xem danh sách người thụ hưởng; verifierId do người chuẩn bị dữ liệu test cung cấp.

## 2. Cấu hình và đăng nhập

Đặt cấu hình bằng User Secrets hoặc biến môi trường trước khi chạy API. Chỉ cấu hình mục cần cho chức năng đang test; đọc tài sản cần SQL, upload cần khóa mã hóa, luồng ký cần chứng thư và OTP cần SMTP.

| Tên cấu hình | Giá trị |
| --- | --- |
| ConnectionStrings__LegacyVault | Connection string SQL Server của database test. |
| Security__EncryptionKey | Base64 của khóa AES 32 byte; giữ nguyên để giải mã file đã lưu. |
| Security__SignerCertificates__<userId> | Chứng thư X.509 PEM của chủ sở hữu/executor tương ứng. |
| Security__TrustedRootCertificates__0 | Tùy chọn: CA gốc PEM cho PKI nội bộ; nếu bỏ trống dùng trust store hệ điều hành. |
| Mail__Host / Mail__Port | SMTP có TLS; port mặc định 587. |
| Mail__Username / Mail__Password / Mail__From | Tài khoản SMTP, mật khẩu và email gửi. |

Ví dụ User Secrets cho kết nối (thay placeholder, không chép mật khẩu thật vào tài liệu):

```powershell
dotnet user-secrets set "ConnectionStrings:LegacyVault" "<connection-string-test>" --project LegacyVault.API
```

Nếu trình duyệt báo chứng chỉ HTTPS chưa được tin cậy:

```powershell
dotnet dev-certs https --trust
```

### Test đăng ký tài khoản

Chọn `POST /api/auth/register` → **Try it out**, nhập JSON rồi **Execute**:

```json
{
  "fullName": "Nguyen Van A",
  "email": "owner@example.test",
  "password": "Registration123!",
  "confirmPassword": "Registration123!",
  "phone": "0901234567"
}
```

Thay email bằng địa chỉ chưa có trong database test. Chỉ gửi fullName, email, password, confirmPassword, phone; phone có thể bỏ trống. Mật khẩu tối thiểu 12, tối đa 1.024 ký tự; confirmPassword phải khớp. Không có trường chọn role: server tự gán Owner. Server dùng lại role Owner trong ROLES, hoặc tạo trong transaction đăng ký nếu chưa có. Executor, pháp lý và admin dùng tài khoản/mật khẩu được cấp riêng, không tự đăng ký quyền qua endpoint này.

Kỳ vọng: **201**, body có userId, fullName, email và roles. Sau đó gọi `/api/auth/login` bằng email/password vừa đăng ký. Đăng ký không tự tạo cookie đăng nhập, vault, tài sản hay assignment. Dùng tài khoản mới có thể nhận danh sách `[]` cho đến khi dữ liệu liên quan được chuẩn bị.

Ca lỗi: đăng ký email đã dùng (kể cả khác hoa/thường) trả **409**; mật khẩu dưới 12 ký tự, xác nhận không khớp, email sai hoặc gửi thêm trường role trả **400**. Endpoint dùng chung giới hạn 10 request/IP/phút với login; vượt giới hạn trả **429**. Swagger tự thêm header `X-Vault-Request: 1`.

Google + OTP là luồng dự kiến cho giai đoạn sau, chưa có endpoint để test. Hiện đăng ký bằng email/mật khẩu tạo tài khoản ngay khi thành công, không yêu cầu OTP đăng ký. OTP beneficiary vẫn chỉ dùng để xác minh quyền truy cập tài sản thừa kế.

### Test đăng nhập và đăng xuất

```json
POST /api/auth/login
{
  "email": "owner@example.test",
  "password": "<mat-khau-tai-khoan-test>"
}
```

Kỳ vọng: 200, body có userId và roles. Browser lưu cookie LegacyVault.Session và tự gửi khi test các API khác cùng origin HTTPS. Không dùng http://localhost:5131 để test đăng nhập vì cookie có Secure.

POST /api/auth/logout → Try it out → Execute. Kỳ vọng: 204, không có body. Gọi API yêu cầu đăng nhập sau logout phải trả 401.

### Điều kiện mật khẩu và file ký

PasswordHash hiện chỉ hỗ trợ PBKDF2-SHA256$iterations$base64Salt$base64Hash: 100.000–1.000.000 vòng, salt ≥16 byte, hash 32 byte. API đăng ký tạo hash đúng định dạng với 600.000 vòng. Không nhập chuỗi hash vào ô password. Chưa có API reset mật khẩu. Sai mật khẩu 5 lần có thể khóa tài khoản 15 phút.

Signature là file binary chữ ký rời RSA-SHA256 PKCS#1 v1.5, khóa RSA ≥2048 bit, ký trên đúng toàn bộ bytes file upload. Người chuẩn bị dữ liệu cung cấp file và chữ ký tương ứng, đồng thời cấu hình chứng thư người ký. Không dùng file .p7s/CMS hoặc chỉ một PDF có chữ ký nhúng; backend hiện chưa hỗ trợ các định dạng đó. Sửa file sau khi ký sẽ làm chữ ký không hợp lệ.

## 3. Test chủ sở hữu — Owner

Đăng nhập tài khoản role Owner. Dùng assetId thuộc tài khoản này; không dùng ID ngẫu nhiên để kiểm tra ca thành công.

### O1. Danh sách và chi tiết tài sản

```text
GET /api/owner/assets
```

Try it out → Execute. Kỳ vọng: 200, mảng tài sản. Mảng [] có thể hợp lệ nếu chủ sở hữu chưa có tài sản. Lưu assetId của một tài sản để dùng tiếp.

```text
GET /api/owner/assets/{assetId}
```

Nhập assetId vừa lấy. Kỳ vọng: 200, có assetId, name, type, description, status và documents. documents là metadata, không chứa bytes file hoặc đường dẫn lưu trữ.

### O2. Chọn và xem người thụ hưởng

Đăng ký tài khoản B bằng API đăng ký thông thường, sau đó login lại tài khoản A sở hữu tài sản. Gọi `POST /api/owner/assets/{assetId}/beneficiaries` với assetId của A:

```json
{
  "email": "beneficiary@example.com",
  "allocation": 100
}
```

Thay email bằng tài khoản B đã đăng ký và Active. Kỳ vọng 201; B giữ Owner và nhận thêm Beneficiary. Response có beneficiaryId để dùng khi upload giấy tờ. Nếu có hai người, chỉ định 60% và 40%; tổng không vượt 100%, tối đa hai chữ số thập phân. Không được chọn chính mình hoặc thêm trùng người trên cùng tài sản. Hiện chưa có API sửa/xóa phân bổ hoặc gửi lời mời cho email chưa đăng ký.

```text
GET /api/owner/beneficiaries
```

Nhập assetId để lọc một tài sản, hoặc bỏ trống để xem trên mọi tài sản của chủ sở hữu. Kỳ vọng: 200, mảng có assetId, beneficiaryId, fullName, allocation, status. Ghi lại một beneficiaryId Active nếu tài sản đã có assignment.

### O3. Upload khi chủ sở hữu còn sống

```text
POST /api/owner/assets/{assetId}/documents
```

Try it out; giao diện multipart có các trường viết hoa đầu như File, Signature, DeathCertificate, BeneficiaryId, OwnerAlive. Điền như bảng, sau đó Execute.

| Trường | Dữ liệu |
| --- | --- |
| assetId | Tài sản Active trong vault Active của chủ sở hữu. |
| File | Chọn PDF/PNG/JPEG/text; dung lượng 1 byte–10 MB. |
| BeneficiaryId | User Beneficiary Active. Nếu đã có assignment Active, chọn một người đang được gán. |
| OwnerAlive | true |
| Signature | Chữ ký rời của chính chủ sở hữu, khớp File. |
| DeathCertificate | Không gửi file. |

Kỳ vọng: 200, body có documentId, fileName, fileType, isEncrypted=true. File được mã hóa khi lưu; chưa có beneficiary Active thì tạo assignment 100%. Gọi lại O1/O2 để kiểm tra metadata và assignment. signatureValid của response upload có thể null; kiểm tra chữ ký đã được thực hiện trước khi lưu.

### O4. Upload nhánh khai báo đã mất

Dùng cùng endpoint, chọn OwnerAlive=false, File và DeathCertificate là hai file hợp lệ; Signature không bắt buộc. Kỳ vọng: 200; lưu hai giấy tờ mã hóa với trạng thái Pending_Legal. Nếu cần xác nhận trạng thái này, kiểm tra database test bằng truy vấn chỉ đọc; DTO giấy tờ không trả trường status.

Endpoint này vẫn yêu cầu phiên chủ sở hữu; chỉ là nhánh khai báo tài liệu, không tự xác minh tử vong hoặc mở bàn giao. Executor nộp chứng cứ thay mặt chủ sở hữu qua E4. Giấy tờ Pending_Legal chưa tải được bằng API beneficiary.

## 4. Test người thi hành — Executor

Logout tài khoản trước, login tài khoản role Executor có assignment Active. Chuẩn bị riêng đơn Draft/Rejected cho E4 và đơn Approved/In_Progress cho E5; chưa có API duyệt pháp lý hoặc tạo đơn để nối tự động hai ca này.

### E1. Danh sách đơn

```text
GET /api/executor/handovers
```

Kỳ vọng: 200, mảng đơn được gán cho executor. Lưu requestId của đơn muốn test. Nếu [] thì đối chiếu dữ liệu đơn và trạng thái ExecutorAssignment.

### E2. Chi tiết đơn và chữ ký

```text
GET /api/executor/handovers/{requestId}
```

Kỳ vọng: 200, có requestId, vaultId, requestType, status, initiatedAt, documents, cases. documents có documentId và signatureValid. Chữ ký được kiểm tra lại trên file đã lưu; file ký hợp lệ trả true, chứng cứ không có chữ ký trả false. Lưu documentId để dùng E3.

### E3. Đọc đầy đủ nội dung file

```text
GET /api/executor/handovers/{requestId}/documents/{documentId}/content
```

Nhập requestId/documentId cùng đơn. Kỳ vọng: 200 và toàn bộ bytes file gốc sau giải mã; kiểm tra Content-Type và X-Signature-Valid trong Response headers. Tùy phiên bản Swagger, dùng Download file hoặc mở đường dẫn endpoint trong cùng trình duyệt đã login; browser có thể tải file thay vì hiển thị inline. Mở file tải về bằng ứng dụng tương ứng và đối chiếu nội dung với bản gốc.

### E4. Gửi yêu cầu xác minh pháp lý

```text
POST /api/executor/handovers/{requestId}/legal-verifications
```

| Trường | Dữ liệu |
| --- | --- |
| requestId | Đơn của executor, trạng thái Draft hoặc Rejected. |
| VerifierId | User LegalVerifier Active; khác executor và chủ sở hữu. |
| Comment | Ví dụ: Đề nghị xác minh giấy chứng tử; tối đa 2.000 ký tự. |
| Evidence | Chọn file chứng cứ hợp lệ, tối đa 10 MB. |

Kỳ vọng: 200, body có verificationId. Gọi E2 thấy đơn đổi sang Pending_Legal và có metadata file mới. Evidence chưa ký có signatureValid=false, đây là kết quả bình thường. Gửi lại cùng đơn ngay sẽ trả 409.

### E5. Xác nhận hoàn thành đơn

```text
POST /api/executor/handovers/{requestId}/complete
```

Chọn đơn Approved/In_Progress đã có LegalVerification Approved và ít nhất một HandoverCase. Chọn File là biên bản hoàn thành hợp lệ; Signature là chữ ký rời của executor khớp biên bản; Execute.

Kỳ vọng: 204, không có body. Gọi E2: status của đơn và các cases là Completed; có file mới signatureValid=true. Gọi E3 để đối chiếu bytes biên bản. Thực hiện complete lần nữa phải trả 409. Ca này ghi dữ liệu và không có API hoàn tác trong phạm vi hiện tại.

## 5. Test người thụ hưởng — Beneficiary

Logout, login tài khoản role Beneficiary. Email của user phải nhận được OTP. Assignment cần Active, có HandoverCase In_Progress/Completed và đơn liên quan có LegalVerification Approved để tài sản xuất hiện.

### B1. Kiểm tra yêu cầu OTP trước khi xem tài sản

```text
GET /api/beneficiary/assets
```

Khi mới login và chưa xác minh OTP: kỳ vọng 403, thông báo yêu cầu xác minh danh tính bằng OTP.

### B2. Yêu cầu OTP

```text
POST /api/beneficiary/otp/request
```

Try it out → Execute, không có JSON body. Kỳ vọng: 200, body có challengeId (32 ký tự) và expiresAt; mã OTP được gửi tới email user, không trả qua API. Sao chép challengeId và mở hộp thư để lấy mã 6 số. Gửi lại trong 60 giây trả 429.

### B3. Xác minh OTP

```json
POST /api/beneficiary/otp/verify
{
  "challengeId": "<challengeId-vua-nhan>",
  "code": "<6-chu-so-trong-email>"
}
```

Code phải là chuỗi để giữ số 0 đầu; thay toàn bộ placeholder trước khi Execute. Kỳ vọng: 200, {"verified":true,"validForSeconds":900}. Cùng OTP không dùng lại được; quyền xem có hiệu lực 15 phút trong phiên đăng nhập hiện tại.

### B4. Danh sách và chi tiết tài sản thừa kế

```text
GET /api/beneficiary/assets
```

Sau OTP hợp lệ: kỳ vọng 200. Mỗi phần tử có handoverId, allocation và asset (assetId, name, type, description, status, documents). Nếu [] thì đối chiếu assignment/case/phê duyệt pháp lý, không mặc định coi là lỗi API.

```text
GET /api/beneficiary/assets/{assetId}
```

Dùng assetId lấy từ B4. Kỳ vọng: 200, thông tin một tài sản được mở cho beneficiary và tỷ lệ allocation. Nếu tài sản có nhiều case đủ điều kiện, detail hiện trả case khớp đầu tiên.

### B5. Đọc giấy tờ tài sản

```text
GET /api/beneficiary/assets/{assetId}/documents/{documentId}/content
```

Dùng documentId thuộc asset, giấy tờ có trạng thái Active. Kỳ vọng: 200, tải được toàn bộ nội dung giải mã, có X-Signature-Valid. Giấy tờ Pending_Legal hoặc thuộc asset khác trả 404; OTP hết hạn/chưa xác minh trả 403.

### B6. Hết hạn và đổi phiên

OTP hết hạn sau 5 phút, tối đa 5 lần nhập sai. Quyền truy cập hết hạn sau 15 phút. Logout rồi login lại phải xác minh OTP lại; restart API làm mất OTP và grant vì lưu trong RAM. Khi test lỗi OTP, dùng challenge mới và một tài khoản riêng để không làm hỏng ca thành công.

OTP ở đây xác minh quyền truy cập email; chưa phải eKYC/đối chiếu giấy tờ tùy thân. Không có API beneficiary xác nhận đã nhận bàn giao trong phạm vi triển khai hiện tại.

## 6. Ca kiểm thử lỗi và phân quyền

Thực hiện trên dữ liệu test riêng. Nếu vừa gặp 429, đợi ít nhất 60 giây trước khi test tiếp; login/OTP giới hạn 10 request/IP/phút.

| Ca kiểm thử | Kết quả mong đợi |
| --- | --- |
| Logout rồi gọi Owner/Executor/Beneficiary API | 401 |
| User chỉ có Beneficiary gọi Owner API | 403. User có cả Owner và Beneficiary được quản lý kho của mình; tài sản của người khác trả 404. |
| Owner xem asset thuộc owner khác | 404 |
| Executor xem đơn của executor khác | 404 |
| OwnerAlive=true nhưng thiếu/sai Signature | 400, không lưu file giấy tờ. |
| OwnerAlive=false nhưng thiếu DeathCertificate | 400 |
| Chọn beneficiary khác khi asset đã có assignment Active | 409; phải chọn người đang được gán. |
| File rỗng, MIME/nội dung sai hoặc định dạng không hỗ trợ | 400 |
| File vượt 10 MB | 400 nếu tới validation; 413 nếu vượt giới hạn request/server. |
| Complete đơn Draft hoặc chưa có legal Approved/case | 409 |
| Complete file có chữ ký sai sau khi đủ điều kiện đơn | 400 |
| Gửi pháp lý vào Pending_Legal hoặc complete lại | 409 |
| Verifier chính là executor/owner, có đủ role verifier | 400; nếu thiếu role verifier thì 403 trước. |
| Beneficiary xem tài sản trước OTP | 403 |
| OTP sai, hết hạn, đã dùng hoặc quá 5 lần thử | 400 |
| Gửi lại OTP trong 60 giây | 429 |
| Beneficiary có OTP nhưng asset không được mở cho mình | 404 ở detail hoặc không có trong danh sách. |

### Cách đọc Response

200: thành công có dữ liệu. 204: thành công không có body. 400: input/chữ ký/OTP không hợp lệ. 401: chưa login hoặc phiên hết hạn. 403: sai role/chưa OTP. 404: không tồn tại hoặc không có quyền tài nguyên. 409: sai trạng thái/xung đột. 429: vượt giới hạn. 503: thiếu cấu hình. 500: lỗi thực thi cần xem log terminal.

Body lỗi nghiệp vụ thường có status và detail. Lỗi model validation có thể dùng ProblemDetails với errors. Chỉ so sánh điều kiện/mã lỗi phù hợp ca test, không bắt buộc mọi lỗi có cùng cấu trúc JSON.

## 7. Xử lý lỗi và ghi kết quả test

| Triệu chứng | Cách kiểm tra |
| --- | --- |
| Không mở được Swagger | API có chạy không; dùng profile https và môi trường Development; mở đúng https://localhost:7015/swagger. Swagger không bật ở Production. |
| Cảnh báo HTTPS | Chạy dotnet dev-certs https --trust, sau đó mở lại browser. |
| 503 Missing ConnectionStrings:LegacyVault | Cấu hình User Secrets/biến môi trường rồi restart API. |
| 500 khi đọc/ghi dữ liệu | Xem log; kiểm tra SQL, schema/check constraints, quyền truy cập, file tồn tại và khóa giải mã. Không dùng schema.sql hiện tại để tạo bảng vì script không có CREATE TABLE. |
| Login 401 | Kiểm tra email/password thật, user Active, khóa tài khoản và định dạng PasswordHash; không nhập hash làm password. |
| Login thành công nhưng API vẫn 401 | Dùng cùng origin HTTPS; cookie Secure có thể không được gửi qua HTTP; tránh đổi localhost sang 127.0.0.1. |
| 403 sai role | RoleName phải khớp: Owner, Executor, Beneficiary, LegalVerifier; user phải Active. |
| 400 chữ ký không hợp lệ | Kiểm tra bytes file không đổi, chữ ký binary RSA-SHA256, cert đúng userId, hạn cert, chain trust và khả năng kiểm tra revocation online. |
| 503 khi gửi OTP/upload | Kiểm tra SMTP/TLS hoặc Security:EncryptionKey (base64 32 byte). |
| Không nhận email OTP | Kiểm tra response trước; xem spam, email user, SMTP credentials/TLS và log. Lỗi SMTP thực tế có thể trả 500. |
| File cũ không đọc được | Storage mới yêu cầu envelope AES-GCM; dữ liệu file cũ cần chuyển đổi theo format cũ. Không đổi khóa AES khi đang có file đã lưu. |

### Mẫu ghi nhận một ca test

| Mục | Thông tin ghi lại |
| --- | --- |
| Mã ca / thời gian | Ví dụ O3 / ngày giờ thực hiện. |
| Tài khoản / dữ liệu | Role, userId, assetId/requestId/documentId; không ghi mật khẩu, cookie hoặc OTP. |
| Request | Method, endpoint, tên file và các trường input. |
| Expected / Actual | HTTP status, các trường response và trạng thái sau test. |
| Kết luận | Pass/Fail; ảnh Swagger và log liên quan nếu lỗi. |

### Chạy bộ kiểm tra nghiệp vụ độc lập

```powershell
dotnet run --project LegacyVault.Tests
```

Đây là executable kiểm tra, không chạy bằng dotnet test. Bộ kiểm tra dùng repository/storage/SMTP giả lập; không thay thế test Swagger với SQL Server, SMTP và chứng thư thật. Các ca hướng dẫn ở trên là kết quả mong đợi theo code; không khẳng định đã chạy thành công trên database của bạn.

Tài liệu tham chiếu trong project: docs/API.md. API không tự seed user, tạo đơn, duyệt pháp lý hoặc kích hoạt giấy tờ Pending_Legal; chuẩn bị trước dữ liệu đúng trạng thái để test từng nhánh.
