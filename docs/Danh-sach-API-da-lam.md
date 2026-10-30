# Danh sách chức năng và API đã triển khai

Cập nhật ngày 28/10/2026, đối chiếu controller và middleware hiện tại. Tài liệu liệt kê các route đã nối vào API; không khẳng định các tích hợp ngoài như Google, SMTP, SQL Server hay VNPay sandbox đã được kiểm thử trực tiếp.

## 1. Tài khoản, xác thực và hồ sơ

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/auth/register` | Đăng ký tài khoản Owner |
| POST | `/api/auth/register/request` | Yêu cầu OTP đăng ký qua email |
| POST | `/api/auth/register/confirm` | Xác nhận OTP và hoàn tất đăng ký |
| POST | `/api/auth/login` | Đăng nhập bằng email/mật khẩu; có thể yêu cầu bước 2FA |
| POST | `/api/auth/2fa/verify` | Xác minh OTP bước đăng nhập và nhận access token |
| POST | `/api/auth/2fa/resend` | Gửi lại OTP bước đăng nhập |
| POST | `/api/auth/refresh-token` | Cấp access token mới bằng refresh token |
| GET | `/api/auth/me` | Xem hồ sơ tài khoản đang đăng nhập |
| PUT | `/api/auth/profile` | Cập nhật hồ sơ cá nhân |
| POST | `/api/auth/change-password/request` | Yêu cầu đổi mật khẩu và gửi OTP email |
| POST | `/api/auth/change-password/confirm` | Xác nhận OTP và hoàn tất đổi mật khẩu |
| POST | `/api/auth/logout` | Kết thúc phiên phía client và thu hồi OTP grant beneficiary |

API nghiệp vụ dùng JWT Bearer. Đăng ký tài khoản thường nhận Owner do server gán; cấp quyền nhân viên được thực hiện qua Admin.

## 2. Đăng nhập Google

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/auth/google/start` | Bắt đầu đăng nhập Google trong trình duyệt |
| GET | `/api/auth/google/oidc-callback` | Callback OIDC do authentication middleware xử lý |
| GET | `/api/auth/google/callback` | Hiển thị trạng thái/challenge sau xác thực Google |
| GET | `/api/auth/google/challenge` | Đọc trạng thái challenge gắn với trình duyệt |
| POST | `/api/auth/google/complete` | Hoàn tất đăng nhập khi tài khoản không cần OTP đăng ký |
| POST | `/api/auth/google/otp/verify` | Xác minh OTP email cho tài khoản Google mới |
| POST | `/api/auth/google/otp/resend` | Gửi lại OTP Google |
| GET | `/api/auth/google/error` | Hiển thị lỗi hoặc trạng thái hủy đăng nhập Google |

Google mới chỉ được tạo tài khoản sau OTP hợp lệ. Google dùng cookie tạm thời cho luồng trình duyệt; API nghiệp vụ nhận access token. Cấu hình Google OAuth và callback URL phải khớp cấu hình môi trường.

## 3. Chủ sở hữu: kho, tài sản và người thụ hưởng

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/owner/vaults` | Tạo kho |
| GET | `/api/owner/vaults` | Danh sách kho của mình |
| GET | `/api/owner/vaults/{vaultId}` | Chi tiết kho của mình |
| PUT | `/api/owner/vaults/{vaultId}` | Cập nhật kho |
| POST | `/api/owner/vaults/{vaultId}/close` | Đóng kho |
| GET | `/api/owner/vaults/{vaultId}/assets` | Danh sách tài sản trong kho cụ thể |
| GET | `/api/owner/vaults/{vaultId}/assets/{assetId}` | Chi tiết tài sản trong kho cụ thể |
| POST | `/api/owner/assets` | Tạo tài sản |
| GET | `/api/owner/assets` | Danh sách tài sản; hỗ trợ lọc `vaultId` |
| GET | `/api/owner/assets/{assetId}` | Chi tiết tài sản; hỗ trợ `vaultId` tùy chọn |
| PUT | `/api/owner/assets/{assetId}` | Cập nhật tài sản |
| POST | `/api/owner/assets/{assetId}/revoke` | Thu hồi tài sản |
| POST | `/api/owner/assets/{assetId}/beneficiaries` | Gán người thụ hưởng bằng email và tỷ lệ |
| GET | `/api/owner/beneficiaries` | Danh sách người thụ hưởng; hỗ trợ lọc `assetId` |
| GET | `/api/owner/assets/{assetId}/allocations` | Danh sách tỷ lệ phân bổ |
| PUT | `/api/owner/assets/{assetId}/allocations/{assignmentId}` | Cập nhật tỷ lệ phân bổ |
| POST | `/api/owner/assets/{assetId}/allocations/{assignmentId}/revoke` | Thu hồi phân bổ |
| POST | `/api/owner/assets/{assetId}/documents` | Upload giấy tờ tài sản |
| GET | `/api/owner/assets/{assetId}/documents` | Danh sách metadata giấy tờ |
| GET | `/api/owner/assets/{assetId}/documents/{documentId}/content` | Tải nội dung giấy tờ được giải mã |
| POST | `/api/owner/assets/{assetId}/documents/{documentId}/revoke` | Thu hồi giấy tờ |

Các API kiểm tra quyền sở hữu ở server. Upload dùng multipart/form-data; file được mã hóa. Khi endpoint yêu cầu chữ ký, chữ ký được xác minh theo cấu hình chứng thư. Tài liệu Owner không nhận giấy tờ pháp lý của handover.

## 4. Owner: Executor, xác nhận còn sống và handover

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/owner/vaults/{vaultId}/executor` | Xem Executor gắn với kho |
| PUT | `/api/owner/vaults/{vaultId}/executor` | Gán Executor cho kho |
| POST | `/api/owner/vaults/{vaultId}/executor/revoke` | Thu hồi Executor |
| GET | `/api/owner/executors` | Tìm/danh sách Executor khả dụng |
| GET | `/api/owner/executors/{executorId}` | Xem thông tin Executor |
| GET | `/api/owner/life-confirmation` | Xem trạng thái xác nhận còn sống |
| POST | `/api/owner/life-confirmation` | Xác nhận còn sống |
| POST | `/api/owner/vaults/{vaultId}/handovers` | Tạo yêu cầu bàn giao |
| GET | `/api/owner/handovers` | Danh sách handover của Owner |
| GET | `/api/owner/handovers/{requestId}` | Chi tiết handover |
| POST | `/api/owner/handovers/{requestId}/cancel` | Hủy handover khi trạng thái cho phép |

## 5. Executor và bàn giao

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/executor/assignments` | Danh sách phân công Executor |
| POST | `/api/executor/assignments/{vaultId}/handovers` | Tạo yêu cầu handover từ phân công |
| GET | `/api/executor/handovers` | Danh sách handover được giao |
| GET | `/api/executor/handovers/{requestId}` | Chi tiết handover được giao |
| POST | `/api/executor/handovers/{requestId}/accept` | Nhận xử lý handover |
| POST | `/api/executor/handovers/{requestId}/decline` | Từ chối handover |
| GET | `/api/executor/handovers/overview` | Danh sách tổng quan, có lọc/phân trang |
| GET | `/api/executor/handovers/completed` | Danh sách handover hoàn tất |
| GET | `/api/executor/handovers/{requestId}/owner-status` | Xem tình trạng xác nhận của Owner |
| GET | `/api/executor/handovers/{requestId}/history` | Lịch sử xác minh pháp lý |
| GET | `/api/executor/handovers/{requestId}/documents` | Danh sách giấy tờ handover |
| GET | `/api/executor/handovers/{requestId}/legal-verifiers` | Danh sách LegalVerifier phù hợp |
| POST | `/api/executor/handovers/{requestId}/start` | Bắt đầu xử lý handover |
| POST | `/api/executor/handovers/{requestId}/death-certificate` | Upload giấy chứng tử |
| POST | `/api/executor/handovers/{requestId}/documents` | Upload giấy tờ handover |
| GET | `/api/executor/handovers/{requestId}/documents/{documentId}/content` | Tải nội dung giấy tờ handover |
| POST | `/api/executor/handovers/{requestId}/legal-verifications` | Gửi yêu cầu xác minh kèm Evidence |
| POST | `/api/executor/handovers/{requestId}/complete` | Hoàn tất handover bằng file và chữ ký |

Các bước đổi trạng thái phụ thuộc quyền Executor được giao và trạng thái workflow. API upload dùng multipart/form-data; file hoàn tất yêu cầu chữ ký theo quy tắc của service.

## 6. LegalVerifier

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/legal/handovers` | Danh sách yêu cầu xác minh được giao |
| GET | `/api/legal/handovers/{requestId}` | Chi tiết yêu cầu và kết quả xác minh |
| GET | `/api/legal/handovers/{requestId}/assets` | Danh sách tài sản trong phạm vi handover |
| GET | `/api/legal/handovers/{requestId}/owner-status` | Xem trạng thái Owner |
| GET | `/api/legal/handovers/{requestId}/history` | Lịch sử xác minh |
| GET | `/api/legal/handovers/{requestId}/documents` | Danh sách chứng cứ handover |
| GET | `/api/legal/handovers/{requestId}/documents/{documentId}/content` | Tải chứng cứ handover |
| GET | `/api/legal/handovers/{requestId}/asset-documents/{documentId}/content` | Tải giấy tờ tài sản liên quan |
| POST | `/api/legal/handovers/{requestId}/approve` | Phê duyệt yêu cầu |
| POST | `/api/legal/handovers/{requestId}/reject` | Từ chối yêu cầu |
| POST | `/api/legal/handovers/{requestId}/request-additional-information` | Yêu cầu bổ sung thông tin |

Quyền truy cập bị giới hạn theo yêu cầu được phân công; kiểm tra xung đột lợi ích được áp dụng trong service.

## 7. Beneficiary và xác nhận nhận bàn giao

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| POST | `/api/beneficiary/otp/request` | Gửi OTP email để xác minh quyền truy cập |
| POST | `/api/beneficiary/otp/verify` | Xác minh OTP trong phiên hiện tại |
| GET | `/api/beneficiary/assets` | Danh sách tài sản thừa kế được phép truy cập |
| GET | `/api/beneficiary/assets/{assetId}` | Chi tiết tài sản thừa kế |
| GET | `/api/beneficiary/assets/{assetId}/documents/{documentId}/content` | Tải giấy tờ tài sản được phép đọc |
| GET | `/api/beneficiary/handovers` | Danh sách hồ sơ bàn giao |
| GET | `/api/beneficiary/handovers/{handoverId}` | Chi tiết hồ sơ bàn giao |
| GET | `/api/beneficiary/handovers/{handoverId}/documents` | Danh sách giấy tờ bàn giao |
| GET | `/api/beneficiary/handovers/{handoverId}/documents/{documentId}/content` | Tải giấy tờ bàn giao |
| POST | `/api/beneficiary/handovers/{handoverId}/receipt` | Xác nhận nhận bàn giao |
| GET | `/api/beneficiary/handovers/{handoverId}/receipt` | Xem biên nhận bàn giao |

Truy cập tài sản yêu cầu OTP còn hiệu lực. Nội dung chỉ hiển thị khi assignment, case và phê duyệt pháp lý đủ điều kiện.

## 8. Quản trị hệ thống

Các endpoint dưới đây yêu cầu role `Admin`.

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/admin/users` | Danh sách, tìm kiếm và lọc người dùng |
| POST | `/api/admin/users` | Tạo tài khoản và gán role |
| PUT | `/api/admin/users/{userId}` | Cập nhật thông tin tài khoản |
| PUT | `/api/admin/users/{userId}/roles` | Cập nhật role |
| PUT | `/api/admin/users/{userId}/status` | Cập nhật trạng thái tài khoản |
| POST | `/api/admin/users/{userId}/reset-password` | Đặt lại mật khẩu người dùng |
| GET | `/api/admin/roles` | Xem role và thông tin phân quyền |
| GET | `/api/admin/audit-logs` | Xem audit log có phân trang/lọc |
| GET | `/api/admin/security/alerts` | Dashboard cảnh báo bảo mật |
| GET | `/api/admin/system/config` | Xem cấu hình hệ thống được phép công khai |
| PUT | `/api/admin/system/config` | Cập nhật cấu hình hệ thống được hỗ trợ |
| GET | `/api/admin/system/key-rotation` | Xem trạng thái luân chuyển khóa |
| POST | `/api/admin/system/key-rotation/rotate` | Thực hiện luân chuyển khóa theo service |
| GET | `/api/admin/integrations` | Danh sách tích hợp ngoài và trạng thái cấu hình |
| POST | `/api/admin/integrations/{serviceId}/ping` | Kiểm tra kết nối tích hợp |

## 9. Thanh toán VNPay và gói dịch vụ

| Method | Endpoint | Quyền | Chức năng |
| --- | --- | --- | --- |
| GET | `/api/payments/plans` | Công khai | Danh sách gói đang hoạt động |
| GET | `/api/payments/me/subscription` | Đăng nhập | Gói đang hoạt động; trả `hasActiveSubscription=false` nếu hiện không có |
| GET | `/api/payments/me/transactions` | Đăng nhập | Lịch sử giao dịch cá nhân; hỗ trợ phân trang và lọc trạng thái |
| POST | `/api/payments/vnpay/checkout` | Đăng nhập | Tạo giao dịch chờ và nhận URL thanh toán |
| GET | `/api/payments/vnpay/return` | Callback công khai | Xác minh chữ ký/giá trị trả về cho trình duyệt |
| GET | `/api/payments/vnpay/ipn` | Callback công khai | Nhận IPN; cập nhật giao dịch và kích hoạt gói khi thành công |

Admin quản lý gói tại `GET/POST /api/admin/subscription-plans`, `PUT/DELETE /api/admin/subscription-plans/{planId}`; DELETE sẽ ngừng bán gói (soft deactivate), không xóa lịch sử. Admin tra giao dịch qua `GET /api/admin/payment-transactions` (lọc userId/status, phân trang) và `GET /api/admin/payment-transactions/{transactionId}`. Trạng thái giao dịch được xác định bởi callback VNPay, không có API sửa trạng thái thanh toán thủ công.

Callback kiểm tra HMAC-SHA512 và số tiền khớp giao dịch. IPN là thông báo máy chủ cập nhật trạng thái; không dùng riêng redirect từ trình duyệt làm bằng chứng thanh toán. Cần cấu hình `VnPay:TmnCode`, `VnPay:HashSecret`, `VnPay:PaymentUrl`, `VnPay:ReturnUrl` và URL IPN trên VNPay. Hướng dẫn tại [VNPay.md](VNPay.md).

Giới hạn `MaxVaults` và `MaxStorageBytes` của gói đang áp dụng khi tạo kho và upload tài liệu (tính dung lượng bytes đã mã hóa, gồm tài liệu tài sản và handover). Người chưa có gói hoạt động dùng mức mặc định `SubscriptionQuota:FreeMaxVaults=1` và `SubscriptionQuota:FreeMaxStorageBytes=1073741824`. Model gói hiện chưa có giới hạn số tài sản; cần mở rộng schema trước khi có thể cấu hình/quản lý hạn mức này theo từng gói.

## 10. Thông báo, trò chuyện và mã hóa trường

| Method | Endpoint | Chức năng |
| --- | --- | --- |
| GET | `/api/notifications` | Danh sách thông báo của tài khoản |
| POST | `/api/notifications/{notificationId}/read` | Đánh dấu thông báo đã đọc |
| GET | `/api/handovers/{requestId}/messages` | Đọc hội thoại của handover |
| POST | `/api/handovers/{requestId}/messages` | Gửi tin nhắn trong hội thoại |
| POST | `/api/account-security/encrypt-field` | Mã hóa trường dữ liệu qua dịch vụ bảo mật |
| POST | `/api/account-security/decrypt-field` | Giải mã trường dữ liệu qua dịch vụ bảo mật |

ChatHub SignalR cũng được map tại `/hubs/chat` để trao đổi realtime.

## 11. Cấu hình, kiểm tra và giới hạn

- Swagger Development: `https://localhost:7015/swagger`. API nghiệp vụ yêu cầu JWT Bearer; các callback OAuth/VNPay công khai nhưng tự xác minh dữ liệu/chữ ký.
- Cấu hình SQL Server, SMTP, khóa mã hóa, chứng thư, Google OAuth và VNPay bằng User Secrets hoặc biến môi trường. Không commit credentials.
- Test harness là ứng dụng console: `dotnet run --project LegacyVault.Tests`. Dùng `dotnet run`, không dùng `dotnet test`.
- Build gần nhất sau khi gộp `main` và VNPay: `dotnet build LegacyVault.sln --no-restore` thành công; còn các cảnh báo compiler hiện có. Chưa chạy lại test harness trong lần cập nhật tài liệu này.
- Các endpoint profile, admin, OTP đăng ký, refresh token, workflow, Beneficiary handover và VNPay đã có trong code hiện tại; các giới hạn tích hợp ngoài phụ thuộc cấu hình và dịch vụ thật.

Tài liệu liên quan: [API tổng quan](API.md), [tích hợp Admin](Admin-API-Integration.md), [hồ sơ và đổi mật khẩu](User-Profile-and-ChangePassword-API.md), [hướng dẫn VNPay](VNPay.md), [hướng dẫn test toàn bộ API](Huong-dan-test-toan-bo-API.md).
