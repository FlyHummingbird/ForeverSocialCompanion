using System.Text.Json;
using ForeverSocial.Companion;
using ForeverSocial.Server;
using System.Net;
using System.Net.Http.Json;

if(args is ["migrate-legacy",var migrationRoot,var backupRoot]){
    var result=LegacyMigration.Retire(migrationRoot,backupRoot,preserveSnapshot:true);
    Console.WriteLine(JsonSerializer.Serialize(result,Protocol.Json));return;
}

if(args is ["inspect-exports",var gameRoot]){
    var files=BridgeFiles.Exports(gameRoot).ToList();var counts=new List<object>();
    foreach(var file in files){
        try{var parsed=SavedVariables.Exports(SavedVariables.Parse(File.ReadAllText(file)));
            counts.Add(new{parsed=true,exports=parsed.Count,enabled=parsed.Count(x=>x.Enabled),records=parsed.Sum(x=>x.Records.Count)});
        }catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or AccountAlreadyClaimedException or ArgumentException){counts.Add(new{parsed=false,error=ex.GetType().Name});}
    }
    Console.WriteLine(JsonSerializer.Serialize(new{files=files.Count,results=counts},Protocol.Json));return;
}
if(args is ["bridge-test",var root,var exported,var endpoint]){
    var export=SavedVariables.Exports(SavedVariables.Parse(File.ReadAllText(exported))).Single(e=>e.Enabled);
    using var client=new SyncClient(BridgeFiles.Endpoint(endpoint));
    var result=await client.Sync(export,Environment.GetEnvironmentVariable("FS_TEST_TOKEN") ?? throw new InvalidDataException("Missing test credential"));
    BridgeFiles.WriteSnapshot(root,export.Actor,export.Client,BridgeFiles.Endpoint(endpoint),result);
    Console.WriteLine(JsonSerializer.Serialize(new{result.Accepted,count=result.Records.Count},Protocol.Json));return;
}
var checks=new List<object>();var failures=0;var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
void Check(string name,Action run){try{run();checks.Add(new{name,ok=true});Console.WriteLine("PASS "+name);}catch(Exception e){failures++;checks.Add(new{name,ok=false,error=e.Message});Console.WriteLine("FAIL "+name+" "+e.Message);}}
void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
void Reject(Action run){try{run();}catch(Exception e)when(e is InvalidDataException or UnauthorizedAccessException or AccountAlreadyClaimedException){return;}throw new Exception("Invalid data was accepted");}
WireRecord Post(string actor="Beren-TestRealm",int version=1,bool deleted=false,string text="Grüße, Azeroth!")=>new("posts",JsonSerializer.SerializeToElement(new{id=actor+"@test",author=actor,created=now-60,updated=now,version,category="general",text,deleted},Protocol.Json));
var temp=Path.Combine(Directory.GetCurrentDirectory(),"TestArtifacts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
void InstallSlot(string root){var folder=Path.Combine(root,"Interface","AddOns","ForeverSocial");Directory.CreateDirectory(Path.Combine(folder,"Data"));var toc=Path.GetFileName(root)=="_forever_"?"ForeverSocial_Camelot.toc":"ForeverSocial_Mainline.toc";File.WriteAllText(Path.Combine(folder,toc),"## X-ForeverSocial-Companion: 1\nData/Companion.lua\n");File.WriteAllText(Path.Combine(folder,"Data","Companion.lua"),"-- Empty optional data cache\n");}

Check("valid UTF-8 post round trip",()=>Assert(Protocol.Field(Protocol.Validate(Post(),now).Record,"text")=="Grüße, Azeroth!"));
Check("text lengths count Unicode scalars",()=>{Assert(Protocol.Text(string.Concat(Enumerable.Repeat("😀",300)),300));Assert(!Protocol.Text(new string('a',301),300));});
Check("wire JSON preserves supplementary Unicode without unsupported escapes",()=>{var encoded=Protocol.EncodeWire(Protocol.Validate(Post(text:"\U0001F600 \u2028"),now));Assert(!encoded.Contains("\\u"));Assert(encoded.Contains("\U0001F600"));});
Check("escaped HTTP profile text uses the canonical UTF-8 size limit",()=>{var text=string.Concat(Enumerable.Repeat("\U0001F600",500));var value=JsonSerializer.SerializeToElement(new{id="Beren-TestRealm",author="Beren-TestRealm",created=now,updated=now,version=1,@class="WARRIOR",level=60,role="TANK",faction="Horde",@public=true,guild=new string('a',100),bio=text,activities=string.Concat(Enumerable.Repeat("\U0001F600",120)),professions=string.Concat(Enumerable.Repeat("\U0001F600",120))});Assert(value.GetRawText().Length>8192);Assert(Protocol.Field(Protocol.Validate(new WireRecord("profiles",value),now).Record,"bio")==text);});
Check("markup and controls rejected",()=>{Reject(()=>Protocol.Validate(Post(text:"|Hmalicious"),now));Reject(()=>Protocol.Validate(Post(text:"tab\t"),now));});
Check("future expired and oversized data rejected",()=>{Reject(()=>Protocol.Validate(Post(),now-10000));Reject(()=>Protocol.Validate(Post(),now+31*86400));Reject(()=>Protocol.Validate(Post(text:new string('a',301)),now));});
Check("author namespace cannot be borrowed",()=>{var row=Post();var d=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(row.Record.GetRawText())!;d["id"]=JsonSerializer.SerializeToElement("Mira-TestRealm@fake");Reject(()=>Protocol.Validate(row with{Record=JsonSerializer.SerializeToElement(d)},now));});
Check("unknown private fields are not forwarded",()=>{var row=Post();var d=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(row.Record.GetRawText())!;d["notes"]=JsonSerializer.SerializeToElement("PRIVATE");Assert(!Protocol.Validate(row with{Record=JsonSerializer.SerializeToElement(d)},now).Record.TryGetProperty("notes",out _));});
Check("deleted text is redacted",()=>Assert(Protocol.Field(Protocol.Validate(Post(deleted:true,text:"Must disappear"),now).Record,"text")==""));
Check("stale versions and resurrection rejected",()=>{Assert(!Protocol.CanReplace(Post(version:2),Post()));Assert(!Protocol.CanReplace(Post(deleted:true),Post(version:2)));});
Check("escaped UTF-8 byte sequences are decoded correctly",()=>Assert((string)SavedVariables.Parse("ForeverSocialBridge = {[\"x\"] = \"\\195\\188\"}")["x"]=="ü"));
Check("SavedVariables data subset supports normal WoW output",()=>{var data=SavedVariables.Parse("ForeverSocialBridge = { [\"format\"] = 1, [\"characters\"] = {}, -- comment\n}");Assert(data["format"].Equals(1L));});
Check("SavedVariables never executes Lua",()=>{foreach(var code in new[]{"ForeverSocialBridge = os.execute(\"x\")","ForeverSocialBridge = {} os.execute(\"x\")","ForeverSocialBridge = { [\"x\"] = function() end }","OtherVariable = {}"})Reject(()=>SavedVariables.Parse(code));});
Check("SavedVariables rejects duplicates and depth floods",()=>{Reject(()=>SavedVariables.Parse("ForeverSocialBridge = {[\"x\"]=1,[\"x\"]=2}"));Reject(()=>SavedVariables.Parse("ForeverSocialBridge = "+new string('{',15)+new string('}',15)));});
Check("HTTPS enforced except loopback",()=>{Assert(BridgeFiles.Endpoint("https://social.wgfreunde.com").Scheme=="https");Assert(BridgeFiles.Endpoint("http://127.0.0.1:18891").IsLoopback);foreach(var value in new[]{"http://social.wgfreunde.com","https://secret@example.com","file:///tmp/test","https://example.com/path"})Reject(()=>BridgeFiles.Endpoint(value));});
Check("generated Lua contains only escaped strings and declared data files",()=>{
    var root=Path.Combine(temp,"_retail_");Directory.CreateDirectory(Path.Combine(root,"Interface","AddOns"));InstallSlot(root);
    BridgeFiles.WriteSnapshot(root,"Alice-TestRealm","retail",new Uri("https://social.wgfreunde.com"),new Snapshot(1,now,[Protocol.Validate(Post(text:"\"}; evil() -- Grüße"),now)],1));
    var content=File.ReadAllText(Path.Combine(root,"Interface","AddOns","ForeverSocial","Data","Companion.lua"));Assert(content.Contains("\\\""));Assert(content.Contains("Grüße"));
    Assert(!Directory.Exists(Path.Combine(root,"WTF")));Assert(Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Count()==2);
});
Check("unrelated folders cannot be chosen as clients",()=>Reject(()=>BridgeFiles.ClientRoot(temp)));
Check("only account-global export files are read, never the full character database",()=>{var root=Path.Combine(temp,"sv-scope","_forever_");Directory.CreateDirectory(Path.Combine(root,"Interface","AddOns"));InstallSlot(root);var global=Path.Combine(root,"WTF","Account","TEST","SavedVariables","ForeverSocial.lua");var character=Path.Combine(root,"WTF","Account","TEST","TestRealm","Alice","SavedVariables","ForeverSocial.lua");Directory.CreateDirectory(Path.GetDirectoryName(global)!);Directory.CreateDirectory(Path.GetDirectoryName(character)!);File.WriteAllText(global,"ForeverSocialBridge = {}");File.WriteAllText(character,"ForeverSocialDB = {PRIVATE_SECRET=true}");Assert(BridgeFiles.Exports(root).Single()==global);});
Check("server binds tokens and authors and persists across restart",()=>{
    Credential credential;
    using(var store=new CommunityStore(Path.Combine(temp,"database"))){credential=store.Enroll("retail","Beren-TestRealm");var account=store.Authenticate(credential.Token)!;Assert(account.Actor=="Beren-TestRealm");Assert(store.Authenticate(new string('0',64))==null);Assert(store.Upload(account,[Post()],now)==1);Assert(store.Upload(account,[Post()],now)==0);Reject(()=>store.Upload(account,[Post("Mira-TestRealm")],now));Reject(()=>store.Enroll("retail","Beren-TestRealm"));}
    using(var store=new CommunityStore(Path.Combine(temp,"database"))){var account=store.Authenticate(credential.Token)!;Assert(store.Snapshot(account,now).Records.Count==1);Assert(store.Upload(account,[Post(version:2,deleted:true)],now)==1);Assert(store.Upload(account,[Post(version:3)],now)==0);store.Delete(account);Assert(store.Authenticate(credential.Token)==null);}
});
Check("batch is atomic when any author is invalid",()=>{using var store=new CommunityStore(Path.Combine(temp,"atomic"));var account=store.Authenticate(store.Enroll("retail","Beren-TestRealm").Token)!;Reject(()=>store.Upload(account,[Post(),Post("Mira-TestRealm")],now));Assert(store.Snapshot(account,now).Records.Count==0);});
Check("retail and forever data are scoped separately",()=>{using var store=new CommunityStore(Path.Combine(temp,"scope"));var a=store.Authenticate(store.Enroll("retail","Beren-TestRealm").Token)!;var b=store.Authenticate(store.Enroll("forever","Beren-TestRealm").Token)!;store.Upload(a,[Post()],now);Assert(store.Snapshot(b,now).Records.Count==0);});
Check("Forever snapshots never include unsupported other communities",()=>{using var store=new CommunityStore(Path.Combine(temp,"communities"));var a=store.Authenticate(store.Enroll("forever","Beren-TestRealm","TestRealm").Token)!;var b=store.Authenticate(store.Enroll("forever","Mira-OtherRealm","OtherRealm").Token)!;store.Upload(a,[Post()],now);Assert(store.Snapshot(b,now).Records.Count==0);Reject(()=>store.Enroll("forever","Rika-TestRealm","OtherRealm"));});
Check("stored credentials are hashes, never bearer secrets",()=>{using var store=new CommunityStore(Path.Combine(temp,"secrets"));var credential=store.Enroll("retail","Beren-TestRealm");Assert(!File.ReadAllText(Path.Combine(temp,"secrets","community.json")).Contains(credential.Token));});
string ClientFolder(string name){var root=Path.Combine(temp,name,"_forever_");Directory.CreateDirectory(Path.Combine(root,"Interface","AddOns"));InstallSlot(root);return root;}
Export OwnExport(long saved=1)=>new("forever","Beren-TestRealm",true,[Post()],saved,"TestRealm");
var service=new Uri("https://social.wgfreunde.com");
Check("enrollment sends permitted character metadata without an invitation",()=>{
    var handler=new SyncHandler();using var client=new SyncClient(service,handler);
    var credential=client.Enroll(OwnExport()).GetAwaiter().GetResult();
    var request=JsonSerializer.Deserialize<Enrollment>(handler.EnrollmentBody!,Protocol.Json)!;
    Assert(credential.Token=="synthetic" && request is {Format:1,Enabled:true,Actor:"Beren-TestRealm",Client:"forever",Community:"TestRealm"});
    Assert(!handler.EnrollmentBody!.Contains("invite",StringComparison.OrdinalIgnoreCase));
});
Check("disabled exports cannot enroll or send HTTP",()=>{
    var handler=new SyncHandler();using var client=new SyncClient(service,handler);
    Reject(()=>client.Enroll(OwnExport() with{Enabled=false}).GetAwaiter().GetResult());Assert(handler.Gets+handler.Posts==0);
});
Check("a second enrollment cannot rotate the first claim's credential",()=>{
    using var store=new CommunityStore(Path.Combine(temp,"duplicate-claim"));var credential=store.Enroll("forever","Beren-TestRealm");
    Reject(()=>store.Enroll("forever","Beren-TestRealm"));Assert(store.Authenticate(credential.Token)?.Actor=="Beren-TestRealm");
});
Check("background download mode always fetches without uploading unchanged records",()=>{
    var handler=new SyncHandler{Records=[Post()]};using var client=new SyncClient(service,handler);
    var snapshot=client.Sync(OwnExport(),"synthetic",uploadRecords:false).GetAwaiter().GetResult();
    Assert(handler.Gets==1 && handler.Posts==0 && snapshot.Records.Count==1);
});
Check("session polls remote changes even when own export and save time are unchanged",()=>{
    var root=ClientFolder("poll");var handler=new SyncHandler{Records=[Post()]};using var client=new SyncClient(service,handler);var session=new SyncSession();
    var first=session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();
    handler.Records.Add(Post("Mira-TestRealm",text:"New server post"));
    var second=session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();
    Assert(first.DataChanged && second.DataChanged && !second.Uploaded && handler.Posts==1 && handler.Gets==2);
    Assert(File.ReadAllText(Path.Combine(root,"Interface/AddOns/ForeverSocial/Data/Companion.lua")).Contains("New server post"));
});
Check("save-time-only changes neither upload again nor rewrite unchanged snapshots",()=>{
    var root=ClientFolder("unchanged");var handler=new SyncHandler{Records=[Post()]};using var client=new SyncClient(service,handler);var session=new SyncSession();
    session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();var file=Path.Combine(root,"Interface/AddOns/ForeverSocial/Data/Companion.lua");var stamp=File.GetLastWriteTimeUtc(file);
    var next=session.Synchronize(root,OwnExport(2),service,"synthetic",client).GetAwaiter().GetResult();Assert(!next.Uploaded && !next.DataChanged && handler.Gets==2 && handler.Posts==1 && File.GetLastWriteTimeUtc(file)==stamp);
});
Check("failed HTTP work is retried with its upload",()=>{
    var root=ClientFolder("retry");var handler=new SyncHandler{FailOnce=true};using var client=new SyncClient(service,handler);var session=new SyncSession();
    try{session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();throw new Exception("Failure expected");}catch(HttpRequestException){}
    var result=session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();Assert(result.Uploaded && handler.Posts==2 && handler.Gets==1);
});
Check("duplicate or invalid snapshot cannot replace the previous valid file",()=>{
    var root=ClientFolder("invalid-response");var handler=new SyncHandler{Records=[Post()]};using var client=new SyncClient(service,handler);var session=new SyncSession();
    session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();var file=Path.Combine(root,"Interface/AddOns/ForeverSocial/Data/Companion.lua");var before=File.ReadAllText(file);
    handler.Records.Add(Post());Reject(()=>session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult());Assert(File.ReadAllText(file)==before);
    handler.Records=[Post(text:"|Hinvalid")];Reject(()=>session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult());Assert(File.ReadAllText(file)==before);
});
Check("session refuses a client-flavor mismatch before network access",()=>{
    var root=ClientFolder("mismatch");var handler=new SyncHandler();using var client=new SyncClient(service,handler);
    Reject(()=>new SyncSession().Synchronize(root,OwnExport() with{Client="retail"},service,"synthetic",client).GetAwaiter().GetResult());Assert(handler.Gets+handler.Posts==0);
});
Check("changed destination uploads once for the new scope and recreates missing payloads",()=>{
    var root=ClientFolder("scope-a");var other=ClientFolder("scope-b");var handler=new SyncHandler();using var client=new SyncClient(service,handler);var session=new SyncSession();
    session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();session.Synchronize(other,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();Assert(handler.Posts==2);
    var file=Path.Combine(other,"Interface/AddOns/ForeverSocial/Data/Companion.lua");File.Delete(file);
    var outcome=session.Synchronize(other,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();Assert(outcome.DataChanged && !outcome.Uploaded && File.Exists(file));
});
Check("addon update replacing the cache is repaired without a repeated upload",()=>{
    var root=ClientFolder("addon-update");var handler=new SyncHandler{Records=[Post()]};using var client=new SyncClient(service,handler);var session=new SyncSession();
    session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();var path=BridgeFiles.SnapshotPath(root);
    File.WriteAllText(path,"-- Empty cache from an addon update\n");
    var outcome=session.Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();
    Assert(outcome.DataChanged && !outcome.Uploaded && handler.Posts==1 && File.ReadAllText(path).Contains("Grüße",StringComparison.Ordinal));
});
Check("old addons are rejected before any upload and no second addon is created",()=>{
    var root=Path.Combine(temp,"old-addon","_forever_");Directory.CreateDirectory(Path.Combine(root,"Interface","AddOns"));var handler=new SyncHandler();using var client=new SyncClient(service,handler);
    var rejected=false;try{new SyncSession().Synchronize(root,OwnExport(),service,"synthetic",client).GetAwaiter().GetResult();}catch(AddonUpdateRequiredException){rejected=true;}
    Assert(rejected && handler.Gets+handler.Posts==0 && !Directory.Exists(Path.Combine(root,"Interface/AddOns/ForeverSocial_Companion")));
});
string Legacy(string root,string? extra=null){
    BridgeFiles.WriteSnapshot(root,"Beren-TestRealm","forever",service,new Snapshot(1,now,[Post()],0));
    var folder=Path.Combine(root,"Interface/AddOns/ForeverSocial_Companion");Directory.CreateDirectory(folder);
    File.Copy(BridgeFiles.SnapshotPath(root),Path.Combine(folder,"Payload.lua"));
    File.WriteAllText(Path.Combine(folder,"ForeverSocial_Companion.toc"),"## Title: Forever Social Companion Data\nPayload.lua\n");
    if(extra is not null)File.WriteAllText(Path.Combine(folder,extra),"Do not delete");return folder;
}
Check("legacy cache is parsed without execution backed up and merged into the main addon",()=>{
    var root=ClientFolder("migration");var old=Legacy(root);var before=File.ReadAllBytes(Path.Combine(old,"Payload.lua"));File.WriteAllText(BridgeFiles.SnapshotPath(root),"-- Empty cache\n");
    var result=LegacyMigration.Retire(root,Path.Combine(temp,"migration-backups"),preserveSnapshot:true);
    Assert(result.Removed && result.PreservedSnapshot && !Directory.Exists(old) && File.ReadAllBytes(Path.Combine(result.Backup!,"Payload.lua")).SequenceEqual(before));
    Assert(File.ReadAllText(BridgeFiles.SnapshotPath(root)).Contains("Grüße",StringComparison.Ordinal));
});
Check("legacy cleanup refuses unknown files and never recursively deletes",()=>{
    var root=ClientFolder("unknown-legacy");var old=Legacy(root,"keep.txt");var result=LegacyMigration.Retire(root,Path.Combine(temp,"unused-backup"));
    Assert(!result.Removed && File.Exists(Path.Combine(old,"keep.txt")) && File.Exists(Path.Combine(old,"Payload.lua")) && result.Backup is null);
});
Check("tampered executable legacy data is rejected with original files intact",()=>{
    var root=ClientFolder("tampered-legacy");var old=Legacy(root);File.AppendAllText(Path.Combine(old,"Payload.lua"),"\nos.execute(\"NEVER_EXECUTE\")");
    Reject(()=>LegacyMigration.Retire(root,Path.Combine(temp,"tampered-backups"),preserveSnapshot:true));Assert(File.Exists(Path.Combine(old,"Payload.lua")));
});
Check("legacy backups cannot be placed inside the game installation",()=>{
    var root=ClientFolder("unsafe-backup");var old=Legacy(root);Reject(()=>LegacyMigration.Retire(root,Path.Combine(root,"backup")));Assert(Directory.Exists(old));
});
Check("snapshot data parser supports generated keys and still refuses function calls",()=>{
    Assert(SavedVariables.ParseSnapshot("ForeverSocialCompanionData={format=1,records={\"safe\"}} ")["format"].Equals(1L));
    foreach(var bad in new[]{"ForeverSocialCompanionData=os.execute(\"x\")","ForeverSocialCompanionData={format=1} os.execute(\"x\")","ForeverSocialCompanionData={records=function()end}"})Reject(()=>SavedVariables.ParseSnapshot(bad));
});
var report=new{generatedAtUTC=DateTimeOffset.UtcNow,total=checks.Count,passed=checks.Count-failures,checks};Directory.CreateDirectory("TestArtifacts");File.WriteAllText("TestArtifacts/unit-results.json",JsonSerializer.Serialize(report,Protocol.Json));
Environment.ExitCode=failures==0?0:1;

sealed class SyncHandler:HttpMessageHandler
{
    internal int Gets,Posts;
    internal bool FailOnce;
    internal string? EnrollmentBody;
    internal List<WireRecord> Records=[];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if(request.Method==HttpMethod.Post)Posts++;else Gets++;
        if(FailOnce){FailOnce=false;return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);}
        if(request.RequestUri!.AbsolutePath=="/v1/enroll"){
            EnrollmentBody=await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new Credential("synthetic","account"),options:Protocol.Json)};
        }
        return new HttpResponseMessage(HttpStatusCode.OK){Content=request.Method==HttpMethod.Post
            ?JsonContent.Create(new{accepted=0},options:Protocol.Json)
            :JsonContent.Create(new Snapshot(1,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),Records,0),options:Protocol.Json)};
    }
}
