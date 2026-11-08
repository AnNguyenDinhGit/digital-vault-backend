# CƠ SỞ PHÁP LÝ VIỆT NAM LIÊN QUAN ĐẾN HỆ THỐNG KHO DI SẢN SỐ (LEGACYVAULT / AETERNAVAULT)

**Dự án:** Hệ thống Quản lý, Lưu trữ Mã hóa và Bàn giao Di sản Số (Digital Legacy Vault - SWP391)  
**Phạm vi áp dụng:** Pháp luật nước Cộng hòa Xã hội Chủ nghĩa Việt Nam hiện hành  

---

## MỤC LỤC
1. [Tổng quan về mô hình nghiệp vụ của Hệ thống LegacyVault](#1-tổng-quan-về-mô-hình-nghiệp-vụ-của-hệ-thống-legacyvault)
2. [Bộ luật Dân sự 2015 (Luật số 91/2015/QH13) – Quyền tài sản, Thừa kế, Di chúc và Người quản lý di sản](#2-bộ-luật-dân-sự-2015-luật-số-912015qh13--quyền-tài-sản-thừa-kế-di-chúc-và-người-quản-lý-di-sản)
3. [Luật Giao dịch điện tử 2023 (Luật số 20/2023/QH15) – Giá trị pháp lý của Thông điệp dữ liệu & Chữ ký số](#3-luật-giao-dịch-điện-tử-2023-luật-số-202023qh15--giá-trị-pháp-lý-của-thông-điệp-dữ-liệu--chữ-ký-số)
4. [Luật Hộ tịch 2014 (Luật số 60/2014/QH13) & Nghị định 123/2015/NĐ-CP – Đăng ký khai tử và Trích lục khai tử](#4-luật-hộ-tịch-2014-luật-số-602014qh13--nghị-định-1232015nđ-cp--đăng-ký-khai-tử-và-trích-lục-khai-tử)
5. [Luật Công chứng 2014 (và Luật Công chứng sửa đổi 2024) – Xác minh pháp lý và Công chứng di chúc/văn bản thừa kế](#5-luật-công-chứng-2014-và-luật-công-chứng-sửa-đổi-2024--xác-minh-pháp-lý-và-công-chứng-di-chúcvăn-bản-thừa-kế)
6. [Nghị định 13/2023/NĐ-CP & Luật An ninh mạng 2018 – Bảo vệ dữ liệu cá nhân và An toàn thông tin](#6-nghị-định-132023nđ-cp--luật-an-ninh-mạng-2018--bảo-vệ-dữ-liệu-cá-nhân-và-an-toàn-thông-tin)
7. [Bảng ánh xạ các tính năng của Hệ thống LegacyVault với Điều luật Việt Nam](#7-bảng-ánh-xạ-các-tính-năng-của-hệ-thống-legacyvault-với-điều-luật-việt-nam)
8. [Giới hạn pháp lý và Khuyến nghị tuân thủ cho Hệ thống](#8-giới-hạn-pháp-lý-và-khuyến-nghị-tuân-thủ-cho-hệ-thống)

---

## 1. Tổng quan về mô hình nghiệp vụ của Hệ thống LegacyVault

Hệ thống **LegacyVault (AeternaVault)** cung cấp nền tảng lưu trữ mã hóa tài sản số/hồ sơ tài sản của **Chủ sở hữu (Owner)** và thiết lập quy trình bàn giao có kiểm soát thông qua 4 vai trò độc lập:
- **Chủ sở hữu (Owner):** Tạo kho di sản (`DigitalVault`), lưu trữ tài sản số/tài liệu đính kèm đã mã hóa (`AES-GCM`) kèm chữ ký số (`RSA-SHA256`), chỉ định **Người thụ hưởng (Beneficiary)** theo tỷ lệ phần trăm (`Allocation %`), chỉ định **Người thi hành (Executor)** và định kỳ thực hiện **Xác nhận còn sống (Proof of Life / Dead-Man Switch)**.
- **Người thi hành (Executor):** Tiếp nhận yêu cầu bàn giao, tải lên **Giấy chứng tử (`DeathCertificate`)** và minh chứng bàn giao (`Evidence`), gửi hồ sơ sang bộ phận Thẩm định pháp lý và thực hiện bàn giao khi đã được phê duyệt.
- **Thẩm định viên pháp lý (Legal Verifier):** Độc lập kiểm tra tính hợp lệ của Giấy chứng tử, chữ ký số trên tài liệu của chủ sở hữu và phê duyệt/từ chối/yêu cầu bổ sung hồ sơ (`Approved` / `Rejected` / `Additional_Info`).
- **Người thụ hưởng (Beneficiary):** Chỉ được mở quyền truy cập tài sản sau khi hồ sơ bàn giao đã được duyệt pháp lý (`Approved`), thực hiện tải xuống và **Xác nhận đã nhận di sản (`ConfirmReceipt`)**.

---

## 2. Bộ luật Dân sự 2015 (Luật số 91/2015/QH13) – Quyền tài sản, Thừa kế, Di chúc và Người quản lý di sản

Đây là văn bản pháp luật nền tảng quan trọng nhất điều chỉnh toàn bộ nghiệp vụ thừa kế, phân chia tài sản và vai trò của các bên trong hệ thống.

### 2.1. Tài sản và Quyền tài sản (Cơ sở của `DigitalAsset`)
- **Điều 105 (Tài sản):**
  - *"1. Tài sản là vật, tiền, giấy tờ có giá và **quyền tài sản**."*
  - **Áp dụng trong hệ thống:** Dữ liệu số, tài khoản số, khóa bí mật, bản quyền tác phẩm số, tên miền, tài liệu hồ sơ nhà đất/tài chính số hóa được lưu trong `DigitalAsset` và `AssetDocument` được pháp luật dân sự bảo vệ dưới góc độ **quyền tài sản** và thông tin bí mật đời tư/bí mật kinh doanh của chủ sở hữu.
- **Điều 115 (Quyền tài sản):**
  - *"Quyền tài sản là quyền trị giá được bằng tiền, bao gồm quyền tài sản đối với đối tượng quyền sở hữu trí tuệ, quyền sử dụng đất và các quyền tài sản khác."*

### 2.2. Thời điểm mở thừa kế & Xác định người để lại di sản đã qua đời
- **Điều 609 (Quyền thừa kế):**
  - *"Cá nhân có quyền lập di chúc để định đoạt tài sản của mình; để lại tài sản của mình cho người thừa kế theo pháp luật; hưởng di sản theo di chúc hoặc theo pháp luật."*
- **Điều 611 (Thời điểm, địa điểm mở thừa kế):**
  - *"1. **Thời điểm mở thừa kế là thời điểm người có tài sản chết.** Trường hợp Tòa án tuyên bố một người là đã chết thì thời điểm mở thừa kế là ngày được xác định tại khoản 2 Điều 71 của Bộ luật này."*
  - **Áp dụng trong hệ thống:**
    - Giải thích lý do hệ thống **bắt buộc phải có Giấy chứng tử (`RequireDeathCertificate`)** và xác nhận pháp lý (`ConfirmOwnerDeceased`) trước khi mở quyền truy cập di sản cho `Beneficiary`.
    - Khi hệ thống đã ghi nhận chủ sở hữu qua đời (`HasOwnerDeathRecord`), tài khoản `Owner` bị khóa toàn bộ quyền chỉnh sửa/check-in còn sống (`ConfirmAlive`) để bảo toàn nguyên trạng di sản tại thời điểm mở thừa kế.
- **Điều 71 (Tuyên bố một người là đã chết):**
  - Cơ sở pháp lý tham khảo cho cơ chế **Dead-Man Switch (Quá hạn xác nhận còn sống `ProofOfLife`)**: Trong hệ thống, khi `Owner` không phản hồi quá thời hạn cấu hình (`IntervalDays + GracePeriodDays`), hệ thống **không tự động chuyển giao tài sản ngay** mà chỉ **kích hoạt hồ sơ yêu cầu xác minh (`DeadManSwitch`)** gửi cho `Executor` để đi xác minh thực tế và nộp Giấy chứng tử/Quyết định của Tòa án tuyên bố một người là đã chết cho `Legal Verifier` thẩm định.

### 2.3. Người thừa kế / Người thụ hưởng (`Beneficiary` & `BeneficiaryAssignment`)
- **Điều 613 (Người thừa kế):**
  - *"Người thừa kế là cá nhân phải là **người còn sống vào thời điểm mở thừa kế** hoặc sinh ra và còn sống sau thời điểm mở thừa kế nhưng đã thành thai trước khi người để lại di sản chết."*
  - **Áp dụng trong hệ thống:** Đây là căn cứ trực tiếp cho quy tắc kiểm tra trong code (`HandoverEvidence.IsUserDeceased`): Hệ thống **chặn không cho chỉ định người đã qua đời làm Người thụ hưởng (`Beneficiary`), Người thi hành (`Executor`) hoặc Thẩm định viên (`Legal Verifier`)**, đồng thời khi duyệt pháp lý (`Review`), hệ thống kiểm tra trạng thái hoạt động của tất cả `Beneficiary`.
- **Điều 624 & Điều 626 (Di chúc và Quyền của người lập di chúc):**
  - Người lập di chúc có quyền:
    1. Chỉ định người thừa kế (`AssignBeneficiary`).
    2. Phân định phần di sản cho từng người thừa kế (`Allocation %` trên từng `DigitalAsset`, tổng không vượt quá `100%`).
- **Điều 640 (Sửa đổi, bổ sung, thay thế, hủy bỏ di chúc):**
  - *"1. Người lập di chúc có thể sửa đổi, bổ sung, thay thế, hủy bỏ di chúc đã lập vào bất cứ lúc nào."*
  - **Áp dụng trong hệ thống:** Khi `Owner` còn sống và kho chưa bước vào trạng thái bàn giao bảo vệ, `Owner` có toàn quyền cập nhật tỷ lệ phân bổ (`UpdateAllocation`), thu hồi phân bổ (`RevokeAllocation`), thu hồi tài liệu (`RevokeDocument`) hoặc thay đổi `Executor` (`AssignExecutor` / `RevokeExecutor`).

### 2.4. Người quản lý di sản và Người phân chia di sản (`Executor`)
- **Điều 616 (Người quản lý di sản):**
  - *"1. Người quản lý di sản là người được chỉ định trong di chúc hoặc do những người thừa kế thỏa thuận cử ra."*
- **Điều 617 (Nghĩa vụ của người quản lý di sản):**
  - Bảo quản di sản; không được tự ý bán, trao đổi, tặng cho, cầm cố, thế chấp hoặc định đoạt tài sản; thông báo về tình trạng di sản; **giao lại di sản theo yêu cầu của người thừa kế hoặc theo nội dung di chúc**.
  - **Áp dụng trong hệ thống:** Vai trò `Executor` trong LegacyVault được thiết kế đúng theo Điều 616 & 617 BLDS 2015: `Executor` chỉ điều phối quy trình nộp minh chứng pháp lý và kích hoạt bàn giao (`StartHandover` / `CompleteHandover`), **không có quyền sửa đổi tài sản hay chiếm đoạt tài liệu mã hóa của Owner**.
- **Điều 620 (Từ chối nhận di sản):**
  - Người thừa kế có quyền từ chối nhận di sản (trừ trường hợp nhằm trốn tránh nghĩa vụ tài sản).
  - **Áp dụng trong hệ thống:** Tính năng `AcceptBeneficiaryAssignment` / `RejectBeneficiaryAssignment` và `ConfirmReceipt` cho phép `Beneficiary` xác nhận hoặc từ chối quyền thụ hưởng minh bạch.

---

## 3. Luật Giao dịch điện tử 2023 (Luật số 20/2023/QH15) – Giá trị pháp lý của Thông điệp dữ liệu & Chữ ký số

Có hiệu lực từ ngày **01/07/2024** (kế thừa và nâng cấp Luật Giao dịch điện tử 2005), kèm theo **Nghị định 130/2018/NĐ-CP** quy định chi tiết thi hành Luật Giao dịch điện tử về chữ ký số và dịch vụ chứng thực chữ ký số.

### 3.1. Thông điệp dữ liệu và Tính toàn vẹn (`AssetDocument`, `HandoverDocument`)
- **Điều 8 & Điều 9 Luật Giao dịch điện tử 2023 (Giá trị pháp lý của thông điệp dữ liệu):**
  - Thông tin trong thông điệp dữ liệu không bị phủ nhận giá trị pháp lý chỉ vì thông tin đó được thể hiện dưới dạng thông điệp dữ liệu.
- **Điều 10 & Điều 11 Luật Giao dịch điện tử 2023 (Thông điệp dữ liệu có giá trị như văn bản / bản gốc):**
  - Thông điệp dữ liệu có giá trị như bản gốc khi bảo đảm **tính toàn vẹn của thông tin** từ thời điểm được khởi tạo lần đầu tiên và thông tin đó có thể truy cập, sử dụng được dưới dạng hoàn chỉnh.
  - **Áp dụng trong hệ thống:** Mỗi tài liệu tải lên hệ thống đều được đóng gói (`ProtectedDocument`) và mã hóa xác thực bằng thuật toán **`AES-GCM` (12-byte Nonce + 16-byte Authentication Tag)**. Nếu bất kỳ byte nào bị can thiệp trái phép trên ổ đĩa, `DocumentProtection.Read` sẽ phát hiện sai lệch thẻ xác thực và từ chối giải mã (`409 Stored document failed integrity validation`).

### 3.2. Chữ ký điện tử và Chữ ký số (`DigitalSignature` & `DocumentProtection.Verify`)
- **Điều 22 & Điều 23 Luật Giao dịch điện tử 2023:**
  - Chữ ký số là chữ ký điện tử sử dụng thuật toán khóa không đối xứng (như RSA), gồm khóa bí mật để ký và khóa công khai (trong Chứng thư số X.509) để kiểm tra.
  - Chữ ký điện tử chuyên dùng bảo đảm an toàn hoặc chữ ký số có giá trị pháp lý tương đương chữ ký tay của cá nhân trên văn bản giấy.
  - **Áp dụng trong hệ thống:**
    - Khi `Owner` tải tài liệu tài sản (`UploadOwnerDocument`) hoặc `Executor` hoàn tất bàn giao (`Complete`), hệ thống bắt buộc truyền kèm chữ ký số RSA (`RequireSignature`).
    - Hàm `DocumentProtection.Verify` kiểm tra chứng thư số X.509 (`X509Certificate2.CreateFromPem`), kiểm tra thời hạn hiệu lực chứng thư (`NotBefore` – `NotAfter`) và xác thực chữ ký bằng `RSA-SHA256` (`RSASignaturePadding.Pkcs1`).
    - Khi `Legal Verifier` duyệt hồ sơ (`Review`), hệ thống kiểm tra lại toàn bộ chữ ký số của `Owner` trên các tài liệu tài sản (`if (d.Status == "Active" && !content.SignatureValid) throw new WorkflowException(...)`).

---

## 4. Luật Hộ tịch 2014 (Luật số 60/2014/QH13) & Nghị định 123/2015/NĐ-CP – Đăng ký khai tử và Trích lục khai tử

- **Điều 32 đến Điều 34 Luật Hộ tịch 2014 (Đăng ký khai tử):**
  - Khi một người qua đời, thân nhân hoặc người có trách nhiệm đi đăng ký khai tử tại Ủy ban nhân dân cấp xã có thẩm quyền và được cấp **Trích lục khai tử (Giấy chứng tử)**.
- **Khoản 6 Điều 4 Luật Hộ tịch 2014:**
  - Trích lục hộ tịch (bao gồm Trích lục khai tử) là văn bản do cơ quan nhà nước có thẩm quyền cấp nhằm chứng minh sự kiện hộ tịch của cá nhân đã đăng ký. Bản sao Trích lục khai tử hoặc bản điện tử có giá trị chứng minh thời điểm tử vong để mở thừa kế theo Điều 611 BLDS 2015.
- **Áp dụng trong hệ thống:**
  - Hệ thống tách biệt rõ mục đích tài liệu `Purpose = "DeathCertificate"` thông qua API `POST /api/handover-requests/{id}/death-certificate`.
  - Hàm `HandoverEvidence.RequireDeathCertificate` bắt buộc hồ sơ bàn giao phải có tài liệu mang mục đích `DeathCertificate` hợp lệ thì mới cho phép gửi thẩm định pháp lý (`RequestLegal`) và phê duyệt (`Review`).

---

## 5. Luật Công chứng 2014 (và Luật Công chứng sửa đổi 2024) – Xác minh pháp lý và Công chứng di chúc/văn bản thừa kế

- **Điều 56 Luật Công chứng 2014 (Công chứng di chúc) & Điều 57, 58 (Công chứng văn bản thỏa thuận phân chia di sản / văn bản khai nhận di sản):**
  - Công chứng viên có trách nhiệm xác minh năng lực hành vi dân sự, ý chí tự nguyện của người lập di chúc; khi khai nhận/phân chia di sản phải kiểm tra Giấy chứng tử của người để lại di sản, giấy tờ chứng minh quyền sở hữu tài sản và mối quan hệ thừa kế.
- **Nguyên tắc xung đột lợi ích (Điều 7 & Điều 44 Luật Công chứng 2014):**
  - Công chứng viên không được công chứng các việc liên quan đến tài sản, lợi ích của bản thân mình hoặc của những người thân thích/có quyền lợi trực tiếp trong vụ việc.
- **Áp dụng trong hệ thống (Cơ chế kiểm soát xung đột lợi ích - Separation of Duties):**
  - Hệ thống LegacyVault áp dụng nguyên tắc độc lập vai trò rất chặt chẽ trong `VaultService` và `ManagementService`:
    1. `Owner` không thể tự chỉ định chính mình làm `Beneficiary` hoặc `Executor`.
    2. `Legal Verifier` (Thẩm định viên pháp lý) **không được phép** đồng thời là `Owner`, `Executor` hoặc `Beneficiary` của chính kho/hồ sơ bàn giao đó (`if (r.ExecutorId == user || r.Vault.OwnerId == user || r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == user)) throw new WorkflowException(403, "Conflicting legal verifier.")`).

---

## 6. Nghị định 13/2023/NĐ-CP & Luật An ninh mạng 2018 – Bảo vệ dữ liệu cá nhân và An toàn thông tin

### 6.1. Nghị định 13/2023/NĐ-CP về Bảo vệ dữ liệu cá nhân (và Luật Bảo vệ dữ liệu cá nhân)
- **Điều 2 (Dữ liệu cá nhân cơ bản và Dữ liệu cá nhân nhạy cảm):**
  - Họ tên, ngày sinh, số điện thoại, email, số Căn cước công dân (`IdentityNumber`), thông tin tài sản, di sản và tình trạng tài khoản thuộc phạm vi **dữ liệu cá nhân cơ bản và dữ liệu cá nhân nhạy cảm**.
- **Điều 19 (Xử lý dữ liệu cá nhân của người đã chết):**
  - Việc xử lý dữ liệu cá nhân của người đã chết phải có sự đồng ý của người đó trước khi chết (thông qua việc chủ động tạo kho và thiết lập quy tắc bàn giao trên hệ thống) hoặc theo quy định về thừa kế/sự đồng ý của thân nhân hợp pháp, và phải tuân thủ đúng mục đích đã cam kết.
- **Điều 26 & Điều 27 (Biện pháp bảo vệ dữ liệu cá nhân):**
  - Tổ chức vận hành hệ thống phải áp dụng biện pháp kỹ thuật bao gồm: **mã hóa dữ liệu**, phân quyền truy cập theo vai trò (RBAC), xác thực đa yếu tố (2FA/OTP) và lưu nhật ký truy cập/xử lý dữ liệu (`AuditLog`).

### 6.2. Luật An toàn thông tin mạng 2015 (Luật số 86/2015/QH13) & Luật An ninh mạng 2018 (Luật số 24/2018/QH14)
- Yêu cầu xác thực danh tính người dùng, bảo mật đường truyền (`HTTPS`), băm mật khẩu an toàn (`PBKDF2-SHA256` với 600.000 vòng lặp trong `IdentityService.HashPassword`), khóa tài khoản tạm thời khi đăng nhập sai nhiều lần (`FailedLoginCount >= 5` khóa 15 phút), và lưu vết kiểm toán hệ thống (`AUDIT_LOGS`) để phục vụ điều tra sự cố.

---

## 7. Bảng ánh xạ các tính năng của Hệ thống LegacyVault với Điều luật Việt Nam

| STT | Tính năng / Nghiệp vụ trong Hệ thống | Thành phần trong Code | Căn cứ Pháp lý Việt Nam |
| :-: | :--- | :--- | :--- |
| 1 | Tạo Kho di sản (`DigitalVault`) & Tài sản số (`DigitalAsset`) | `VaultService.CreateVault`, `CreateAsset` | **Điều 105, Điều 115 BLDS 2015** (Quyền tài sản) |
| 2 | Chỉ định Người thụ hưởng (`Beneficiary`) & Tỷ lệ phân bổ (`Allocation %`) | `VaultService.AssignBeneficiary`, `ManagementService.ChangeAllocation` | **Điều 609, Điều 624, Điều 626, Điều 640 BLDS 2015** (Quyền lập, sửa đổi di chúc & phân định di sản) |
| 3 | Chỉ định Người thi hành (`Executor`) | `ManagementService.AssignExecutor`, `RevokeExecutor` | **Điều 616, Điều 617 BLDS 2015** (Người quản lý di sản và nghĩa vụ bảo quản, bàn giao di sản) |
| 4 | Chặn chỉ định người đã qua đời làm `Beneficiary`, `Executor`, `Verifier` | `HandoverEvidence.IsUserDeceased` | **Điều 613, Điều 616 BLDS 2015** (Người thừa kế và người quản lý di sản phải còn sống/có năng lực hành vi dân sự) |
| 5 | Khóa chỉnh sửa kho và khóa Check-in (`ConfirmAlive`) khi chủ sở hữu đã có hồ sơ báo tử/xác nhận qua đời | `HandoverEvidence.EnsureOwnerNotDeceased`, `WorkflowRepository.HasOwnerDeathRecord` | **Điều 611, Điều 614 BLDS 2015** (Bảo toàn nguyên trạng di sản kể từ thời điểm mở thừa kế) |
| 6 | Bắt buộc nộp Giấy chứng tử (`DeathCertificate`) trước khi gửi duyệt pháp lý & bàn giao | `VaultService.UploadDeathCertificate`, `HandoverEvidence.RequireDeathCertificate` | **Điều 611 BLDS 2015** & **Điều 4, Điều 32–34 Luật Hộ tịch 2014** (Chứng minh sự kiện tử vong bằng Trích lục khai tử) |
| 7 | Kiểm tra xung đột lợi ích giữa `Owner`, `Executor`, `Beneficiary` và `Legal Verifier` | `ManagementService.Legal`, `LegalVerifiers`, `RequestLegal` | **Điều 7, Điều 44 Luật Công chứng 2014** (Độc lập thẩm định, không thẩm định vụ việc có quyền lợi liên quan) |
| 8 | Ký số tài liệu bằng `RSA-SHA256` (Chứng thư X.509) & Mã hóa `AES-GCM` | `DocumentProtection.Verify`, `DocumentProtection.Encrypt` | **Điều 10, 11, 22, 23 Luật Giao dịch điện tử 2023** & **Nghị định 130/2018/NĐ-CP** |
| 9 | Người thụ hưởng chấp nhận/từ chối quyền thụ hưởng & Xác nhận đã nhận di sản | `VaultService.AcceptBeneficiaryAssignment`, `RejectBeneficiaryAssignment`, `ManagementService.ConfirmReceipt` | **Điều 617, Điều 620 BLDS 2015** (Quyền nhận hoặc từ chối nhận di sản, biên nhận bàn giao di sản) |
| 10 | Định danh CCCD 12 số, Xác thực OTP Email, Mã hóa mật khẩu `PBKDF2-SHA256`, Nhật ký kiểm toán `AuditLog` | `IdentityService`, `AdminService.GetAuditLogsAsync` | **Nghị định 13/2023/NĐ-CP** (Bảo vệ dữ liệu cá nhân, Điều 19 về dữ liệu người đã chết) & **Luật An toàn thông tin mạng 2015** |

---

## 8. Giới hạn pháp lý và Khuyến nghị tuân thủ cho Hệ thống

Để hệ thống vận hành đúng chuẩn pháp luật Việt Nam trong thực tiễn, cần lưu ý 3 giới hạn pháp lý quan trọng sau:

1. **Đối với các tài sản bắt buộc phải đăng ký quyền sở hữu theo pháp luật (Bất động sản, Ô tô/Xe máy, Cổ phần, Sổ tiết kiệm ngân hàng):**
   - Theo **Điều 627 – 630, Điều 657 BLDS 2015** và **Luật Đất đai / Luật Công chứng**, hệ thống LegacyVault đóng vai trò là **Kho lưu trữ thông tin chỉ dẫn, hồ sơ số hóa và truyền đạt di nguyện an toàn** cho Người thi hành và Người thụ hưởng. Việc sang tên quyền sử dụng đất hoặc rút tiền tại ngân hàng ngoài đời thực vẫn phải thực hiện thủ tục **Khai nhận/Phân chia di sản thừa kế tại Tổ chức hành nghề công chứng** dựa trên hồ sơ gốc và di chúc hợp pháp.
2. **Quy định về "Người thừa kế không phụ thuộc vào nội dung của di chúc" (Điều 644 BLDS 2015):**
   - Theo Điều 644 BLDS 2015, con chưa thành niên, cha, mẹ, vợ, chồng hoặc con thành niên mà không có khả năng lao động vẫn được hưởng phần di sản bằng $2/3$ suất của một người thừa kế theo pháp luật nếu họ không được người lập di chúc cho hưởng di sản hoặc chỉ cho hưởng phần ít hơn $2/3$ suất đó. Khi thẩm định pháp lý ngoài thực tế, `Legal Verifier` cần lưu ý quy định này đối với tổng khối di sản.
3. **Tích hợp Chứng thư số công cộng (CA công cộng được Bộ TT&TT cấp phép):**
   - Khi triển khai thương mại chính thức tại Việt Nam, phần xác thực chữ ký số (`DocumentProtection.Verify`) và định danh điện tử nên kết nối với các nhà cung cấp dịch vụ chứng thực chữ ký số công cộng hợp pháp (như VNPT-CA, Viettel-CA, FPT-CA hoặc Chữ ký số từ xa Remote Signing trên VNeID theo Nghị định 23/2025/NĐ-CP / Nghị định 130/2018/NĐ-CP).

