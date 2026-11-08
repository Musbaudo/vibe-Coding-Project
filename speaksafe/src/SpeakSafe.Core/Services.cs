using System.Text.RegularExpressions;

namespace SpeakSafe.Core;

// ====================================================================== accounts

public sealed partial class AccountService(ISpeakStore store, TimeProvider clock)
{
    [GeneratedRegex("^[A-Za-z0-9_]{4,20}$")]
    private static partial Regex NicknameRule();

    [GeneratedRegex("^[a-z0-9._-]{3,40}$")]
    private static partial Regex UsernameRule();

    public const int MinPassphrase = 8;

    /// <summary>Creates an account. Returns the survivor and the one-time recovery code.</summary>
    public async Task<(Survivor Survivor, string RecoveryCode)> RegisterAsync(string? nickname, string? passphrase, string? email)
    {
        nickname = (nickname ?? "").Trim();
        if (!NicknameRule().IsMatch(nickname)) throw new SpeakException("Nickname must be 4 to 20 letters, numbers or underscores.");
        CheckPassphrase(passphrase);
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (email is not null && (email.Length > 200 || !email.Contains('@'))) throw new SpeakException("That email address doesn't look right. Leave it blank to stay fully anonymous.");

        var recovery = Codes.Recovery();
        var s = new Survivor
        {
            Nickname = nickname, PassHash = Hasher.Hash(passphrase!), RecoveryHash = Hasher.Hash(recovery), Email = email,
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
        };
        try { await store.InsertSurvivorAsync(s); }
        catch (DuplicateException) { throw new SpeakException("That nickname is taken. Try another one.", 409); }
        return (s, recovery);
    }

    public async Task<Survivor> SignInAsync(string? nickname, string? passphrase)
    {
        var s = await store.GetSurvivorByNicknameAsync((nickname ?? "").Trim());
        if (s is null || !Hasher.Verify(passphrase ?? "", s.PassHash)) throw new SpeakException("That nickname and passphrase don't match.", 401);
        return s;
    }

    /// <summary>Resets the passphrase with the recovery code. Returns a new recovery code (the old one stops working).</summary>
    public async Task<(Survivor Survivor, string RecoveryCode)> RecoverAsync(string? nickname, string? recoveryCode, string? newPassphrase)
    {
        var s = await store.GetSurvivorByNicknameAsync((nickname ?? "").Trim());
        var code = (recoveryCode ?? "").Trim().ToUpperInvariant();
        if (s is null || !Hasher.Verify(code, s.RecoveryHash)) throw new SpeakException("That nickname and recovery code don't match.", 401);
        CheckPassphrase(newPassphrase);
        var fresh = Codes.Recovery();
        await store.UpdateSurvivorSecretsAsync(s.Id, Hasher.Hash(newPassphrase!), Hasher.Hash(fresh));
        return (s, fresh);
    }

    public async Task<Report> TrackAsync(string? code, string? pin)
    {
        var r = await store.GetReportByCodeAsync(Codes.NormalizeCode(code));
        if (r is null || !Hasher.Verify((pin ?? "").Trim(), r.PinHash)) throw new SpeakException("We couldn't find a report with that code and PIN.", 401);
        return r;
    }

    public async Task<Officer> OfficerSignInAsync(string? username, string? passphrase)
    {
        var o = await store.GetOfficerByUsernameAsync((username ?? "").Trim());
        if (o is null || !o.IsActive || !Hasher.Verify(passphrase ?? "", o.PassHash)) throw new SpeakException("That username and passphrase don't match.", 401);
        var org = await store.GetOrganisationAsync(o.OrgId);
        if (org is null || !org.IsActive) throw new SpeakException("Your organisation is not active on SpeakSafe. Contact the coordinator.", 403);
        return o;
    }

    public static void CheckPassphrase(string? p)
    {
        if (string.IsNullOrEmpty(p) || p.Length < MinPassphrase) throw new SpeakException($"Passphrase must be at least {MinPassphrase} characters.");
        if (p.Length > 200) throw new SpeakException("Passphrase is too long.");
    }

    public static bool ValidUsername(string u) => UsernameRule().IsMatch(u);
}

// ====================================================================== reports

public sealed record NewReport(
    List<string>? Types, string? Place, string? State, DateOnly? IncidentDate, string? Frequency, string? Responsible,
    string? Narrative, string? Witnesses, List<string>? Support, IdentityMode Identity, string? OrgId);

public sealed record EvidenceFile(Evidence Meta, byte[] Data);

public sealed class ReportService(ISpeakStore store, ICipher cipher, IEvidenceFiles files, TimeProvider clock)
{
    public const int MaxNarrative = 5000, MaxEvidencePerReport = 5;
    public const long MaxEvidenceBytes = 10 * 1024 * 1024;

    public static readonly string[] EvidenceTypes =
    [
        "image/jpeg", "image/png", "image/webp", "image/heic", "image/gif", "application/pdf",
        "audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav", "audio/x-m4a", "video/mp4", "text/plain",
    ];

    public ICipher Cipher => cipher;

    public async Task<(Report Report, string Pin)> CreateAsync(Actor actor, NewReport n)
    {
        var types = (n.Types ?? []).Distinct().ToList();
        if (types.Count == 0) throw new SpeakException("Select at least one option for what happened.");
        if (types.Any(t => Catalog.Types.All(x => x.Id != t))) throw new SpeakException("One of the selected options is not recognised.");
        var place = n.Place ?? "";
        if (Catalog.Places.All(p => p.Id != place)) throw new SpeakException("Choose where it happened.");
        var state = string.IsNullOrWhiteSpace(n.State) ? null : n.State;
        if (state is not null && !Catalog.States.Contains(state)) throw new SpeakException("Choose a state from the list.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (n.IncidentDate is { } d && d > DateOnly.FromDateTime(now.AddDays(1))) throw new SpeakException("The date can't be in the future.");
        var freq = Pick(n.Frequency, Catalog.Frequencies, "How often");
        var resp = Pick(n.Responsible, Catalog.Responsible, "Who was responsible");
        var wit = Pick(n.Witnesses, Catalog.Witnesses, "Witnesses");
        var narrative = (n.Narrative ?? "").Trim();
        if (narrative.Length > MaxNarrative) throw new SpeakException($"Please keep the description under {MaxNarrative} characters.");
        var support = (n.Support ?? []).Distinct().ToList();
        if (support.Any(x => !Catalog.Support.Contains(x))) throw new SpeakException("One of the support options is not recognised.");
        if (n.Identity == IdentityMode.ShareNickname && !actor.IsSurvivor)
            throw new SpeakException("Sharing a nickname needs an account. Sign in, or choose fully anonymous.");

        Organisation? org = null;
        if (!string.IsNullOrEmpty(n.OrgId))
        {
            org = await store.GetOrganisationAsync(n.OrgId);
            if (org is null || !org.IsActive) throw new SpeakException("That organisation is not available.");
        }

        var pin = Codes.Pin();
        var r = new Report
        {
            PinHash = Hasher.Hash(pin), SurvivorId = actor.IsSurvivor ? actor.SurvivorId : null, Types = types, Place = place, State = state,
            IncidentDate = n.IncidentDate, Frequency = freq, Responsible = resp, Narrative = narrative.Length == 0 ? null : cipher.Protect(narrative),
            Witnesses = wit, Support = support, Identity = n.Identity, OrgId = org?.Id,
            Status = org is null ? ReportStatus.Submitted : ReportStatus.Referred, Urgent = types.Contains(Catalog.AssaultType),
            CreatedUtc = now, UpdatedUtc = now,
        };
        for (var attempt = 0; ; attempt++)
        {
            r.Code = Codes.CaseCode();
            try { await store.InsertReportAsync(r); break; }
            catch (DuplicateException) when (attempt < 8) { }
        }

        await store.AddEventAsync(new ReportEvent { ReportId = r.Id, Kind = "Submitted", By = "Reporter", Utc = now });
        if (org is not null) await ReferAsync(r, org, actor, now);
        return (r, pin);
    }

    private async Task ReferAsync(Report r, Organisation org, Actor actor, DateTime now)
    {
        await store.AddEventAsync(new ReportEvent { ReportId = r.Id, Kind = "Referred", Note = org.Name, By = "Reporter", Utc = now });
        var c = new Conversation
        {
            SurvivorId = r.SurvivorId, ReportId = r.Id, OrgId = org.Id, CreatedUtc = now, LastUtc = now, SurvivorReadUtc = now, OrgReadUtc = DateTime.MinValue,
        };
        await store.InsertConversationAsync(c);
        var who = r.Identity == IdentityMode.ShareNickname && actor.IsSurvivor ? $"They see you as {actor.Name}." : "Your identity is hidden.";
        await store.InsertMessageAsync(new Message { ConversationId = c.Id, From = Sender.System, Body = cipher.Protect($"Report {r.Code} shared with {org.Name}. {who}"), Utc = now });
    }

    /// <summary>Shares a not-yet-shared report with one organisation.</summary>
    public async Task<Report> ShareAsync(Actor actor, string code, string? orgId)
    {
        var r = await OwnedAsync(actor, code);
        if (r.Status == ReportStatus.Closed) throw new SpeakException("This report is closed.");
        if (r.OrgId is not null) throw new SpeakException("This report is already shared with an organisation.", 409);
        var org = string.IsNullOrEmpty(orgId) ? null : await store.GetOrganisationAsync(orgId);
        if (org is null || !org.IsActive) throw new SpeakException("That organisation is not available.");
        var now = clock.GetUtcNow().UtcDateTime;
        await store.UpdateReportStatusAsync(r.Id, ReportStatus.Referred, org.Id, now);
        r.Status = ReportStatus.Referred; r.OrgId = org.Id;
        await ReferAsync(r, org, actor, now);
        return r;
    }

    public async Task CloseByReporterAsync(Actor actor, string code)
    {
        var r = await OwnedAsync(actor, code);
        if (r.Status == ReportStatus.Closed) return;
        var now = clock.GetUtcNow().UtcDateTime;
        await store.UpdateReportStatusAsync(r.Id, ReportStatus.Closed, r.OrgId, now);
        await store.AddEventAsync(new ReportEvent { ReportId = r.Id, Kind = "Closed", Note = "Closed by reporter", By = "Reporter", Utc = now });
    }

    /// <summary>Officer moves a case to InProgress or Closed.</summary>
    public async Task SetStatusByOfficerAsync(Actor actor, string code, ReportStatus status, string? note)
    {
        if (!actor.IsOfficer) throw new SpeakException("Only organisation staff can do this.", 403);
        if (status is not (ReportStatus.InProgress or ReportStatus.Closed)) throw new SpeakException("Status can only be set to in progress or closed.");
        var r = await store.GetReportByCodeAsync(Codes.NormalizeCode(code));
        if (r is null || r.OrgId != actor.OfficerOrgId) throw new SpeakException("Case not found.", 404);
        if (r.Status == ReportStatus.Closed) throw new SpeakException("This case is already closed.");
        note = (note ?? "").Trim();
        if (note.Length > 300) throw new SpeakException("Keep the note under 300 characters.");
        var org = await store.GetOrganisationAsync(r.OrgId!);
        var now = clock.GetUtcNow().UtcDateTime;
        await store.UpdateReportStatusAsync(r.Id, status, r.OrgId, now);
        await store.AddEventAsync(new ReportEvent { ReportId = r.Id, Kind = status.ToString(), Note = note.Length == 0 ? null : note, By = org?.Name ?? "Organisation", Utc = now });
    }

    /// <summary>Reports the reporter can act on: their own (survivor) or the tracked one (tracker).</summary>
    public async Task<Report> OwnedAsync(Actor actor, string code)
    {
        var r = await store.GetReportByCodeAsync(Codes.NormalizeCode(code));
        var mine = r is not null && ((actor.IsSurvivor && r.SurvivorId == actor.SurvivorId) || (actor.IsTracker && r.Id == actor.TrackedReportId));
        if (!mine) throw new SpeakException("Report not found.", 404);
        return r!;
    }

    public bool CanSee(Actor actor, Report r) =>
        (actor.IsSurvivor && r.SurvivorId == actor.SurvivorId) ||
        (actor.IsTracker && r.Id == actor.TrackedReportId) ||
        (actor.IsOfficer && r.OrgId == actor.OfficerOrgId);

    public string? ReadNarrative(Report r) => r.Narrative is null ? null : cipher.Unprotect(r.Narrative);

    public async Task<string> ReporterLabelAsync(Report r)
    {
        if (r.Identity == IdentityMode.ShareNickname && r.SurvivorId is { } sid && await store.GetSurvivorAsync(sid) is { } s) return s.Nickname;
        return $"Anonymous reporter ({r.Code})";
    }

    // ------------------------------------------------------------------ evidence

    public async Task<Evidence> AddEvidenceAsync(Actor actor, string code, string? fileName, string? contentType, byte[] data)
    {
        var r = await OwnedAsync(actor, code);
        if (r.Status == ReportStatus.Closed) throw new SpeakException("This report is closed.");
        if (data.Length == 0) throw new SpeakException("The file is empty.");
        if (data.Length > MaxEvidenceBytes) throw new SpeakException("Each file must be 10 MB or smaller.");
        var type = (contentType ?? "").ToLowerInvariant();
        if (!EvidenceTypes.Contains(type)) throw new SpeakException("That file type isn't supported. Use photos, PDFs, audio, MP4 video or text files.");
        var existing = await store.GetEvidenceForReportAsync(r.Id);
        if (existing.Count >= MaxEvidencePerReport) throw new SpeakException($"A report can have up to {MaxEvidencePerReport} files.");

        var name = Path.GetFileName(fileName ?? "file");
        if (name.Length > 120) name = name[^120..];
        var e = new Evidence
        {
            ReportId = r.Id, FileName = name.Length == 0 ? "file" : name, ContentType = type, Size = data.Length,
            StoredName = Codes.StoredFileName(), Utc = clock.GetUtcNow().UtcDateTime,
        };
        await files.SaveAsync(e.StoredName, cipher.Protect(data));
        await store.InsertEvidenceAsync(e);
        await store.AddEventAsync(new ReportEvent { ReportId = r.Id, Kind = "Evidence", Note = "File added", By = "Reporter", Utc = e.Utc });
        return e;
    }

    public async Task<EvidenceFile> ReadEvidenceAsync(Actor actor, long evidenceId)
    {
        var e = await store.GetEvidenceAsync(evidenceId);
        var r = e is null ? null : await store.GetReportAsync(e.ReportId);
        if (e is null || r is null || !CanSee(actor, r)) throw new SpeakException("File not found.", 404);
        var bytes = await files.ReadAsync(e.StoredName) ?? throw new SpeakException("File not found.", 404);
        return new EvidenceFile(e, cipher.Unprotect(bytes));
    }

    private static string Pick(string? value, string[] allowed, string field)
    {
        if (string.IsNullOrEmpty(value)) return allowed[0];
        if (!allowed.Contains(value)) throw new SpeakException($"{field}: choose an option from the list.");
        return value;
    }
}

// ====================================================================== conversations

public sealed record ConversationSummary(long Id, string OrgId, string OrgName, string OrgInitials, string OrgColor, string With,
    string? ReportCode, string LastText, DateTime LastUtc, int Unread);

public sealed record ChatMessage(long Id, Sender From, string Who, string Text, DateTime Utc);

public sealed class ChatService(ISpeakStore store, ICipher cipher, TimeProvider clock)
{
    public const int MaxMessage = 2000;

    /// <summary>Opens (or reuses) a general conversation between a signed-in survivor and an organisation.</summary>
    public async Task<Conversation> StartAsync(Actor actor, string? orgId)
    {
        if (!actor.IsSurvivor) throw new SpeakException("Sign in to message an organisation.", 401);
        var org = string.IsNullOrEmpty(orgId) ? null : await store.GetOrganisationAsync(orgId);
        if (org is null || !org.IsActive) throw new SpeakException("That organisation is not available.");
        var existing = await store.FindConversationAsync(actor.SurvivorId, null, org.Id);
        if (existing is not null) return existing;
        var now = clock.GetUtcNow().UtcDateTime;
        var c = new Conversation { SurvivorId = actor.SurvivorId, OrgId = org.Id, CreatedUtc = now, LastUtc = now, SurvivorReadUtc = now, OrgReadUtc = DateTime.MinValue };
        await store.InsertConversationAsync(c);
        await store.InsertMessageAsync(new Message { ConversationId = c.Id, From = Sender.System, Body = cipher.Protect($"Private conversation with {org.Name}. They see you as {actor.Name}."), Utc = now });
        return c;
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(Actor actor)
    {
        IReadOnlyList<Conversation> list = actor switch
        {
            { IsSurvivor: true } => await store.GetConversationsForSurvivorAsync(actor.SurvivorId),
            { IsTracker: true } => await store.GetConversationsForReportAsync(actor.TrackedReportId),
            { IsOfficer: true } => await store.GetConversationsForOrgAsync(actor.OfficerOrgId),
            _ => throw new SpeakException("Sign in first.", 401),
        };
        var orgs = (await store.GetOrganisationsAsync()).ToDictionary(o => o.Id);
        var result = new List<ConversationSummary>();
        foreach (var c in list)
        {
            var org = orgs.GetValueOrDefault(c.OrgId);
            var last = await store.GetLastMessageAsync(c.Id);
            var bySurvivorSide = !actor.IsOfficer;
            var unread = await store.CountUnreadAsync(c.Id, bySurvivorSide ? Sender.Survivor : Sender.Officer, bySurvivorSide ? c.SurvivorReadUtc : c.OrgReadUtc);
            var report = c.ReportId is { } rid ? await store.GetReportAsync(rid) : null;
            result.Add(new ConversationSummary(c.Id, c.OrgId, org?.Name ?? c.OrgId, org?.Initials ?? "?", org?.Color ?? "#0b7a4b",
                await WithLabelAsync(actor, c, report), report?.Code,
                last is null ? "" : cipher.Unprotect(last.Body), c.LastUtc, unread));
        }
        return result;
    }

    public async Task<(Conversation Conversation, IReadOnlyList<ChatMessage> Messages)> MessagesAsync(Actor actor, long conversationId, long afterId)
    {
        var c = await AccessAsync(actor, conversationId);
        var msgs = await store.GetMessagesAsync(c.Id, afterId);
        await store.MarkReadAsync(c.Id, !actor.IsOfficer, clock.GetUtcNow().UtcDateTime);
        var officers = (await store.GetOfficersAsync(c.OrgId)).ToDictionary(o => o.Id, o => o.Name);
        var report = c.ReportId is { } rid ? await store.GetReportAsync(rid) : null;
        var survivorLabel = actor.IsOfficer ? await WithLabelAsync(actor, c, report) : "You";
        return (c, msgs.Select(m => new ChatMessage(m.Id, m.From,
            m.From switch
            {
                Sender.Officer => m.OfficerId is { } oid && officers.TryGetValue(oid, out var n) ? n : "Case officer",
                Sender.Survivor => survivorLabel,
                _ => "SpeakSafe",
            },
            cipher.Unprotect(m.Body), m.Utc)).ToList());
    }

    public async Task<ChatMessage> SendAsync(Actor actor, long conversationId, string? text)
    {
        var c = await AccessAsync(actor, conversationId);
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new SpeakException("Write a message first.");
        if (text.Length > MaxMessage) throw new SpeakException($"Keep messages under {MaxMessage} characters.");
        var report = c.ReportId is { } rid ? await store.GetReportAsync(rid) : null;
        if (report?.Status == ReportStatus.Closed && actor.IsOfficer) throw new SpeakException("This case is closed.");

        var now = clock.GetUtcNow().UtcDateTime;
        var m = new Message
        {
            ConversationId = c.Id, From = actor.IsOfficer ? Sender.Officer : Sender.Survivor, OfficerId = actor.IsOfficer ? actor.OfficerId : null,
            Body = cipher.Protect(text), Utc = now,
        };
        await store.InsertMessageAsync(m);
        await store.MarkReadAsync(c.Id, !actor.IsOfficer, now);

        // First reply from the organisation moves a referred report forward.
        if (actor.IsOfficer && report is { Status: ReportStatus.Referred })
        {
            var org = await store.GetOrganisationAsync(c.OrgId);
            await store.UpdateReportStatusAsync(report.Id, ReportStatus.InProgress, report.OrgId, now);
            await store.AddEventAsync(new ReportEvent { ReportId = report.Id, Kind = "InProgress", Note = "Organisation replied", By = org?.Name ?? "Organisation", Utc = now });
        }
        return new ChatMessage(m.Id, m.From, actor.IsOfficer ? actor.Name : "You", text, now);
    }

    private async Task<Conversation> AccessAsync(Actor actor, long id)
    {
        var c = await store.GetConversationAsync(id);
        var ok = c is not null && (
            (actor.IsSurvivor && c.SurvivorId == actor.SurvivorId) ||
            (actor.IsTracker && c.ReportId is not null && c.ReportId == actor.TrackedReportId) ||
            (actor.IsOfficer && c.OrgId == actor.OfficerOrgId));
        if (!ok) throw new SpeakException("Conversation not found.", 404);
        return c!;
    }

    private async Task<string> WithLabelAsync(Actor actor, Conversation c, Report? report)
    {
        if (!actor.IsOfficer) return (await store.GetOrganisationAsync(c.OrgId))?.Name ?? c.OrgId;
        if (report is not null && report.Identity == IdentityMode.Anonymous) return $"Anonymous reporter ({report.Code})";
        if (c.SurvivorId is { } sid && await store.GetSurvivorAsync(sid) is { } s) return s.Nickname;
        return report is null ? "Reporter" : $"Anonymous reporter ({report.Code})";
    }
}

// ====================================================================== organisations + coordinator

public sealed record OrgInput(string? Id, string? Name, string? Initials, string? Color, List<string>? Services, List<string>? States,
    string? Description, string? ResponseNote, string? Languages, bool IsActive);

public sealed record Overview(int Reports, int Urgent, Dictionary<string, int> ByStatus, Dictionary<string, int> ByType,
    Dictionary<string, int> ByPlace, Dictionary<string, int> ByState, IReadOnlyList<OrgLoad> Organisations);

public sealed record OrgLoad(Organisation Org, int Officers, int OpenCases, int ClosedCases);

public sealed partial class OrgService(ISpeakStore store)
{
    [GeneratedRegex("^[a-z0-9-]{2,20}$")]
    private static partial Regex IdRule();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRule();

    public async Task<IReadOnlyList<Organisation>> PublicListAsync(string? service, string? state)
    {
        var all = await store.GetOrganisationsAsync();
        return all.Where(o => o.IsActive && (string.IsNullOrEmpty(service) || o.Services.Contains(service)) && o.Covers(state)).ToList();
    }

    /// <summary>Aggregates only. The coordinator never sees narratives, messages or who reported.</summary>
    public async Task<Overview> OverviewAsync()
    {
        var reports = await store.GetAllReportsAsync();
        var orgs = await store.GetOrganisationsAsync();
        var officers = await store.GetOfficersAsync(null);
        var loads = orgs.Select(o => new OrgLoad(o,
            officers.Count(x => x.OrgId == o.Id && x.IsActive),
            reports.Count(r => r.OrgId == o.Id && r.Status != ReportStatus.Closed),
            reports.Count(r => r.OrgId == o.Id && r.Status == ReportStatus.Closed))).ToList();
        return new Overview(
            reports.Count, reports.Count(r => r.Urgent && r.Status != ReportStatus.Closed),
            Enum.GetValues<ReportStatus>().ToDictionary(s => s.ToString(), s => reports.Count(r => r.Status == s)),
            Catalog.Types.ToDictionary(t => t.Label, t => reports.Count(r => r.Types.Contains(t.Id))),
            Catalog.Places.ToDictionary(p => p.Label, p => reports.Count(r => r.Place == p.Id)),
            reports.Where(r => r.State is not null).GroupBy(r => r.State!).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count()),
            loads);
    }

    public async Task<Organisation> SaveAsync(OrgInput i)
    {
        var id = (i.Id ?? "").Trim().ToLowerInvariant();
        if (!IdRule().IsMatch(id)) throw new SpeakException("Short ID must be 2 to 20 lowercase letters, numbers or dashes.");
        var name = (i.Name ?? "").Trim();
        if (name.Length is < 3 or > 150) throw new SpeakException("Name must be 3 to 150 characters.");
        var services = (i.Services ?? []).Distinct().ToList();
        if (services.Count == 0 || services.Any(s => !Catalog.Services.Contains(s))) throw new SpeakException("Pick at least one service from the list.");
        var states = (i.States ?? []).Distinct().ToList();
        if (states.Count == 0 || states.Any(s => s != Catalog.AllStates && !Catalog.States.Contains(s))) throw new SpeakException("Pick at least one state, or All states.");
        var color = string.IsNullOrEmpty(i.Color) ? "#0b7a4b" : i.Color;
        if (!ColorRule().IsMatch(color)) throw new SpeakException("Colour must look like #0b7a4b.");
        var initials = (i.Initials ?? "").Trim().ToUpperInvariant();
        if (initials.Length is 0 or > 3) initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

        var existing = await store.GetOrganisationAsync(id);
        var o = new Organisation
        {
            Id = id, Name = name, Initials = initials, Color = color, Services = services, States = states,
            Description = Trim(i.Description, 500), ResponseNote = Trim(i.ResponseNote, 80), Languages = Trim(i.Languages, 120),
            IsActive = i.IsActive, IsSample = existing?.IsSample ?? false,
        };
        await store.SaveOrganisationAsync(o);
        return o;
    }

    /// <summary>Creates a case officer. Returns the officer and a temporary passphrase shown once.</summary>
    public async Task<(Officer Officer, string Passphrase)> AddOfficerAsync(string orgId, string? name, string? username)
    {
        var org = await store.GetOrganisationAsync(orgId) ?? throw new SpeakException("Organisation not found.", 404);
        name = (name ?? "").Trim();
        if (name.Length is < 2 or > 100) throw new SpeakException("Enter the officer's name.");
        username = (username ?? "").Trim().ToLowerInvariant();
        if (!AccountService.ValidUsername(username)) throw new SpeakException("Username must be 3 to 40 lowercase letters, numbers, dots, dashes or underscores.");
        var pass = Codes.Random(4) + "-" + Codes.Random(4) + "-" + Codes.Random(4);
        var o = new Officer { OrgId = org.Id, Name = name, Username = username, PassHash = Hasher.Hash(pass), IsActive = true };
        try { await store.InsertOfficerAsync(o); }
        catch (DuplicateException) { throw new SpeakException("That username is taken.", 409); }
        return (o, pass);
    }

    private static string Trim(string? s, int max) { s = (s ?? "").Trim(); return s.Length > max ? s[..max] : s; }
}
