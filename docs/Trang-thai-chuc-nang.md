# Trạng thái chức năng — cập nhật 08/10/2026

Giữ API → BLL → DAL. Google login giữ nguyên schema và entities scaffold.

## Chức năng có endpoint

| Nhóm | Chức năng |
| --- | --- |
| Tài khoản | Đăng ký Owner, login mật khẩu, logout |
| Google | Bắt đầu OIDC, callback, trang lấy challengeId, xác minh/gửi lại OTP và cấp cookie |
| Owner | Tạo/danh sách/chi tiết kho; tạo/danh sách/chi tiết tài sản; chọn người thụ hưởng bằng email và tỷ lệ; danh sách người thụ hưởng; upload giấy tờ mã hóa |
| Executor | Danh sách/chi tiết đơn; đọc giấy tờ và kiểm tra chữ ký; xác nhận hoàn thành bằng file/chữ ký; gửi yêu cầu xác minh pháp lý |
| Beneficiary | Yêu cầu/xác minh OTP riêng; danh sách/chi tiết tài sản thừa kế; đọc giấy tờ sau khi đủ điều kiện bàn giao |

Google mới tạo User Active + Owner sau OTP, không tạo mật khẩu giả. Email đã có dùng lại User và giữ mật khẩu/role. Chặn tài khoản nhân viên, không Active và đang khóa. SMTP đã được bật gửi thật; phải cấu hình credentials hợp lệ.

## Kiểm thử

Đã bổ sung UsersByRole vào FakeRepository để sửa CS0535. Build thành công, **154 kiểm tra đạt**. SQLite in-memory, Google discovery/token/JWKS và SMTP giả lập được dùng trong test; HTTPS loopback kiểm tra cookie và callback. Không tạo tài khoản test trên SQL Server thật. Chưa test Google/SMTP thật.

## Phần chưa hoàn chỉnh

- Tạo đơn bàn giao và random Executor có logic BLL, chưa có endpoint. Cần kiểm tra quyền, transaction và đồng bộ lọc AutoAssigned ở danh sách đơn.
- BeneficiaryService bổ sung chưa nối controller/DI; một số hàm còn placeholder hoặc thiếu kiểm tra quyền. Không coi các hàm này là API đã hoàn thành.
- Chưa có API duyệt/từ chối pháp lý, cấp tài khoản nhân viên, sửa/xóa phân bổ hoặc thiết lập mật khẩu cho Google-only User.
- Google nhận diện bằng email, không lưu sub; email đổi không tự nhận ra User cũ. Challenge OTP lưu trong bộ nhớ một instance, mất khi restart.

## Tài liệu test

- [Google và Swagger](Google-login-Swagger.md)
- [Đăng ký, tạo tài sản và chọn người thụ hưởng](Luong-chinh-dang-ky-tao-assets.md)
- [Danh sách API](API.md)
- [Swagger: giấy tờ, bàn giao và OTP](Huong-dan-test-API-Swagger.md)
