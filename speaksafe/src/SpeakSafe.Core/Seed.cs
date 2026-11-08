namespace SpeakSafe.Core;

public sealed record SeedConversation(Conversation Conversation, List<Message> Messages);
public sealed record SeedReport(Report Report, string? SurvivorNickname, List<ReportEvent> Events, SeedConversation? Conversation);

public sealed class SeedSet
{
    public List<Organisation> Organisations { get; } = [];
    public List<Officer> Officers { get; } = [];
    public List<Survivor> Survivors { get; } = [];
    public List<SeedReport> Reports { get; } = [];
}

/// <summary>Fictional demo partners, officers and two sample reports. Everything is flagged IsSample.</summary>
public static class SeedData
{
    public const string SampleSurvivor = "BraveSun21";
    public const string SampleCode = "SS-7Q2M", SamplePin = "4821";
    public const string SampleAnonCode = "SS-3HVK", SampleAnonPin = "1357";

    /// <summary>(username, name, orgId) of the sample officers, shown as demo sign-ins.</summary>
    public static readonly (string Username, string Name, string OrgId)[] SampleOfficers =
    [
        ("kemi.csn", "Kemi Adebayo", "csn"),
        ("amaka.lrd", "Amaka Obi", "lrd"),
        ("tobi.hcc", "Tobi Lawal", "hcc"),
        ("hauwa.nwj", "Hauwa Musa", "nwj"),
        ("chidi.wdc", "Chidi Okeke", "wdc"),
        ("ngozi.cpm", "Dr. Ngozi Eze", "cpm"),
    ];

    public static SeedSet Build(DateTime now, string samplePassword, ICipher cipher)
    {
        var s = new SeedSet();
        s.Organisations.AddRange(
        [
            Org("lrd", "Lagos Rights Desk", "LR", "#0b7a4b", ["Lagos", "Ogun"], ["Human rights", "Legal aid"],
                "Free legal advice and case follow-up for survivors of sexual harassment and gender-based violence.", "Replies within 2 hours", "English, Yorùbá, Pidgin"),
            Org("hcc", "Haven Counselling Collective", "HC", "#7a3fb0", [Catalog.AllStates], ["Counselling"],
                "Trauma-informed counsellors available by chat and phone, nationwide.", "Replies within 1 hour", "English, Igbo, Pidgin"),
            Org("csn", "Campus Safe Network", "CS", "#1f62b5", ["Lagos", "Oyo", "Ogun", "Osun"], ["Campus support", "Human rights"],
                "Supports students facing harassment by lecturers or peers and follows up with university panels.", "Replies within 4 hours", "English, Yorùbá"),
            Org("nwj", "Northern Women Justice Forum", "NW", "#b5591f", ["Kano", "Kaduna", "FCT Abuja", "Bauchi"], ["Human rights", "Legal aid"],
                "Human rights advocates and lawyers working with survivors across the North.", "Replies within 6 hours", "English, Hausa"),
            Org("wdc", "Workplace Dignity Clinic", "WD", "#0f7d7d", ["Lagos", "Rivers", "FCT Abuja"], ["Workplace support", "Legal aid"],
                "Employment lawyers who help you raise complaints at work and protect your job.", "Replies within 1 day", "English"),
            Org("cpm", "ClearPath Medical Support", "CP", "#c2263a", ["Lagos", "Rivers", "Enugu", "FCT Abuja"], ["Medical care", "Counselling"],
                "Arranges free medical examination, emergency care and referrals after sexual assault.", "Replies within 30 minutes", "English, Igbo"),
        ]);

        foreach (var (user, name, org) in SampleOfficers)
            s.Officers.Add(new Officer { OrgId = org, Name = name, Username = user, PassHash = Hasher.Hash(samplePassword), IsSample = true });

        s.Survivors.Add(new Survivor
        {
            Nickname = SampleSurvivor, PassHash = Hasher.Hash(samplePassword), RecoveryHash = Hasher.Hash("RQ7-4KD-9PX"),
            CreatedUtc = now.AddDays(-26), IsSample = true,
        });

        // Report 1: account holder, shared with Campus Safe Network, conversation under way.
        var t1 = now.AddDays(-25);
        s.Reports.Add(new SeedReport(
            new Report
            {
                Code = SampleCode, PinHash = Hasher.Hash(SamplePin), Types = ["quid", "verbal"], Place = "school", State = "Lagos",
                IncidentDate = DateOnly.FromDateTime(now.AddDays(-27)), Frequency = "A few times", Responsible = "Lecturer or teacher",
                Narrative = cipher.Protect("After class my lecturer kept asking me to come to his office alone. He said my result depends on how 'cooperative' I am and made comments about my body in front of other students."),
                Witnesses = "Yes", Support = ["Legal advice", "Help reporting to my employer or school"], Identity = IdentityMode.ShareNickname,
                OrgId = "csn", Status = ReportStatus.InProgress, CreatedUtc = t1, UpdatedUtc = t1.AddHours(14), IsSample = true,
            },
            SampleSurvivor,
            [
                new ReportEvent { Kind = "Submitted", By = "Reporter", Utc = t1 },
                new ReportEvent { Kind = "Referred", Note = "Campus Safe Network", By = "Reporter", Utc = t1 },
                new ReportEvent { Kind = "InProgress", Note = "Case officer assigned", By = "Campus Safe Network", Utc = t1.AddHours(14) },
            ],
            new SeedConversation(
                new Conversation { OrgId = "csn", CreatedUtc = t1, LastUtc = t1.AddHours(14.5), SurvivorReadUtc = t1.AddHours(14.5), OrgReadUtc = t1.AddHours(14.5), IsSample = true },
                [
                    new Message { From = Sender.System, Body = cipher.Protect($"Report {SampleCode} shared with Campus Safe Network. They see you as {SampleSurvivor}."), Utc = t1 },
                    new Message { From = Sender.Officer, Body = cipher.Protect("Hello, I'm Kemi, a case officer at Campus Safe Network. Thank you for trusting us with this. You don't need to share anything you're not ready to."), Utc = t1.AddHours(14) },
                    new Message { From = Sender.Survivor, Body = cipher.Protect("Thank you. I'm worried about my grades if I report him."), Utc = t1.AddHours(14.2) },
                    new Message { From = Sender.Officer, Body = cipher.Protect("That fear is very common, and retaliation is itself a violation. We can explain the university's complaint process and how your identity can be protected. Would a short call this week help?"), Utc = t1.AddHours(14.5) },
                ])));

        // Report 2: no account, fully anonymous, waiting for Lagos Rights Desk.
        var t2 = now.AddHours(-5);
        s.Reports.Add(new SeedReport(
            new Report
            {
                Code = SampleAnonCode, PinHash = Hasher.Hash(SampleAnonPin), Types = ["touch"], Place = "work", State = "Lagos",
                IncidentDate = DateOnly.FromDateTime(now.AddDays(-2)), Frequency = "It is still happening", Responsible = "Boss or supervisor",
                Narrative = cipher.Protect("My supervisor touches my waist and shoulders when nobody is around and says I should be 'friendlier' if I want my contract renewed."),
                Witnesses = "No", Support = ["Legal advice"], Identity = IdentityMode.Anonymous, OrgId = "lrd", Status = ReportStatus.Referred,
                CreatedUtc = t2, UpdatedUtc = t2, IsSample = true,
            },
            null,
            [
                new ReportEvent { Kind = "Submitted", By = "Reporter", Utc = t2 },
                new ReportEvent { Kind = "Referred", Note = "Lagos Rights Desk", By = "Reporter", Utc = t2 },
            ],
            new SeedConversation(
                new Conversation { OrgId = "lrd", CreatedUtc = t2, LastUtc = t2, SurvivorReadUtc = t2, OrgReadUtc = DateTime.MinValue, IsSample = true },
                [new Message { From = Sender.System, Body = cipher.Protect($"Report {SampleAnonCode} shared with Lagos Rights Desk. Your identity is hidden."), Utc = t2 }])));

        return s;
    }

    private static Organisation Org(string id, string name, string ini, string color, List<string> states, List<string> services,
        string desc, string resp, string lang) => new()
    {
        Id = id, Name = name, Initials = ini, Color = color, States = states, Services = services, Description = desc,
        ResponseNote = resp, Languages = lang, IsActive = true, IsSample = true,
    };
}
