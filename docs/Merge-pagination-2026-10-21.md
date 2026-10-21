# Contract phân trang sau merge main vào dung

Giữ chức năng xác nhận còn sống và Executor/LegalVerifier trên dung, đồng thời giữ thay đổi phân trang từ main. Không sửa frontend hoặc chạy migration.

Các GET dưới đây nhận `page` (mặc định 1), `pageSize` (mặc định 20, tối đa 100) và trả `{ "page": 1, "pageSize": 20, "items": [] }` thay vì mảng trực tiếp:

- `/api/owner/vaults`
- `/api/owner/assets`
- `/api/owner/beneficiaries` (giữ filter `assetId`)
- `/api/owner/assets/{assetId}/allocations`
- `/api/owner/assets/{assetId}/documents`
- `/api/executor/assignments`
- `/api/executor/handovers`
- `/api/beneficiary/assets`

Frontend đọc `response.items` và gửi page/pageSize để chuyển trang. Không thay response của create/detail, xác nhận còn sống, metadata bằng chứng, lịch sử pháp lý hoặc danh sách người thẩm định mới. Các endpoint workflow đã trả PageDto như overview/legal handovers/notifications tiếp tục dùng contract cũ của chúng. Giới hạn quyền và beneficiary OTP được giữ nguyên.

Test console được cập nhật để kiểm tra PageDto và kiểm tra phân trang Executor thật: metadata, page size, input không hợp lệ và hai trang đơn không trùng nhau. Các luồng xác nhận còn sống, legal và login tiếp tục chạy trong bộ test cô lập.
