-- Read-only administrator audit. Does not grant roles, delete users or change handovers.
-- Review results before designing an explicit, case-by-case reassignment.
SELECT r.RoleId, r.RoleName, ur.UserId, u.FullName, u.Status
FROM dbo.ROLES r
LEFT JOIN dbo.USER_ROLES ur ON ur.RoleId = r.RoleId
LEFT JOIN dbo.USERS u ON u.UserId = ur.UserId
WHERE r.RoleName = 'Staff';

SELECT a.ExecutorAssignmentId, a.VaultId, a.ExecutorId, a.Status, a.AssignedAt
FROM dbo.EXECUTOR_ASSIGNMENTS a
WHERE EXISTS (SELECT 1 FROM dbo.USER_ROLES ur JOIN dbo.ROLES r ON r.RoleId = ur.RoleId
              WHERE ur.UserId = a.ExecutorId AND r.RoleName = 'Staff')
  AND NOT EXISTS (SELECT 1 FROM dbo.USER_ROLES ur JOIN dbo.ROLES r ON r.RoleId = ur.RoleId
                  WHERE ur.UserId = a.ExecutorId AND r.RoleName = 'Executor');

SELECT h.RequestId, h.VaultId, h.ExecutorId, h.Status
FROM dbo.HANDOVER_REQUESTS h
WHERE EXISTS (SELECT 1 FROM dbo.USER_ROLES ur JOIN dbo.ROLES r ON r.RoleId = ur.RoleId
              WHERE ur.UserId = h.ExecutorId AND r.RoleName = 'Staff')
  AND NOT EXISTS (SELECT 1 FROM dbo.USER_ROLES ur JOIN dbo.ROLES r ON r.RoleId = ur.RoleId
                  WHERE ur.UserId = h.ExecutorId AND r.RoleName = 'Executor');

-- No automatic conversion: confirm a separately provisioned Active/unlocked Executor,
-- independence from owner/beneficiaries/legal reviewer, request state and signed evidence.
-- Reassign assignment + request atomically under administrative review if permissible;
-- locked/completed historical records must not be silently rewritten.
