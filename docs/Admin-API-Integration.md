# Tài liệu Tích hợp API Admin (Luồng 5: Quản trị Hệ thống)

> **Dành cho:** Frontend Developers  
> **Phiên bản:** 1.0  
> **Cập nhật lần cuối:** 26/10/2026  
> **Base URL (Dev):** `https://localhost:7015/api/admin` (hoặc `http://localhost:5131/api/admin` qua proxy)

---

## 🔐 1. Quy định Xác thực & Quyền hạn (Authentication)

Tất cả các API trong module Admin đều yêu cầu access token mang vai trò **Admin**:

- **Header bắt buộc:**
  ```http
  Authorization: Bearer <Access_Token_Cua_Admin>
  Content-Type: application/json
  ```
- **Xử lý lỗi xác thực:**
  - `401 Unauthorized`: Token không hợp lệ, hết hạn hoặc chưa đăng nhập.
  - `403 Forbidden`: Người dùng không có vai trò `Admin` trong tài khoản.

---

## 📋 2. Danh sách API Chi tiết

### NHÓM 1: QUẢN LÝ NGƯỜI DÙNG & MA TRẬN PHÂN QUYỀN (RBAC)

#### 1.1. Lấy danh sách Người dùng (Phân trang & Lọc)
- **Method / Endpoint:** `GET /api/admin/users`
- **Query Parameters:**
  - `page` (int, default `1`): Số trang.
  - `pageSize` (int, default `20`): Số bản ghi trên mỗi trang.
  - `role` (string, optional): Lọc theo vai trò (`Owner`, `Executor`, `LegalVerifier`, `Beneficiary`, `Admin`).
  - `status` (string, optional): Lọc theo trạng thái (`Active`, `Disabled`, `Locked`).
- **Response `200 OK`:**
  ```json
  {
    "page": 1,
    "pageSize": 20,
    "total": 45,
    "items": [
      {
        "userId": 1,
        "fullName": "Nguyễn Văn A",
        "email": "owner@example.com",
        "phone": "0912345678",
        "identityNumber": "001200001234",
        "status": "Active",
        "roles": [ "Owner" ],
        "createdAt": "2026-10-15T08:30:00Z",
        "updatedAt": "2026-10-20T10:15:00Z"
      }
    ]
  }
  ```

---

#### 1.2. Tạo tài khoản Người dùng mới bởi Admin
- **Method / Endpoint:** `POST /api/admin/users`
- **Request Body:**
  ```json
  {
    "fullName": "Trần Thị B",
    "email": "executor@example.com",
    "password": "SecurePassword123!",
    "phone": "0987654321",
    "identityNumber": "079200005678",
    "roles": [ "Executor" ]
  }
  ```
  *(Lưu ý: Mật khẩu tối thiểu 12 ký tự, email không được trùng lặp).*
- **Response `201 Created`:**
  ```json
  {
    "userId": 15,
    "fullName": "Trần Thị B",
    "email": "executor@example.com",
    "phone": "0987654321",
    "identityNumber": "079200005678",
    "status": "Active",
    "roles": [ "Executor" ],
    "createdAt": "2026-10-26T11:00:00Z",
    "updatedAt": "2026-10-26T11:00:00Z"
  }
  ```

---

#### 1.3. Cập nhật Vai trò (Roles) của Người dùng
- **Method / Endpoint:** `PUT /api/admin/users/{userId}/roles`
- **Path Parameter:** `userId` (int) - ID người dùng cần sửa.
- **Request Body:**
  ```json
  {
    "roles": [ "Owner", "Executor" ]
  }
  ```
- **Response `204 No Content`:** Thành công (Hệ thống tự động ghi nhật ký Audit Log).

---

#### 1.4. Cập nhật Trạng thái Tài khoản (Khóa / Mở khóa)
- **Method / Endpoint:** `PUT /api/admin/users/{userId}/status`
- **Path Parameter:** `userId` (int) - ID người dùng cần cập nhật.
- **Request Body:**
  ```json
  {
    "status": "Disabled"
  }
  ```
  *(Giá trị hợp lệ: `"Active"`, `"Disabled"`, `"Locked"`).*
- **Response `204 No Content`:** Thành công.

---

#### 1.5. Chỉnh sửa Thông tin Người dùng (Bởi Admin)
- **Method / Endpoint:** `PUT /api/admin/users/{userId}`
- **Path Parameter:** `userId` (int) - ID người dùng cần sửa.
- **Request Body:**
  ```json
  {
    "fullName": "Nguyễn Văn A (Đã cập nhật)",
    "phone": "0912345678",
    "identityNumber": "001200999888"
  }
  ```
- **Response `200 OK`:** Trả về `UserAdminDto` đã cập nhật.

---

#### 1.6. Đặt lại Mật khẩu Người dùng (Reset Password bởi Admin)
- **Method / Endpoint:** `POST /api/admin/users/{userId}/reset-password`
- **Path Parameter:** `userId` (int) - ID người dùng cần đặt lại mật khẩu.
- **Request Body:**
  ```json
  {
    "newPassword": "NewAdminPassword123!"
  }
  ```
  *(Lưu ý: Mật khẩu mới tối thiểu 12 ký tự).*
- **Response `200 OK`:**
  ```json
  {
    "message": "Đặt lại mật khẩu cho người dùng thành công."
  }
  ```

---

#### 1.7. Lấy Bảng Ma trận Phân quyền (RBAC Matrix)
- **Method / Endpoint:** `GET /api/admin/roles`
- **Mục đích:** Hiển thị trang quản lý vai trò, danh mục quyền chi tiết của 5 vai trò hệ thống và số lượng người dùng đang hoạt động trong từng vai trò.
- **Response `200 OK`:**
  ```json
  [
    {
      "roleId": 1,
      "roleName": "Owner",
      "description": "Chủ sở hữu kho di sản số",
      "permissions": [
        "vault:create",
        "vault:manage",
        "asset:create",
        "asset:manage",
        "executor:assign",
        "beneficiary:assign",
        "proof_of_life:ping",
        "audit:view_own"
      ],
      "activeUsersCount": 28
    },
    {
      "roleId": 2,
      "roleName": "Executor",
      "description": "Người thi hành / Người bàn giao",
      "permissions": [
        "handover:view_assigned",
        "handover:accept",
        "handover:decline",
        "handover:submit_proof",
        "handover:start",
        "handover:complete"
      ],
      "activeUsersCount": 10
    },
    {
      "roleId": 3,
      "roleName": "LegalVerifier",
      "description": "Thẩm định viên pháp lý",
      "permissions": [
        "legal:view_queue",
        "legal:review_evidence",
        "legal:approve",
        "legal:reject",
        "legal:request_info",
        "legal:sign"
      ],
      "activeUsersCount": 3
    },
    {
      "roleId": 4,
      "roleName": "Beneficiary",
      "description": "Người thụ hưởng di sản",
      "permissions": [
        "claim:view_cases",
        "claim:download_asset",
        "claim:decrypt",
        "claim:confirm_receipt"
      ],
      "activeUsersCount": 14
    },
    {
      "roleId": 5,
      "roleName": "Admin",
      "description": "Quản trị viên hệ thống",
      "permissions": [
        "admin:users_manage",
        "admin:roles_view",
        "admin:audit_logs",
        "admin:security_alerts",
        "admin:system_config",
        "admin:key_rotation",
        "admin:integrations_manage"
      ],
      "activeUsersCount": 2
    }
  ]
  ```

---

### NHÓM 2: GIÁM SÁT NHẬT KÝ & CẢNH BÁO AN NINH (AUDIT & ALERTS)

#### 2.1. Tra cứu Nhật ký Hoạt động (Audit Logs)
- **Method / Endpoint:** `GET /api/admin/audit-logs`
- **Query Parameters:**
  - `page` (int, default `1`)
  - `pageSize` (int, default `20`)
  - `action` (string, optional): Lọc theo tên hành động (vd: `AdminLogin`, `FailedLogin`, `UpdateSystemConfig`, `RotateEncryptionKeys`, ...).
- **Response `200 OK`:**
  ```json
  {
    "page": 1,
    "pageSize": 20,
    "total": 120,
    "items": [
      {
        "logId": 201,
        "userId": 1,
        "userEmail": "admin@example.com",
        "action": "UpdateSystemConfig",
        "entityType": "SystemConfig",
        "entityId": null,
        "result": "Success",
        "ipaddress": "192.168.1.1",
        "timestamp": "2026-10-26T11:05:00Z"
      }
    ]
  }
  ```

---

#### 2.2. Dashboard & Cảnh báo An ninh Tự động (Security Alerts Engine)
- **Method / Endpoint:** `GET /api/admin/security/alerts`
- **Mục đích:** Rule Engine quét dữ liệu trong 24 giờ qua để phát hiện dấu hiệu bất thường (đăng nhập thất bại liên tiếp, IP quét Brute-force, tỷ lệ thao tác lỗi tăng vọt).
- **Response `200 OK`:**
  ```json
  {
    "totalAuditLogs24h": 140,
    "failedActions24h": 6,
    "activeAlertsCount": 2,
    "failureRatePercentage": 4.29,
    "recentAlerts": [
      {
        "alertId": "ALERT-SPIKE-FAIL",
        "severity": "Warning",
        "title": "Tỷ lệ thao tác thất bại tăng bất thường",
        "description": "Ghi nhận 6 thao tác thất bại trong 24 giờ qua (chiếm 4.29% tổng số thao tác).",
        "targetUserEmail": null,
        "ipAddress": null,
        "detectedAt": "2026-10-26T11:00:00Z",
        "status": "Active"
      },
      {
        "alertId": "ALERT-FAIL-USER-5",
        "severity": "Critical",
        "title": "Phát hiện người dùng có nhiều thao tác lỗi liên tiếp",
        "description": "Người dùng ID 5 (baduser@example.com) có 4 lần thao tác không thành công.",
        "targetUserEmail": "baduser@example.com",
        "ipAddress": "118.69.12.34",
        "detectedAt": "2026-10-26T10:45:00Z",
        "status": "Active"
      }
    ],
    "topFailedActions": [
      { "key": "FailedLogin", "value": 4 },
      { "key": "AccessRevokedDocument", "value": 2 }
    ]
  }
  ```

---

### NHÓM 3: CẤU HÌNH THAM SỐ HỆ THỐNG & KEY ROTATION

#### 3.1. Xem Bộ Tham số Cấu hình Hệ thống
- **Method / Endpoint:** `GET /api/admin/system/config`
- **Response `200 OK`:**
  ```json
  {
    "encryptionPolicy": {
      "defaultAlgorithm": "AES-256-GCM",
      "keyRotationIntervalDays": 90,
      "minPasswordLength": 12,
      "require2FAForAdmin": true
    },
    "proofOfLifePolicy": {
      "defaultPingIntervalDays": 30,
      "gracePeriodDays": 7,
      "warningDaysBeforeGrace": 3
    },
    "storagePolicy": {
      "maxUploadSizeBytes": 10485760,
      "allowedExtensions": [ ".pdf", ".png", ".jpg", ".jpeg", ".docx" ]
    },
    "backupPolicy": {
      "autoBackupEnabled": true,
      "cronSchedule": "0 0 * * *",
      "retentionDays": 30
    },
    "lastUpdatedAt": "2026-10-26T10:30:00Z",
    "updatedByAdminEmail": "admin@legacyvault.local"
  }
  ```

---

#### 3.2. Cập nhật Tham số Cấu hình Hệ thống
- **Method / Endpoint:** `PUT /api/admin/system/config`
- **Request Body:** *(Gửi trường cần cập nhật, các trường khác có thể để null)*
  ```json
  {
    "encryptionPolicy": {
      "defaultAlgorithm": "AES-256-GCM",
      "keyRotationIntervalDays": 60,
      "minPasswordLength": 12,
      "require2FAForAdmin": true
    },
    "proofOfLifePolicy": {
      "defaultPingIntervalDays": 45,
      "gracePeriodDays": 10,
      "warningDaysBeforeGrace": 3
    }
  }
  ```
- **Response `200 OK`:** Trả về cấu hình mới cập nhật.

---

#### 3.3. Thống kê Xoay vòng Khóa Mã hóa (Key Rotation Summary)
- **Method / Endpoint:** `GET /api/admin/system/key-rotation`
- **Response `200 OK`:**
  ```json
  {
    "totalKeys": 50,
    "activeKeys": 48,
    "rotatedKeys": 12,
    "keysNeedingRotation": 3,
    "rotationIntervalDays": 90,
    "lastRotatedAt": "2026-10-15T09:00:00Z",
    "keys": [
      {
        "keyId": 1,
        "assetId": 10,
        "assetName": "Tài khoản Binance",
        "algorithm": "AES-256-GCM",
        "keyVersion": 1,
        "status": "Active",
        "createdAt": "2026-06-01T00:00:00Z",
        "rotatedAt": null,
        "needsRotation": true
      }
    ]
  }
  ```

---

#### 3.4. Kích hoạt Xoay vòng Khóa (Trigger Key Rotation)
- **Method / Endpoint:** `POST /api/admin/system/key-rotation/rotate`
- **Mục đích:** Tự động tăng `KeyVersion` (+1) và ghi nhận thời gian `RotatedAt` cho các khóa mã hóa tài sản đang hoạt động, đồng thời lưu Audit Log.
- **Request Body:** Không cần body (`{}`).
- **Response `200 OK`:** Trả về `KeyRotationSummaryDto` sau khi đã cập nhật.

---

### NHÓM 4: QUẢN LÝ TÍCH HỢP BÊN THỨ BA (THIRD-PARTY INTEGRATIONS)

#### 4.1. Xem Danh sách & Trạng thái Kết nối Dịch vụ Tích hợp
- **Method / Endpoint:** `GET /api/admin/integrations`
- **Response `200 OK`:**
  ```json
  [
    {
      "serviceId": "ekyc-vnpt",
      "serviceName": "Hệ thống eKYC Định danh điện tử",
      "serviceType": "eKYC",
      "provider": "VNPT AI",
      "status": "Connected",
      "latencyMs": 35,
      "lastCheckedAt": "2026-10-26T11:00:00Z",
      "endpointUrl": "https://api.vnpt.vn/ekyc/v1",
      "errorMessage": null
    },
    {
      "serviceId": "ca-vnpt",
      "serviceName": "Dịch vụ Chữ ký số Chứng thư CA",
      "serviceType": "DigitalSignature",
      "provider": "VNPT-CA",
      "status": "Connected",
      "latencyMs": 40,
      "lastCheckedAt": "2026-10-26T11:00:00Z",
      "endpointUrl": "https://ca.vnpt.vn/api/sign",
      "errorMessage": null
    },
    {
      "serviceId": "notary-gateway",
      "serviceName": "Cổng Công chứng điện tử Pháp lý",
      "serviceType": "ElectronicNotary",
      "provider": "VN Notary Gateway",
      "status": "Connected",
      "latencyMs": 55,
      "lastCheckedAt": "2026-10-26T11:00:00Z",
      "endpointUrl": "https://notary.gov.vn/api/v2",
      "errorMessage": null
    },
    {
      "serviceId": "blob-storage",
      "serviceName": "Lưu trữ tài liệu mã hóa Blob Storage",
      "serviceType": "CloudStorage",
      "provider": "Azure Blob Storage",
      "status": "Connected",
      "latencyMs": 28,
      "lastCheckedAt": "2026-10-26T11:00:00Z",
      "endpointUrl": "https://legacyvault.blob.core.windows.net",
      "errorMessage": null
    },
    {
      "serviceId": "smtp-gateway",
      "serviceName": "Cổng phát hành OTP & Email thông báo",
      "serviceType": "EmailGateway",
      "provider": "SMTP Relay Service",
      "status": "Connected",
      "latencyMs": 30,
      "lastCheckedAt": "2026-10-26T11:00:00Z",
      "endpointUrl": "smtp://mail.legacyvault.local:587",
      "errorMessage": null
    }
  ]
  ```

---

#### 4.2. Kiểm tra Kết nối Thời gian thực (Health Check / Ping)
- **Method / Endpoint:** `POST /api/admin/integrations/{serviceId}/ping`
- **Path Parameter:** `serviceId` (`ekyc-vnpt`, `ca-vnpt`, `notary-gateway`, `blob-storage`, `smtp-gateway`).
- **Response `200 OK`:**
  ```json
  {
    "serviceId": "ekyc-vnpt",
    "success": true,
    "latencyMs": 27,
    "message": "Kết nối tới dịch vụ Hệ thống eKYC Định danh điện tử (VNPT AI) thành công.",
    "testedAt": "2026-10-26T11:15:20Z"
  }
  ```
- **Response `404 Not Found`:** Nếu truyền `serviceId` không tồn tại.
