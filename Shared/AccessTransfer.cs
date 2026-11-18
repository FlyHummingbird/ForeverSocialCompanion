using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ForeverSocial.Companion;

public sealed record TransferredAccess(string Endpoint,string Client,string Actor,string Community,string Token);

/// <summary>An explicit, password-encrypted copy of an existing bearer credential.
/// Does not enroll, rotate, verify Blizzard ownership, or contact a server.</summary>
public static class AccessTransfer
{
    private sealed record Envelope(int Format,int Iterations,string Salt,string Nonce,string Data,string Tag);
    private const int Iterations=600000;
    public const int MaxBytes=16384;
    private static readonly byte[] AssociatedData=Encoding.UTF8.GetBytes("ForeverSocialCredentialTransfer/1");
    private static void Password(string password)
    {
        if(password.Length<12 || password.Length>256 || string.IsNullOrWhiteSpace(password))throw new InvalidDataException("Use a password with 12 to 256 characters");
    }
    private static void Validate(TransferredAccess access)
    {
        if(BridgeFiles.Endpoint(access.Endpoint).AbsoluteUri!=access.Endpoint || access.Client is not ("forever" or "retail")
            || !Protocol.Identity(access.Actor) || string.IsNullOrEmpty(access.Community) || !Protocol.Text(access.Community,100)
            || access.Community.Any(char.IsWhiteSpace) || !access.Actor.EndsWith("-"+access.Community,StringComparison.Ordinal)
            || access.Token is null || access.Token.Length!=64 || !access.Token.All(char.IsAsciiHexDigit))throw new InvalidDataException("Invalid access transfer");
    }
    public static string Protect(TransferredAccess access,string password)
    {
        Validate(access);Password(password);
        var salt=RandomNumberGenerator.GetBytes(16);var nonce=RandomNumberGenerator.GetBytes(12);
        var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
        var plain=JsonSerializer.SerializeToUtf8Bytes(access,Protocol.Json);
        try{
            var data=new byte[plain.Length];var tag=new byte[16];
            using var aes=new AesGcm(key,16);aes.Encrypt(nonce,plain,data,tag,AssociatedData);
            return JsonSerializer.Serialize(new Envelope(1,Iterations,Convert.ToBase64String(salt),Convert.ToBase64String(nonce),Convert.ToBase64String(data),Convert.ToBase64String(tag)),Protocol.Json);
        }finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(plain);}
    }
    public static TransferredAccess Unprotect(string text,string password)
    {
        Password(password);
        if(Encoding.UTF8.GetByteCount(text)>MaxBytes)throw new InvalidDataException("Transfer too large");
        var envelope=JsonSerializer.Deserialize<Envelope>(text,Protocol.Json) ?? throw new InvalidDataException("Invalid transfer");
        if(envelope.Format!=1 || envelope.Iterations!=Iterations)throw new InvalidDataException("Unsupported transfer");
        var salt=Convert.FromBase64String(envelope.Salt);var nonce=Convert.FromBase64String(envelope.Nonce);
        var data=Convert.FromBase64String(envelope.Data);var tag=Convert.FromBase64String(envelope.Tag);
        if(salt.Length!=16 || nonce.Length!=12 || tag.Length!=16 || data.Length is <1 or >4096)throw new InvalidDataException("Invalid transfer");
        var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);var plain=new byte[data.Length];
        try{
            using var aes=new AesGcm(key,16);aes.Decrypt(nonce,data,tag,plain,AssociatedData);
            var access=JsonSerializer.Deserialize<TransferredAccess>(plain,Protocol.Json) ?? throw new InvalidDataException("Invalid transfer");
            Validate(access);return access;
        }finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(plain);}
    }
    public static void Match(TransferredAccess access,Uri endpoint,Export export)
    {
        Validate(access);
        if(!export.Enabled || access.Endpoint!=endpoint.AbsoluteUri || access.Client!=export.Client || access.Actor!=export.Actor || access.Community!=export.Community)
            throw new InvalidDataException("Transfer does not match the permitted character and server");
    }
}
