# BÁO CÁO TỔNG HỢP CÁC CHỨC NĂNG ĐÃ NÂNG CẤP & TRIỂN KHAI

---

## 1. Bổ sung trường Căn cước công dân (CCCD - `IdentityNumber`)

### **Mục đích**
Phục vụ nhu cầu xác minh danh tính người dùng trong luồng đăng ký tài khoản (`Register`) và phục vụ xác minh pháp lý khi bàn giao di sản (`Handover`).

### **Các file đã chỉnh sửa & Nội dung thay đổi**

1. **`LegacyVault.BLL/DTOs/RegistrationDtos.cs`**
   * **`RegisterInput`**: Bổ sung thuộc tính `IdentityNumber` nhận từ Client:
     ```csharp
     [StringLength(20)]
     public string? IdentityNumber { get; init; }
     ```
   * **`RegistrationDto`**: Đồng bộ record kết quả trả về:
     ```csharp
     public sealed record RegistrationDto(
         int UserId, 
         string FullName, 
         string Email, 
         string? Phone, 
         string? IdentityNumber, 
         string[] Roles
     );
     ```

2. **`LegacyVault.BLL/Services/IdentityService.cs`**
   * **Hàm `Register`**: Cập nhật gán dữ liệu `IdentityNumber` vào Entity `User` khi khởi tạo:
     ```csharp
     IdentityNumber = string.IsNullOrWhiteSpace(input.IdentityNumber) ? null : input.IdentityNumber.Trim()
     ```
   * **Hàm `UpdateProfile` (Bổ sung tiện ích)**: Tạo thêm logic cập nhật thông tin cá nhân (`FullName`, `Phone`, `IdentityNumber`) cho người dùng.

---

## 2. Triển khai Chức năng Upload & Mã hóa Tài liệu (`UploadDocumentAsync`)

### **Mục đích**
Cho phép chủ sở hữu (`Owner`) tải lên các tài liệu đính kèm tài sản di sản (`DigitalAsset`). Tài liệu phải được mã hóa chuẩn **AES-256-GCM**, ký số (Digital Signature), lưu vào bộ nhớ cục bộ (`DocumentStore`) và lưu metadata vào Database (`AssetDocument`).

### **Cấu trúc & Kiến trúc áp dụng**

* **Mã hóa Authenticated Encryption (AES-GCM)**: Tận dụng class `DocumentProtection` sẵn có của dự án. File được gói cùng chữ ký số (`ProtectedDocument`) trước khi chuyển thành mảng byte đã mã hóa.
* **Lưu trữ Cục bộ (`IDocumentStore`)**: Sử dụng class `DocumentStore` lưu file đã mã hóa dưới dạng file vật lý với định nghĩa GUID ngẫu nhiên (`Write(byte[] bytes)`).

### **Các file đã chỉnh sửa & Nội dung thay đổi**

1. **`LegacyVault.BLL/DTOs/UploadDocumentInput.cs`** (Tạo mới DTO):
   * Định nghĩa DTO tiếp nhận thông tin tải lên từ Controller:
     ```csharp
     public class UploadDocumentInput
     {
         [Required]
         public IFormFile File { get; set; } = null!;
         public int SignerId { get; set; }
     }
     ```

2. **`LegacyVault.BLL/Services/VaultService.cs`**:
   * **Dependency Injection**: Tiêm `IVaultRepository repository`, `IDocumentStore store`, và `DocumentProtection protection` qua Primary Constructor.
   * **Tự động phân công Executor (`GetOrAssignRandomExecutor`)**: Thêm logic chọn ngẫu nhiên một Executor đang `Active` trong hệ thống nếu Két (`DigitalVault`) chưa được phân công.
   * **Viết hàm `UploadDocumentAsync`**:
     * **Bước 1**: Xác thực quyền sở hữu Két/Tài sản của `Owner` bằng `Owned(user, assetId, ct)`.
     * **Bước 2**: Đọc `IFormFile` thành byte array và validate chuẩn magic bytes/dung lượng/MIME type qua `Validate(uploadFile)`.
     * **Bước 3**: Gom thành `ProtectedDocument` và thực hiện mã hóa AES-GCM bằng `protection.Encrypt(protectedDoc)`.
     * **Bước 4**: Lưu mảng byte mã hóa ra ổ đĩa local qua `store.Write(encryptedBytes, ct)`.
     * **Bước 5**: Tạo record `AssetDocument` chứa `StoragePath`, `AssetId`, `UploadedBy`, `IsEncrypted = true` và lưu vào DB qua `repository.Add()` & `repository.Save(ct)`.

---

## 3. Tổng kết luồng dữ liệu (Data Flow) của tính năng Upload

Client (FormFile) -> Validate -> UploadFile -> AesGcm Encrypt -> ProtectedDocument (Bytes) -> Write to Disk -> DocumentStore (GUID) -> Save Metadata -> SQL Server