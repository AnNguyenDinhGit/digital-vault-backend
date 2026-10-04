# Luồng chính: đăng ký → đăng nhập → tạo kho → tạo tài sản

Chạy API, rồi mở **https://localhost:7015/swagger**:

```powershell
dotnet run --project LegacyVault.API --launch-profile https
```

Nếu API đang chạy bản cũ, Ctrl+C rồi chạy lại. Thực hiện toàn bộ các bước trên cùng trình duyệt và cùng địa chỉ HTTPS để cookie được gửi tự động. Swagger tự thêm `X-Vault-Request: 1` vào POST. Các ví dụ email/ID dưới đây cần thay bằng dữ liệu test của bạn.

## 1. Đăng ký

`POST /api/auth/register` → **Try it out** → nhập:

```json
{
  "fullName": "Nguyen Van A",
  "email": "owner@example.com",
  "password": "Registration123!",
  "confirmPassword": "Registration123!",
  "phone": "0901234567"
}
```

**Execute**. Kỳ vọng **201**, body có userId, fullName, email và roles = `["Owner"]`. Email đã đăng ký trả 409. Không có trường chọn role. Chưa tạo vault hoặc tài sản ở bước này; đăng ký cũng không tự đăng nhập.

## 2. Đăng nhập

`POST /api/auth/login`:

```json
{
  "email": "owner@example.com",
  "password": "Registration123!"
}
```

Kỳ vọng **200**; browser lưu cookie `LegacyVault.Session`. Không nhập token/Authorize. Mật khẩu là mật khẩu gốc vừa đăng ký, không phải hash.

## 3. Tạo kho

Tài sản phải thuộc một kho, vì `DigitalAsset.VaultId` là khóa ngoại tới `DigitalVault`. Dùng `POST /api/owner/vaults`:

```json
{
  "name": "Kho tai san cua toi",
  "description": "Kho luu tru tai san ca nhan"
}
```

Kỳ vọng **201**, ví dụ:

```json
{
  "vaultId": 10,
  "name": "Kho tai san cua toi",
  "description": "Kho luu tru tai san ca nhan",
  "status": "Active"
}
```

Ghi lại vaultId thực tế, không mặc định là 10. OwnerId tự lấy từ phiên đăng nhập; request không nhận ownerId hoặc status. `name` bắt buộc, tối đa 100 ký tự; `description` tùy chọn, tối đa 4.000 ký tự.

Có thể kiểm tra bằng `GET /api/owner/vaults` và `GET /api/owner/vaults/{vaultId}`. Nếu đã có kho, dùng vaultId của mình thay vì tạo kho mới.

## 4. Tạo tài sản

`POST /api/owner/assets`, thay vaultId bằng ID lấy từ bước 3:

```json
{
  "vaultId": 10,
  "name": "Tai khoan tiet kiem",
  "type": "BankAccount",
  "description": "Thong tin mo ta tai san"
}
```

Kỳ vọng **201**, ví dụ:

```json
{
  "assetId": 20,
  "name": "Tai khoan tiet kiem",
  "type": "BankAccount",
  "description": "Thong tin mo ta tai san",
  "status": "Active",
  "documents": []
}
```

Ghi lại assetId thực tế. `name` bắt buộc, tối đa 100; `type` bắt buộc, tối đa 50 ký tự ASCII vì mapping AssetType là varchar(50); `description` tùy chọn, tối đa 4.000. Ví dụ type: BankAccount, CryptoWallet, SocialAccount, Document, Other; đây là ví dụ, không phải enum bắt buộc. Name và description có thể chứa tiếng Việt.

Kho phải thuộc user đang đăng nhập và có Status=Active. Kho không tồn tại/thuộc người khác trả 404; kho không hoạt động trả 409. Request không nhận assetId, ownerId hay status. Bước này tạo thông tin tài sản, không upload giấy tờ, gán người thụ hưởng hoặc tạo bàn giao; chưa áp dụng hạn mức gói dịch vụ.

## 5. Xem lại tài sản

- `GET /api/owner/assets`: kỳ vọng 200, mảng có tài sản mới.
- `GET /api/owner/assets/{assetId}`: nhập assetId thực tế, kỳ vọng 200 và metadata tài sản.
- Hai response 201 ở bước tạo có header Location trỏ tới endpoint chi tiết tương ứng.
- `POST /api/auth/logout`: kỳ vọng 204; gọi API owner sau logout trả 401.

## Người thụ hưởng có đăng ký được không?

**Có.** Mọi tài khoản đăng ký đều nhận role **Owner** để quản lý kho của mình. Khi được chủ sở hữu khác chỉ định, tài khoản nhận thêm **Beneficiary**, vẫn giữ Owner. Quan hệ `BeneficiaryAssignment` xác định người đó được thụ hưởng tài sản nào; role không cho phép đọc mọi tài sản.

## 6. Chọn người thụ hưởng bằng email

1. Đăng ký tài khoản B bằng `/api/auth/register`, với email `beneficiary@example.com` (thay bằng email thật để nhận OTP).
2. Login tài khoản A đã tạo kho/tài sản. Trong Swagger, gọi `POST /api/owner/assets/{assetId}/beneficiaries`, nhập assetId của A và body:

```json
{
  "email": "beneficiary@example.com",
  "allocation": 100
}
```

Kỳ vọng 201, trả về `assetId`, `beneficiaryId`, `fullName`, `allocation`, `status`. Server tạo assignment Active và cấp thêm Beneficiary cho B trong cùng lần lưu. Không gửi role hoặc beneficiaryId trong request này.

3. Gọi `GET /api/owner/beneficiaries?assetId=...` để lấy danh sách. Dùng `beneficiaryId` trả về khi upload giấy tờ.
4. Logout A, login B. Gọi `/api/beneficiary/otp/request`, nhận mã trong email rồi gọi `/api/beneficiary/otp/verify`. API danh sách tài sản thừa kế vẫn trả mảng rỗng nếu chưa có bàn giao đủ điều kiện: assignment Active, HandoverCase In_Progress/Completed và LegalVerification Approved. Chỉ định người thụ hưởng không tự mở tài sản.

Email phải thuộc tài khoản đã đăng ký và Active; hiện chưa có lời mời cho email chưa đăng ký. Không được chọn chính mình (400), chọn trùng trên cùng tài sản (409), hoặc phân bổ vượt tổng 100% (409). Tỷ lệ từ 0,01 đến 100, tối đa hai chữ số thập phân. Với hai người, thêm lần lượt 60% và 40%; hiện chưa có API sửa/xóa phân bổ đã tạo. Executor, LegalVerifier và Admin tiếp tục dùng tài khoản được cấp riêng.

## Kiểm thử đã chạy

Test `MainFlowTests` chạy các controller thật qua HTTP, đăng ký/login bằng cookie, tạo kho/tài sản, đọc lại thông tin, kiểm tra khóa ngoại, từ chối owner khác và kho không hoạt động. Database dùng SQLite in-memory; không tạo tài khoản test trong SQL Server hiện tại. HTTP test host dùng cookie bảo vệ bằng khóa tạm trong RAM và HTTP loopback; cấu hình production vẫn HTTPS + Secure cookie.

```powershell
dotnet run --project LegacyVault.Tests
```

Hướng dẫn upload/chữ ký/OTP chi tiết: [Huong-dan-test-API-Swagger.md](Huong-dan-test-API-Swagger.md).
