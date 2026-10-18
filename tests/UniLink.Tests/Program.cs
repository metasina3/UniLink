using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using UniLink.Core;
using UniLink.Windows;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

internal static class Program
{
    private static int _passed, _failed;
    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-17T08:00:00Z");
    private static readonly NetworkLink Campus = new("adapter-wifi", "UUWN", "10.0.0.25", 4, "campus-1");
    private const string ExceededMessage = "Your data transfer has been exceeded, Please contact the administrator";
    private static NetworkLink LocalLink => new("loopback", "UUWN", "127.0.0.1", OperatingSystem.IsWindows() ? NetworkInterface.LoopbackInterfaceIndex : 0, "test");
    private static NetworkSnapshot Net(NetworkLink? link = null) => new([link ?? Campus]);
    private static string LoginXml(string status, string message = "") => $"<?xml version='1.0'?><requestresponse><status><![CDATA[{status}]]></status><message><![CDATA[{message}]]></message><state><![CDATA[]]></state></requestresponse>";
    private static string LiveXml(string ack, string message = "") => $"<?xml version='1.0'?><liverequestresponse><ack><![CDATA[{ack}]]></ack><livemessage><![CDATA[{message}]]></livemessage></liverequestresponse>";
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Equal<TV>(TV expected, TV actual) { if (!EqualityComparer<TV>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static async Task Throws<TException>(Func<Task> action) where TException : Exception
    { try { await action(); } catch (TException) { return; } throw new Exception("Expected " + typeof(TException).Name); }
    private static async Task Test(string name, Func<Task> action)
    {
        try { await action(); Console.WriteLine("PASS " + name); _passed++; }
        catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.Message); _failed++; }
    }
    private static AppConfig Config(int accounts = 2)
    {
        var config = AppConfig.CreateDefault(); var g = config.Groups[0];
        g.Accounts = Enumerable.Range(1, accounts).Select(i => new Account { Label = "Account " + i, Username = "TEST-STUDENT-" + i, Password = "FAKE-secret+&=" + i }).ToList();
        g.DefaultAccountId = g.Accounts.FirstOrDefault()?.Id; return config;
    }

    public static async Task<int> Main()
    {
        await Test("exact university exhaustion message works with existing custom settings", () =>
        {
            var g = Config().Groups[0]; g.QuotaPhrases = [];
            Equal(ReplyKind.Quota, PortalSession.Parse(LoginXml("LOGIN", ExceededMessage), false, g).Kind);
            Equal(ReplyKind.Quota, PortalSession.Parse(LiveXml("nack", ExceededMessage.ToUpperInvariant()), true, g).Kind);
            Equal(ReplyKind.Online, PortalSession.Parse(LoginXml("LIVE", ExceededMessage), false, g).Kind);
            Equal(ReplyKind.Unknown, PortalSession.Parse(LoginXml("LOGIN", "Data transfer service is temporarily unavailable"), false, g).Kind);
            return Task.CompletedTask;
        });
        await Test("university quota reply advances sequentially through saved accounts", async () =>
        {
            var config = Config(3); var g = config.Groups[0]; g.QuotaPhrases = []; var store = new MemoryStore(); var f = new FakeFactory();
            f.Responses[g.Accounts[0].Username] = new([PortalSession.Parse(LoginXml("LOGIN", ExceededMessage), false, g)]);
            f.Responses[g.Accounts[1].Username] = new([PortalSession.Parse(LoginXml("LOGIN", ExceededMessage), false, g)]);
            using var engine = new ConnectionEngine(config, store, f, new ActivityLog());
            await engine.TickAsync(Net(), T); Equal(1, f.Logins.Count);
            await engine.TickAsync(Net(), T.AddSeconds(2)); Equal(1, f.Logins.Count);
            await engine.TickAsync(Net(), T.AddSeconds(3)); Equal(2, f.Logins.Count);
            await engine.TickAsync(Net(), T.AddSeconds(6)); Equal(3, f.Logins.Count);
            Equal(ConnectionState.Online, engine.Status.State); Equal(1, f.MaxActiveLogins);
            Equal(string.Join(',',g.Accounts.Select(a=>a.Username)), string.Join(',',f.Logins));
            Check(store.Last!.Groups[0].Accounts.Take(2).All(a=>a.ExhaustedMonth==g.MonthAt(T)));
        });
        await Test("university quota on keepalive switches accounts without a 300-second delay", async () =>
        {
            var config=Config();var g=config.Groups[0];g.QuotaPhrases=[];var f=new FakeFactory();
            using var engine=new ConnectionEngine(config,new MemoryStore(),f,new ActivityLog());
            await engine.TickAsync(Net(),T);f.LiveResponses.Enqueue(PortalSession.Parse(LiveXml("nack",ExceededMessage),true,g));
            await engine.TickAsync(Net(),T.AddSeconds(180));await engine.TickAsync(Net(),T.AddSeconds(183));
            Equal(g.Accounts[1].Username,f.Logins.Last());Equal(ConnectionState.Online,engine.Status.State);
        });
        await Test("physical Wi-Fi and Ethernet are accepted; Ethernet-like VPNs and filters are rejected", () =>
        {
            var id=Guid.NewGuid();
            Check(PhysicalNetworkInterface.IsEligible(new(4,id,71,0,1,1)));
            Check(PhysicalNetworkInterface.IsEligible(new(7,id,6,0,5,1)));
            foreach(var facts in new PhysicalNetworkInterface.Facts[] {new(7,id,6,0,0,1),new(7,id,6,0,3,1),new(7,id,131,0,1,1),new(7,id,6,13,1,1),new(0,id,6,0,1,1),new(7,id,6,0,1,2)})
                Check(!PhysicalNetworkInterface.IsEligible(facts));
            return Task.CompletedTask;
        });
        await Test("native adapter and DNS layouts match the Windows x64 ABI", () =>
        {
            Equal(1352,Marshal.SizeOf<PhysicalNetworkInterface.MibIfRow2>());
            Equal(1152,Marshal.OffsetOf<PhysicalNetworkInterface.MibIfRow2>("Flags").ToInt32());
            Equal(1128,Marshal.OffsetOf<PhysicalNetworkInterface.MibIfRow2>("Type").ToInt32());
            Equal(64,Marshal.SizeOf<InterfaceDns.QueryRequest>());Equal(32,Marshal.SizeOf<InterfaceDns.QueryResult>());
            Equal(40,Marshal.OffsetOf<InterfaceDns.QueryRequest>("InterfaceIndex").ToInt32());Equal(32,Marshal.SizeOf<InterfaceDns.RecordHeader>());
            return Task.CompletedTask;
        });
        await Test("literal portal address bypasses DNS and canceled lookups stop before resolution", async () =>
        {
            Equal("192.168.100.40",(await InterfaceDns.ResolveAsync("192.168.100.40",0,default)).Single().ToString());
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            await Throws<OperationCanceledException>(()=>InterfaceDns.ResolveAsync("must-not-resolve.invalid",4,cancel.Token));
        });
        await Test("scoped DNS record reader ignores CNAMEs and deduplicates IPv4 answers", () =>
        {
            var size=Marshal.SizeOf<InterfaceDns.RecordHeader>();var one=Marshal.AllocHGlobal(size+16);var two=Marshal.AllocHGlobal(size+16);var three=Marshal.AllocHGlobal(size+16);
            try
            {
                Marshal.StructureToPtr(new InterfaceDns.RecordHeader {Next=two,Type=5,DataLength=8},one,false);
                Marshal.StructureToPtr(new InterfaceDns.RecordHeader {Next=three,Type=1,DataLength=4},two,false);
                Marshal.StructureToPtr(new InterfaceDns.RecordHeader {Type=1,DataLength=4},three,false);
                foreach(var ptr in new[]{two,three})Marshal.Copy(new byte[]{192,168,100,40},0,IntPtr.Add(ptr,size),4);
                Equal("192.168.100.40",InterfaceDns.ReadAddresses(one).Single().ToString());
            }
            finally {Marshal.FreeHGlobal(one);Marshal.FreeHGlobal(two);Marshal.FreeHGlobal(three);}
            return Task.CompletedTask;
        });
        if(OperatingSystem.IsWindows())
        {
            await Test("Windows IP_UNICAST_IF is set in network order and read back as the selected index", () =>
            {
                using var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
                var index=NetworkInterface.LoopbackInterfaceIndex;socket.SetSocketOption(SocketOptionLevel.IP,(SocketOptionName)31,IPAddress.HostToNetworkOrder(index));
                Equal(index,(int)socket.GetSocketOption(SocketOptionLevel.IP,(SocketOptionName)31)!);return Task.CompletedTask;
            });
            await Test("Windows refuses a missing interface index without sending HTTP credentials", async () =>
            {
                await using var server=new MockPortal();var g=Config().Groups[0];
                using var client=new PortalSession(new() {Url=server.Url,CertificateSha256=server.Fingerprint},LocalLink with {InterfaceIndex=0});
                await Throws<PortalTransportException>(()=>client.LoginAsync(g.Accounts[0],g,default));Equal(0,server.Requests.Count);
            });
            await Test("Windows native adapter identity matches .NET interface GUIDs", () =>
            {
                var inspected=0;
                foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var index=nic.GetIPProperties().GetIPv4Properties()?.Index??0;if(index<=0)continue;
                    Check(PhysicalNetworkInterface.TryRead(index,out var facts));Equal(Guid.Parse(nic.Id),facts.Id);inspected++;
                }
                Check(inspected>0);return Task.CompletedTask;
            });
        }
        await Test("HAR login LIVE and live ack are parsed", () =>
        {
            var g = Config().Groups[0]; Equal(ReplyKind.Online, PortalSession.Parse(LoginXml("LIVE", "You are signed in as {username}"), false, g).Kind);
            Equal(ReplyKind.Online, PortalSession.Parse(LiveXml("ack"), true, g).Kind); return Task.CompletedTask;
        });
        await Test("quota, wrong password, challenge and unknown remain distinct", () =>
        {
            var g = Config().Groups[0];
            Equal(ReplyKind.Quota, PortalSession.Parse(LoginXml("LOGIN", "You have reached your Internet usage limit"), false, g).Kind);
            Equal(ReplyKind.Credentials, PortalSession.Parse(LoginXml("LOGIN", "Invalid username/password"), false, g).Kind);
            Equal(ReplyKind.Challenge, PortalSession.Parse(LoginXml("CHALLENGE"), false, g).Kind);
            Equal(ReplyKind.Unknown, PortalSession.Parse(LoginXml("LOGIN", "Login is not allowed at this time."), false, g).Kind);
            Equal(ReplyKind.Online, PortalSession.Parse(LoginXml("LIVE", "Your quota exceeded last month."), false, g).Kind);
            Equal(ReplyKind.SignedOut, PortalSession.Parse(LiveXml("login_again"), true, g).Kind);
            Equal(ReplyKind.Quota, PortalSession.Parse(LiveXml("nack", "Quota exhausted"), true, g).Kind); return Task.CompletedTask;
        });
        await Test("malformed XML, HTML, foreign root and DTD cannot authenticate", () =>
        {
            var g = Config().Groups[0];
            foreach (var xml in new[] { "<html>login</html>", "<requestresponse>", "<other><status>LIVE</status></other>", "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><requestresponse><status>LIVE</status><message>&e;</message></requestresponse>" })
                Equal(ReplyKind.Unknown, PortalSession.Parse(xml, false, g).Kind);
            return Task.CompletedTask;
        });
        await Test("default first, then ordered fallback, never parallel", async () =>
        {
            var config = Config(4); var g = config.Groups[0]; g.DefaultAccountId = g.Accounts[2].Id;
            var f = new FakeFactory(); f.Responses[g.Accounts[2].Username] = new([new(ReplyKind.Quota)]); f.Responses[g.Accounts[0].Username] = new([new(ReplyKind.Quota)]);
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), T); await e.TickAsync(Net(), T.AddSeconds(3)); await e.TickAsync(Net(), T.AddSeconds(6));
            Equal(string.Join(',', new[] { g.Accounts[2].Username, g.Accounts[0].Username, g.Accounts[1].Username }), string.Join(',', f.Logins));
            Equal(ConnectionState.Online, e.Status.State); Equal(1, f.MaxActiveLogins);
            await e.TickAsync(Net(), T.AddSeconds(7)); Equal(3, f.Logins.Count);
        });
        await Test("heartbeat follows the 180-second interval", async () =>
        {
            var f = new FakeFactory(); using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), T); await e.TickAsync(Net(), T.AddSeconds(179)); Equal(0, f.Heartbeats);
            await e.TickAsync(Net(), T.AddSeconds(180)); Equal(1, f.Heartbeats); Equal(1, f.Logins.Count);
            await e.TickAsync(Net(), T.AddSeconds(359)); Equal(1, f.Heartbeats);
            await e.TickAsync(Net(), T.AddSeconds(360)); Equal(2, f.Heartbeats);
        });
        await Test("network failures do not consume or rotate accounts", async () =>
        {
            var config = Config(); var g = config.Groups[0]; g.Portals.Add(new PortalEndpoint { Url = "https://192.168.100.41:8090/" });
            var f = new FakeFactory { FailProbes = 1 }; using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), T); Equal(ConnectionState.Retry, e.Status.State); Equal(0, f.Logins.Count);
            await e.TickAsync(Net(), T.AddSeconds(4)); Equal(1, f.Probes);
            await e.TickAsync(Net(), T.AddSeconds(5)); Equal(g.Accounts[0].Username, f.Logins.Single()); Equal(g.Portals[1].Id, f.CreatedPortals.Last());
            Check(e.SnapshotConfig().Groups[0].Accounts.All(a => a.ExhaustedMonth == ""));
        });
        await Test("ambiguous errors use bounded backoff on the same account", async () =>
        {
            var config = Config(); var g = config.Groups[0]; var f = new FakeFactory();
            f.Responses[g.Accounts[0].Username] = new([new(ReplyKind.Unknown, "The server is busy."), new(ReplyKind.Unknown, "Try later.")]);
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), T); await e.TickAsync(Net(), T.AddSeconds(299)); Equal(1, f.Logins.Count);
            await e.TickAsync(Net(), T.AddSeconds(300)); Equal(2, f.Logins.Count);
            await e.TickAsync(Net(), T.AddSeconds(899)); Equal(2, f.Logins.Count);
            await e.TickAsync(Net(), T.AddSeconds(900)); Equal(3, f.Logins.Count);
            Check(f.Logins.All(u => u == g.Accounts[0].Username)); Check(e.SnapshotConfig().Groups[0].Accounts.All(a => a.ExhaustedMonth == ""));
        });
        await Test("all exhausted accounts wait without repeated logins", async () =>
        {
            var config = Config(); var f = new FakeFactory(); foreach (var a in config.Groups[0].Accounts) f.Responses[a.Username] = new([new(ReplyKind.Quota)]);
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), T); await e.TickAsync(Net(), T.AddSeconds(3)); await e.TickAsync(Net(), T.AddSeconds(6));
            Equal(ConnectionState.NoAccounts, e.Status.State); Check(e.Status.NextAction.HasValue);
            await e.TickAsync(Net(), T.AddDays(2)); Equal(2, f.Logins.Count);
        });
        await Test("monthly reset uses Tehran midnight and returns to default", async () =>
        {
            var t = DateTimeOffset.Parse("2026-10-31T20:29:59Z");
            var config = Config(); var g = config.Groups[0]; g.Accounts[0].ExhaustedMonth = "2026-10"; g.LastMonth = "2026-10";
            Equal("2026-10", g.MonthAt(t)); Equal("2026-11", g.MonthAt(t.AddSeconds(1))); Equal(t.AddSeconds(1), g.NextReset(t).ToUniversalTime());
            var f = new FakeFactory(); using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), t); Equal(g.Accounts[1].Username, f.Logins.Single());
            await e.TickAsync(Net(), t.AddSeconds(1)); Equal(g.Accounts[0].Username, f.Logins.Last()); Equal(1, f.Logouts);
        });
        await Test("delayed reset job on day 1 is rechecked hourly", async () =>
        {
            var t = DateTimeOffset.Parse("2026-10-31T20:30:00Z"); var config = Config(1); var g = config.Groups[0];
            g.LastMonth = "2026-10"; g.Accounts[0].ExhaustedMonth = "2026-10"; var f = new FakeFactory();
            f.Responses[g.Accounts[0].Username] = new([new(ReplyKind.Quota), new(ReplyKind.Online)]);
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), t); await e.TickAsync(Net(), t.AddSeconds(3)); Equal(ConnectionState.NoAccounts, e.Status.State);
            Equal(t.AddHours(1), e.Status.NextAction!.Value); await e.TickAsync(Net(), t.AddMinutes(59)); Equal(1, f.Logins.Count);
            await e.TickAsync(Net(), t.AddHours(1)); Equal(2, f.Logins.Count); Equal(ConnectionState.Online, e.Status.State);
            Equal("", e.SnapshotConfig().Groups[0].Accounts[0].ExhaustedMonth); Check(e.SnapshotConfig().Groups[0].Accounts[0].QuotaNextProbeAt is null);
        });
        await Test("ordinary mid-month exhaustion is not polled hourly", () =>
        {
            var g = Config().Groups[0]; Check(g.QuotaProbeAfter(T) is null);
            Check(g.QuotaProbeAfter(DateTimeOffset.Parse("2026-11-01T20:00:00Z")) is null); return Task.CompletedTask;
        });
        await Test("offline restart resets old quota but never rejected credentials", async () =>
        {
            var config = Config(3); var g = config.Groups[0]; g.LastMonth = "2026-08"; g.Accounts[0].NeedsAttention = true;
            g.Accounts[0].ExhaustedMonth = "2026-08"; g.Accounts[1].ExhaustedMonth = "2026-08";
            var f = new FakeFactory(); using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T);
            Equal(g.Accounts[1].Username, f.Logins.Single()); Check(e.SnapshotConfig().Groups[0].Accounts[0].NeedsAttention);
        });
        await Test("clock rollback does not release a future quota block", async () =>
        {
            var config = Config(1); config.Groups[0].LastMonth = "2026-11"; config.Groups[0].Accounts[0].ExhaustedMonth = "2026-11";
            var f = new FakeFactory(); using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T); Equal(0, f.Logins.Count);
        });
        await Test("quota blocks survive a restart", async () =>
        {
            var config = Config(); var g = config.Groups[0]; var store = new MemoryStore(); var f = new FakeFactory(); f.Responses[g.Accounts[0].Username] = new([new(ReplyKind.Quota)]);
            using (var e = new ConnectionEngine(config, store, f, new ActivityLog())) await e.TickAsync(Net(), T);
            var resumed = new FakeFactory(); using var restarted = new ConnectionEngine(store.Last!, store, resumed, new ActivityLog()); await restarted.TickAsync(Net(), T.AddMinutes(1));
            Equal(g.Accounts[1].Username, resumed.Logins.Single());
        });
        await Test("SSID matching is exact and case sensitive", async () =>
        {
            var f = new FakeFactory(); using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(Campus with { Ssid = "uuwn", Signature = "other" }), T); Equal(0, f.Logins.Count);
            await e.TickAsync(Net(Campus with { Ssid = "Home", Signature = "home" }), T.AddSeconds(3)); Equal(0, f.Probes);
        });
        await Test("matching Wi-Fi interface is selected even with Ethernet present", async () =>
        {
            var f = new FakeFactory(); using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(new([new("ethernet", null, "10.1.1.5", 8, "ethernet"), Campus]), T); Equal(Campus.InterfaceId, f.CreatedLinks.Single().InterfaceId);
        });
        await Test("network change reconnects without marking quota", async () =>
        {
            var f = new FakeFactory(); using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T);
            e.NetworkChanged(); await e.TickAsync(Net(Campus with { Signature = "campus-2", LocalAddress = "10.0.0.26" }), T.AddSeconds(3));
            Equal(2, f.Logins.Count); Equal(0, f.Logouts); Check(e.SnapshotConfig().Groups[0].Accounts.All(a => a.ExhaustedMonth == ""));
        });
        await Test("pause signs out and manual resume still works with auto-connect disabled", async () =>
        {
            var config = Config(); config.AutoConnect = false; var g = config.Groups[0]; var f = new FakeFactory();
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T); Equal(0, f.Logins.Count);
            await e.ConnectAsync(g.Id); await e.TickAsync(Net(), T.AddSeconds(3)); Equal(1, f.Logins.Count);
            await e.PauseAsync(); await e.TickAsync(Net(), T.AddMinutes(5)); Equal(1, f.Logouts); Equal(1, f.Logins.Count);
        });
        await Test("live nack retries the same account, while quota moves on", async () =>
        {
            var config = Config(); var f = new FakeFactory(); f.LiveResponses.Enqueue(new(ReplyKind.SignedOut));
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            await e.TickAsync(Net(), T); await e.TickAsync(Net(), T.AddSeconds(180)); await e.TickAsync(Net(), T.AddSeconds(185));
            Equal(2, f.Logins.Count); Equal(f.Logins[0], f.Logins[1]);
            f.LiveResponses.Enqueue(new(ReplyKind.Quota)); await e.TickAsync(Net(), T.AddSeconds(365)); await e.TickAsync(Net(), T.AddSeconds(368));
            Equal(config.Groups[0].Accounts[1].Username, f.Logins.Last());
        });
        await Test("live_off suppresses unnecessary heartbeats", async () =>
        {
            var f = new FakeFactory(); f.LiveResponses.Enqueue(new(ReplyKind.LiveOff));
            using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T);
            await e.TickAsync(Net(), T.AddSeconds(180)); await e.TickAsync(Net(), T.AddHours(1)); Equal(1, f.Heartbeats); Equal(ConnectionState.Online, e.Status.State);
        });
        await Test("automatic fallback can be disabled", async () =>
        {
            var config = Config(); var g = config.Groups[0]; g.AutoSwitch = false; g.Accounts[0].ExhaustedMonth = "2026-10";
            var f = new FakeFactory(); using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T); Equal(0, f.Logins.Count);
        });
        await Test("explicit account selection overrides automatic-fallback preference", async () =>
        {
            var config = Config(); config.Groups[0].AutoSwitch = false; var g = config.Groups[0]; var f = new FakeFactory();
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T);
            await e.ConnectAsync(g.Id, g.Accounts[1].Id); await e.TickAsync(Net(), T.AddSeconds(3)); Equal(g.Accounts[1].Username, f.Logins.Last());
        });
        await Test("cosmetic settings edits preserve an active connection", async () =>
        {
            var f = new FakeFactory(); using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T);
            var updated = e.SnapshotConfig(); updated.Groups[0].Name = "Campus renamed"; updated.CloseToTray = false; await e.ApplyConfigAsync(updated);
            await e.TickAsync(Net(), T.AddSeconds(3)); Equal(1, f.Logins.Count); Equal(0, f.Logouts); Equal(ConnectionState.Online, e.Status.State);
            updated = e.SnapshotConfig(); updated.Groups[0].Accounts[0].Password = "replaced"; await e.ApplyConfigAsync(updated); Equal(1, f.Logouts);
        });
        await Test("backwards clock adjustment triggers a prompt heartbeat", async () =>
        {
            var f = new FakeFactory(); using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T);
            await e.TickAsync(Net(), T.AddMinutes(-5)); Equal(1, f.Heartbeats); Equal(ConnectionState.Online, e.Status.State);
        });
        await Test("manual group override ends after leaving the network", async () =>
        {
            var config = Config(); var f = new FakeFactory(); using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog());
            var home = Net(Campus with { Ssid = "Home", Signature = "home-1" }); await e.TickAsync(home, T);
            await e.ConnectAsync(config.Groups[0].Id); await e.TickAsync(home, T.AddSeconds(3)); Equal(1, f.Logins.Count);
            await e.TickAsync(Net(Campus with { Ssid = "Other", Signature = "other-2" }), T.AddSeconds(6)); Equal(1, f.Logins.Count); Equal(ConnectionState.Waiting, e.Status.State);
        });
        await Test("portal-only mode requires a pin before automatic probing", async () =>
        {
            var config = Config(); var g = config.Groups[0]; g.Ssids.Clear(); g.MatchByPortal = true;
            var f = new FakeFactory(); using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog()); await e.TickAsync(Net(), T); Equal(0, f.Probes);
            await e.ConnectAsync(g.Id); await e.TickAsync(Net(), T.AddSeconds(3)); Equal(1, f.Logins.Count);
        });
        await Test("certificate approval blocks credential submission until approved", async () =>
        {
            var config = Config(); var p = config.Groups[0].Portals[0]; var f = new FakeFactory { RequirePin = true }; var store = new MemoryStore();
            using var e = new ConnectionEngine(config, store, f, new ActivityLog()); await e.TickAsync(Net(), T);
            Equal(ConnectionState.Certificate, e.Status.State); Equal(0, f.Logins.Count); var pending = e.PendingCertificate!;
            await e.TickAsync(Net(), T.AddSeconds(3)); Equal(1, f.Probes);
            await Throws<InvalidOperationException>(() => e.ApproveCertificateAsync(pending with { Fingerprint = new string('B', 64) }));
            await e.ApproveCertificateAsync(pending); Equal(pending.Fingerprint, store.Last!.Groups[0].Portals[0].CertificateSha256);
            await e.TickAsync(Net(), T.AddSeconds(6)); Equal(1, f.Logins.Count);
        });
        await Test("editing during activity preserves new quota state", async () =>
        {
            var config = Config(); var f = new FakeFactory(); f.Responses[config.Groups[0].Accounts[0].Username] = new([new(ReplyKind.Quota)]);
            using var e = new ConnectionEngine(config, new MemoryStore(), f, new ActivityLog()); var editorCopy = e.SnapshotConfig(); editorCopy.Groups[0].Name = "Renamed";
            await e.TickAsync(Net(), T); await e.ApplyConfigAsync(editorCopy); Equal("2026-10", e.SnapshotConfig().Groups[0].Accounts[0].ExhaustedMonth);
            var changed = e.SnapshotConfig(); changed.Groups[0].Accounts[0].Password = "new-password"; await e.ApplyConfigAsync(changed);
            Equal("", e.SnapshotConfig().Groups[0].Accounts[0].ExhaustedMonth);
        });
        await Test("one in-flight login even when timer ticks overlap", async () =>
        {
            var f = new FakeFactory { BlockLogin = true }; using var e = new ConnectionEngine(Config(), new MemoryStore(), f, new ActivityLog());
            var inFlight = e.TickAsync(Net(), T); await f.LoginStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await e.TickAsync(Net(), T.AddSeconds(3)); Equal(1, f.Logins.Count);
            e.NetworkChanged(); await inFlight.WaitAsync(TimeSpan.FromSeconds(3)); Equal(1, f.MaxActiveLogins);
            Check(e.SnapshotConfig().Groups[0].Accounts.All(a => a.ExhaustedMonth == ""));
        });
        await Test("failed state persistence pauses instead of silently resetting", async () =>
        {
            var f = new FakeFactory(); using var e = new ConnectionEngine(Config(), new MemoryStore { Fail = true }, f, new ActivityLog());
            await e.TickAsync(Net(), T); Equal(ConnectionState.Attention, e.Status.State); Check(e.Paused); Equal(0, f.Logins.Count);
        });
        await Test("shared templates omit credentials, runtime and server pins", () =>
        {
            var config = Config(); config.Groups[0].Portals[0].CertificateSha256 = new string('A', 64); var source = JsonSerializer.Serialize(config);
            var shared = JsonSerializer.Serialize(config.NetworkTemplate());
            foreach (var a in config.Groups[0].Accounts) { Check(!shared.Contains(a.Username)); Check(!shared.Contains(a.Password)); }
            Check(!shared.Contains(new string('A', 64))); Equal(source, JsonSerializer.Serialize(config)); return Task.CompletedTask;
        });
        await Test("diagnostics redact raw and URL-encoded credentials", () =>
        {
            var config = Config(); var a = config.Groups[0].Accounts[0];
            var message = "<b>" + a.Username + "</b> " + a.Password + " " + Uri.EscapeDataString(a.Password) + " " + WebUtility.UrlEncode(a.Password);
            var redacted = ActivityLog.Sanitize(message, config); Check(!redacted.Contains(a.Username)); Check(!redacted.Contains(a.Password)); Check(!redacted.Contains(Uri.EscapeDataString(a.Password))); return Task.CompletedTask;
        });
        await Test("HTTP and credential-bearing portal URLs are rejected", () =>
        {
            foreach (var bad in new[] { "http://192.168.1.1/", "https://user:pass@example.test/", "https://example.test/?key=value", "https://example.test/#fragment" })
            { try { PortalEndpoint.NormalizeUrl(bad); throw new Exception("URL should fail"); } catch (InvalidDataException) { } }
            return Task.CompletedTask;
        });

        await Test("real TLS transport sends exact Sophos form and keepalive fields", async () =>
        {
            await using var server = new MockPortal(); var g = Config(1).Groups[0]; var a = g.Accounts[0]; a.Username = "STu+dent'1"; a.Password = "FAKE +&=% رمز";
            var p = new PortalEndpoint { Url = server.Url, CertificateSha256 = server.Fingerprint };
            using var client = new PortalSession(p, LocalLink);
            await client.ProbeAsync(default); Equal(ReplyKind.Online, (await client.LoginAsync(a, g, default)).Kind);
            Equal(ReplyKind.Online, (await client.HeartbeatAsync(a, g, default)).Kind); await client.LogoutAsync(a, default);
            var requests = server.Requests.ToArray(); Equal(4, requests.Length);
            var login = requests.Single(r => r.Path == "/login.xml"); Equal("POST", login.Method); Equal("191", login.Form["mode"]); Equal("0", login.Form["producttype"]);
            Equal("STu+dent''1", login.Form["username"]); Equal(a.Password, login.Form["password"]); Check(long.TryParse(login.Form["a"], out _));
            Equal(server.Url.TrimEnd('/'), login.Headers["Origin"]); Equal(server.Url, login.Headers["Referer"]);
            var live = requests.Single(r => r.Path.StartsWith("/live?", StringComparison.Ordinal)); Equal("GET", live.Method);
            var query = ParseForm(live.Path.Split('?', 2)[1]); Equal("192", query["mode"]); Equal(a.Username.ToLowerInvariant(), query["username"]); Equal("0", query["producttype"]);
            var logout = requests.Single(r => r.Path == "/logout.xml"); Equal("193", logout.Form["mode"]); Check(!logout.Form.ContainsKey("password"));
        });
        await Test("a removed or unverified base adapter fails before any HTTP credentials are sent", async () =>
        {
            await using var server=new MockPortal();var g=Config().Groups[0];
            using var client=new PortalSession(new() {Url=server.Url,CertificateSha256=server.Fingerprint},LocalLink,_=>false);
            await Throws<PortalTransportException>(()=>client.LoginAsync(g.Accounts[0],g,default));Equal(0,server.Requests.Count);
        });
        await Test("real HTTPS bypasses the system proxy and uses the selected local source address", async () =>
        {
            await using var server=new MockPortal();
            using var client=new PortalSession(new() {Url=server.Url,CertificateSha256=server.Fingerprint},LocalLink with {LocalAddress="127.0.0.2"});
            var oldProxy=HttpClient.DefaultProxy;
            try { HttpClient.DefaultProxy=new WebProxy("http://127.0.0.1:1");await client.ProbeAsync(default);Equal("127.0.0.2",server.Requests.Single().SourceAddress); }
            finally { HttpClient.DefaultProxy=oldProxy; }
        });
        await Test("untrusted and changed TLS certificates send zero HTTP credentials", async () =>
        {
            await using var server = new MockPortal(); var g = Config().Groups[0];
            foreach (var pin in new[] { "", new string('A', 64) })
            {
                using var client = new PortalSession(new() { Url = server.Url, CertificateSha256 = pin }, LocalLink);
                await Throws<CertificateApprovalException>(() => client.LoginAsync(g.Accounts[0], g, default));
            }
            Equal(0, server.Requests.Count);
        });
        await Test("portal redirects are rejected without a second request", async () =>
        {
            await using var server = new MockPortal { Status = 302 };
            using var client = new PortalSession(new() { Url = server.Url, CertificateSha256 = server.Fingerprint }, LocalLink);
            await Throws<PortalTransportException>(() => client.ProbeAsync(default)); Equal(1, server.Requests.Count);
        });
        await Test("unrecognized page and oversized body fail safely", async () =>
        {
            await using var server = new MockPortal { Page = "<html>Different device</html>" };
            using var client = new PortalSession(new() { Url = server.Url, CertificateSha256 = server.Fingerprint }, LocalLink);
            await Throws<PortalTransportException>(() => client.ProbeAsync(default));
            server.Page = new string('x', 270000); await Throws<PortalTransportException>(() => client.ProbeAsync(default));
            Check(server.Requests.All(r => r.Method == "GET"));
        });
        Console.WriteLine($"\n{_passed} passed; {_failed} failed."); return _failed == 0 ? 0 : 1;
    }

    private sealed class MemoryStore : IConfigStore
    {
        public AppConfig? Last; public bool Fail;
        public void Save(AppConfig config) { if (Fail) throw new IOException("Simulated disk failure."); Last = config.Clone(); }
    }
    private sealed class FakeFactory : IPortalFactory
    {
        public Dictionary<string, Queue<PortalReply>> Responses = [];
        public Queue<PortalReply> LiveResponses = new();
        public List<string> Logins = [], CreatedPortals = [];
        public List<NetworkLink> CreatedLinks = [];
        public int Probes, Heartbeats, Logouts, FailProbes, ActiveLogins, MaxActiveLogins;
        public bool BlockLogin, RequirePin;
        public TaskCompletionSource LoginStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IPortalSession Create(PortalEndpoint endpoint, NetworkLink link)
        { CreatedPortals.Add(endpoint.Id); CreatedLinks.Add(link); return new FakeSession(this, endpoint); }
        private sealed class FakeSession(FakeFactory f, PortalEndpoint p) : IPortalSession
        {
            public Task ProbeAsync(CancellationToken ct)
            {
                f.Probes++;
                if (f.FailProbes-- > 0) throw new PortalTransportException("Simulated network outage.");
                if (f.RequirePin && p.CertificateSha256.Length == 0) throw new CertificateApprovalException(new(p.Id, p.Url, new string('C', 64), "Test", "Test", DateTime.UtcNow.AddDays(30), false));
                return Task.CompletedTask;
            }
            public async Task<PortalReply> LoginAsync(Account a, NetworkGroup g, CancellationToken ct)
            {
                f.Logins.Add(a.Username); f.ActiveLogins++; f.MaxActiveLogins = Math.Max(f.MaxActiveLogins, f.ActiveLogins); f.LoginStarted.TrySetResult();
                try { if (f.BlockLogin) await Task.Delay(Timeout.Infinite, ct); return f.Responses.TryGetValue(a.Username, out var q) && q.Count > 0 ? q.Dequeue() : new(ReplyKind.Online); }
                finally { f.ActiveLogins--; }
            }
            public Task<PortalReply> HeartbeatAsync(Account a, NetworkGroup g, CancellationToken ct)
            { f.Heartbeats++; return Task.FromResult(f.LiveResponses.Count > 0 ? f.LiveResponses.Dequeue() : new PortalReply(ReplyKind.Online)); }
            public Task LogoutAsync(Account a, CancellationToken ct) { f.Logouts++; return Task.CompletedTask; }
            public void Dispose() { }
        }
    }
    private sealed record Captured(string Method, string Path, Dictionary<string, string> Form, Dictionary<string, string> Headers, string SourceAddress);
    private static Dictionary<string, string> ParseForm(string text) => text.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p.Length > 1 ? p[1] : ""));
    private sealed class MockPortal : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly X509Certificate2 _cert;
        public System.Collections.Concurrent.ConcurrentQueue<Captured> Requests { get; } = new();
        public string Page = "<html><script src='javascript/validation/httpclient.js'></script></html>";
        public int Status = 200;
        public string Url { get; }
        public string Fingerprint => Convert.ToHexString(SHA256.HashData(_cert.RawData));
        public MockPortal()
        {
            using var key = RSA.Create(2048); var request = new CertificateRequest("CN=UniLink-Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            _cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            _listener.Start(); Url = "https://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/";
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient tcp;
                    try { tcp = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (OperationCanceledException) { break; }
                    using (tcp)
                    using (var ssl = new SslStream(tcp.GetStream(), false))
                    {
                        try
                        {
                            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _cert, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, _stop.Token);
                            var header = new List<byte>(); var one = new byte[1];
                            while (header.Count < 32768)
                            {
                                if (await ssl.ReadAsync(one, _stop.Token) == 0) break; header.Add(one[0]);
                                if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                            }
                            if (header.Count == 0) continue;
                            var h = Encoding.ASCII.GetString(header.ToArray()); var lines = h.Split("\r\n"); var first = lines[0].Split(' ');
                            var lengthHeader = lines.FirstOrDefault(s => s.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                            var length = lengthHeader is null ? 0 : int.Parse(lengthHeader.Split(':', 2)[1]); var body = new byte[length];
                            await ssl.ReadExactlyAsync(body, _stop.Token);
                            var headers = lines.Skip(1).Where(s => s.Contains(':')).Select(s => s.Split(':', 2)).ToDictionary(p => p[0], p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
                            Requests.Enqueue(new(first[0], first[1], ParseForm(Encoding.UTF8.GetString(body)), headers, ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString()));
                            var response = first[1] == "/" ? Page : first[1].StartsWith("/live?", StringComparison.Ordinal) ? LiveXml("ack") : LoginXml(first[1] == "/logout.xml" ? "LOGIN" : "LIVE");
                            var payload = Encoding.UTF8.GetBytes(response);
                            var responseHeader = Encoding.ASCII.GetBytes($"HTTP/1.1 {Status} Test\r\nContent-Type: text/xml; charset=utf-8\r\nContent-Length: {payload.Length}\r\nLocation: https://redirect.invalid/\r\nConnection: close\r\n\r\n");
                            await ssl.WriteAsync(responseHeader, _stop.Token); await ssl.WriteAsync(payload, _stop.Token); await ssl.FlushAsync(_stop.Token);
                        }
                        catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException) { }
                    }
                }
            });
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); _listener.Stop(); await _loop; _stop.Dispose(); _cert.Dispose(); }
    }
}
