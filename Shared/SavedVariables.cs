using System.Globalization;
using System.Text;

namespace ForeverSocial.Companion;

// Data-only Lua subset. Never invokes Lua, PowerShell, loadstring or an external process.
public sealed class SavedVariables
{
    private readonly string text;private int position,nodes;
    private SavedVariables(string text){this.text=text;}
    public static Dictionary<string,object> Parse(string text)
        => ParseAssignment(text,"ForeverSocialBridge");
    public static Dictionary<string,object> ParseSnapshot(string text)
        => ParseAssignment(text,"ForeverSocialCompanionData");
    private static Dictionary<string,object> ParseAssignment(string text,string variable)
    {
        if(Encoding.UTF8.GetByteCount(text)>Protocol.MaxFileBytes)throw new InvalidDataException("SavedVariables too large");
        var parser=new SavedVariables(text);parser.Space();
        if(!parser.Take(variable))throw new InvalidDataException("Wrong variable");
        parser.Expect('=');var value=parser.Value(0) as Dictionary<string,object> ?? throw new InvalidDataException("Table expected");parser.Space();
        if(parser.position!=text.Length)throw new InvalidDataException("Trailing executable content");return value;
    }
    private void Space(){while(position<text.Length){if(char.IsWhiteSpace(text[position])){position++;continue;}if(position+1<text.Length && text[position..(position+2)]=="--"){while(position<text.Length && text[position]!='\n')position++;continue;}break;}}
    private bool Take(string token){Space();if(text.AsSpan(position).StartsWith(token,StringComparison.Ordinal)){position+=token.Length;return true;}return false;}
    private void Expect(char value){Space();if(position>=text.Length || text[position++]!=value)throw new InvalidDataException("Unexpected data");}
    private object Value(int depth)
    {
        Space();if(++nodes>100000 || depth>8 || position>=text.Length)throw new InvalidDataException("Structure limit");
        var c=text[position];if(c=='"')return String();
        if(c=='{'){
            position++;var result=new Dictionary<string,object>();var index=1;
            while(true){Space();if(Take("}"))return result;string key;
                if(Take("[")){var raw=Value(depth+1);key=Convert.ToString(raw,CultureInfo.InvariantCulture)!;Expect(']');Expect('=');}
                else {
                    var saved=position;while(position<text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position]=='_'))position++;
                    var candidate=text[saved..position];Space();
                    if(candidate.Length is >0 and <=64 && (char.IsAsciiLetter(candidate[0]) || candidate[0]=='_') && Take("="))key=candidate;
                    else {position=saved;key=(index++).ToString(CultureInfo.InvariantCulture);}
                }
                if(!result.TryAdd(key,Value(depth+1)))throw new InvalidDataException("Duplicate key");Space();if(Take(",") || Take(";"))continue;Expect('}');return result;}
        }
        if(Take("true"))return true;if(Take("false"))return false;
        var start=position;if(c=='-')position++;while(position<text.Length && char.IsAsciiDigit(text[position]))position++;
        if(start==position || !long.TryParse(text[start..position],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var number))throw new InvalidDataException("Unsupported Lua expression");return number;
    }
    private string String()
    {
        Expect('"');var result=new StringBuilder();
        while(position<text.Length){var c=text[position++];if(c=='"')return result.ToString();
            if(c=='\\'){if(position>=text.Length)break;c=text[position++];
                if(char.IsAsciiDigit(c)){
                    var bytes=new List<byte>();
                    while(true){var number=c-'0';for(var i=1;i<3 && position<text.Length && char.IsAsciiDigit(text[position]);i++)number=number*10+text[position++]-'0';if(number>255)throw new InvalidDataException("Invalid escape");bytes.Add((byte)number);
                        if(position+1>=text.Length || text[position]!='\\' || !char.IsAsciiDigit(text[position+1]))break;position++;c=text[position++];}
                    try{result.Append(new UTF8Encoding(false,true).GetString(bytes.ToArray()));}catch(DecoderFallbackException){throw new InvalidDataException("Invalid escaped UTF-8");}continue;}
                c=c switch{'n'=>'\n','r'=>'\r','t'=>'\t','\\'=>'\\','"'=>'"','\n'=>'\n',_=>throw new InvalidDataException("Unsupported escape")};}
            result.Append(c);if(result.Length>65536)throw new InvalidDataException("String limit");}
        throw new InvalidDataException("Unterminated string");
    }
    public static List<Export> Exports(Dictionary<string,object> root)
    {
        var result=new List<Export>();if(!root.TryGetValue("format",out var format) || !format.Equals(1L))return result;
        if(!root.TryGetValue("characters",out var data) || data is not Dictionary<string,object> characters || characters.Count>16)return result;
        foreach(var (actor,value) in characters){
            if(!Protocol.Identity(actor) || value is not Dictionary<string,object> row || !row.TryGetValue("client",out var clientValue) || clientValue is not string client || client is not ("forever" or "retail"))continue;
            var enabled=row.TryGetValue("enabled",out var enabledValue) && enabledValue is true;var records=new List<WireRecord>();
            if(row.TryGetValue("records",out var entries) && entries is Dictionary<string,object> map && map.Count<=1500)
                foreach(var entry in map.OrderBy(p=>int.TryParse(p.Key,out var number)?number:int.MaxValue)){
                    if(entry.Value is not string encoded || encoded.Length>8192)continue;
                    try{var wire=System.Text.Json.JsonSerializer.Deserialize<WireRecord>(encoded,Protocol.Json);if(wire!=null){wire=Protocol.Validate(wire,DateTimeOffset.UtcNow.ToUnixTimeSeconds());if(Protocol.Field(wire.Record,"author")==actor)records.Add(wire);}}catch(Exception e)when(e is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or InvalidDataException or FormatException){} }
            var savedAt=row.TryGetValue("savedAt",out var stamp) && stamp is long number ? number : 0;
            var community=row.TryGetValue("community",out var realm) && realm is string name ? name : actor[(actor.LastIndexOf('-')+1)..];
            result.Add(new Export(client,actor,enabled,records,savedAt,community));
        }return result;
    }
    public static string Quote(string value)=>"\""+value.Replace("\\","\\\\",StringComparison.Ordinal).Replace("\"","\\\"",StringComparison.Ordinal).Replace("\r","\\r",StringComparison.Ordinal).Replace("\n","\\n",StringComparison.Ordinal)+"\"";
}
