-- =============================================================================
-- SCRIPT DỌN SẠCH DỮ LIỆU HỆ THỐNG ĐỂ CHUẨN BỊ DEMO (LEGACYVAULT)
-- Giữ lại:
--   1. Danh mục hệ thống: ROLES, SUBSCRIPTION_PLANS
--   2. Các tài khoản USERS (kèm mật khẩu AUTHENTICATIONS, phân quyền USER_ROLES,
--      2FA TWO_FACTOR_AUTHS và ngày tạo CreatedAt) có UserId thuộc danh sách:
--      (2, 4, 5, 6, 7, 11, 12, 13, 15, 16, 17, 19, 20, 26, 27, 28, 29, 30, 32)
-- Xóa toàn bộ:
--   - Tất cả tài sản, tài liệu tải lên, khóa mã hóa, kho di sản (Vaults)
--   - Tất cả hồ sơ bàn giao, phân công Executor/Beneficiary, thẩm định pháp lý, biên nhận
--   - Tất cả giao dịch thanh toán (PAYMENT_TRANSACTIONS) và gói đã mua (USER_SUBSCRIPTIONS)
--   - Tất cả tin nhắn, cuộc trò chuyện, thông báo, nhật ký audit, proof-of-life
--   - Tất cả các tài khoản rác/test nằm ngoài danh sách 19 UserId trên
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Danh sách UserId cần giữ lại
    DECLARE @KeepUsers TABLE (UserId INT PRIMARY KEY);
    INSERT INTO @KeepUsers (UserId)
    VALUES (2), (4), (5), (6), (7), (11), (12), (13), (15), (16),
           (17), (19), (20), (26), (27), (28), (29), (30), (32);

    -- 1. Xóa dữ liệu trò chuyện & thông báo
    IF OBJECT_ID('dbo.MESSAGES', 'U') IS NOT NULL
        DELETE FROM dbo.MESSAGES;

    IF OBJECT_ID('dbo.CONVERSATIONS', 'U') IS NOT NULL
        DELETE FROM dbo.CONVERSATIONS;

    IF OBJECT_ID('dbo.NOTIFICATIONS', 'U') IS NOT NULL
        DELETE FROM dbo.NOTIFICATIONS;

    -- 2. Xóa dữ liệu biên nhận, chữ ký số, thẩm định pháp lý, tài liệu bàn giao & hồ sơ bàn giao
    IF OBJECT_ID('dbo.BENEFICIARY_RECEIPTS', 'U') IS NOT NULL
        DELETE FROM dbo.BENEFICIARY_RECEIPTS;

    IF OBJECT_ID('dbo.DIGITAL_SIGNATURES', 'U') IS NOT NULL
        DELETE FROM dbo.DIGITAL_SIGNATURES;

    IF OBJECT_ID('dbo.LEGAL_VERIFICATIONS', 'U') IS NOT NULL
        DELETE FROM dbo.LEGAL_VERIFICATIONS;

    IF OBJECT_ID('dbo.HANDOVER_DOCUMENTS', 'U') IS NOT NULL
        DELETE FROM dbo.HANDOVER_DOCUMENTS;

    IF OBJECT_ID('dbo.HANDOVER_CASES', 'U') IS NOT NULL
        DELETE FROM dbo.HANDOVER_CASES;

    IF OBJECT_ID('dbo.HANDOVER_REQUESTS', 'U') IS NOT NULL
        DELETE FROM dbo.HANDOVER_REQUESTS;

    -- 3. Xóa dữ liệu phân bổ thụ hưởng, tài liệu tài sản, khóa mã hóa, tài sản số & kho di sản
    IF OBJECT_ID('dbo.BENEFICIARY_ASSIGNMENTS', 'U') IS NOT NULL
        DELETE FROM dbo.BENEFICIARY_ASSIGNMENTS;

    IF OBJECT_ID('dbo.ASSET_DOCUMENTS', 'U') IS NOT NULL
        DELETE FROM dbo.ASSET_DOCUMENTS;

    IF OBJECT_ID('dbo.ENCRYPTION_KEYS', 'U') IS NOT NULL
        DELETE FROM dbo.ENCRYPTION_KEYS;

    IF OBJECT_ID('dbo.DIGITAL_ASSETS', 'U') IS NOT NULL
        DELETE FROM dbo.DIGITAL_ASSETS;

    IF OBJECT_ID('dbo.EXECUTOR_ASSIGNMENTS', 'U') IS NOT NULL
        DELETE FROM dbo.EXECUTOR_ASSIGNMENTS;

    IF OBJECT_ID('dbo.DIGITAL_VAULTS', 'U') IS NOT NULL
        DELETE FROM dbo.DIGITAL_VAULTS;

    -- 4. Xóa toàn bộ lịch sử giao dịch thanh toán & đăng ký gói cước
    IF OBJECT_ID('dbo.PAYMENT_TRANSACTIONS', 'U') IS NOT NULL
        DELETE FROM dbo.PAYMENT_TRANSACTIONS;

    IF OBJECT_ID('dbo.USER_SUBSCRIPTIONS', 'U') IS NOT NULL
        DELETE FROM dbo.USER_SUBSCRIPTIONS;

    -- 5. Xóa lịch sử xác nhận sự sống (Proof of Life), nhật ký Audit & RefreshTokens
    IF OBJECT_ID('dbo.PROOF_OF_LIFE', 'U') IS NOT NULL
        DELETE FROM dbo.PROOF_OF_LIFE;

    IF OBJECT_ID('dbo.AUDIT_LOGS', 'U') IS NOT NULL
        DELETE FROM dbo.AUDIT_LOGS;

    IF OBJECT_ID('dbo.RefreshTokens', 'U') IS NOT NULL
        DELETE FROM dbo.RefreshTokens;

    -- 6. Xóa các tài khoản KHÔNG nằm trong danh sách giữ lại (cùng bảng phụ thuộc của họ)
    IF OBJECT_ID('dbo.TWO_FACTOR_AUTHS', 'U') IS NOT NULL
        DELETE FROM dbo.TWO_FACTOR_AUTHS
        WHERE UserId NOT IN (SELECT UserId FROM @KeepUsers);

    IF OBJECT_ID('dbo.AUTHENTICATIONS', 'U') IS NOT NULL
    BEGIN
        DELETE FROM dbo.AUTHENTICATIONS
        WHERE UserId NOT IN (SELECT UserId FROM @KeepUsers);

        -- Mở khóa đăng nhập sai (nếu có) cho các tài khoản giữ lại để sẵn sàng demo
        UPDATE dbo.AUTHENTICATIONS
        SET FailedLoginCount = 0,
            LockedUntil = NULL
        WHERE UserId IN (SELECT UserId FROM @KeepUsers);
    END

    IF OBJECT_ID('dbo.USER_ROLES', 'U') IS NOT NULL
        DELETE FROM dbo.USER_ROLES
        WHERE UserId NOT IN (SELECT UserId FROM @KeepUsers);

    DELETE FROM dbo.USERS
    WHERE UserId NOT IN (SELECT UserId FROM @KeepUsers);

    COMMIT TRANSACTION;

    PRINT N'Đã dọn sạch dữ liệu hệ thống thành công!';
    SELECT UserId, FullName, Email, Status, CreatedAt
    FROM dbo.USERS
    ORDER BY UserId;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    RAISERROR(@ErrorMessage, 16, 1);
END CATCH;

