# Tài liệu API: Cập nhật Thông tin & Đổi mật khẩu với OTP qua Email

> **Dành cho:** Frontend Developers  
> **Áp dụng cho:** Tất cả người dùng đã đăng nhập vào hệ thống  
> **Base URL:** `https://localhost:7015/api/auth` (hoặc `http://localhost:5131/api/auth`)

---

## 🔐 Yêu cầu Xác thực chung (Authentication)

Tất cả các API dưới đây đều yêu cầu người dùng đã đăng nhập:

- **Header bắt buộc:**
  ```http
  Authorization: Bearer <Access_Token>
  Content-Type: application/json
  ```
- **Xử lý lỗi xác thực:**
  - `401 Unauthorized`: Token không hợp lệ hoặc đã hết hạn.

---

## 📋 Chi tiết các API

### 1. Xem thông tin cá nhân hiện tại
- **Method / Endpoint:** `GET /api/auth/me`
- **Response `200 OK`:**
  ```json
  {
    "userId": 1,
    "fullName": "Nguyễn Văn A",
    "email": "user@example.com",
    "roles": [ "Owner" ],
    "status": "Active"
  }
  ```

---

### 2. Cập nhật thông tin cá nhân (Profile)
- **Method / Endpoint:** `PUT /api/auth/profile`
- **Request Body:**
  ```json
  {
    "fullName": "Nguyễn Văn A (Mới)",
    "phone": "0912345678",
    "identityNumber": "001200001234"
  }
  ```
- **Response `200 OK`:** Trả về thông tin cá nhân mới cập nhật (`MeDto`):
  ```json
  {
    "userId": 1,
    "fullName": "Nguyễn Văn A (Mới)",
    "email": "user@example.com",
    "roles": [ "Owner" ],
    "status": "Active"
  }
  ```

---

### 3. Quy trình Đổi mật khẩu 2 bước với OTP qua Email

Quy trình đổi mật khẩu được thiết kế bảo mật 2 bước:
1. **Bước 1**: Người dùng nhập mật khẩu cũ, mật khẩu mới -> Hệ thống xác thực mật khẩu cũ và tự động gửi mã OTP 6 số đến email của người dùng.
2. **Bước 2**: Người dùng nhập mã OTP để xác nhận hoàn tất đổi mật khẩu.

```
Frontend                           Backend                          Email User
   |                                  |                                  |
   |-- 1. POST change-password/request->|                                  |
   |   (Old, New, ConfirmPass)        |-- Gửi mã OTP 6 số qua SMTP ------>|
   |<- 2. Trả về ChallengeId ----------|                                  |
   |                                  |                                  |
   |                                  |           (Nhận mã OTP)          |
   |-- 3. POST change-password/confirm->|                                  |
   |   (ChallengeId, OTP Code)        |-- Cập nhật mật khẩu mới & Audit |
   |<- 4. Trả về Thành công ----------|
```

---

#### BƯỚC 1: Yêu cầu đổi mật khẩu (Gửi OTP)
- **Method / Endpoint:** `POST /api/auth/change-password/request`
- **Request Body:**
  ```json
  {
    "oldPassword": "CurrentPassword123!",
    "newPassword": "NewStrongPassword123!",
    "confirmPassword": "NewStrongPassword123!"
  }
  ```
  **Ràng buộc kiểm tra:**
  - Mật khẩu mới tối thiểu 12 ký tự.
  - `confirmPassword` phải khớp chính xác với `newPassword`.
  - `newPassword` không được trùng với `oldPassword`.
  - `oldPassword` phải đúng mật khẩu hiện tại của người dùng.
  - Giới hạn tốc độ: Mỗi người dùng phải đợi tối thiểu 60 giây giữa 2 lần yêu cầu gửi OTP.

- **Response `200 OK`:**
  ```json
  {
    "challengeId": "a1b2c3d4e5f67890123456789abcdef0",
    "expiresInSeconds": 300,
    "message": "Mã OTP xác nhận đã được gửi đến email của bạn."
  }
  ```
  *(Frontend lưu lại `challengeId` để dùng cho Bước 2).*

- **Response lỗi thường gặp:**
  - `400 Bad Request`: Mật khẩu hiện tại không chính xác, hoặc mật khẩu mới không hợp lệ.
  - `429 Too Many Requests`: Vui lòng đợi 60 giây trước khi yêu cầu mã OTP mới.

---

#### BƯỚC 2: Xác nhận mã OTP để hoàn tất đổi mật khẩu
- **Method / Endpoint:** `POST /api/auth/change-password/confirm`
- **Request Body:**
  ```json
  {
    "challengeId": "a1b2c3d4e5f67890123456789abcdef0",
    "code": "123456"
  }
  ```
  *(Lưu ý: `code` gồm chính xác 6 chữ số).*

- **Response `200 OK`:**
  ```json
  {
    "message": "Đổi mật khẩu thành công."
  }
  ```

- **Response lỗi thường gặp:**
  - `400 Bad Request`: Mã OTP không chính xác, đã quá 5 phút hết hạn, hoặc nhập sai quá 5 lần.

---

### 4. Quy trình Đăng ký tài khoản 2 bước với OTP qua Email

Quy trình đăng ký tài khoản mới được bảo mật 2 bước:
1. **Bước 1**: Người dùng nhập thông tin đăng ký -> Hệ thống kiểm tra tính hợp lệ và gửi mã OTP 6 số đến email đăng ký.
2. **Bước 2**: Người dùng nhập mã OTP để xác thực email -> Hệ thống tạo tài khoản thành công (`Owner`).

#### BƯỚC 1: Yêu cầu đăng ký (Gửi mã OTP đến email)
- **Method / Endpoint:** `POST /api/auth/register/request`
- **Request Body:**
  ```json
  {
    "fullName": "Nguyễn Văn A",
    "email": "user@example.com",
    "password": "StrongPassword123!",
    "confirmPassword": "StrongPassword123!",
    "phone": "0912345678",
    "identityNumber": "001200001234"
  }
  ```
  **Ràng buộc kiểm tra:**
  - Mật khẩu tối thiểu 12 ký tự.
  - `confirmPassword` phải khớp chính xác với `password`.
  - `email` chưa từng được đăng ký trong hệ thống (nếu đã có -> trả về `409 Conflict`).
  - Giới hạn tốc độ: Phải đợi tối thiểu 60 giây giữa 2 lần gửi OTP cho cùng 1 email.

- **Response `200 OK`:**
  ```json
  {
    "challengeId": "d3b07384d113edec49eaa6238ad5ff00",
    "expiresInSeconds": 300,
    "message": "Mã OTP xác nhận đã được gửi đến email của bạn."
  }
  ```
  *(Frontend lưu lại `challengeId` để gửi kèm ở Bước 2).*

- **Response lỗi thường gặp:**
  - `400 Bad Request`: Thông tin không hợp lệ hoặc mật khẩu xác nhận không khớp.
  - `409 Conflict`: Email này đã được đăng ký tài khoản.
  - `429 Too Many Requests`: Vui lòng đợi 60 giây trước khi yêu cầu mã OTP mới.

---

#### BƯỚC 2: Nhập mã OTP để hoàn tất đăng ký tài khoản
- **Method / Endpoint:** `POST /api/auth/register/confirm`
- **Request Body:**
  ```json
  {
    "challengeId": "d3b07384d113edec49eaa6238ad5ff00",
    "code": "123456"
  }
  ```
  *(Lưu ý: `code` gồm chính xác 6 chữ số).*

- **Response `201 Created`:**
  ```json
  {
    "userId": 18,
    "fullName": "Nguyễn Văn A",
    "email": "user@example.com",
    "phone": "0912345678",
    "identityNumber": "001200001234",
    "roles": [ "Owner" ]
  }
  ```
  *(Tài khoản được tạo thành công với vai trò mặc định là `Owner` và có thể đăng nhập ngay lập tức).*

- **Response lỗi thường gặp:**
  - `400 Bad Request`: Mã OTP không chính xác, đã quá 5 phút hết hạn, hoặc nhập sai quá 5 lần.

