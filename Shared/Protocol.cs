using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ForeverSocial.Companion;

public sealed record WireRecord(string Kind, JsonElement Record);
public sealed record Upload(List<WireRecord> Records);
public sealed record Snapshot(int Format, long FetchedAt, List<WireRecord> Records, int Accepted, List<WireRecord>? Published=null);
public sealed record AccountInfo(string Id,string Client,string Actor,string Community);
public sealed record Enrollment(string Client, string Actor, string Community="", int Format=0, bool Enabled=false);
public sealed record Credential(string Token, string Account);
public sealed record Export(string Client, string Actor, bool Enabled, List<WireRecord> Records, long SavedAt=0, string Community="");

public static partial class Protocol
{
    public const int MaxFileBytes = 16 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 12 };
    public static readonly string[] Kinds = ["profiles", "posts", "listings", "ratings", "follows", "comments", "likes"];
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N}\-' ]{0,158}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();
    public static bool Identity(string? value) => value!=null && value.Contains('-') && !value.Contains(' ') && Text(value, 160) && IdentityPattern().IsMatch(value);
    public static bool Text(string value, int max) => value.EnumerateRunes().Count() <= max && !value.Any(c => c=='|' || c==127 || (c<32 && c!='\n'));
    public static string Key(WireRecord row) => row.Kind + ":" + row.Record.GetProperty("id").GetString();
    public static long Stamp(JsonElement row, string key) => row.GetProperty(key).GetInt64();
    public static string Field(JsonElement row, string key) => row.GetProperty(key).GetString() ?? throw new InvalidDataException("Missing text");
    public static string EncodeWire(WireRecord row)
    {
        // WoW's intentionally small JSON reader accepts literal UTF-8, not \u escapes.
        // Re-encode parsed, validated values; never copy server-provided Lua/source text.
        static string Quote(string value)=>"\""+value.Replace("\\","\\\\",StringComparison.Ordinal).Replace("\"","\\\"",StringComparison.Ordinal).Replace("\n","\\n",StringComparison.Ordinal)+"\"";
        static string Value(JsonElement value)=>value.ValueKind switch{
            JsonValueKind.String=>Quote(value.GetString()!),
            JsonValueKind.Number=>value.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.True=>"true",JsonValueKind.False=>"false",
            JsonValueKind.Object=>"{"+string.Join(',',value.EnumerateObject().Select(p=>Quote(p.Name)+":"+Value(p.Value)))+"}",
            _=>throw new InvalidDataException("Unsupported wire value")};
        return "{\"kind\":"+Quote(row.Kind)+",\"record\":"+Value(row.Record)+"}";
    }
    public static WireRecord Validate(WireRecord row, long now)
    {
        if (row==null || !Kinds.Contains(row.Kind) || row.Record.ValueKind!=JsonValueKind.Object || Encoding.UTF8.GetByteCount(row.Record.GetRawText())>32768) throw new InvalidDataException("Invalid record");
        var r=row.Record; var author=Field(r,"author");var id=Field(r,"id");
        if (!Identity(author) || !Text(id,240) || id.Length==0 || id.Any(char.IsWhiteSpace)) throw new InvalidDataException("Invalid identity");
        if(row.Kind is "posts" or "comments" or "listings" && !id.StartsWith(author+"@",StringComparison.Ordinal))throw new InvalidDataException("Invalid author namespace");
        var created=Stamp(r,"created");var updated=Stamp(r,"updated");var version=Stamp(r,"version");
        if(created<1 || updated<created || updated>now+300 || version<1 || version>1_000_000_000) throw new InvalidDataException("Invalid version");
        if ((row.Kind is "posts" or "comments" or "listings" ? created : updated)<now-30*86400) throw new InvalidDataException("Expired record");
        var allowed=new List<string>{"id","author","created","updated","version"};
        void String(string key,int max,bool required=false) { var value=Field(r,key);if(!Text(value,max) || required && string.IsNullOrWhiteSpace(value))throw new InvalidDataException("Invalid text");allowed.Add(key); }
        void Bool(string key) { if(r.GetProperty(key).ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Invalid flag");allowed.Add(key); }
        void Choice(string key,params string[] values) { if(!values.Contains(Field(r,key)))throw new InvalidDataException("Invalid choice");allowed.Add(key); }
        var deleted=r.TryGetProperty("deleted",out var flag) && flag.ValueKind==JsonValueKind.True;
        switch(row.Kind)
        {
            case "profiles":
                if(id!=author)throw new InvalidDataException("Invalid profile");
                Bool("public");Choice("class","WARRIOR","PALADIN","HUNTER","ROGUE","PRIEST","DEATHKNIGHT","SHAMAN","MAGE","WARLOCK","MONK","DRUID","DEMONHUNTER","EVOKER","UNKNOWN");
                Choice("role","","TANK","HEALER","DAMAGER");Choice("faction","Horde","Alliance","Neutral","Unknown");
                if(Stamp(r,"level") is <1 or >1000)throw new InvalidDataException("Invalid level");allowed.Add("level");
                String("guild",100);String("bio",500);String("activities",120);String("professions",120);break;
            case "posts": Bool("deleted");Choice("category","general","group","trade","guild","achievement","other");String("text",300,!deleted);break;
            case "comments": Bool("deleted");String("post",240,true);String("text",200,!deleted);break;
            case "likes": String("post",240,true);Bool("active");if(id!=Field(r,"post")+"~"+author)throw new InvalidDataException("Invalid like");break;
            case "follows":
                String("target",160,true);Bool("active");if(!Identity(Field(r,"target")) || Field(r,"target")==author || id!=Field(r,"target")+"~"+author)throw new InvalidDataException("Invalid follow");break;
            case "ratings":
                String("target",160,true);Bool("encounter");
                if(!r.GetProperty("encounter").GetBoolean() || !Identity(Field(r,"target")) || Field(r,"target")==author || id!=Field(r,"target")+"~"+author)throw new InvalidDataException("Invalid review");
                var scores=r.GetProperty("scores");var scoreKeys=new[]{"friendly","teamwork","reliable","helpful"};
                if(scores.ValueKind!=JsonValueKind.Object || scores.EnumerateObject().Count()!=4 || scoreKeys.Any(k=>!scores.TryGetProperty(k,out var s) || !s.TryGetInt32(out var n) || n<1 || n>5))throw new InvalidDataException("Invalid scores");
                allowed.Add("scores");String("note",200);String("awards",120);var awards=Field(r,"awards").Split(',',StringSplitOptions.RemoveEmptyEntries);
                if(string.Join(',',awards)!=Field(r,"awards") || awards.Distinct().Count()!=awards.Length || awards.Any(a=>!new[]{"helpful","leader","tank","friendly","pleasant","healer"}.Contains(a)))throw new InvalidDataException("Invalid awards");break;
            case "listings":
                String("activity",80,true);String("text",300,true);String("planned",64);Bool("active");String("roles",24,true);
                var roles=Field(r,"roles").Split(',');if(roles.Length>3 || roles.Distinct().Count()!=roles.Length || roles.Any(a=>a is not ("TANK" or "HEALER" or "DAMAGER")))throw new InvalidDataException("Invalid roles");break;
        }
        // Build a fresh whitelist object; local notes, provenance and arbitrary nested fields cannot cross this boundary.
        var clean=new Dictionary<string,JsonElement>();foreach(var key in allowed)clean[key]=r.GetProperty(key).Clone();
        if(deleted){clean["text"]=JsonSerializer.SerializeToElement("");if(row.Kind=="posts")clean["category"]=JsonSerializer.SerializeToElement("general");}
        if(row.Kind=="profiles" && !r.GetProperty("public").GetBoolean()){
            foreach(var key in new[]{"guild","bio","activities","professions","role"})clean[key]=JsonSerializer.SerializeToElement("");
            clean["class"]=JsonSerializer.SerializeToElement("UNKNOWN");clean["faction"]=JsonSerializer.SerializeToElement("Unknown");clean["level"]=JsonSerializer.SerializeToElement(1);
        }
        var result=new WireRecord(row.Kind,JsonSerializer.SerializeToElement(clean,Json));
        if(Encoding.UTF8.GetByteCount(EncodeWire(result))>8192)throw new InvalidDataException("Canonical wire limit");
        return result;
    }
    public static bool CanReplace(WireRecord previous,WireRecord next)
    {
        var a=previous.Record;var b=next.Record;
        if(Field(a,"author")!=Field(b,"author") || Stamp(a,"created")!=Stamp(b,"created") || Stamp(b,"version")<=Stamp(a,"version") || Stamp(b,"updated")<Stamp(a,"updated"))return false;
        if(a.TryGetProperty("deleted",out var deleted) && deleted.ValueKind==JsonValueKind.True && !b.GetProperty("deleted").GetBoolean())return false;
        return next.Kind is not ("comments" or "likes") || Field(a,"post")==Field(b,"post");
    }
}
