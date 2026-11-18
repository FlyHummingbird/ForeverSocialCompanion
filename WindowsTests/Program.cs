using System.Reflection;
using System.Text.Json;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Runtime.Loader;
using System.Net;
using System.Net.Http.Json;
using ForeverSocial.Companion;

internal static class WindowsRegressionTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args is ["--access-http",var accessRoot,var accessEndpoint,var accessReport])return AccessHttpProbe.Run(accessRoot,accessEndpoint,accessReport);
        if(args is ["--background-http",var fixtureRoot,var testEndpoint,var backgroundReport])return LoopbackBackgroundProbe.Run(fixtureRoot,testEndpoint,backgroundReport);
        if(args is ["--inspect-game",var gameRoot]){
            using var window=new CompanionWindow(_=>null,Path.Combine(Path.GetTempPath(),"ForeverSocial-probe-"+Guid.NewGuid().ToString("N")+".json"));
            var input=(TextBox)window.Controls.Find("gameFolder",true).Single();input.Text=gameRoot;
            typeof(TextBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(input,[new KeyEventArgs(Keys.Enter)]);
            var count=((ComboBox)window.Controls.Find("characters",true).Single()).Items.Count;
            Console.WriteLine(JsonSerializer.Serialize(new{characters=count,publicExportReadable=count>0,networkUsed=false,credentialsRead=false,gameFilesWritten=false}));return count>0?0:1;
        }
        if (args is ["--entrypoint", var assemblyPath, var entryReport]) {
            var probe=new AssemblyLoadContext("packaged-entrypoint",isCollectible:true);
            var assembly = probe.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
            var ok = assembly.EntryPoint?.GetCustomAttribute<STAThreadAttribute>() is not null;
            File.WriteAllText(entryReport, JsonSerializer.Serialize(new {
                assembly = Path.GetFileName(assemblyPath), sha256=Digest(assemblyPath),
                exactAssemblyLoaded=Path.GetFullPath(assembly.Location)==Path.GetFullPath(assemblyPath),
                requiredUiApartment = "STA", mainHasStaThreadAttribute = ok,
                passed = ok ? 1 : 0, total = 1
            }));
            Console.WriteLine((ok ? "PASS" : "FAIL") + " packaged entry point uses STA for Windows shell dialogs");
            probe.Unload();
            return ok ? 0 : 1;
        }
        if (args is not ["--report", var reportPath]) return 2;
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Keep a Forms message host alive while many test windows are closed and
        // recreated. Production uses Application.Run with one main form.
        using var messageHost=new Form{ShowInTaskbar=false,Opacity=0,Size=new System.Drawing.Size(1,1)};
        messageHost.Show();Application.DoEvents();
        var checks=new List<object>();var failed=0;
        var work=Path.Combine(Path.GetTempPath(),"ForeverSocial-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var windows=new List<CompanionWindow>();
        void Assert(bool value,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){if(!value)throw new InvalidOperationException("UI assertion failed at line "+line);}
        void Check(string name,Action action){
            var filter=Environment.GetEnvironmentVariable("FS_UI_TEST_FILTER");if(filter is not null && !name.Contains(filter,StringComparison.Ordinal))return;
            try{action();checks.Add(new{name,ok=true});Console.WriteLine("PASS "+name);}
            catch(Exception ex){failed++;checks.Add(new{name,ok=false,error=ex.GetType().Name+": "+ex.Message});Console.WriteLine("FAIL "+name+": "+ex.Message);}
            finally{foreach(var window in windows){window.Close();window.Dispose();}windows.Clear();}
        }
        CompanionWindow Window(Func<IWin32Window,string?> picker,CompanionServices? services=null,string? settings=null,bool setup=true){
            var window=new CompanionWindow(picker,settings ?? Path.Combine(work,"settings-"+Guid.NewGuid().ToString("N")+".json"),services){
                ShowInTaskbar=false,Opacity=0};
            windows.Add(window);window.Show();Application.DoEvents();
            // There is no Application.Run in this component harness. Install the
            // real UI context explicitly before invoking async window handlers.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            if(setup)window.ShowSetup(true);
            return window;
        }
        T Control<T>(CompanionWindow window,string name)where T:System.Windows.Forms.Control => (T)window.Controls.Find(name,true).Single();
        void Browse(CompanionWindow window)=>Control<Button>(window,"browse").PerformClick();
        void Enter(CompanionWindow window,string path){
            var input=Control<TextBox>(window,"gameFolder");input.Text=path;
            var key=new KeyEventArgs(Keys.Enter);
            typeof(TextBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(input,[key]);
            Assert(key.SuppressKeyPress && key.Handled);
        }
        string Export(string client,string actor="Alice-TestRealm")=>"ForeverSocialBridge = {[\"format\"]=1,[\"characters\"]={[\""+actor+"\"]={[\"client\"]=\""+client+"\",[\"community\"]=\"TestRealm\",[\"enabled\"]=true,[\"savedAt\"]=1,[\"records\"]={}}}}";
        string Client(string flavor){
            var root=Path.Combine(work,"WoW ä "+Guid.NewGuid().ToString("N"),"_"+flavor+"_");
            Directory.CreateDirectory(Path.Combine(root,"Interface","AddOns"));
            var slot=Path.Combine(root,"Interface","AddOns","ForeverSocial");Directory.CreateDirectory(Path.Combine(slot,"Data"));
            File.WriteAllText(Path.Combine(slot,flavor=="forever"?"ForeverSocial_Camelot.toc":"ForeverSocial_Mainline.toc"),"## X-ForeverSocial-Companion: 1\nData/Companion.lua\n");
            File.WriteAllText(Path.Combine(slot,"Data","Companion.lua"),"-- Empty optional cache\n");
            var sv=Path.Combine(root,"WTF","Account","TEST","SavedVariables");Directory.CreateDirectory(sv);
            File.WriteAllText(Path.Combine(sv,"ForeverSocial.lua"),Export(flavor));return root;
        }
        void PumpUntil(Func<bool> ready){var watch=System.Diagnostics.Stopwatch.StartNew();while(!ready() && watch.ElapsedMilliseconds<5000){Application.DoEvents();Thread.Sleep(5);}Assert(ready());}
        void Await(Task task,[System.Runtime.CompilerServices.CallerArgumentExpression(nameof(task))]string expression=""){
            try{PumpUntil(()=>task.IsCompleted);}catch(Exception ex){throw new InvalidOperationException("Pending test task: "+expression,ex);}task.GetAwaiter().GetResult();}
        string ExportFile(string root)=>Path.Combine(root,"WTF","Account","TEST","SavedVariables","ForeverSocial.lua");
        string Payload(string root)=>Path.Combine(root,"Interface","AddOns","ForeverSocial","Data","Companion.lua");
        void WritePost(string root,string text="My public post",long savedAt=1){
            var record=FakeCommunity.Post("Alice-TestRealm",text);
            File.WriteAllText(ExportFile(root),Export("forever").Replace("[\"savedAt\"]=1","[\"savedAt\"]="+savedAt,StringComparison.Ordinal)
                .Replace("[\"records\"]={}","[\"records\"]={"+SavedVariables.Quote(Protocol.EncodeWire(record))+"}",StringComparison.Ordinal));
        }
        string CompactSettings(string root){var config=Path.Combine(work,"compact-"+Guid.NewGuid().ToString("N")+".json");new CompanionSettings{Folder=root}.Save(config);return config;}
        Check("application entry point declares STA",()=>Assert(typeof(CompanionWindow).Assembly.EntryPoint?.GetCustomAttribute<STAThreadAttribute>() is not null));
        Check("actual Windows Forms thread and OLE initialize as STA",()=>Assert(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA && Application.OleRequired()==ApartmentState.STA));
        foreach(var culture in new[]{"en-US","de-DE"}){
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(culture);
            var manualHint=culture=="de-DE"?"direkt ein":"directly";
            Check(culture+": accepted folder loads character with dialog owner and STA",()=>{
                var root=Client("forever");IWin32Window? owner=null;ApartmentState? apartment=null;
                using var window=Window(w=>{owner=w;apartment=Thread.CurrentThread.GetApartmentState();return root;});Browse(window);
                Assert(owner==window && apartment==ApartmentState.STA);
                Assert(Control<TextBox>(window,"gameFolder").Text==root && Control<ComboBox>(window,"characters").Items.Count==1);
            });
            Check(culture+": cancel preserves an existing valid selection",()=>{
                var root=Client("forever");var count=0;using var window=Window(_=>++count==1?root:null);
                Browse(window);var actor=Control<ComboBox>(window,"characters").SelectedItem;Browse(window);
                Assert(Control<TextBox>(window,"gameFolder").Text==root && Equals(Control<ComboBox>(window,"characters").SelectedItem,actor));
            });
            Check(culture+": COM dialog failure is recoverable on the next click",()=>{
                var root=Client("retail");var count=0;using var window=Window(_=>++count==1?throw new COMException("Synthetic picker failure"):root);
                Browse(window);Assert(Control<Label>(window,"status").Text.Contains(manualHint,StringComparison.Ordinal));
                Browse(window);Assert(Control<ComboBox>(window,"characters").Items.Count==1 && !window.IsDisposed);
            });
            Check(culture+": thread-state failure offers manual path entry",()=>{
                using var window=Window(_=>throw new ThreadStateException("Synthetic wrong apartment"));Browse(window);
                Assert(Control<Label>(window,"status").Text.Contains(manualHint,StringComparison.Ordinal) && !window.IsDisposed);
            });
            Check(culture+": denied shell dialog retains the selected path",()=>{
                var root=Client("forever");var count=0;using var window=Window(_=>++count==1?root:throw new System.ComponentModel.Win32Exception(5));
                Browse(window);Browse(window);Assert(Control<TextBox>(window,"gameFolder").Text==root && Control<ComboBox>(window,"characters").Items.Count==1);
            });
            Check(culture+": manually entered Retail path and Enter load characters",()=>{
                var root=Client("retail");using var window=Window(_=>null);Enter(window,root);
                Assert(Control<ComboBox>(window,"characters").Items.Count==1 && Control<ComboBox>(window,"characters").SelectedItem!.ToString()!.EndsWith("retail",StringComparison.Ordinal));
            });
            Check(culture+": invalid then valid folders cannot retain stale characters",()=>{
                var root=Client("forever");using var window=Window(_=>root);Browse(window);Enter(window,work);
                Assert(Control<ComboBox>(window,"characters").Items.Count==0);Enter(window,root);Assert(Control<ComboBox>(window,"characters").Items.Count==1);
            });
            Check(culture+": corrupt export is contained and recovery works",()=>{
                var root=Client("forever");var file=Path.Combine(root,"WTF","Account","TEST","SavedVariables","ForeverSocial.lua");
                using var window=Window(_=>root);Browse(window);File.WriteAllText(file,"ForeverSocialBridge = {broken");Enter(window,root);
                Assert(Control<ComboBox>(window,"characters").Items.Count==0);File.WriteAllText(file,Export("forever"));Enter(window,root);
                Assert(Control<ComboBox>(window,"characters").Items.Count==1);
            });
            Check(culture+": removed consent clears the old character selection",()=>{
                var root=Client("forever");using var window=Window(_=>root);Browse(window);
                var file=Path.Combine(root,"WTF","Account","TEST","SavedVariables","ForeverSocial.lua");File.WriteAllText(file,Export("forever").Replace("true","false",StringComparison.Ordinal));
                Enter(window,root);Assert(Control<ComboBox>(window,"characters").Items.Count==0);
            });
            Check(culture+": repeated manual validation preserves the chosen character",()=>{
                var root=Client("forever");var extra=Path.Combine(root,"WTF","Account","SECOND","SavedVariables");Directory.CreateDirectory(extra);
                File.WriteAllText(Path.Combine(extra,"ForeverSocial.lua"),Export("forever","Beren-TestRealm"));
                using var window=Window(_=>root);Browse(window);var characters=Control<ComboBox>(window,"characters");Assert(characters.Items.Count==2);
                characters.SelectedIndex=1;var selected=characters.SelectedItem;Enter(window,root);Assert(Equals(characters.SelectedItem,selected));
            });
            Check(culture+": folder selection performs no network, credential or game-data writes",()=>{
                var root=Client("forever");var configs=Directory.EnumerateFiles(work,"settings-*.json").Order().ToArray();using var window=Window(_=>root);Browse(window);
                Assert(Directory.EnumerateFiles(Path.Combine(root,"Interface","AddOns"),"*",SearchOption.AllDirectories).Count()==2 && !File.ReadAllText(Payload(root)).Contains("ForeverSocialCompanionData",StringComparison.Ordinal));
                Assert(Directory.EnumerateFiles(work,"settings-*.json").Order().SequenceEqual(configs));
            });
            Check(culture+": returning to the app discovers an export saved after startup",()=>{
                var root=Client("forever");var file=Path.Combine(root,"WTF","Account","TEST","SavedVariables","ForeverSocial.lua");File.Delete(file);
                using var window=Window(_=>root);Enter(window,root);Assert(Control<ComboBox>(window,"characters").Items.Count==0);
                File.WriteAllText(file,Export("forever"));
                typeof(Form).GetMethod("OnActivated",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[EventArgs.Empty]);
                Assert(Control<ComboBox>(window,"characters").Items.Count==1);
            });
            Check(culture+": explicit connect refreshes late exports and enrolls without a code",()=>{
                var root=Client("forever");var file=ExportFile(root);File.Delete(file);var server=new FakeCommunity();
                using var window=Window(_=>root,server.Services(credential:false));Enter(window,root);
                File.WriteAllText(file,Export("forever"));Control<Button>(window,"connect").PerformClick();PumpUntil(()=>!window.IsSynchronizing);
                Assert(Control<ComboBox>(window,"characters").Items.Count==1 && server.Enrolls==1 && server.Downloads==1);
                Assert(server.LastEnrollment is {Format:1,Enabled:true,Actor:"Alice-TestRealm",Client:"forever",Community:"TestRealm"});
                Await(window.SynchronizeNowAsync());Assert(server.Enrolls==1 && server.Downloads==2);
            });
            Check(culture+": a missing export gives reload instructions before any connection attempt",()=>{
                var root=Client("forever");File.Delete(Path.Combine(root,"WTF","Account","TEST","SavedVariables","ForeverSocial.lua"));
                using var window=Window(_=>root);Enter(window,root);Control<Button>(window,"connect").PerformClick();
                Assert(Control<Label>(window,"status").Text.Contains("/reload",StringComparison.Ordinal));
                Assert(Control<ComboBox>(window,"characters").Items.Count==0);
            });
            Check(culture+": activation preserves successful status without a network request",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>root,server.Services());Enter(window,root);
                Await(window.SynchronizeNowAsync());var status=Control<Label>(window,"status").Text;var requests=server.Downloads;
                using(var bitmap=new System.Drawing.Bitmap(window.Width,window.Height)){window.DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,window.Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"companion-"+typeof(CompanionWindow).Assembly.GetName().Version?.ToString(3)+"-"+culture+".png"));}
                typeof(Form).GetMethod("OnActivated",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[EventArgs.Empty]);
                Assert(Control<Label>(window,"status").Text==status && server.Downloads==requests);
            });
            Check(culture+": real timer receives remote updates with unchanged export while minimized",()=>{
                var root=Client("forever");WritePost(root);var original=Digest(ExportFile(root));var server=new FakeCommunity();
                using var window=Window(_=>root,server.Services(60));Enter(window,root);Await(window.SynchronizeNowAsync());
                Assert(server.Uploads==1 && server.Downloads==1);server.AddPost("Beren-TestRealm","Arrived while minimized");
                Control<CheckBox>(window,"automatic").Checked=true;Control<Button>(window,"minimize").PerformClick();
                PumpUntil(()=>server.Downloads>=2 && !window.IsSynchronizing);Control<CheckBox>(window,"automatic").Checked=false;
                Assert(server.Uploads==1 && File.ReadAllText(Payload(root)).Contains("Arrived while minimized",StringComparison.Ordinal));
                Assert(!window.Visible && window.TrayVisible && Digest(ExportFile(root))==original);
            });
            Check(culture+": unchanged content and a newer savedAt still poll without upload or disk rewrite",()=>{
                var root=Client("forever");WritePost(root);var server=new FakeCommunity();using var window=Window(_=>root,server.Services());Enter(window,root);
                Await(window.SynchronizeNowAsync());var hash=Digest(Payload(root));var stamp=File.GetLastWriteTimeUtc(Payload(root));
                WritePost(root,savedAt:2);Await(window.SynchronizeNowAsync());
                Assert(server.Downloads==2 && server.Uploads==1 && Digest(Payload(root))==hash && File.GetLastWriteTimeUtc(Payload(root))==stamp);
            });
            Check(culture+": changed public export is uploaded on the next automatic timer tick",()=>{
                var root=Client("forever");WritePost(root);var server=new FakeCommunity();using var window=Window(_=>root,server.Services(60));Enter(window,root);
                Await(window.SynchronizeNowAsync());WritePost(root,"Updated public post");Control<CheckBox>(window,"automatic").Checked=true;
                PumpUntil(()=>server.Uploads>=2 && !window.IsSynchronizing);Control<CheckBox>(window,"automatic").Checked=false;
                Assert(File.ReadAllText(Payload(root)).Contains("Updated public post",StringComparison.Ordinal));
            });
            Check(culture+": automatic opt-in and selected character survive restart without storing secrets",()=>{
                var root=Client("forever");var extra=Path.Combine(root,"WTF","Account","SECOND","SavedVariables");Directory.CreateDirectory(extra);
                File.WriteAllText(Path.Combine(extra,"ForeverSocial.lua"),Export("forever","Beren-TestRealm"));
                var config=Path.Combine(work,"restart-"+Guid.NewGuid().ToString("N")+".json");var server=new FakeCommunity();
                object? selected=null;
                using(var first=Window(_=>root,server.Services(60),config)){
                    Enter(first,root);Control<ComboBox>(first,"characters").SelectedIndex=1;
                    selected=Control<ComboBox>(first,"characters").SelectedItem;
                    Control<CheckBox>(first,"automatic").Checked=true;
                    first.Close();
                }
                var text=File.ReadAllText(config);Assert(!text.Contains(FakeCommunity.Token,StringComparison.Ordinal));
                using var second=Window(_=>null,server.Services(60),config);
                Assert(Control<CheckBox>(second,"automatic").Checked && Equals(Control<ComboBox>(second,"characters").SelectedItem,selected));
                PumpUntil(()=>server.Downloads>0 && !second.IsSynchronizing);Control<CheckBox>(second,"automatic").Checked=false;
            });
            Check(culture+": pausing automatic sync stops future ticks",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>root,server.Services(60));Enter(window,root);
                var checkbox=Control<CheckBox>(window,"automatic");checkbox.Checked=true;
                PumpUntil(()=>server.Downloads>0 && !window.IsSynchronizing);checkbox.Checked=false;var requests=server.Downloads;
                var stop=System.Diagnostics.Stopwatch.StartNew();while(stop.ElapsedMilliseconds<180){Application.DoEvents();Thread.Sleep(5);}
                Assert(server.Downloads==requests && !window.PollingEnabled);
            });
            Check(culture+": an unconnected character causes no background enrollment or sync request",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>root,server.Services(60,credential:false));Enter(window,root);
                Control<CheckBox>(window,"automatic").Checked=true;
                PumpUntil(()=>Control<Label>(window,"status").Text.Contains(culture=="de-DE"?"zuerst":"first",StringComparison.Ordinal));
                Control<CheckBox>(window,"automatic").Checked=false;Assert(server.Uploads+server.Downloads==0);
            });
            Check(culture+": revoking export consent stops background HTTP",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>root,server.Services(60));Enter(window,root);Await(window.SynchronizeNowAsync());
                var requests=server.Downloads;File.WriteAllText(ExportFile(root),Export("forever").Replace("true","false",StringComparison.Ordinal));
                Control<CheckBox>(window,"automatic").Checked=true;PumpUntil(()=>Control<ComboBox>(window,"characters").Items.Count==0);
                Control<CheckBox>(window,"automatic").Checked=false;Assert(server.Downloads==requests);
            });
            Check(culture+": server failures use backoff and recover without discarding pending upload",()=>{
                var root=Client("forever");WritePost(root);var clock=DateTimeOffset.UtcNow;var server=new FakeCommunity { Failure=HttpStatusCode.ServiceUnavailable };
                using var window=Window(_=>root,server.Services(now:()=>clock));Enter(window,root);Control<CheckBox>(window,"automatic").Checked=true;
                clock=window.NextPoll;Await(window.PollAsync());var first=window.NextPoll;
                Await(window.PollAsync());Assert(server.Uploads==1);
                clock=first;Await(window.PollAsync());Assert(window.NextPoll-clock>=TimeSpan.FromSeconds(60));
                server.Failure=null;clock=window.NextPoll;Await(window.PollAsync());Control<CheckBox>(window,"automatic").Checked=false;
                Assert(server.Uploads==3 && server.Downloads==1 && File.ReadAllText(Payload(root)).Contains("My public post",StringComparison.Ordinal));
            });
            Check(culture+": concurrent tick and button cannot start overlapping requests",()=>{
                var root=Client("forever");var server=new FakeCommunity { WaitForRelease=true };using var window=Window(_=>root,server.Services(60));Enter(window,root);
                var first=window.SynchronizeNowAsync();Assert(window.IsSynchronizing);Await(window.SynchronizeNowAsync());Await(window.PollAsync());Assert(server.Downloads==1);
                server.Release.TrySetResult();Await(first);Assert(!window.IsSynchronizing && Control<Button>(window,"synchronize").Enabled);
            });
            Check(culture+": closing cancels an outstanding request and stops the timer",()=>{
                var root=Client("forever");var server=new FakeCommunity { WaitForRelease=true };using var window=Window(_=>root,server.Services());Enter(window,root);
                var task=window.SynchronizeNowAsync();Assert(window.IsSynchronizing);window.Close();Await(task);Assert(window.IsDisposed && server.Canceled);
            });
            Check(culture+": changing endpoint invalidates upload deduplication",()=>{
                var root=Client("forever");WritePost(root);var server=new FakeCommunity();using var window=Window(_=>root,server.Services());Enter(window,root);Await(window.SynchronizeNowAsync());
                Control<TextBox>(window,"endpoint").Text="https://another.example";Await(window.SynchronizeNowAsync());Assert(server.Uploads==2 && server.Downloads==2);
                Assert(File.ReadAllText(Payload(root)).Contains("https://another.example",StringComparison.Ordinal));
            });
            Check(culture+": recent history is bounded and no server error body or bearer appears in UI",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>root,server.Services());Enter(window,root);
                for(var i=0;i<35;i++)Await(window.SynchronizeNowAsync());Assert(Control<ListBox>(window,"activity").Items.Count==30);
                server.Failure=HttpStatusCode.Unauthorized;Await(window.SynchronizeNowAsync());
                var text=Control<Label>(window,"status").Text+string.Join(' ',Control<ListBox>(window,"activity").Items.Cast<string>());
                Assert(!text.Contains("SERVER_SECRET",StringComparison.Ordinal) && !text.Contains(FakeCommunity.Token,StringComparison.Ordinal));
                Assert(text.Contains(culture=="de-DE"?"ungültig":"invalid",StringComparison.Ordinal));
            });

            Check(culture+": obsolete invitation references are ignored and removed on settings save",()=>{
                var root=Client("forever");var config=CompactSettings(root);
                File.WriteAllText(config,File.ReadAllText(config).TrimEnd()[..^1]+",\"invitationFile\":\"missing-private.txt\",\"invitationEndpoint\":\"https://old.example\"}");
                var server=new FakeCommunity();using var window=Window(_=>null,server.Services(credential:false),config);
                Control<Button>(window,"connect").PerformClick();PumpUntil(()=>!window.IsSynchronizing);
                Assert(server.Enrolls==1 && server.Downloads==1 && !File.ReadAllText(config).Contains("invitation",StringComparison.OrdinalIgnoreCase));
            });
            Check(culture+": existing credentials sync without registering again",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>null,server.Services(),CompactSettings(root));
                Control<Button>(window,"connect").PerformClick();PumpUntil(()=>!window.IsSynchronizing);
                Assert(server.Enrolls==0 && server.Downloads==1);
            });
            Check(culture+": no invitation or clipboard controls remain in setup",()=>{
                using var window=Window(_=>null,new FakeCommunity().Services());
                foreach(var name in new[]{"invite","useInvitation","pasteInvitation","copyInvitation","invitationHint"})Assert(window.Controls.Find(name,true).Length==0);
            });
            Check(culture+": an unsupported addon cannot create an orphan server account",()=>{
                var root=Client("forever");File.Delete(Path.Combine(root,"Interface/AddOns/ForeverSocial/ForeverSocial_Camelot.toc"));
                var server=new FakeCommunity();using var window=Window(_=>null,server.Services(credential:false),CompactSettings(root));
                Control<Button>(window,"connect").PerformClick();PumpUntil(()=>!window.IsSynchronizing);
                Assert(server.Enrolls+server.Downloads==0 && Control<Label>(window,"status").Text.Contains("1.3.2",StringComparison.Ordinal));
            });
            Check(culture+": a conflicting name does not overwrite existing credentials or retry enrollment automatically",()=>{
                var root=Client("forever");var server=new FakeCommunity{Failure=HttpStatusCode.Conflict};
                using var window=Window(_=>null,server.Services(credential:false),CompactSettings(root));
                Control<Button>(window,"connect").PerformClick();PumpUntil(()=>!window.IsSynchronizing);
                Assert(server.EnrollmentAttempts==1 && server.Enrolls==0 && server.Downloads==0 && Control<Label>(window,"status").Text.Contains(culture=="de-DE"?"bereits":"already",StringComparison.Ordinal));
                Await(window.SynchronizeNowAsync());Assert(server.EnrollmentAttempts==1 && server.Enrolls==0 && server.Downloads==0);
            });
            Check(culture+": configured app opens compact with no editable setup fields",()=>{
                var root=Client("forever");using var window=Window(_=>null,new FakeCommunity().Services(),CompactSettings(root),setup:false);
                Assert(!Control<TableLayoutPanel>(window,"setupPanel").Visible && !Control<TextBox>(window,"gameFolder").Visible && !Control<TextBox>(window,"endpoint").Visible);
                Assert(!Control<ComboBox>(window,"characters").Visible && Control<Label>(window,"characterSummary").Text.Contains("Alice-TestRealm",StringComparison.Ordinal));
                Assert(!Control<TableLayoutPanel>(window,"historyPanel").Visible);
            });
            Check(culture+": first setup remains accessible when no game folder is configured",()=>{
                using var window=Window(_=>null,new FakeCommunity().Services(),setup:false);Assert(Control<TableLayoutPanel>(window,"setupPanel").Visible && Control<Button>(window,"browse").Visible);
            });
            Check(culture+": setup link opens and closes fields without network access",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>null,server.Services(),CompactSettings(root),setup:false);
                var link=Control<LinkLabel>(window,"setupToggle");
                void Click()=>typeof(LinkLabel).GetMethod("OnLinkClicked",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(link,[new LinkLabelLinkClickedEventArgs(link.Links[0])]);
                Click();Assert(Control<TextBox>(window,"gameFolder").Visible);Click();Assert(!Control<TextBox>(window,"gameFolder").Visible && server.Downloads+server.Enrolls==0);
            });
            Check(culture+": compact primary button connects without an invitation and writes only the main addon",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>null,server.Services(credential:false),CompactSettings(root),setup:false);
                Control<Button>(window,"synchronize").PerformClick();PumpUntil(()=>!window.IsSynchronizing);
                Assert(server.Enrolls==1 && File.ReadAllText(Payload(root)).Contains("ForeverSocialCompanionData",StringComparison.Ordinal));
                Assert(!Directory.Exists(Path.Combine(root,"Interface/AddOns/ForeverSocial_Companion")) && !Control<TableLayoutPanel>(window,"setupPanel").Visible);
            });
            Check(culture+": revoked export does not enroll even from the primary button",()=>{
                var root=Client("forever");File.WriteAllText(ExportFile(root),Export("forever").Replace("[\"enabled\"]=true","[\"enabled\"]=false",StringComparison.Ordinal));
                var server=new FakeCommunity();using var window=Window(_=>null,server.Services(credential:false),CompactSettings(root),setup:false);
                Control<Button>(window,"synchronize").PerformClick();PumpUntil(()=>!window.IsSynchronizing);
                Assert(server.Enrolls+server.Downloads==0 && Control<Label>(window,"status").Text.Contains("/reload",StringComparison.Ordinal));
            });
            Check(culture+": multiple characters keep a selector and update the visible character name",()=>{
                var root=Client("forever");var other=Path.Combine(root,"WTF/Account/SECOND/SavedVariables");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"ForeverSocial.lua"),Export("forever","Beren-TestRealm"));
                using var window=Window(_=>null,new FakeCommunity().Services(),CompactSettings(root),setup:false);var selector=Control<ComboBox>(window,"characters");
                Assert(selector.Visible && selector.Items.Count==2);selector.SelectedIndex=1;Assert(Control<Label>(window,"characterSummary").Text.StartsWith(selector.SelectedItem!.ToString()!.Split(" · ")[0],StringComparison.Ordinal));
            });
            Check(culture+": recent activity can be expanded independently of connection settings",()=>{
                var root=Client("forever");using var window=Window(_=>null,new FakeCommunity().Services(),CompactSettings(root),setup:false);
                var link=Control<LinkLabel>(window,"historyToggle");typeof(LinkLabel).GetMethod("OnLinkClicked",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(link,[new LinkLabelLinkClickedEventArgs(link.Links[0])]);
                Assert(Control<ListBox>(window,"activity").Visible && !Control<TableLayoutPanel>(window,"setupPanel").Visible);
            });
        }
        foreach(var culture in new[]{"en-US","de-DE"}){
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(culture);
            var password="Private-transfer-password!";
            var uri=new Uri("https://social.wgfreunde.com");
            Check(culture+": access tools stay collapsed and do not send requests on opening",()=>{
                var root=Client("forever");var server=new FakeCommunity();using var window=Window(_=>null,server.Services(),CompactSettings(root));
                var card=Control<TableLayoutPanel>(window,"accessPanel");Assert(!card.Visible);
                var link=Control<LinkLabel>(window,"accessToggle");typeof(LinkLabel).GetMethod("OnLinkClicked",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(link,[new LinkLabelLinkClickedEventArgs(new LinkLabel.Link())]);
                Assert(card.Visible && server.AccountGets+server.Deletes+server.Enrolls+server.Downloads==0);
            });
            Check(culture+": access backup encrypts the credential without publishing or exposing it",()=>{
                var root=Client("forever");var path=Path.Combine(work,Guid.NewGuid()+".fsaccess");var config=CompactSettings(root);var server=new FakeCommunity();
                using var window=Window(_=>null,server.Services(accessFile:(_,_)=>path,password:(_,_)=>password),config);
                Await(window.ManageAccessAsync("export"));Assert(File.Exists(path));var content=File.ReadAllText(path);
                Assert(!content.Contains(FakeCommunity.Token,StringComparison.Ordinal) && !content.Contains("Alice-TestRealm",StringComparison.Ordinal));
                var access=AccessTransfer.Unprotect(content,password);Assert(access.Actor=="Alice-TestRealm" && access.Token==FakeCommunity.Token);
                Assert(server.AccountGets==1 && server.Uploads+server.Downloads+server.Enrolls==0 && !File.ReadAllText(config).Contains(FakeCommunity.Token,StringComparison.Ordinal));
            });
            Check(culture+": canceling access backup never creates a credential file",()=>{
                var root=Client("forever");var path=Path.Combine(work,Guid.NewGuid()+".fsaccess");var server=new FakeCommunity();
                using var window=Window(_=>null,server.Services(accessFile:(_,_)=>path,password:(_,_)=>null),CompactSettings(root));
                Await(window.ManageAccessAsync("export"));Assert(!File.Exists(path) && server.Deletes==0 && !window.IsSynchronizing);
            });
            Check(culture+": importing access verifies server scope before storing a credential",()=>{
                var root=Client("forever");var path=Path.Combine(work,Guid.NewGuid()+".fsaccess");
                File.WriteAllText(path,AccessTransfer.Protect(new(uri.AbsoluteUri,"forever","Alice-TestRealm","TestRealm",FakeCommunity.Token),password));
                var server=new FakeCommunity();using var window=Window(_=>null,server.Services(credential:false,accessFile:(_,_)=>path,password:(_,_)=>password),CompactSettings(root));
                Await(window.ManageAccessAsync("import"));Assert(server.SavedToken==FakeCommunity.Token && server.AccountGets==1 && server.Enrolls+server.Uploads+server.Downloads==0);
                Await(window.SynchronizeNowAsync());Assert(server.Downloads==1 && server.Enrolls==0);
            });
            Check(culture+": wrong password and mismatched character cannot replace credentials",()=>{
                var root=Client("forever");var path=Path.Combine(work,Guid.NewGuid()+".fsaccess");
                File.WriteAllText(path,AccessTransfer.Protect(new(uri.AbsoluteUri,"forever","Mira-TestRealm","TestRealm",FakeCommunity.Token),password));
                var server=new FakeCommunity();using var window=Window(_=>null,server.Services(credential:false,accessFile:(_,_)=>path,password:(_,_)=>password),CompactSettings(root));
                Await(window.ManageAccessAsync("import"));Assert(server.SavedToken is null && server.AccountGets==0);
                File.WriteAllText(path,AccessTransfer.Protect(new(uri.AbsoluteUri,"forever","Alice-TestRealm","TestRealm",FakeCommunity.Token),"Another-password-value!"));
                Await(window.ManageAccessAsync("import"));Assert(server.SavedToken is null && server.AccountGets==0 && !Control<Label>(window,"status").Text.Contains(FakeCommunity.Token,StringComparison.Ordinal));
            });
            Check(culture+": server scope mismatch rejects an otherwise valid access file",()=>{
                var root=Client("forever");var path=Path.Combine(work,Guid.NewGuid()+".fsaccess");
                File.WriteAllText(path,AccessTransfer.Protect(new(uri.AbsoluteUri,"forever","Alice-TestRealm","TestRealm",FakeCommunity.Token),password));
                var server=new FakeCommunity{AccountActor="Mira-TestRealm"};using var window=Window(_=>null,server.Services(credential:false,accessFile:(_,_)=>path,password:(_,_)=>password),CompactSettings(root));
                Await(window.ManageAccessAsync("import"));Assert(server.SavedToken is null && server.AccountGets==1 && server.Deletes==0);
            });
            Check(culture+": deletion confirmation can be canceled without network or preference changes",()=>{
                var root=Client("forever");var config=CompactSettings(root);var before=File.ReadAllBytes(config);var server=new FakeCommunity();
                using var window=Window(_=>null,server.Services(confirm:(_,_)=>false),config);
                Await(window.ManageAccessAsync("delete"));Assert(server.Deletes+server.AccountGets==0 && before.SequenceEqual(File.ReadAllBytes(config)) && !server.CredentialDeleted);
            });
            Check(culture+": deletion revokes access clears imported data and prevents silent recreation after restart",()=>{
                var root=Client("forever");var config=CompactSettings(root);WritePost(root);var server=new FakeCommunity();
                using(var window=Window(_=>null,server.Services(confirm:(_,_)=>true),config)){
                    Await(window.ManageAccessAsync("delete"));Assert(server.Deletes==1 && server.CredentialDeleted && !Control<CheckBox>(window,"automatic").Checked);
                    Assert(!File.ReadAllText(Payload(root)).Contains("My public post",StringComparison.Ordinal));
                    Await(window.SynchronizeNowAsync());Control<Button>(window,"synchronize").PerformClick();PumpUntil(()=>!window.IsSynchronizing);Assert(server.Enrolls+server.Uploads+server.Downloads==0);
                }
                using var reopened=Window(_=>null,server.Services(credential:false,confirm:(_,_)=>false),config);
                Control<Button>(reopened,"synchronize").PerformClick();PumpUntil(()=>!reopened.IsSynchronizing);Assert(server.Enrolls==0);
                Assert(CompanionSettings.Load(config).RetiredScopes.Count==1);
                Await(reopened.ManageAccessAsync("reconnect"));Assert(CompanionSettings.Load(config).RetiredScopes.Count==1);
            });
            Check(culture+": failed server deletion remains retryable while automatic recreation is blocked",()=>{
                var root=Client("forever");var config=CompactSettings(root);var server=new FakeCommunity{Failure=HttpStatusCode.ServiceUnavailable};
                using var window=Window(_=>null,server.Services(confirm:(_,_)=>true),config);Await(window.ManageAccessAsync("delete"));
                Assert(server.Deletes==0 && !server.CredentialDeleted && CompanionSettings.Load(config).RetiredScopes.Count==1);
                server.Failure=null;Await(window.ManageAccessAsync("delete"));Assert(server.Deletes==1 && server.CredentialDeleted);
            });
            Check(culture+": access operations exclude overlapping sync and recover their controls",()=>{
                var root=Client("forever");var server=new FakeCommunity{WaitForRelease=true};using var window=Window(_=>null,server.Services(confirm:(_,_)=>true),CompactSettings(root));
                var pending=window.ManageAccessAsync("delete");PumpUntil(()=>window.IsSynchronizing);
                Await(window.SynchronizeNowAsync());Assert(server.AccountGets==1 && !Control<Button>(window,"synchronize").Enabled);
                server.Release.SetResult();Await(pending);Assert(server.Deletes==1 && Control<Button>(window,"synchronize").Enabled);
            });
        }
        Check("access backups refuse game-file destinations without altering the data slot",()=>{
            var root=Client("forever");var path=Payload(root);var before=File.ReadAllBytes(path);var server=new FakeCommunity();
            using var window=Window(_=>null,server.Services(accessFile:(_,_)=>path,password:(_,_)=>"Private-transfer-password!"),CompactSettings(root));
            Await(window.ManageAccessAsync("export"));Assert(before.SequenceEqual(File.ReadAllBytes(path)));
        });
        Check("oversized preferences cannot be persisted before a destructive server action",()=>{
            var path=Path.Combine(work,"large-settings.json");var denied=false;
            try{new CompanionSettings{RetiredScopes=Enumerable.Repeat(new string('x',600),100).ToList()}.Save(path);}catch(InvalidDataException){denied=true;}
            Assert(denied && !File.Exists(path));
        });
        // Fixtures are deliberately isolated; no real game or account paths are used.
        var result=new{generatedAtUTC=DateTimeOffset.UtcNow,passed=checks.Count-failed,total=checks.Count,
            checks,componentUiTests=true,nativeFolderDialogInteracted=false,
            appAssemblySHA256=Digest(typeof(CompanionWindow).Assembly.Location),
            appSourceSHA256=Directory.EnumerateFiles(AppSource()).Where(p=>Path.GetExtension(p) is ".cs" or ".csproj" or ".manifest").ToDictionary(p=>Path.GetFileName(p),Digest)};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{checks.Count-failed}/{checks.Count} Windows regressions passed");
        return failed==0?0:1;
    }
    private static string Digest(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string AppSource()=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../App"));
}

internal sealed class FakeCommunity
{
    private static readonly long Epoch=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    internal const string Token="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    internal int Uploads,Downloads,Enrolls,EnrollmentAttempts,AccountGets,Deletes;
    internal string? SavedToken;
    internal string AccountActor="Alice-TestRealm";
    internal bool CredentialDeleted;
    internal Enrollment? LastEnrollment;
    internal HttpStatusCode? Failure;
    internal bool WaitForRelease,Canceled;
    internal TaskCompletionSource Release { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string,WireRecord> rows=[];
    internal static WireRecord Post(string actor,string text)=>new("posts",JsonSerializer.SerializeToElement(new{id=actor+"@test",author=actor,created=Epoch-60,updated=Epoch,version=1,category="general",text,deleted=false},Protocol.Json));
    internal void AddPost(string actor,string text){var row=Post(actor,text);rows[Protocol.Key(row)]=row;}
    internal CompanionServices Services(int interval=30000,bool credential=true,Func<DateTimeOffset>? now=null,Func<IWin32Window,bool,string?>? accessFile=null,Func<IWin32Window,bool,string?>? password=null,Func<IWin32Window,string,bool>? confirm=null){
        var saved=new Dictionary<(string,string,string),string>();
        return new(){CreateClient=uri=>new SyncClient(uri,new Handler(this)),
            LoadCredential=(uri,e)=>saved.GetValueOrDefault((uri.AbsoluteUri,e.Client,e.Actor)) ?? (credential && !CredentialDeleted?Token:null),
            SaveCredential=(uri,e,token)=>{saved[(uri.AbsoluteUri,e.Client,e.Actor)]=token;SavedToken=token;},
            DeleteCredential=(uri,e)=>{saved.Remove((uri.AbsoluteUri,e.Client,e.Actor));CredentialDeleted=true;},
            AccessFile=accessFile ?? ((_,_)=>null),AccessPassword=password ?? ((_,_)=>null),ConfirmAccess=confirm ?? ((_,_)=>false),
            PollIntervalMilliseconds=interval,Now=now ?? (()=>DateTimeOffset.UtcNow)};
    }
    private sealed class Handler(FakeCommunity server):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path=="/v1/records")server.Uploads++;else if(path=="/v1/snapshot")server.Downloads++;
            if(path=="/v1/enroll")server.EnrollmentAttempts++;
            if(path=="/v1/account" && request.Method==HttpMethod.Get)server.AccountGets++;
            if(server.Failure is { } failure)return new HttpResponseMessage(failure){Content=new StringContent("SERVER_SECRET")};
            if(server.WaitForRelease){try{await server.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);}catch(OperationCanceledException){server.Canceled=true;throw;}}
            if(path=="/v1/account"){
                if(request.Method==HttpMethod.Delete){server.Deletes++;return new(HttpStatusCode.NoContent);}
                return new(HttpStatusCode.OK){Content=Body(new AccountInfo("synthetic-account","forever",server.AccountActor,"TestRealm"))};
            }
            if(path=="/v1/records"){
                var upload=(await request.Content!.ReadFromJsonAsync<Upload>(Protocol.Json,cancellationToken).ConfigureAwait(false))!;
                foreach(var row in upload.Records)server.rows[Protocol.Key(row)]=row;
                return new(HttpStatusCode.OK){Content=Body(new{accepted=upload.Records.Count})};
            }
            if(path=="/v1/enroll"){
                server.Enrolls++;server.LastEnrollment=await request.Content!.ReadFromJsonAsync<Enrollment>(Protocol.Json,cancellationToken).ConfigureAwait(false);
                return new(HttpStatusCode.OK){Content=Body(new Credential(Token,"synthetic-account"))};
            }
            return new(HttpStatusCode.OK){Content=Body(new Snapshot(1,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),server.rows.Values.ToList(),0))};
        }
        private static StringContent Body<T>(T value)=>new(JsonSerializer.Serialize(value,Protocol.Json),System.Text.Encoding.UTF8,"application/json");
    }
}
