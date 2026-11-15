using System.Security.Cryptography;
using System.Text;

namespace ForeverSocial.Companion;

public sealed record SyncOutcome(Snapshot Snapshot, bool Uploaded, bool DataChanged);

/// <summary>A server poll always downloads. Only an unchanged upload may be skipped.</summary>
public sealed class SyncSession
{
    private string? lastScope, lastUpload, lastSnapshot, lastFileHash;
    private long writtenAt;

    private static string Digest(IEnumerable<WireRecord> records) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\n', records.OrderBy(Protocol.Key, StringComparer.Ordinal).Select(Protocol.EncodeWire)))));

    public async Task<SyncOutcome> Synchronize(string root, Export export, Uri endpoint, string token,
        SyncClient client, CancellationToken cancel = default)
    {
        root = BridgeFiles.ClientRoot(root);
        var path = BridgeFiles.SnapshotPath(root);
        endpoint = BridgeFiles.Endpoint(endpoint.AbsoluteUri);
        if (!export.Enabled || export.Client is not ("retail" or "forever") ||
            Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) != "_" + export.Client + "_")
            throw new InvalidDataException("Export does not match the selected client");
        var scope = root + "\n" + endpoint.AbsoluteUri + "\n" + export.Client + "\n" + export.Actor + "\n" + export.Community;
        // savedAt changes on every /reload, even when no public content changed.
        var upload = Digest(export.Records);
        var send = scope != lastScope || upload != lastUpload;
        var snapshot = await client.Sync(export, token, cancel, uploadRecords: send).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (snapshot.FetchedAt < now - 30 * 86400 || snapshot.FetchedAt > now + 300)
            throw new InvalidDataException("Invalid snapshot time");
        var clean = snapshot.Records.Select(r => Protocol.Validate(r, now)).ToList();
        if (clean.Select(Protocol.Key).Distinct(StringComparer.Ordinal).Count() != clean.Count)
            throw new InvalidDataException("Duplicate snapshot records");
        snapshot = snapshot with { Records = clean };
        var content = Digest(clean);
        if (File.Exists(path) && new FileInfo(path).Length > Protocol.MaxFileBytes) throw new InvalidDataException("Snapshot file too large");
        var fileHash = File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
        var changed = scope != lastScope || content != lastSnapshot || fileHash is null ||
            fileHash != lastFileHash || now - writtenAt >= 86400;
        if (changed) BridgeFiles.WriteSnapshot(root, export.Actor, export.Client, endpoint, snapshot);
        // Commit only after both the HTTP request and local write succeeded. Failed work is retried.
        lastScope = scope; lastUpload = upload; lastSnapshot = content;
        lastFileHash = changed ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : fileHash;
        if (changed) writtenAt = now;
        return new SyncOutcome(snapshot, send, changed);
    }
}
