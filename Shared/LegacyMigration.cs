using System.Security.Cryptography;
using System.Text.Json;

namespace ForeverSocial.Companion;

public sealed record LegacyMigrationResult(bool Removed, bool PreservedSnapshot, string? Backup);

public static class LegacyMigration
{
    public static LegacyMigrationResult Retire(string root,string backupRoot,bool preserveSnapshot=false)
    {
        root=BridgeFiles.ClientRoot(root);var destination=BridgeFiles.SnapshotPath(root);
        var legacy=Path.Combine(root,"Interface","AddOns","ForeverSocial_Companion");BridgeFiles.RejectLink(legacy);
        if(!Directory.Exists(legacy))return new(false,false,null);
        var names=new[]{"ForeverSocial_Companion.toc","Payload.lua"};
        var files=Directory.EnumerateFileSystemEntries(legacy).Take(3).ToArray();
        if(files.Length!=2 || files.Any(p=>!names.Contains(Path.GetFileName(p))))return new(false,false,null);
        foreach(var file in files){BridgeFiles.RejectLink(file);if(!File.Exists(file) || new FileInfo(file).Length>Protocol.MaxFileBytes)return new(false,false,null);}
        var payload=File.ReadAllText(Path.Combine(legacy,"Payload.lua"));
        var toc=File.ReadAllLines(Path.Combine(legacy,names[0]));
        if(!payload.StartsWith(BridgeFiles.PayloadHeader,StringComparison.Ordinal) || !toc.Contains("## Title: Forever Social Companion Data") || !toc.Contains("Payload.lua"))return new(false,false,null);
        backupRoot=Path.GetFullPath(backupRoot);
        if(backupRoot.Equals(root,StringComparison.OrdinalIgnoreCase) || backupRoot.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Backup must be outside the game folder");
        // Reject linked backup ancestors too; never recursively delete or move a directory.
        for(var parent=backupRoot;!string.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent))BridgeFiles.RejectLink(parent);
        var backup=Path.Combine(backupRoot,"LegacyCompanion-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);var hashes=new Dictionary<string,string>();
        foreach(var file in files){var name=Path.GetFileName(file);var bytes=File.ReadAllBytes(file);hashes[file]=Convert.ToHexString(SHA256.HashData(bytes));File.WriteAllBytes(Path.Combine(backup,name),bytes);if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(backup,name))))!=hashes[file])throw new IOException("Backup verification failed");}
        var preserved=false;
        if(preserveSnapshot)
        {
            // Data-only parsing: even a tampered old Lua file is never executed.
            var data=SavedVariables.ParseSnapshot(payload);
            var client=(string)data["client"];var actor=(string)data["actor"];var service=BridgeFiles.Endpoint((string)data["service"]);
            var fetched=(long)data["fetchedAt"];var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if(!data["format"].Equals(1L) || !Protocol.Identity(actor) || "_"+client+"_"!=Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) || fetched>now+300 || fetched<now-30*86400)throw new InvalidDataException("Invalid legacy snapshot scope");
            var rows=(Dictionary<string,object>)data["records"];if(rows.Count>1500)throw new InvalidDataException("Legacy snapshot limit");
            var records=new List<WireRecord>();
            for(var i=1;i<=rows.Count;i++){
                var encoded=(string)rows[i.ToString(System.Globalization.CultureInfo.InvariantCulture)];
                records.Add(Protocol.Validate(JsonSerializer.Deserialize<WireRecord>(encoded,Protocol.Json) ?? throw new InvalidDataException("Invalid legacy record"),now));
            }
            if(records.Select(Protocol.Key).Distinct(StringComparer.Ordinal).Count()!=records.Count)throw new InvalidDataException("Duplicate legacy records");
            BridgeFiles.WriteSnapshot(root,actor,client,service,new Snapshot(1,fetched,records,0));preserved=true;
        }
        if(!File.Exists(destination) || new FileInfo(destination).Length>Protocol.MaxFileBytes || !File.ReadAllText(destination).StartsWith(BridgeFiles.PayloadHeader,StringComparison.Ordinal))throw new IOException("Integrated snapshot is missing");
        foreach(var file in files)if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))!=hashes[file])throw new IOException("Legacy files changed during migration");
        foreach(var file in files)File.Delete(file);
        Directory.Delete(legacy,recursive:false);
        return new(true,preserved,backup);
    }
}
