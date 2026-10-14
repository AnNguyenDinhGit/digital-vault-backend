-- Explicit administrator operation only. Does not grant this role to any user.
-- Run against the intended existing DigitalVault database after review.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.ROLES WITH (UPDLOCK, HOLDLOCK) WHERE RoleName = 'Staff')
BEGIN
    IF COLUMNPROPERTY(OBJECT_ID('dbo.ROLES'), 'RoleId', 'IsIdentity') <> 1
        THROW 50001, 'ROLES.RoleId is not an identity column; review the existing role provisioning procedure.', 1;
    INSERT INTO dbo.ROLES (RoleName) VALUES ('Staff');
END;
COMMIT TRANSACTION;
