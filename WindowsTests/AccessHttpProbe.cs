using System.Net;
using System.Text.Json;
using ForeverSocial.Companion;

internal static class AccessHttpProbe
{
    internal static int Run(string root,string endpoint,string report)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        var checks=new List<object>();var work=Path.GetDirectoryName(root)!;
        var token=Environment.GetEnvironmentVariable("FS_TEST_TOKEN") ?? throw new InvalidDataException("Synthetic token missing");
        var uri=BridgeFiles.Endpoint(endpoint);var password="Isolated-test-password!";var transfer=Path.Combine(work,"access.fsaccess");
        var config=Path.Combine(work,"access-settings.json");var saved=(string?)null;var removed=false;var httpCalls=0;
        void Check(string name,bool condition){if(!condition)throw new InvalidDataException(name);checks.Add(new{name,ok=true});Console.WriteLine("PASS "+name);}
        void Await(Task task){var watch=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted && watch.ElapsedMilliseconds<20000){Application.DoEvents();Thread.Sleep(5);}if(!task.IsCompleted)throw new TimeoutException("Access probe timed out");task.GetAwaiter().GetResult();}
        CompanionWindow Window(bool existing,string settings)=>new(_=>null,settings,new CompanionServices{
            CreateClient=address=>new SyncClient(address,new CountingHandler(()=>httpCalls++)),
            LoadCredential=(_,_)=>saved ?? (existing && !removed?token:null),SaveCredential=(_,_,value)=>saved=value,
            DeleteCredential=(_,_)=>{saved=null;removed=true;},AccessFile=(_,_)=>transfer,AccessPassword=(_,_)=>password,ConfirmAccess=(_,_)=>true
        }){Opacity=0,ShowInTaskbar=false};
        void Show(CompanionWindow window){window.Show();Application.DoEvents();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());}
        new CompanionSettings{Folder=root,Endpoint=uri.AbsoluteUri}.Save(config);
        using(var first=Window(true,config)){
            Show(first);Await(first.ManageAccessAsync("export"));
            Check("real API verifies export scope before encrypted access backup",File.Exists(transfer) && !File.ReadAllText(transfer).Contains(token,StringComparison.Ordinal));
        }
        using(var second=Window(false,config)){
            Show(second);Await(second.ManageAccessAsync("import"));Check("second Forms instance imports and validates existing access over real HTTP",saved==token);
            Await(second.SynchronizeNowAsync());Check("imported credential downloads actual server content",File.ReadAllText(BridgeFiles.SnapshotPath(root)).Contains("Synthetic access record",StringComparison.Ordinal));
            Await(second.ManageAccessAsync("delete"));Check("confirmed deletion clears local access data and persists recreation guard",removed && saved is null && CompanionSettings.Load(config).RetiredScopes.Count==1 && !File.ReadAllText(BridgeFiles.SnapshotPath(root)).Contains("Synthetic access record",StringComparison.Ordinal));
        }
        using(var client=new HttpClient()){
            client.DefaultRequestHeaders.Authorization=new("Bearer",token);
            Check("real API revokes transferred token after account deletion",client.GetAsync(new Uri(uri,"v1/account")).GetAwaiter().GetResult().StatusCode==HttpStatusCode.Unauthorized);
        }
        var before=httpCalls;
        using(var restarted=Window(false,config)){
            Show(restarted);Await(restarted.SynchronizeNowAsync());
            var primary=(Button)restarted.Controls.Find("synchronize",true).Single();primary.PerformClick();Application.DoEvents();
            Check("restarted Forms instance cannot silently enroll or poll a deleted account",httpCalls==before && !restarted.IsSynchronizing);
        }
        File.WriteAllText(report,JsonSerializer.Serialize(new{generatedAtUTC=DateTimeOffset.UtcNow,passed=checks.Count,total=checks.Count,checks,realHttp=true,realPlayerData=false},Protocol.Json));return 0;
    }
    private sealed class CountingHandler(Action count):DelegatingHandler(new HttpClientHandler{AllowAutoRedirect=false})
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){count();return base.SendAsync(request,cancellationToken);}
    }
}
