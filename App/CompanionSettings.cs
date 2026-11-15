using System.Text.Json;
using ForeverSocial.Companion;

internal sealed record CompanionSettings
{
    public string Folder { get; init; } = "";
    public string Endpoint { get; init; } = "https://social.wgfreunde.com";
    public string Actor { get; init; } = "";
    public string Client { get; init; } = "";
    public bool Automatic { get; init; }
    public string InvitationFile { get; init; } = "";
    public string InvitationEndpoint { get; init; } = "";

    public static CompanionSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Settings too large");
        return JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(path), Protocol.Json)
            ?? throw new InvalidDataException("Invalid settings");
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        BridgeFiles.WriteAtomic(path, JsonSerializer.Serialize(this, Protocol.Json));
    }
}
