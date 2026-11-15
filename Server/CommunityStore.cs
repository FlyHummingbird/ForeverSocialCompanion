using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForeverSocial.Companion;

namespace ForeverSocial.Server;
public sealed record Account(string Id,string Client,string Actor,string Community="");
public sealed record StoredRecord(string Client,string Owner,WireRecord Wire,string Community="");
public sealed record State(int Format,Dictionary<string,Account> Accounts,Dictionary<string,StoredRecord> Records);

public sealed class CommunityStore : IDisposable
{
    private readonly string path;private readonly object gate=new();private readonly FileStream lease;
    private State state;
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public CommunityStore(string folder)
    {
        Directory.CreateDirectory(folder);path=Path.Combine(folder,"community.json");
        lease=new FileStream(Path.Combine(folder,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(path)){
            if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("Database too large");
            state=JsonSerializer.Deserialize<State>(File.ReadAllText(path),Protocol.Json) ?? throw new InvalidDataException("Invalid database");
            if(state.Format!=1 || state.Accounts.Count>1000 || state.Records.Count>10000)throw new InvalidDataException("Unsupported database");
        }else state=new State(1,[],[]);
    }
    private void Save(State next)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(next,Protocol.Json);if(bytes.Length>64*1024*1024)throw new InvalidDataException("Storage limit");
        var temp=path+".new";using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
        File.Move(temp,path,true);state=next;
    }
    public Credential Enroll(string client,string actor,string community="")
    {
        if(client is not ("retail" or "forever") || !Protocol.Identity(actor))throw new InvalidDataException("Invalid character");
        if(community=="")community=actor[(actor.LastIndexOf('-')+1)..];
        if(!Protocol.Text(community,100) || string.IsNullOrWhiteSpace(community) || community.Any(char.IsWhiteSpace) || !actor.EndsWith("-"+community,StringComparison.Ordinal))throw new InvalidDataException("Invalid community");
        lock(gate){
            if(state.Accounts.Values.Any(a=>a.Client==client && a.Actor==actor))throw new InvalidDataException("Character already claimed; this is an unverified account claim, not Blizzard verification");
            if(state.Accounts.Count>=1000)throw new InvalidDataException("Account limit");
            var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var account=new Account(Guid.NewGuid().ToString("N"),client,actor,community);
            var accounts=new Dictionary<string,Account>(state.Accounts){[Hash(token)]=account};Save(state with{Accounts=accounts});return new Credential(token,account.Id);
        }
    }
    public Account? Authenticate(string token)
    {
        if(token.Length!=64 || !token.All(char.IsAsciiHexDigit))return null;
        lock(gate)return state.Accounts.GetValueOrDefault(Hash(token));
    }
    public int Upload(Account account,List<WireRecord> records,long now)
    {
        if(records.Count>96)throw new InvalidDataException("Batch limit");
        var clean=records.Select(r=>Protocol.Validate(r,now)).ToList();
        if(clean.Any(r=>Protocol.Field(r.Record,"author")!=account.Actor))throw new UnauthorizedAccessException();
        if(account.Client=="forever" && clean.Any(r=>r.Kind is "ratings" or "follows" && !Protocol.Field(r.Record,"target").EndsWith("-"+account.Community,StringComparison.Ordinal)))throw new InvalidDataException("Unsupported target community");
        lock(gate){
            var next=new Dictionary<string,StoredRecord>(state.Records);var accepted=0;
            foreach(var row in clean){
                var key=account.Client+":"+Protocol.Key(row);var old=next.GetValueOrDefault(key);
                if(old!=null && old.Owner!=account.Id)throw new UnauthorizedAccessException();
                if(old!=null && !Protocol.CanReplace(old.Wire,row))continue;
                if(row.Kind is "comments" or "likes"){
                    var parent=next.GetValueOrDefault(account.Client+":posts:"+Protocol.Field(row.Record,"post"));
                    if(parent==null || parent.Wire.Record.GetProperty("deleted").GetBoolean() || account.Client=="forever" && parent.Community!=account.Community)continue;
                }
                if(old==null && next.Values.Count(r=>r.Owner==account.Id)>=1500)throw new InvalidDataException("Account record limit");
                if(next.Count>=10000 && old==null)throw new InvalidDataException("Community record limit");
                next[key]=new StoredRecord(account.Client,account.Id,row,account.Community);accepted++;
            }
            foreach(var key in next.Where(p=>(p.Value.Wire.Kind is "posts" or "comments" or "listings" ? Protocol.Stamp(p.Value.Wire.Record,"created") : Protocol.Stamp(p.Value.Wire.Record,"updated"))<now-30*86400).Select(p=>p.Key).ToList())next.Remove(key);
            if(accepted>0 || next.Count!=state.Records.Count)Save(state with{Records=next});return accepted;
        }
    }
    public Snapshot Snapshot(Account account,long now)
    {
        lock(gate){
            var records=state.Records.Values.Where(r=>r.Client==account.Client && (account.Client!="forever" || r.Community==account.Community) && (r.Wire.Kind is "posts" or "comments" or "listings" ? Protocol.Stamp(r.Wire.Record,"created") : Protocol.Stamp(r.Wire.Record,"updated"))>=now-30*86400)
                .OrderBy(r=>Array.IndexOf(Protocol.Kinds,r.Wire.Kind)).ThenByDescending(r=>Protocol.Stamp(r.Wire.Record,"updated"))
                .GroupBy(r=>r.Wire.Kind).SelectMany(g=>g.Take(g.Key=="profiles"?300:g.Key=="posts"?300:150)).Select(r=>r.Wire).Take(1500).ToList();
            var posts=records.Where(r=>r.Kind=="posts" && !r.Record.GetProperty("deleted").GetBoolean()).Select(r=>Protocol.Field(r.Record,"id")).ToHashSet();
            records.RemoveAll(r=>r.Kind is "comments" or "likes" && !posts.Contains(Protocol.Field(r.Record,"post")));
            return new Snapshot(1,now,records,0);
        }
    }
    public void Delete(Account account){lock(gate){var accounts=state.Accounts.Where(p=>p.Value.Id!=account.Id).ToDictionary();var records=state.Records.Where(p=>p.Value.Owner!=account.Id).ToDictionary();Save(new State(1,accounts,records));}}
    public void Dispose()=>lease.Dispose();
}
