using System.Text.Json;
using ForeverSocial.Companion;

internal sealed record CompanionSettings
{
    public string Folder { get; init; } = "";
    public string Endpoint { get; init; } = "https://social.wgfreunde.com";
    public string Actor { get; init; } = "";
    public string Client { get; init; } = "";
    public bool Automatic { get; init; }
    public List<string> RetiredScopes {get;init;}=[];

    public static CompanionSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Settings too large");
        var result=JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(path), Protocol.Json)
            ?? throw new InvalidDataException("Invalid settings");
        if(result.RetiredScopes is null || result.RetiredScopes.Count>100 || result.RetiredScopes.Any(s=>s is null || s.Length>600))throw new InvalidDataException("Invalid retired scopes");
        return result;
    }

    public void Save(string path)
    {
        var text=JsonSerializer.Serialize(this,Protocol.Json);
        if(System.Text.Encoding.UTF8.GetByteCount(text)>16384)throw new InvalidDataException("Settings too large");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        BridgeFiles.WriteAtomic(path,text);
    }
}
