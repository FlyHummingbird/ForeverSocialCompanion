using System.Text;
using System.Text.Json;

namespace ForeverSocial.Companion;

public static class BridgeFiles
{
    public const string PayloadHeader = "-- Generated data only. No remote Lua is accepted.";
    public static string SnapshotPath(string root)
    {
        root=ClientRoot(root);
        var folder=Path.Combine(root,"Interface","AddOns","ForeverSocial");RejectLink(folder);
        var toc=Path.Combine(folder,Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar))=="_forever_"?"ForeverSocial_Camelot.toc":"ForeverSocial_Mainline.toc");RejectLink(toc);
        if(!File.Exists(toc) || new FileInfo(toc).Length>65536)throw new AddonUpdateRequiredException();
        var lines=File.ReadAllLines(toc).Select(s=>s.Trim().Replace('\\','/')).ToArray();
        if(!lines.Contains("## X-ForeverSocial-Companion: 1") || !lines.Contains("Data/Companion.lua"))throw new AddonUpdateRequiredException();
        var data=Path.Combine(folder,"Data");RejectLink(data);
        var path=Path.Combine(data,"Companion.lua");RejectLink(path);return path;
    }
    public static string ClientRoot(string input)
    {
        var root=Path.GetFullPath(input);var flavor=Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        if(flavor is not ("_forever_" or "_retail_"))throw new InvalidDataException("Select _forever_ or _retail_");
        if(!Directory.Exists(Path.Combine(root,"Interface","AddOns")))throw new InvalidDataException("AddOns folder missing");
        foreach(var part in new[]{root,Path.Combine(root,"Interface"),Path.Combine(root,"Interface","AddOns")}) RejectLink(part);
        return root;
    }
    public static void RejectLink(string path){if((File.Exists(path)||Directory.Exists(path)) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked folders/files are not supported");}
    public static IEnumerable<string> Exports(string root)
    {
        var account=Path.Combine(ClientRoot(root),"WTF","Account");if(!Directory.Exists(account))yield break;RejectLink(account);
        foreach(var folder in Directory.EnumerateDirectories(account).Take(32)){
            // WoW names the file after the addon, not after its SavedVariable.
            // This account-global file contains only the exported bridge variable;
            // the full DB is a different, per-character ForeverSocial.lua file.
            RejectLink(folder);var sv=Path.Combine(folder,"SavedVariables");RejectLink(sv);var path=Path.Combine(sv,"ForeverSocial.lua");
            if(File.Exists(path)){RejectLink(path);if(new FileInfo(path).Length<=Protocol.MaxFileBytes)yield return path;}
        }
    }
    public static void WriteSnapshot(string root,string actor,string client,Uri endpoint,Snapshot snapshot)
    {
        root=ClientRoot(root);if(!Protocol.Identity(actor) || snapshot.Format!=1 || snapshot.Records.Count>1500)throw new InvalidDataException("Invalid snapshot");
        var path=SnapshotPath(root);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder=new StringBuilder(PayloadHeader+"\nForeverSocialCompanionData={format=1,service=");
        builder.Append(SavedVariables.Quote(endpoint.AbsoluteUri.TrimEnd('/'))).Append(",actor=").Append(SavedVariables.Quote(actor)).Append(",client=").Append(SavedVariables.Quote(client));
        builder.Append(",fetchedAt=").Append(snapshot.FetchedAt).Append(",records={\n");
        foreach(var candidate in snapshot.Records){var record=Protocol.Validate(candidate,DateTimeOffset.UtcNow.ToUnixTimeSeconds());builder.Append(SavedVariables.Quote(Protocol.EncodeWire(record))).Append(",\n");}
        builder.Append("}}\n");if(Encoding.UTF8.GetByteCount(builder.ToString())>Protocol.MaxFileBytes)throw new InvalidDataException("Snapshot too large");
        // The main addon's declared data slot is the only game file written.
        WriteAtomic(path,builder.ToString());
    }
    public static void WriteAtomic(string path,string text)
    {
        RejectLink(path);var temporary=path+".new";RejectLink(temporary);
        try{File.WriteAllText(temporary,text,new UTF8Encoding(false));File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static Uri Endpoint(string text)
    {
        if(!Uri.TryCreate(text,UriKind.Absolute,out var uri) || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0 || uri.AbsolutePath!="/" || !(uri.Scheme=="https" || uri.Scheme=="http" && uri.IsLoopback))throw new InvalidDataException("Use an HTTPS server URL (HTTP only on loopback)");
        return uri;
    }
}

public sealed class AddonUpdateRequiredException : IOException
{
    public AddonUpdateRequiredException() : base("Install Forever Social 1.3.2 or newer before synchronizing") { }
}
