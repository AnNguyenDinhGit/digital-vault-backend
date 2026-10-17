-- OPTIONAL ADMINISTRATOR SCRIPT. Never executed by the API or tests.
-- Run review-legacy-staff.sql first. Supply reviewed IDs and explicitly set @Apply = 1.
-- This reassigns an editable vault to an EXISTING Executor; never promotes Staff.
SET XACT_ABORT ON;
DECLARE @Apply bit = 0, @VaultId int = NULL, @ReplacementExecutorId int = NULL;
IF @Apply = 0
BEGIN
    SELECT 'No changes. Review legacy Staff assignments and supply approved IDs before applying.' AS Result;
    RETURN;
END;
IF @VaultId IS NULL OR @ReplacementExecutorId IS NULL
    THROW 50001, 'Reviewed vault and replacement Executor IDs are required.', 1;

BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @OwnerId int, @OldExecutorId int;
    SELECT @OwnerId = OwnerId FROM dbo.DIGITAL_VAULTS WITH (UPDLOCK, HOLDLOCK)
    WHERE VaultId = @VaultId AND Status = 'Active';
    IF @OwnerId IS NULL THROW 50002, 'Active vault not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.HANDOVER_REQUESTS WITH (UPDLOCK, HOLDLOCK)
               WHERE VaultId = @VaultId AND Status NOT IN ('Draft', 'Rejected', 'Additional_Info', 'Cancelled'))
        THROW 50003, 'Locked or completed handovers require separate administrative/legal review.', 1;
    IF (SELECT COUNT(*) FROM dbo.EXECUTOR_ASSIGNMENTS WITH (UPDLOCK, HOLDLOCK)
        WHERE VaultId = @VaultId AND Status IN ('Active', 'AutoAssigned')) <> 1
        THROW 50004, 'Exactly one reviewed legacy assignment is required.', 1;
    SELECT @OldExecutorId = ExecutorId FROM dbo.EXECUTOR_ASSIGNMENTS
    WHERE VaultId = @VaultId AND Status IN ('Active', 'AutoAssigned');
    IF NOT EXISTS (SELECT 1 FROM dbo.USER_ROLES ur JOIN dbo.ROLES r ON r.RoleId = ur.RoleId
                   WHERE ur.UserId = @OldExecutorId AND r.RoleName = 'Staff')
       OR EXISTS (SELECT 1 FROM dbo.USER_ROLES ur JOIN dbo.ROLES r ON r.RoleId = ur.RoleId
                  WHERE ur.UserId = @OldExecutorId AND r.RoleName = 'Executor')
        THROW 50005, 'The old assignee is not a Staff-only legacy assignment.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.USERS u WHERE u.UserId = @ReplacementExecutorId AND u.Status = 'Active')
       OR NOT EXISTS (SELECT 1 FROM dbo.USER_ROLES ur JOIN dbo.ROLES r ON r.RoleId = ur.RoleId
                      WHERE ur.UserId = @ReplacementExecutorId AND r.RoleName = 'Executor')
       OR EXISTS (SELECT 1 FROM dbo.AUTHENTICATIONS WHERE UserId = @ReplacementExecutorId AND LockedUntil > SYSUTCDATETIME())
        THROW 50006, 'Replacement must already be an Active unlocked Executor.', 1;
    IF @ReplacementExecutorId = @OwnerId
       OR EXISTS (SELECT 1 FROM dbo.BENEFICIARY_ASSIGNMENTS b JOIN dbo.DIGITAL_ASSETS a ON a.AssetId = b.AssetId
                  WHERE a.VaultId = @VaultId AND b.BeneficiaryId = @ReplacementExecutorId AND b.Status = 'Active')
       OR EXISTS (SELECT 1 FROM dbo.LEGAL_VERIFICATIONS l JOIN dbo.HANDOVER_REQUESTS h ON h.RequestId = l.RequestId
                  WHERE h.VaultId = @VaultId AND l.VerifierId = @ReplacementExecutorId)
        THROW 50007, 'Replacement Executor has an owner, beneficiary or verifier conflict.', 1;

    UPDATE dbo.EXECUTOR_ASSIGNMENTS SET Status = 'Revoked'
    WHERE VaultId = @VaultId AND ExecutorId = @OldExecutorId AND Status IN ('Active', 'AutoAssigned');
    INSERT dbo.EXECUTOR_ASSIGNMENTS (VaultId, ExecutorId, Status, AssignedAt)
    VALUES (@VaultId, @ReplacementExecutorId, 'Active', SYSUTCDATETIME());
    UPDATE dbo.HANDOVER_REQUESTS SET ExecutorId = @ReplacementExecutorId
    WHERE VaultId = @VaultId AND ExecutorId = @OldExecutorId AND Status IN ('Draft', 'Rejected', 'Additional_Info');
    UPDATE dbo.DIGITAL_VAULTS SET UpdatedAt = CASE WHEN UpdatedAt >= SYSUTCDATETIME()
        THEN DATEADD(nanosecond, 100, UpdatedAt) ELSE SYSUTCDATETIME() END WHERE VaultId = @VaultId;
    INSERT dbo.AUDIT_LOGS (UserId, Action, EntityType, EntityId, Timestamp, Result)
    VALUES (NULL, 'ReassignLegacyExecutor', 'DigitalVault', @VaultId, SYSUTCDATETIME(), 'Success');
    INSERT dbo.NOTIFICATIONS (UserId, Type, Title, Message, CreatedAt)
    VALUES (@ReplacementExecutorId, 'Handover', 'Assignment reviewed',
            'A reviewed legacy vault was assigned to you. Submit new evidence before legal review.', SYSUTCDATETIME());
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
-- Does not delete Staff roles/users, cases, files or historical evidence.
-- New Executor must upload their own death certificate; old files are not re-attributed.
