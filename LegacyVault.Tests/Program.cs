using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;

if (args.Contains("--database-audit")) { await DatabaseAudit.Run(); return; }

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++; Console.WriteLine("PASS: " + name);
}
async Task Reject(Func<Task> action, int status, string name)
{
    try { await action(); throw new Exception("Expected rejection: " + name); }
    catch (WorkflowException e) { Check(e.StatusCode == status, name); }
}

var protection = new DocumentProtection(new SecurityOptions { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
var file = new UploadFile("instructions.txt", "text/plain", Encoding.UTF8.GetBytes("private inheritance instructions"));
var envelope = new ProtectedDocument(file, 1, null);
var cipher = protection.Encrypt(envelope);
Check(protection.Decrypt(cipher).File.Content.SequenceEqual(file.Content), "encrypted document round trip preserves every byte");
Check(!cipher.SequenceEqual(protection.Encrypt(envelope)), "fresh nonce on every encryption");
cipher[^1] ^= 1;
try { protection.Decrypt(cipher); throw new Exception("Expected integrity failure"); }
catch (CryptographicException) { Check(true, "tampered ciphertext rejected"); }
Check(!protection.Verify(file.Content, new byte[256], 1), "unconfigured signer rejected");
using var rsa = RSA.Create(2048);
var certificateRequest = new CertificateRequest("CN=LegacyVault Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
var signedProtection = new DocumentProtection(new SecurityOptions
{
    EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    SignerCertificates = new() { [1] = certificate.ExportCertificatePem(), [6] = certificate.ExportCertificatePem() },
    TrustedRootCertificates = new() { certificate.ExportCertificatePem() }
});
var signature = rsa.SignData(file.Content, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
Check(signedProtection.Verify(file.Content, signature, 1), "trusted RSA certificate signature accepted");
Check(!signedProtection.Verify(Encoding.UTF8.GetBytes("changed"), signature, 1), "signature cannot validate modified document");
Check(!signedProtection.Verify(file.Content, signature, 7), "signature cannot validate unconfigured signer identity");
var salt = RandomNumberGenerator.GetBytes(16);
var hash = Rfc2898DeriveBytes.Pbkdf2("correct-password", salt, 100000, HashAlgorithmName.SHA256, 32);
var encoded = $"PBKDF2-SHA256$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
Check(IdentityService.VerifyPassword("correct-password", encoded) && !IdentityService.VerifyPassword("wrong-password", encoded), "password verification");

var sender = new FakeSender(); var otp = new OtpService(sender);
var challenge = await otp.Request(3, "session-a", "beneficiary@example.test", default);
await Reject(() => otp.Request(3, "session-a", "beneficiary@example.test", default), 429, "OTP resend cooldown");
await Reject(() => { otp.Verify(4, "session-a", challenge.ChallengeId, sender.Code); return Task.CompletedTask; }, 400, "OTP bound to user");
await Reject(() => { otp.Verify(3, "session-b", challenge.ChallengeId, sender.Code); return Task.CompletedTask; }, 400, "OTP bound to login session");
otp.Verify(3, "session-a", challenge.ChallengeId, sender.Code); otp.RequireVerified("session-a"); Check(true, "valid OTP unlocks current session");
await Reject(() => { otp.Verify(3, "session-a", challenge.ChallengeId, sender.Code); return Task.CompletedTask; }, 400, "OTP replay rejected");
await Reject(() => { otp.RequireVerified("session-b"); return Task.CompletedTask; }, 403, "OTP grant cannot cross sessions");
otp.Revoke("session-a");
await Reject(() => { otp.RequireVerified("session-a"); return Task.CompletedTask; }, 403, "logout revokes OTP grant");
var exhausted = await otp.Request(5, "session-c", "other@example.test", default);
var actual = sender.Code;
var wrong = actual == "000000" ? "000001" : "000000";
for (var i = 0; i < 5; i++) await Reject(() => { otp.Verify(5, "session-c", exhausted.ChallengeId, wrong); return Task.CompletedTask; }, 400, $"OTP failed attempt {i + 1}");
await Reject(() => { otp.Verify(5, "session-c", exhausted.ChallengeId, actual); return Task.CompletedTask; }, 400, "OTP attempt limit cannot be bypassed with valid code");

var repo = new FakeRepository(); var store = new FakeStore(); var service = new VaultService(repo, store, protection);
await Reject(() => service.OwnerAsset(2, 10, default), 404, "other owner cannot read asset");
await Reject(() => service.OwnerAsset(3, 10, default), 403, "beneficiary cannot use owner APIs");
await Reject(() => service.UploadOwnerDocument(1, 10, 3, true, file, null, null, default), 400, "living owner must supply verified signature");
await Reject(() => service.UploadOwnerDocument(1, 10, 3, false, file, null, null, default), 400, "deceased branch requires death certificate");
Check(store.Files.Count == 0 && repo.Added.Count == 0, "rejected uploads write no files or records");
var uploaded = await service.UploadOwnerDocument(1, 10, 3, false, file, null, file, default);
Check(uploaded.IsEncrypted && store.Files.Count == 2 && repo.Added.OfType<AssetDocument>().All(x => x.Status == "Pending_Legal"), "deceased documents remain encrypted and pending legal verification");
repo.FailSave = true;
var count = store.Files.Count;
try { await service.UploadOwnerDocument(1, 10, 3, false, file, null, file, default); throw new Exception("Expected save failure"); }
catch (RepositoryConflictException) { Check(store.Files.Count == count, "failed database save removes newly stored files"); }
repo.FailSave = false;
await Reject(() => service.Complete(6, 20, file, new byte[256], default), 409, "draft handover cannot be completed");
await Reject(() => service.Request(7, 20, default), 404, "another executor cannot inspect handover");
await Reject(() => service.RequestLegal(6, 20, 3, null, file, default), 403, "legal request requires verifier role");
var verification = await service.RequestLegal(6, 20, 8, "Evidence attached", file, default);
Check(repo.Handover.Status == "Pending_Legal" && repo.Added.OfType<LegalVerification>().Any(), "legal submission changes status and persists evidence together");
await Reject(() => service.RequestLegal(6, 20, 8, null, file, default), 409, "duplicate legal submission rejected");
repo.Handover.Status = "Approved";
await Reject(() => service.Complete(6, 20, file, new byte[256], default), 409, "completion requires approved legal verification and cases");
var signedRepo = new FakeRepository(); var signedStore = new FakeStore();
var signedService = new VaultService(signedRepo, signedStore, signedProtection);
var signedUpload = await signedService.UploadOwnerDocument(1, 10, 3, true, file, signature, null, default);
Check(signedUpload.IsEncrypted && signedRepo.Added.OfType<AssetDocument>().Single().Status == "Active", "signed living-owner upload accepted");
signedRepo.Handover.Status = "Approved";
signedRepo.Handover.LegalVerifications.Add(new LegalVerification { Status = "Approved" });
signedRepo.Handover.HandoverCases.Add(new HandoverCase { Status = "In_Progress", Assignment = new BeneficiaryAssignment { AssetId = 10, BeneficiaryId = 3 } });
await signedService.Complete(6, 20, file, signature, default);
Check(signedRepo.Handover.Status == "Completed" && signedRepo.Handover.HandoverCases.All(x => x.Status == "Completed"), "signed completion updates request and every case");
var saved = signedRepo.Added.OfType<HandoverDocument>().Single();
signedRepo.Handover.HandoverDocuments.Add(saved);
var detail = await signedService.Request(6, 20, default);
Check(detail.Documents.Single().SignatureValid == true, "handover detail rechecks stored signature");
var downloaded = await signedService.RequestDocument(6, 20, saved.HandoverDocumentId, default);
Check(downloaded.Content.SequenceEqual(file.Content) && downloaded.SignatureValid, "executor reads entire decrypted document and signature status");
var registrationRepo = new FakeRepository();
var identity = new IdentityService(registrationRepo);
RegisterInput Registration(string email = " NewUser@Example.test ", string password = "Registration123!", string? confirmation = null) =>
    new() { FullName = " Test User ", Email = email, Password = password, ConfirmPassword = confirmation ?? password, Phone = " 0901234567 " };
var registered = await identity.Register(Registration(), default);
var account = registrationRepo.RegisteredUsers.Single();
Check(registered.UserId > 0 && registered.Email == "newuser@example.test" && registered.FullName == "Test User" && account.Phone == "0901234567", "registration creates normalized account");
Check(account.Status == "Active" && account.Roles.Single().RoleName == VaultService.OwnerRole && account.Authentications.Count == 1, "registration saves active user, authentication and allowed role together");
Check(account.Authentications.Single().PasswordHash != "Registration123!" && IdentityService.VerifyPassword("Registration123!", account.Authentications.Single().PasswordHash), "registration hashes password compatible with login");
var login = await identity.Login("NEWUSER@example.test", "Registration123!", default);
Check(login.UserId == registered.UserId, "registered user can log in immediately");
await Reject(() => identity.Register(Registration("NEWUSER@EXAMPLE.TEST"), default), 409, "registration rejects case-insensitive duplicate email");
await Reject(() => identity.Register(Registration("second@example.test", password: "short"), default), 400, "registration rejects short password");
await Reject(() => identity.Register(Registration("second@example.test", confirmation: "DoesNotMatch123!"), default), 400, "registration requires matching password confirmation");
await Reject(() => identity.Register(Registration("invalid-email"), default), 400, "registration rejects invalid email");
await Reject(() => identity.Register(new RegisterInput { FullName = " ", Email = "space@example.test", Password = "Registration123!", ConfirmPassword = "Registration123!" }, default), 400, "registration rejects blank name");
foreach (var role in new[] { VaultService.ExecutorRole, VaultService.VerifierRole, VaultService.AdminRole, VaultService.BeneficiaryRole })
{
    var payload = System.Text.Json.JsonSerializer.Serialize(new { fullName = "Test User", email = "test@example.test", password = "Registration123!", confirmPassword = "Registration123!", role });
    try
    {
        System.Text.Json.JsonSerializer.Deserialize<RegisterInput>(payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        throw new Exception("Expected role field rejection");
    }
    catch (System.Text.Json.JsonException) { Check(true, "registration rejects caller-supplied role: " + role); }
}
var fiveFields = System.Text.Json.JsonSerializer.Deserialize<RegisterInput>("{\"fullName\":\"Test User\",\"email\":\"five@example.test\",\"password\":\"Registration123!\",\"confirmPassword\":\"Registration123!\",\"phone\":\"0901234567\"}", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
var fiveFieldResult = await identity.Register(fiveFields!, default);
Check(fiveFieldResult.Roles.SequenceEqual(new[] { VaultService.OwnerRole }), "five-field public registration always assigns owner role");
registrationRepo.MissingRole = true;
var missingRoleRegistration = await identity.Register(Registration("missing@example.test"), default);
Check(missingRoleRegistration.Roles.Single() == VaultService.OwnerRole && registrationRepo.CreatedRoles.Single().RoleName == VaultService.OwnerRole, "registration initializes missing owner role instead of returning 503");
registrationRepo.MissingRole = false; registrationRepo.DuplicateOnSave = true;
await Reject(() => identity.Register(Registration("race@example.test"), default), 409, "concurrent duplicate database insert maps to conflict");
await RelationalRegistrationTests.Run(Check);
await MainFlowTests.Run(Check);
await BeneficiaryAssignmentTests.Run(Check);
Console.WriteLine($"{checks} checks passed.");

sealed class FakeSender : IOtpSender
{
    public string Code { get; private set; } = "";
    public Task Send(string email, string code, CancellationToken ct) { Code = code; return Task.CompletedTask; }
}
sealed class FakeStore : IDocumentStore
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public Task<string> Write(byte[] bytes, CancellationToken ct) { var id = Guid.NewGuid().ToString("N"); Files[id] = bytes; return Task.FromResult(id); }
    public Task<byte[]> Read(string id, CancellationToken ct) => Task.FromResult(Files[id]);
    public Task Delete(string id) { Files.Remove(id); return Task.CompletedTask; }
}
sealed class FakeRepository : IVaultRepository
{
    public List<object> Added { get; } = new();
    public bool FailSave { get; set; }
    public bool MissingRole { get; set; }
    public bool DuplicateOnSave { get; set; }
    public List<User> RegisteredUsers { get; } = new();
    public List<Role> CreatedRoles { get; } = new();
    public HandoverRequest Handover { get; } = new() { RequestId = 20, ExecutorId = 6, Status = "Draft", Vault = new DigitalVault { OwnerId = 1, ExecutorAssignments = new List<ExecutorAssignment> { new() { ExecutorId = 6, Status = "Active" } } } };
    private readonly DigitalAsset asset = new() { AssetId = 10, AssetName = "Account", AssetType = "Account", Status = "Active", Vault = new DigitalVault { OwnerId = 1, Status = "Active" } };
    public Task<User?> User(int id, CancellationToken ct)
    {
        var role = id switch { 1 or 2 => VaultService.OwnerRole, 3 => VaultService.BeneficiaryRole, 6 or 7 => VaultService.ExecutorRole, 8 => VaultService.VerifierRole, _ => "None" };
        return Task.FromResult<User?>(new User { UserId = id, Status = "Active", Roles = new List<Role> { new() { RoleName = role } } });
    }
    public Task<User?> LoginUser(string email, CancellationToken ct) => Task.FromResult(RegisteredUsers.SingleOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)));
    public Task<User?> UserByEmail(string email, CancellationToken ct) => LoginUser(email, ct);
    public Task<Role?> Role(string name, CancellationToken ct) => Task.FromResult<Role?>(MissingRole ? null : new Role { RoleId = 1, RoleName = name });
    public Task<List<DigitalVault>> OwnerVaults(int owner, CancellationToken ct) => Task.FromResult(new List<DigitalVault> { asset.Vault });
    public Task<DigitalVault?> Vault(int id, CancellationToken ct) => Task.FromResult<DigitalVault?>(asset.Vault);
    public async Task Register(User user, string defaultRole, CancellationToken ct)
    {
        var role = await Role(defaultRole, ct);
        if (role is null) { role = new Role { RoleId = 2, RoleName = defaultRole }; CreatedRoles.Add(role); MissingRole = false; }
        user.Roles.Add(role);
        Add(user);
        await Save(ct);
    }
    public Task<List<DigitalAsset>> OwnerAssets(int owner, CancellationToken ct) => Task.FromResult(new List<DigitalAsset> { asset });
    public Task<DigitalAsset?> Asset(int id, CancellationToken ct) => Task.FromResult<DigitalAsset?>(id == 10 ? asset : null);
    public Task<List<HandoverRequest>> Requests(int executor, CancellationToken ct) => Task.FromResult(new List<HandoverRequest> { Handover });
    public Task<HandoverRequest?> Request(int id, CancellationToken ct) => Task.FromResult<HandoverRequest?>(id == 20 ? Handover : null);
    public Task<List<HandoverCase>> Inherited(int beneficiary, CancellationToken ct) => Task.FromResult(new List<HandoverCase>());
    public void Add<T>(T entity) where T : class => Added.Add(entity);
    public Task Save(CancellationToken ct)
    {
        if (FailSave) return Task.FromException(new RepositoryConflictException());
        if (DuplicateOnSave) return Task.FromException(new RepositoryDuplicateException());
        foreach (var user in Added.OfType<User>().Where(x => x.UserId == 0)) { user.UserId = 100 + RegisteredUsers.Count; RegisteredUsers.Add(user); }
        return Task.CompletedTask;
    }
}
