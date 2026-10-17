# Danh sách API đã triển khai

Cập nhật ngày 14/10/2026, đối chiếu controller và cấu hình middleware trong workspace. Có **27 endpoint controller** và **1 callback OIDC** do middleware xử lý. Danh sách này phản ánh code đã triển khai, không khẳng định mọi endpoint đã được test trên dịch vụ thật.

## 1. Tài khoản — 3 API

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/auth/register` | Đăng ký email/mật khẩu; mặc định Owner |
| POST | `/api/auth/login` | Đăng nhập email/mật khẩu, cấp cookie |
| POST | `/api/auth/logout` | Đăng xuất và thu hồi xác minh OTP của phiên |

Đăng ký nhận fullName, email, password, confirmPassword và phone tùy chọn; không cho chọn role. Tài khoản nhân viên được cấp riêng. Google bổ sung phương thức đăng nhập, không thay thế đăng nhập mật khẩu.

## 2. Google — 5 endpoint controller và 1 callback middleware

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/auth/google/start` | Chuyển trình duyệt sang Google để chọn tài khoản |
| GET | `/api/auth/google/oidc-callback` | Middleware xác minh OIDC và bắt đầu OTP; không gọi thủ công |
| GET | `/api/auth/google/callback` | Trang HTML hiển thị challengeId và hướng dẫn nhập OTP |
| GET | `/api/auth/google/error` | Trả thông báo khi Google thất bại hoặc bị hủy |
| POST | `/api/auth/google/otp/verify` | Xác minh OTP, tạo/tìm User theo email và cấp cookie |
| POST | `/api/auth/google/otp/resend` | Gửi lại OTP, trả challengeId mới |

Chỉ tạo tài khoản sau OTP hợp lệ. User mới nhận Owner và không có mật khẩu giả; email có sẵn giữ mật khẩu/role. Chặn User không Active, đang khóa hoặc có Admin/Executor/LegalVerifier. Giữ nguyên schema, không lưu Google sub. OTP lưu trong bộ nhớ một instance; restart làm mất challenge. OTP Google không thay thế OTP người thụ hưởng.

## 3. Chủ sở hữu — 9 API

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/owner/vaults` | Tạo kho của User đang đăng nhập |
| GET | `/api/owner/vaults` | Danh sách kho của mình |
| GET | `/api/owner/vaults/{vaultId}` | Chi tiết kho của mình |
| POST | `/api/owner/assets` | Tạo tài sản trong kho Active của mình |
| GET | `/api/owner/assets` | Danh sách tài sản của mình |
| GET | `/api/owner/assets/{assetId}` | Chi tiết tài sản và metadata giấy tờ |
| POST | `/api/owner/assets/{assetId}/beneficiaries` | Chọn người thụ hưởng bằng email và tỷ lệ; cấp thêm Beneficiary |
| GET | `/api/owner/beneficiaries` | Danh sách người thụ hưởng; query assetId tùy chọn |
| POST | `/api/owner/assets/{assetId}/documents` | Upload giấy tờ mã hóa và chọn beneficiaryId đã có quyền |

Không chọn chính mình, không gán trùng assignment Active; tổng tỷ lệ không vượt 100%. Upload dùng multipart/form-data: File, Signature, DeathCertificate, BeneficiaryId, OwnerAlive. Khi khai báo còn sống phải có chữ ký rời hợp lệ; khi đã mất phải có giấy chứng tử. Chữ ký hỗ trợ RSA-SHA256 rời, chưa hỗ trợ PDF ký nhúng/CMS.

## 4. Người thi hành — 5 API

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/executor/handovers` | Danh sách đơn được phân công |
| GET | `/api/executor/handovers/{requestId}` | Chi tiết đơn được phân công |
| GET | `/api/executor/handovers/{requestId}/documents/{documentId}/content` | Nội dung file giải mã; kết quả chữ ký trong header X-Signature-Valid |
| POST | `/api/executor/handovers/{requestId}/complete` | Xác nhận hoàn thành bằng File và Signature |
| POST | `/api/executor/handovers/{requestId}/legal-verifications` | Gửi yêu cầu xác minh với VerifierId, Comment và Evidence |

Hai API POST dùng multipart/form-data. Hoàn thành yêu cầu đủ điều kiện nghiệp vụ và phê duyệt pháp lý. Hiện danh sách đơn chỉ lọc phân công Active, còn kiểm tra chi tiết chấp nhận Active/AutoAssigned; phần này chưa đồng bộ.

## 5. Người thụ hưởng — 5 API

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/beneficiary/otp/request` | Gửi OTP email của người thụ hưởng đang đăng nhập |
| POST | `/api/beneficiary/otp/verify` | Xác minh challengeId/code, mở quyền trong phiên |
| GET | `/api/beneficiary/assets` | Danh sách tài sản thừa kế được phép truy cập |
| GET | `/api/beneficiary/assets/{assetId}` | Chi tiết tài sản thừa kế |
| GET | `/api/beneficiary/assets/{assetId}/documents/{documentId}/content` | Nội dung giấy tờ được phép đọc |

Xem tài sản/file cần OTP beneficiary và điều kiện bàn giao: assignment Active, case In_Progress/Completed và legal verification Approved. Chỉ định người thụ hưởng không tự mở tài sản. Một User có thể giữ cả Owner và Beneficiary.

## 6. Cách test và cấu hình

Swagger Development: `https://localhost:7015/swagger`. Các API nghiệp vụ yêu cầu cookie đăng nhập; POST cần `X-Vault-Request: 1`, Swagger tự thêm header này. Google start phải mở trực tiếp trên trình duyệt; OTP test trong Swagger cùng trình duyệt/host/port.

Cấu hình SQL Server, SMTP, khóa mã hóa/chứng thư theo chức năng sử dụng. Google cần ClientId/ClientSecret. Không đưa credentials vào tài liệu hoặc Git.

Kết quả kiểm thử đã ghi nhận gần nhất: build thành công, **154 kiểm tra đạt** với SQLite và Google/SMTP giả lập; không phải kết quả Google/SMTP thật. Lần cập nhật tài liệu này không chạy lại test.

- [Hướng dẫn Google](Google-login-Swagger.md)
- [Luồng tạo kho, tài sản, chọn người thụ hưởng](Luong-chinh-dang-ky-tao-assets.md)
- [Hướng dẫn Swagger](Huong-dan-test-API-Swagger.md)

## 7. Chưa có API hoàn chỉnh

- Tạo đơn bàn giao/random Executor: có logic BLL, chưa nối endpoint và cần hoàn thiện quyền/transaction.
- Duyệt/từ chối xác minh pháp lý, cấp tài khoản nhân viên.
- Sửa/xóa phân bổ người thụ hưởng, thiết lập mật khẩu cho Google-only User, cập nhật profile/CCCD trong phiên bản workspace này.
- Thông báo và xác nhận nhận tài sản trong BeneficiaryService: chưa nối controller/DI, còn hàm mẫu và thiếu kiểm tra quyền; không tính là API đã hoàn thành.
