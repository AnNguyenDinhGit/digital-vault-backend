# Đặc tả quy tắc đổi gói, hết hạn và vòng đời gói dịch vụ (Subscription)

Tài liệu đặc tả nghiệp vụ xử lý đổi gói, hết hạn, thanh toán và kiểm soát quyền tài khoản dành cho Frontend và Backend.

---

## 1. Nguyên tắc cốt lõi
- **Thời hạn gói:** Mọi gói dịch vụ tính theo chu kỳ tháng (30 ngày).
- **Kiểm soát bảo mật:** Backend tự động kiểm tra độc lập ở tầng server; không tin cậy dữ liệu tính tiền từ Frontend.
- **Không hoàn tiền:** Hệ thống không thực hiện hoàn lại tiền khi nâng cấp/đổi gói.
- **Gói miễn phí:** Có giá trị vô thời hạn, tự động gán cho người dùng mới khi đăng ký.
- **Gói chờ kích hoạt (`Scheduled`):** Tự động chuyển sang `Active` khi tới `startDate` (thời điểm gói trước đó hết hạn).

---

## 2. Quy tắc đổi gói theo mốc thời gian

Xét gói trả phí đang có hiệu lực $S$ với ngày bắt đầu `start` và ngày hết hạn `end`:

| Thời điểm hiện tại (`now`) | Gói muốn mua | Kết quả xử lý & Mã phản hồi |
|---|---|---|
| **`now ≤ start + 3 ngày`** | Giá cao hơn gói hiện tại | **Cho phép** (`allowed = true`, `changeType = "Upgrade"`).<br>• Số tiền thanh toán: `amountDue = giá gói mới − giá gói hiện tại`.<br>• Gói mới kích hoạt ngay trong 30 ngày kể từ lúc thanh toán.<br>• Gói cũ chuyển sang trạng thái `Replaced`. |
| **`now ≤ start + 3 ngày`** | Giá thấp hơn hoặc bằng (kể cả gói miễn phí) | **Chặn** (`allowed = false`).<br>• Trả HTTP `409 Conflict`.<br>• `reasonCode = "UPGRADE_PRICE_TOO_LOW"`.<br>• Thông báo: *"Trong 3 ngày đầu chỉ được nâng cấp lên gói có giá cao hơn gói hiện tại."* |
| **`start + 3 ngày < now < end − 5 ngày`** | Bất kỳ gói nào | **Chặn** (`allowed = false`).<br>• Trả HTTP `409 Conflict`.<br>• `reasonCode = "PLAN_CHANGE_LOCKED"`.<br>• Thông báo: *"Chỉ đổi gói được từ <end − 5 ngày>."* |
| **`end − 5 ngày ≤ now < end`** | Mọi gói (kể cả thấp hơn) | **Cho phép** (`allowed = true`, `changeType = "Renew"` hoặc `"Scheduled"`).<br>• Thu đủ 100% giá gói mới.<br>• Tạo đăng ký trạng thái `Scheduled` có `startDate = end` của gói hiện tại.<br>• **Lưu ý:** Chỉ cho phép tối đa 1 gói chờ; nếu đã có gói chờ thì trả `409 Conflict` (`SCHEDULED_PLAN_EXISTS`). |
| **`now ≥ end` hoặc chưa có gói trả phí** | Bất kỳ gói nào | **Cho phép** (`allowed = true`, `changeType = "New"` hoặc `"Renew"`).<br>• Thu đủ 100% giá gói.<br>• Kích hoạt gói mới ngay lập tức. |

---

## 3. Gói miễn phí (Free Tier) & Tự động gán cho người mới
1. **Tự động gán:** Khi người dùng mới đăng ký tài khoản (qua form đăng ký hoặc Google OAuth), hệ thống tự động gán gói Free (`Price = 0`) với trạng thái `Active` và thời hạn vô thời hạn (`EndDate` được đặt ~100 năm).
2. **Endpoint `POST /api/payments/select-free`:**
   - Chỉ được sử dụng khi tài khoản **không có gói trả phí đang còn hiệu lực**.
   - Nếu đang có gói trả phí hiệu lực mà gọi endpoint này, server trả về lỗi `409 Conflict` (`CANNOT_CANCEL_PAID_SUBSCRIPTION`) để ngăn việc hủy gói trả phí.

---

## 4. Chế độ chỉ xem (Read-Only) & Mã lỗi `SUBSCRIPTION_EXPIRED`

### 4.1. Điều kiện rơi vào Chế độ chỉ xem
Tài khoản Owner chuyển sang chế độ **Chỉ xem** (`isReadOnly = true`) khi:
- Người dùng **từng có gói trả phí**, nhưng gói đó đã hết hạn (`now >= end`).
- Hiện tại **không có gói trả phí nào đang Active**.
- Hiện tại **chưa có gói đăng ký chờ kích hoạt (`Scheduled`)**.

*(Lưu ý: Tài khoản không bị tụt về hạn mức của gói miễn phí mà sẽ bị khóa quyền ghi).*

### 4.2. Giới hạn thao tác
Khi tài khoản ở chế độ chỉ xem (`isReadOnly = true`):
- **Bị chặn:** Mọi API ghi / thay đổi dữ liệu của Owner đều bị chặn và trả về HTTP `403 Forbidden` kèm mã lỗi riêng:
  - Tạo kho (`POST /api/owner/vaults`)
  - Cập nhật kho (`PUT /api/owner/vaults/{vaultId}`)
  - Đóng kho (`POST /api/owner/vaults/{vaultId}/close`)
  - Thêm tài sản (`POST /api/owner/assets`)
  - Sửa tài sản (`PUT /api/owner/assets/{assetId}`)
  - Thu hồi tài sản (`POST /api/owner/assets/{assetId}/revoke`)
  - Chỉ định người thụ hưởng (`POST /api/owner/assets/{assetId}/beneficiaries`)
  - Sửa / thu hồi phân bổ (`PUT / POST .../allocations/...`)
  - Tải lên / thu hồi tài liệu (`POST .../documents/...`)
  - Gán / thu hồi Executor (`PUT / POST .../executor/...`)
  - Tạo / hủy yêu cầu bàn giao mới (`POST .../handovers/...`)

- **Format mã lỗi trả về (HTTP 403):**
  ```json
  {
    "status": 403,
    "code": "SUBSCRIPTION_EXPIRED",
    "errorCode": "SUBSCRIPTION_EXPIRED",
    "detail": "Gói dịch vụ đã hết hạn. Tài khoản đang ở chế độ chỉ xem."
  }
  ```

- **Các API vẫn được phép hoạt động bình thường:**
  - Mọi API đọc thông tin (`GET /api/owner/...`).
  - Điểm danh sự sống (`POST /api/owner/life-confirmation`).
  - Toàn bộ API thanh toán mua gói (`/api/payments/...`).
  - Đổi mật khẩu, hồ sơ cá nhân, đăng xuất (`/api/auth/...`, `/api/account/...`).

### 4.3. Hồ sơ bàn giao khi chủ kho đã mất
- Dù gói dịch vụ của Owner đã hết hạn, toàn bộ các bước xử lý của **Executor**, **LegalVerifier** và **Beneficiary** (nộp giấy chứng tử, tải tài liệu bàn giao, thẩm định pháp lý, phê duyệt, nhận bàn giao) **vẫn chạy bình thường không bị gián đoạn**.

---

## 5. Danh sách API phục vụ luồng Subscription

### 5.1. Báo giá đổi gói: `GET /api/payments/quote?planId={planId}`
Dùng để Frontend kiểm tra trước và hiển thị giao diện phù hợp (cho phép đổi gói hay chặn, số tiền cần thanh toán, lý do chặn nếu có).

- **Quyền:** Đăng nhập (`Bearer JWT`)
- **Query Params:** `planId` (int, bắt buộc)
- **Response `200 OK` (Khi được phép):**
  ```json
  {
    "allowed": true,
    "reasonCode": null,
    "reasonMessage": null,
    "changeType": "Upgrade", // "New" | "Upgrade" | "Renew" | "Scheduled"
    "amountDue": 150000.0,
    "startsAt": "2026-11-04T12:00:00Z",
    "endsAt": "2026-12-04T12:00:00Z"
  }
  ```
- **Response `200 OK` (Khi bị chặn):**
  ```json
  {
    "allowed": false,
    "reasonCode": "PLAN_CHANGE_LOCKED", // "UPGRADE_PRICE_TOO_LOW" | "PLAN_CHANGE_LOCKED" | "SCHEDULED_PLAN_EXISTS"
    "reasonMessage": "Chỉ đổi gói được từ 2026-11-25 12:00:00.",
    "changeType": null,
    "amountDue": 0.0,
    "startsAt": null,
    "endsAt": null
  }
  ```

### 5.2. Thông tin gói hiện tại: `GET /api/payments/me/subscription`
- **Quyền:** Đăng nhập (`Bearer JWT`)
- **Response `200 OK`:**
  ```json
  {
    "hasActiveSubscription": true,
    "subscription": {
      "subscriptionId": 12,
      "status": "Active",
      "startDate": "2026-11-01T08:00:00Z",
      "endDate": "2026-12-01T08:00:00Z",
      "plan": {
        "planId": 2,
        "planName": "Gói Tiêu Chuẩn",
        "maxVaults": 5,
        "maxStorageBytes": 10485760,
        "price": 100000.0,
        "durationDays": 30
      }
    },
    "scheduledSubscription": {
      "subscriptionId": 13,
      "status": "Scheduled",
      "startDate": "2026-12-01T08:00:00Z",
      "endDate": "2026-12-31T08:00:00Z",
      "plan": {
        "planId": 3,
        "planName": "Gói Cao Cấp",
        "maxVaults": 20,
        "maxStorageBytes": 52428800,
        "price": 250000.0,
        "durationDays": 30
      }
    },
    "upgradeWindowEndsAt": "2026-11-04T08:00:00Z",
    "renewalWindowStartsAt": "2026-11-26T08:00:00Z",
    "daysLeft": 27,
    "isReadOnly": false
  }
  ```

### 5.3. Tạo Checkout VNPay: `POST /api/payments/vnpay/checkout`
- **Quyền:** Đăng nhập (`Bearer JWT`)
- **Body:**
  ```json
  {
    "planId": 3
  }
  ```
- **Xử lý phía Server:**
  - Tự động gọi bảng Quote để kiểm tra tính hợp lệ và lấy số tiền chính xác (`amountDue`).
  - Nếu vi phạm mốc thời gian: trả lỗi `409 Conflict` kèm `reasonCode`.
  - Nếu hợp lệ: sinh URL VNPay hoặc kích hoạt ngay nếu số tiền bằng 0.
- **Response `200 OK`:**
  ```json
  {
    "transactionCode": "2611041930250001",
    "amount": 150000.0,
    "paymentUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?..."
  }
  ```

### 5.4. Đăng ký gói miễn phí: `POST /api/payments/select-free`
- **Quyền:** Đăng nhập (`Bearer JWT`)
- **Body:**
  ```json
  {
    "planId": 1
  }
  ```
- **Lưu ý:** Bị chặn với mã lỗi `409 Conflict` nếu tài khoản đang có gói trả phí hiệu lực.

