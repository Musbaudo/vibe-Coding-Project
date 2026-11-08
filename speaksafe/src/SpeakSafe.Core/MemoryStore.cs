namespace SpeakSafe.Core;

/// <summary>In-process store for tests and `Store=Memory`. Returns copies so callers can't mutate stored state.</summary>
public sealed class MemoryStore : ISpeakStore
{
    private readonly Lock _gate = new();
    private readonly List<Survivor> _survivors = [];
    private readonly List<Organisation> _orgs = [];
    private readonly List<Officer> _officers = [];
    private readonly List<Report> _reports = [];
    private readonly List<ReportEvent> _events = [];
    private readonly List<Evidence> _evidence = [];
    private readonly List<Conversation> _convs = [];
    private readonly List<Message> _msgs = [];
    private int _survivorSeq, _officerSeq;
    private long _reportSeq, _eventSeq, _evidenceSeq, _convSeq, _msgSeq;

    public MemoryStore(SeedSet? seed = null)
    {
        if (seed is not null) Load(seed);
    }

    private void Load(SeedSet seed)
    {
        foreach (var o in seed.Organisations) _orgs.Add(Copy(o));
        foreach (var s in seed.Survivors) { s.Id = ++_survivorSeq; _survivors.Add(Copy(s)); }
        foreach (var o in seed.Officers) { o.Id = ++_officerSeq; _officers.Add(Copy(o)); }
        foreach (var sr in seed.Reports)
        {
            var r = Copy(sr.Report);
            r.Id = ++_reportSeq;
            r.SurvivorId = sr.SurvivorNickname is null ? null : _survivors.First(x => x.Nickname == sr.SurvivorNickname).Id;
            _reports.Add(r);
            foreach (var e in sr.Events) { var ce = Copy(e); ce.Id = ++_eventSeq; ce.ReportId = r.Id; _events.Add(ce); }
            if (sr.Conversation is { } conv)
            {
                var c = Copy(conv.Conversation);
                c.Id = ++_convSeq; c.ReportId = r.Id; c.SurvivorId = r.SurvivorId;
                _convs.Add(c);
                foreach (var m in conv.Messages)
                {
                    var cm = Copy(m);
                    cm.Id = ++_msgSeq; cm.ConversationId = c.Id;
                    if (cm.From == Sender.Officer) cm.OfficerId = _officers.First(o => o.OrgId == c.OrgId).Id;
                    _msgs.Add(cm);
                }
            }
        }
    }

    // ------------------------------------------------------------------ survivors

    public Task InsertSurvivorAsync(Survivor s)
    {
        lock (_gate)
        {
            if (_survivors.Any(x => x.Nickname.Equals(s.Nickname, StringComparison.OrdinalIgnoreCase))) throw new DuplicateException();
            s.Id = ++_survivorSeq;
            _survivors.Add(Copy(s));
        }
        return Task.CompletedTask;
    }

    public Task<Survivor?> GetSurvivorAsync(int id) { lock (_gate) return Task.FromResult(CopyN(_survivors.FirstOrDefault(s => s.Id == id))); }

    public Task<Survivor?> GetSurvivorByNicknameAsync(string nickname)
    {
        lock (_gate) return Task.FromResult(CopyN(_survivors.FirstOrDefault(s => s.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase))));
    }

    public Task UpdateSurvivorSecretsAsync(int id, string passHash, string recoveryHash)
    {
        lock (_gate)
        {
            var s = _survivors.First(x => x.Id == id);
            s.PassHash = passHash; s.RecoveryHash = recoveryHash;
        }
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ organisations + officers

    public Task<IReadOnlyList<Organisation>> GetOrganisationsAsync()
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Organisation>>(_orgs.OrderBy(o => o.Name).Select(Copy).ToList());
    }

    public Task<Organisation?> GetOrganisationAsync(string id) { lock (_gate) return Task.FromResult(CopyN(_orgs.FirstOrDefault(o => o.Id == id))); }

    public Task SaveOrganisationAsync(Organisation o)
    {
        lock (_gate)
        {
            _orgs.RemoveAll(x => x.Id == o.Id);
            _orgs.Add(Copy(o));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Officer>> GetOfficersAsync(string? orgId)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Officer>>(_officers.Where(o => orgId is null || o.OrgId == orgId).OrderBy(o => o.Name).Select(Copy).ToList());
    }

    public Task<Officer?> GetOfficerAsync(int id) { lock (_gate) return Task.FromResult(CopyN(_officers.FirstOrDefault(o => o.Id == id))); }

    public Task<Officer?> GetOfficerByUsernameAsync(string username)
    {
        lock (_gate) return Task.FromResult(CopyN(_officers.FirstOrDefault(o => o.Username.Equals(username, StringComparison.OrdinalIgnoreCase))));
    }

    public Task InsertOfficerAsync(Officer o)
    {
        lock (_gate)
        {
            if (_officers.Any(x => x.Username.Equals(o.Username, StringComparison.OrdinalIgnoreCase))) throw new DuplicateException();
            o.Id = ++_officerSeq;
            _officers.Add(Copy(o));
        }
        return Task.CompletedTask;
    }

    public Task SetOfficerActiveAsync(int id, bool active)
    {
        lock (_gate) { var o = _officers.FirstOrDefault(x => x.Id == id); if (o is not null) o.IsActive = active; }
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ reports

    public Task InsertReportAsync(Report r)
    {
        lock (_gate)
        {
            if (_reports.Any(x => x.Code == r.Code)) throw new DuplicateException();
            r.Id = ++_reportSeq;
            _reports.Add(Copy(r));
        }
        return Task.CompletedTask;
    }

    public Task<Report?> GetReportAsync(long id) { lock (_gate) return Task.FromResult(CopyN(_reports.FirstOrDefault(r => r.Id == id))); }
    public Task<Report?> GetReportByCodeAsync(string code) { lock (_gate) return Task.FromResult(CopyN(_reports.FirstOrDefault(r => r.Code == code))); }

    public Task<IReadOnlyList<Report>> GetReportsForSurvivorAsync(int survivorId) => Reports(r => r.SurvivorId == survivorId);
    public Task<IReadOnlyList<Report>> GetReportsForOrgAsync(string orgId) => Reports(r => r.OrgId == orgId);
    public Task<IReadOnlyList<Report>> GetAllReportsAsync() => Reports(_ => true);

    private Task<IReadOnlyList<Report>> Reports(Func<Report, bool> where)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Report>>(_reports.Where(where).OrderByDescending(r => r.CreatedUtc).Select(Copy).ToList());
    }

    public Task UpdateReportStatusAsync(long id, ReportStatus status, string? orgId, DateTime utc)
    {
        lock (_gate)
        {
            var r = _reports.First(x => x.Id == id);
            r.Status = status; r.OrgId = orgId; r.UpdatedUtc = utc;
        }
        return Task.CompletedTask;
    }

    public Task AddEventAsync(ReportEvent e) { lock (_gate) { e.Id = ++_eventSeq; _events.Add(Copy(e)); } return Task.CompletedTask; }

    public Task<IReadOnlyList<ReportEvent>> GetEventsAsync(long reportId)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<ReportEvent>>(_events.Where(e => e.ReportId == reportId).OrderBy(e => e.Utc).ThenBy(e => e.Id).Select(Copy).ToList());
    }

    // ------------------------------------------------------------------ evidence

    public Task InsertEvidenceAsync(Evidence e) { lock (_gate) { e.Id = ++_evidenceSeq; _evidence.Add(Copy(e)); } return Task.CompletedTask; }
    public Task<Evidence?> GetEvidenceAsync(long id) { lock (_gate) return Task.FromResult(CopyN(_evidence.FirstOrDefault(e => e.Id == id))); }

    public Task<IReadOnlyList<Evidence>> GetEvidenceForReportAsync(long reportId)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Evidence>>(_evidence.Where(e => e.ReportId == reportId).OrderBy(e => e.Id).Select(Copy).ToList());
    }

    // ------------------------------------------------------------------ conversations

    public Task InsertConversationAsync(Conversation c) { lock (_gate) { c.Id = ++_convSeq; _convs.Add(Copy(c)); } return Task.CompletedTask; }
    public Task<Conversation?> GetConversationAsync(long id) { lock (_gate) return Task.FromResult(CopyN(_convs.FirstOrDefault(c => c.Id == id))); }

    public Task<Conversation?> FindConversationAsync(int? survivorId, long? reportId, string orgId)
    {
        lock (_gate) return Task.FromResult(CopyN(_convs.FirstOrDefault(c => c.SurvivorId == survivorId && c.ReportId == reportId && c.OrgId == orgId)));
    }

    public Task<IReadOnlyList<Conversation>> GetConversationsForSurvivorAsync(int survivorId) => Convs(c => c.SurvivorId == survivorId);
    public Task<IReadOnlyList<Conversation>> GetConversationsForReportAsync(long reportId) => Convs(c => c.ReportId == reportId);
    public Task<IReadOnlyList<Conversation>> GetConversationsForOrgAsync(string orgId) => Convs(c => c.OrgId == orgId);

    private Task<IReadOnlyList<Conversation>> Convs(Func<Conversation, bool> where)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Conversation>>(_convs.Where(where).OrderByDescending(c => c.LastUtc).Select(Copy).ToList());
    }

    public Task InsertMessageAsync(Message m)
    {
        lock (_gate)
        {
            m.Id = ++_msgSeq;
            _msgs.Add(Copy(m));
            var c = _convs.First(x => x.Id == m.ConversationId);
            if (m.Utc > c.LastUtc) c.LastUtc = m.Utc;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Message>> GetMessagesAsync(long conversationId, long afterId)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<Message>>(_msgs.Where(m => m.ConversationId == conversationId && m.Id > afterId).OrderBy(m => m.Id).Select(Copy).ToList());
    }

    public Task<Message?> GetLastMessageAsync(long conversationId)
    {
        lock (_gate) return Task.FromResult(CopyN(_msgs.Where(m => m.ConversationId == conversationId).OrderByDescending(m => m.Id).FirstOrDefault()));
    }

    public Task<int> CountUnreadAsync(long conversationId, Sender notFrom, DateTime sinceUtc)
    {
        lock (_gate) return Task.FromResult(_msgs.Count(m => m.ConversationId == conversationId && m.From != notFrom && m.From != Sender.System && m.Utc > sinceUtc));
    }

    public Task MarkReadAsync(long conversationId, bool bySurvivor, DateTime utc)
    {
        lock (_gate)
        {
            var c = _convs.FirstOrDefault(x => x.Id == conversationId);
            if (c is not null) { if (bySurvivor) c.SurvivorReadUtc = utc; else c.OrgReadUtc = utc; }
        }
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ copies

    private static Survivor? CopyN(Survivor? x) => x is null ? null : Copy(x);
    private static Organisation? CopyN(Organisation? x) => x is null ? null : Copy(x);
    private static Officer? CopyN(Officer? x) => x is null ? null : Copy(x);
    private static Report? CopyN(Report? x) => x is null ? null : Copy(x);
    private static Evidence? CopyN(Evidence? x) => x is null ? null : Copy(x);
    private static Conversation? CopyN(Conversation? x) => x is null ? null : Copy(x);
    private static Message? CopyN(Message? x) => x is null ? null : Copy(x);

    private static Survivor Copy(Survivor s) => new()
        { Id = s.Id, Nickname = s.Nickname, PassHash = s.PassHash, RecoveryHash = s.RecoveryHash, Email = s.Email, CreatedUtc = s.CreatedUtc, IsSample = s.IsSample };
    private static Organisation Copy(Organisation o) => new()
    {
        Id = o.Id, Name = o.Name, Initials = o.Initials, Color = o.Color, Services = [.. o.Services], States = [.. o.States],
        Description = o.Description, ResponseNote = o.ResponseNote, Languages = o.Languages, IsActive = o.IsActive, IsSample = o.IsSample,
    };
    private static Officer Copy(Officer o) => new()
        { Id = o.Id, OrgId = o.OrgId, Name = o.Name, Username = o.Username, PassHash = o.PassHash, IsActive = o.IsActive, IsSample = o.IsSample };
    private static Report Copy(Report r) => new()
    {
        Id = r.Id, Code = r.Code, PinHash = r.PinHash, SurvivorId = r.SurvivorId, Types = [.. r.Types], Place = r.Place, State = r.State,
        IncidentDate = r.IncidentDate, Frequency = r.Frequency, Responsible = r.Responsible, Narrative = r.Narrative, Witnesses = r.Witnesses,
        Support = [.. r.Support], Identity = r.Identity, OrgId = r.OrgId, Status = r.Status, Urgent = r.Urgent, CreatedUtc = r.CreatedUtc,
        UpdatedUtc = r.UpdatedUtc, IsSample = r.IsSample,
    };
    private static ReportEvent Copy(ReportEvent e) => new() { Id = e.Id, ReportId = e.ReportId, Kind = e.Kind, Note = e.Note, By = e.By, Utc = e.Utc };
    private static Evidence Copy(Evidence e) => new()
        { Id = e.Id, ReportId = e.ReportId, FileName = e.FileName, ContentType = e.ContentType, Size = e.Size, StoredName = e.StoredName, Utc = e.Utc };
    private static Conversation Copy(Conversation c) => new()
    {
        Id = c.Id, SurvivorId = c.SurvivorId, ReportId = c.ReportId, OrgId = c.OrgId, CreatedUtc = c.CreatedUtc, LastUtc = c.LastUtc,
        SurvivorReadUtc = c.SurvivorReadUtc, OrgReadUtc = c.OrgReadUtc, IsSample = c.IsSample,
    };
    private static Message Copy(Message m) => new()
        { Id = m.Id, ConversationId = m.ConversationId, From = m.From, OfficerId = m.OfficerId, Body = m.Body, Utc = m.Utc };
}
