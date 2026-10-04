namespace LegacyVault.DAL.Storage;
public interface IDocumentStore
{
    Task<string> Write(byte[] bytes, CancellationToken ct);
    Task<byte[]> Read(string id, CancellationToken ct);
    Task Delete(string id);
}
public sealed class DocumentStore(string root) : IDocumentStore
{
    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid document identifier.");
        return Path.Combine(Path.GetFullPath(root), id);
    }
    public async Task<string> Write(byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(PathFor(id), bytes, ct); return id; }
        catch { File.Delete(PathFor(id)); throw; }
    }
    public Task<byte[]> Read(string id, CancellationToken ct) => File.ReadAllBytesAsync(PathFor(id), ct);
    public Task Delete(string id) { File.Delete(PathFor(id)); return Task.CompletedTask; }
}
