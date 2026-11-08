// Dependency-free test runner: `dotnet run --project tests/SpeakSafe.Tests`. Exit code 1 on any failure.
using System.Text;
using SpeakSafe.Core;

var t = new T();
var now = new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
const string Pass = "demo1234";

(MemoryStore store, AccountService acc, ReportService reports, ChatService chat, OrgService orgs, FakeClock clock, TestCipher cipher) Fresh()
{
    var clock = new FakeClock(now);
    var cipher = new TestCipher();
    var store = new MemoryStore(SeedData.Build(now, Pass, cipher));
    return (store, new AccountService(store, clock), new ReportService(store, cipher, new MemoryEvidenceFiles(), clock),
        new ChatService(store, cipher, clock), new OrgService(store), clock, cipher);
}

NewReport Basic(string? org = null, IdentityMode id = IdentityMode.Anonymous, params string[] types) =>
    new(types.Length == 0 ? ["verbal"] : [.. types], "work", "Lagos", new DateOnly(2026, 11, 1), "Once", "Colleague",
        "He keeps making comments.", "No", ["Legal advice"], id, org);

async Task<Actor> SurvivorActor(MemoryStore s, string nick = SeedData.SampleSurvivor)
{
    var v = await s.GetSurvivorByNicknameAsync(nick);
    return new Actor(Actor.SurvivorRole, v!.Id.ToString(), v.Nickname);
}

async Task<Actor> OfficerActor(MemoryStore s, string username)
{
    var o = await s.GetOfficerByUsernameAsync(username);
    return new Actor(Actor.OfficerRole, $"{o!.Id}:{o.OrgId}", o.Name);
}

Actor Tracker(Report r) => new(Actor.TrackerRole, r.Id.ToString(), r.Code);

// ------------------------------------------------------------------ security primitives
t.Run("Hasher verifies and rejects", () =>
{
    var h = Hasher.Hash("secret-pass");
    t.True(Hasher.Verify("secret-pass", h));
    t.False(Hasher.Verify("secret-pasS", h));
    t.False(Hasher.Verify("x", null));
    t.False(Hasher.Verify("x", "garbage"));
});

t.Run("Codes: format and normalisation", () =>
{
    var c = Codes.CaseCode();
    t.True(c.StartsWith("SS-") && c.Length == 7, c);
    t.False(c[3..].Any(ch => "01ILO".Contains(ch)), "ambiguous characters");
    t.Eq(Codes.Pin().Length, 4);
    t.Eq(Codes.NormalizeCode(" ss-7q2m "), "SS-7Q2M");
    t.Eq(Codes.NormalizeCode("7q2m"), "SS-7Q2M");
    t.Eq(Codes.NormalizeCode("SS7Q2M"), "SS-7Q2M");
    t.True(System.Text.RegularExpressions.Regex.IsMatch(Codes.Recovery(), "^[A-Z0-9]{3}-[A-Z0-9]{3}-[A-Z0-9]{3}$"));
});

// ------------------------------------------------------------------ accounts
t.Run("Register, sign in, duplicate nickname", async () =>
{
    var (_, acc, _, _, _, _, _) = Fresh();
    var (s, rec) = await acc.RegisterAsync("QuietRiver48", "longenough", null);
    t.True(s.Id > 0);
    t.Eq(rec.Length, 11);
    t.Eq((await acc.SignInAsync("quietriver48", "longenough")).Id, s.Id);
    t.Eq((await t.Throws<SpeakException>(() => acc.RegisterAsync("QuietRiver48", "longenough", null))).Status, 409);
    await t.Throws<SpeakException>(() => acc.SignInAsync("QuietRiver48", "wrongpass1"));
});

t.Run("Register validation", async () =>
{
    var (_, acc, _, _, _, _, _) = Fresh();
    await t.Throws<SpeakException>(() => acc.RegisterAsync("ab", "longenough", null));
    await t.Throws<SpeakException>(() => acc.RegisterAsync("Real Name", "longenough", null));
    await t.Throws<SpeakException>(() => acc.RegisterAsync("GoodName1", "short", null));
    await t.Throws<SpeakException>(() => acc.RegisterAsync("GoodName1", "longenough", "not-an-email"));
});

t.Run("Recovery code resets passphrase and rotates", async () =>
{
    var (_, acc, _, _, _, _, _) = Fresh();
    var (_, rec) = await acc.RegisterAsync("CalmDawn11", "firstpass1", null);
    var (_, rec2) = await acc.RecoverAsync("CalmDawn11", rec.ToLowerInvariant(), "secondpass2");
    await acc.SignInAsync("CalmDawn11", "secondpass2");
    await t.Throws<SpeakException>(() => acc.SignInAsync("CalmDawn11", "firstpass1"));
    await t.Throws<SpeakException>(() => acc.RecoverAsync("CalmDawn11", rec, "thirdpass3"));
    t.True(rec2 != rec);
});

t.Run("Track by code and PIN", async () =>
{
    var (_, acc, _, _, _, _, _) = Fresh();
    t.Eq((await acc.TrackAsync("ss-7q2m", SeedData.SamplePin)).Code, SeedData.SampleCode);
    t.Eq((await t.Throws<SpeakException>(() => acc.TrackAsync(SeedData.SampleCode, "0000"))).Status, 401);
    await t.Throws<SpeakException>(() => acc.TrackAsync("SS-ZZZZ", SeedData.SamplePin));
});

t.Run("Officer sign-in blocked when org inactive", async () =>
{
    var (store, acc, _, _, orgs, _, _) = Fresh();
    await acc.OfficerSignInAsync("kemi.csn", Pass);
    var csn = await store.GetOrganisationAsync("csn");
    await orgs.SaveAsync(new OrgInput(csn!.Id, csn.Name, csn.Initials, csn.Color, csn.Services, csn.States, csn.Description, csn.ResponseNote, csn.Languages, false));
    t.Eq((await t.Throws<SpeakException>(() => acc.OfficerSignInAsync("kemi.csn", Pass))).Status, 403);
});

// ------------------------------------------------------------------ reports
t.Run("Anonymous report without account", async () =>
{
    var (store, _, reports, _, _, _, cipher) = Fresh();
    var (r, pin) = await reports.CreateAsync(Actor.Nobody, Basic());
    t.Eq(r.Status, ReportStatus.Submitted);
    t.Eq(r.SurvivorId, null);
    t.Eq(pin.Length, 4);
    var saved = await store.GetReportByCodeAsync(r.Code);
    t.True(saved!.Narrative!.StartsWith("enc:"), "narrative must be stored encrypted");
    t.Eq(reports.ReadNarrative(saved), "He keeps making comments.");
    t.True(Hasher.Verify(pin, saved.PinHash));
    t.Eq((await store.GetEventsAsync(r.Id)).Count, 1);
});

t.Run("Report validation", async () =>
{
    var (_, _, reports, _, _, _, _) = Fresh();
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic() with { Types = [] }));
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic() with { Types = ["nope"] }));
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic() with { Place = "moon" }));
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic() with { State = "Atlantis" }));
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic() with { IncidentDate = new DateOnly(2027, 1, 1) }));
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic() with { Narrative = new string('x', 5001) }));
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic() with { OrgId = "ghost" }));
    var r = (await reports.CreateAsync(Actor.Nobody, Basic() with { State = "", Frequency = null, Narrative = "  " })).Report;
    t.Eq(r.State, null);
    t.Eq(r.Frequency, "Once");
    t.Eq(r.Narrative, null);
});

t.Run("Sharing nickname needs an account", async () =>
{
    var (store, _, reports, _, _, _, _) = Fresh();
    await t.Throws<SpeakException>(() => reports.CreateAsync(Actor.Nobody, Basic(id: IdentityMode.ShareNickname)));
    var me = await SurvivorActor(store);
    var (r, _) = await reports.CreateAsync(me, Basic("lrd", IdentityMode.ShareNickname));
    t.Eq(await reports.ReporterLabelAsync(r), SeedData.SampleSurvivor);
});

t.Run("Assault is flagged urgent", async () =>
{
    var (_, _, reports, _, _, _, _) = Fresh();
    t.True((await reports.CreateAsync(Actor.Nobody, Basic(types: [Catalog.AssaultType, "touch"]))).Report.Urgent);
    t.False((await reports.CreateAsync(Actor.Nobody, Basic())).Report.Urgent);
});

t.Run("Report sent to org opens a conversation", async () =>
{
    var (store, _, reports, chat, _, _, _) = Fresh();
    var (r, _) = await reports.CreateAsync(Actor.Nobody, Basic("wdc"));
    t.Eq(r.Status, ReportStatus.Referred);
    var convs = await store.GetConversationsForReportAsync(r.Id);
    t.Eq(convs.Count, 1);
    var list = await chat.ListAsync(Tracker(r));
    t.Eq(list.Single().OrgName, "Workplace Dignity Clinic");
    t.Contains(list[0].LastText, "Your identity is hidden");
});

t.Run("Share later, once only", async () =>
{
    var (_, _, reports, _, _, _, _) = Fresh();
    var (r, _) = await reports.CreateAsync(Actor.Nobody, Basic());
    var tr = Tracker(r);
    var shared = await reports.ShareAsync(tr, r.Code, "hcc");
    t.Eq(shared.Status, ReportStatus.Referred);
    t.Eq((await t.Throws<SpeakException>(() => reports.ShareAsync(tr, r.Code, "lrd"))).Status, 409);
});

t.Run("Only the owner or tracker can act on a report", async () =>
{
    var (store, acc, reports, _, _, _, _) = Fresh();
    var (r, _) = await reports.CreateAsync(Actor.Nobody, Basic());
    var me = await SurvivorActor(store);
    t.Eq((await t.Throws<SpeakException>(() => reports.ShareAsync(me, r.Code, "hcc"))).Status, 404);
    var other = (await reports.CreateAsync(Actor.Nobody, Basic())).Report;
    await t.Throws<SpeakException>(() => reports.CloseByReporterAsync(Tracker(other), r.Code));
    await reports.CloseByReporterAsync(me, SeedData.SampleCode);
    t.Eq((await store.GetReportByCodeAsync(SeedData.SampleCode))!.Status, ReportStatus.Closed);
});

t.Run("Officer status changes limited to own org", async () =>
{
    var (store, _, reports, _, _, _, _) = Fresh();
    var kemi = await OfficerActor(store, "kemi.csn");
    var amaka = await OfficerActor(store, "amaka.lrd");
    await t.Throws<SpeakException>(() => reports.SetStatusByOfficerAsync(amaka, SeedData.SampleCode, ReportStatus.Closed, null));
    await t.Throws<SpeakException>(() => reports.SetStatusByOfficerAsync(kemi, SeedData.SampleCode, ReportStatus.Referred, null));
    await reports.SetStatusByOfficerAsync(kemi, SeedData.SampleCode, ReportStatus.Closed, "Resolved with university panel");
    var r = await store.GetReportByCodeAsync(SeedData.SampleCode);
    t.Eq(r!.Status, ReportStatus.Closed);
    t.Eq((await store.GetEventsAsync(r.Id)).Last().Note, "Resolved with university panel");
    await t.Throws<SpeakException>(() => reports.SetStatusByOfficerAsync(kemi, SeedData.SampleCode, ReportStatus.InProgress, null));
});

t.Run("CanSee: owner, tracker, matching org only", async () =>
{
    var (store, _, reports, _, _, _, _) = Fresh();
    var r = (await store.GetReportByCodeAsync(SeedData.SampleCode))!;
    t.True(reports.CanSee(await SurvivorActor(store), r));
    t.True(reports.CanSee(Tracker(r), r));
    t.True(reports.CanSee(await OfficerActor(store, "kemi.csn"), r));
    t.False(reports.CanSee(await OfficerActor(store, "amaka.lrd"), r));
    t.False(reports.CanSee(new Actor(Actor.AdminRole, "admin", "Coordinator"), r));
    t.False(reports.CanSee(Actor.Nobody, r));
});

// ------------------------------------------------------------------ evidence
t.Run("Evidence is encrypted, limited, and access-checked", async () =>
{
    var (store, _, reports, _, _, _, _) = Fresh();
    var (r, _) = await reports.CreateAsync(Actor.Nobody, Basic("lrd"));
    var tr = Tracker(r);
    var data = Encoding.UTF8.GetBytes("screenshot bytes");
    var e = await reports.AddEvidenceAsync(tr, r.Code, "../../etc/chat.png", "image/png", data);
    t.Eq(e.FileName, "chat.png");
    var back = await reports.ReadEvidenceAsync(tr, e.Id);
    t.True(back.Data.SequenceEqual(data));
    t.Eq((await reports.ReadEvidenceAsync(await OfficerActor(store, "amaka.lrd"), e.Id)).Meta.Id, e.Id);
    var kemi = await OfficerActor(store, "kemi.csn");
    await t.Throws<SpeakException>(() => reports.ReadEvidenceAsync(kemi, e.Id));
    await t.Throws<SpeakException>(() => reports.AddEvidenceAsync(tr, r.Code, "a.exe", "application/x-msdownload", data));
    await t.Throws<SpeakException>(() => reports.AddEvidenceAsync(tr, r.Code, "big.png", "image/png", new byte[ReportService.MaxEvidenceBytes + 1]));
    for (var i = 0; i < 4; i++) await reports.AddEvidenceAsync(tr, r.Code, $"f{i}.png", "image/png", data);
    await t.Throws<SpeakException>(() => reports.AddEvidenceAsync(tr, r.Code, "f5.png", "image/png", data));
});

// ------------------------------------------------------------------ conversations
t.Run("Start conversation is idempotent and needs an account", async () =>
{
    var (store, _, _, chat, _, _, _) = Fresh();
    await t.Throws<SpeakException>(() => chat.StartAsync(Actor.Nobody, "hcc"));
    var me = await SurvivorActor(store);
    var a = await chat.StartAsync(me, "hcc");
    var b = await chat.StartAsync(me, "hcc");
    t.Eq(a.Id, b.Id);
    t.Eq((await chat.ListAsync(me)).Count, 2);
});

t.Run("Messages round trip, encrypted, unread counts", async () =>
{
    var (store, _, reports, chat, _, clock, _) = Fresh();
    var (r, _) = await reports.CreateAsync(Actor.Nobody, Basic("lrd"));
    var tr = Tracker(r);
    var conv = (await chat.ListAsync(tr)).Single();
    clock.Advance(TimeSpan.FromMinutes(1));
    await chat.SendAsync(tr, conv.Id, "Can someone help me?");
    var amaka = await OfficerActor(store, "amaka.lrd");
    var orgList = await chat.ListAsync(amaka);
    var mine = orgList.Single(c => c.Id == conv.Id);
    t.Eq(mine.Unread, 1);
    t.Eq(mine.With, $"Anonymous reporter ({r.Code})");
    var raw = await store.GetMessagesAsync(conv.Id, 0);
    t.True(raw.All(m => m.Body.StartsWith("enc:")), "messages stored encrypted");
    var (_, msgs) = await chat.MessagesAsync(amaka, conv.Id, 0);
    t.Eq(msgs.Last().Text, "Can someone help me?");
    t.Eq((await chat.ListAsync(amaka)).Single(c => c.Id == conv.Id).Unread, 0);
    clock.Advance(TimeSpan.FromMinutes(1));
    await chat.SendAsync(amaka, conv.Id, "Hello, I'm Amaka.");
    t.Eq((await chat.ListAsync(tr)).Single().Unread, 1);
    var (_, after) = await chat.MessagesAsync(tr, conv.Id, msgs.Last().Id);
    t.Eq(after.Single().Who, "Amaka Obi");
});

t.Run("First officer reply moves report to in progress", async () =>
{
    var (store, _, reports, chat, _, _, _) = Fresh();
    var r = (await store.GetReportByCodeAsync(SeedData.SampleAnonCode))!;
    t.Eq(r.Status, ReportStatus.Referred);
    var amaka = await OfficerActor(store, "amaka.lrd");
    var conv = (await chat.ListAsync(amaka)).Single();
    await chat.SendAsync(amaka, conv.Id, "We received your report.");
    t.Eq((await store.GetReportByCodeAsync(SeedData.SampleAnonCode))!.Status, ReportStatus.InProgress);
});

t.Run("Conversation access is isolated", async () =>
{
    var (store, acc, _, chat, _, _, _) = Fresh();
    var seeded = (await store.GetConversationsForOrgAsync("csn")).Single();
    var (stranger, _) = await acc.RegisterAsync("Stranger99", "longenough", null);
    var sa = new Actor(Actor.SurvivorRole, stranger.Id.ToString(), stranger.Nickname);
    t.Eq((await t.Throws<SpeakException>(() => chat.MessagesAsync(sa, seeded.Id, 0))).Status, 404);
    var amaka = await OfficerActor(store, "amaka.lrd");
    await t.Throws<SpeakException>(() => chat.SendAsync(amaka, seeded.Id, "hi"));
    var anon = (await store.GetReportByCodeAsync(SeedData.SampleAnonCode))!;
    await t.Throws<SpeakException>(() => chat.MessagesAsync(Tracker(anon), seeded.Id, 0));
    await t.Throws<SpeakException>(() => chat.ListAsync(Actor.Nobody));
});

t.Run("Message validation", async () =>
{
    var (store, _, _, chat, _, _, _) = Fresh();
    var me = await SurvivorActor(store);
    var c = (await chat.ListAsync(me)).Single();
    await t.Throws<SpeakException>(() => chat.SendAsync(me, c.Id, "   "));
    await t.Throws<SpeakException>(() => chat.SendAsync(me, c.Id, new string('a', ChatService.MaxMessage + 1)));
});

// ------------------------------------------------------------------ organisations + coordinator
t.Run("Public org list filters by service and state", async () =>
{
    var (_, _, _, _, orgs, _, _) = Fresh();
    t.Eq((await orgs.PublicListAsync(null, null)).Count, 6);
    var kano = await orgs.PublicListAsync(null, "Kano");
    t.True(kano.Any(o => o.Id == "nwj") && kano.Any(o => o.Id == "hcc") && kano.All(o => o.Id != "lrd"));
    t.Eq((await orgs.PublicListAsync("Medical care", null)).Single().Id, "cpm");
});

t.Run("Coordinator overview has aggregates only", async () =>
{
    var (_, _, reports, _, orgs, _, _) = Fresh();
    await reports.CreateAsync(Actor.Nobody, Basic(types: [Catalog.AssaultType]));
    var ov = await orgs.OverviewAsync();
    t.Eq(ov.Reports, 3);
    t.Eq(ov.Urgent, 1);
    t.Eq(ov.ByStatus["Submitted"], 1);
    t.Eq(ov.ByState["Lagos"], 3);
    t.Eq(ov.Organisations.Single(o => o.Org.Id == "csn").OpenCases, 1);
});

t.Run("Org save validation and officer creation", async () =>
{
    var (store, acc, _, _, orgs, _, _) = Fresh();
    await t.Throws<SpeakException>(() => orgs.SaveAsync(new OrgInput("Bad ID!", "Name here", null, null, ["Legal aid"], ["Lagos"], "", "", "", true)));
    await t.Throws<SpeakException>(() => orgs.SaveAsync(new OrgInput("new-org", "Name here", null, null, ["Magic"], ["Lagos"], "", "", "", true)));
    var o = await orgs.SaveAsync(new OrgInput("new-org", "Kano Legal Aid Hub", null, null, ["Legal aid"], ["Kano"], "Desc", "Replies within a day", "Hausa", true));
    t.Eq(o.Initials, "KL");
    t.False(o.IsSample);
    var (off, pass) = await orgs.AddOfficerAsync("new-org", "Musa Bello", "Musa.Bello");
    t.Eq(off.Username, "musa.bello");
    t.Eq((await acc.OfficerSignInAsync("musa.bello", pass)).Id, off.Id);
    t.Eq((await t.Throws<SpeakException>(() => orgs.AddOfficerAsync("new-org", "Other", "musa.bello"))).Status, 409);
});

return t.Done();

// ====================================================================== helpers

sealed class TestCipher : ICipher
{
    public string Protect(string plain) => "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
    public string Unprotect(string cipher) => Encoding.UTF8.GetString(Convert.FromBase64String(cipher[4..]));
    public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).ToArray();
    public byte[] Unprotect(byte[] cipher) => Protect(cipher);
}

sealed class FakeClock(DateTime start) : TimeProvider
{
    private DateTimeOffset _now = new(start, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan d) => _now += d;
}

sealed class T
{
    private int _pass, _fail;
    private readonly List<string> _failures = [];

    public void Run(string name, Action a) => Run(name, () => { a(); return Task.CompletedTask; });
    public void Run(string name, Func<Task> a)
    {
        try { a().GetAwaiter().GetResult(); _pass++; Console.WriteLine($"  ✓ {name}"); }
        catch (Exception ex) { _fail++; _failures.Add($"{name}: {ex.Message}"); Console.WriteLine($"  ✗ {name}\n      {ex.Message}"); }
    }

    public void True(bool c, string? msg = null) { if (!c) throw new Exception(msg ?? "expected true"); }
    public void False(bool c, string? msg = null) { if (c) throw new Exception(msg ?? "expected false"); }
    public void Eq<TV>(TV actual, TV expected) { if (!EqualityComparer<TV>.Default.Equals(actual, expected)) throw new Exception($"expected <{expected}> but got <{actual}>"); }
    public void Contains(string s, string part) { if (!s.Contains(part)) throw new Exception($"expected text to contain <{part}> but was:\n{s}"); }
    public async Task<TE> Throws<TE>(Func<Task> f) where TE : Exception
    {
        try { await f(); }
        catch (TE e) { return e; }
        throw new Exception($"expected {typeof(TE).Name}");
    }

    public int Done()
    {
        Console.WriteLine($"\n{_pass} passed, {_fail} failed");
        foreach (var f in _failures) Console.WriteLine("  FAIL " + f);
        return _fail == 0 ? 0 : 1;
    }
}
