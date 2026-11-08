using System.Data;
using System.Data.Common;
using System.Text.Json;
using SpeakSafe.Core;
using static SpeakSafe.Web.Data.Db;

namespace SpeakSafe.Web.Data;

public sealed class SqlStore(Db db) : ISpeakStore
{
    private static string J(List<string> list) => JsonSerializer.Serialize(list);
    private static List<string> L(string? json) => string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    /// <summary>SQL Server unique-index violations (2601, 2627), read by reflection so this compiles without SqlClient.</summary>
    private static bool IsDuplicate(DbException ex) =>
        ex.GetType().GetProperty("Number")?.GetValue(ex) is int n && (n == 2601 || n == 2627);

    private async Task<long> InsertAsync(string sql, Action<DbCommand> bind, DbConnection? conn = null, DbTransaction? tx = null)
    {
        DbConnection? owned = null;
        var c = conn ?? (owned = await db.OpenAsync());
        try
        {
            await using var cmd = Cmd(c, sql + " SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", tx);
            bind(cmd);
            try { return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
            catch (DbException ex) when (IsDuplicate(ex)) { throw new DuplicateException(); }
        }
        finally { if (owned is not null) await owned.DisposeAsync(); }
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<DbDataReader, T> map, params (string n, object? v)[] ps)
    {
        await using var c = await db.OpenAsync();
        await using var cmd = Cmd(c, sql);
        foreach (var (n, v) in ps) P(cmd, n, v);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await r.ReadAsync()) list.Add(map(r));
        return list;
    }

    private async Task ExecAsync(string sql, params (string n, object? v)[] ps)
    {
        await using var c = await db.OpenAsync();
        await using var cmd = Cmd(c, sql);
        foreach (var (n, v) in ps) P(cmd, n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------ survivors

    private const string SurvivorCols = "Id, Nickname, PassHash, RecoveryHash, Email, IsSample, CreatedUtc";

    private static Survivor MapSurvivor(DbDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")), Nickname = Str(r, "Nickname")!, PassHash = Str(r, "PassHash")!, RecoveryHash = Str(r, "RecoveryHash")!,
        Email = Str(r, "Email"), IsSample = r.GetBoolean(r.GetOrdinal("IsSample")), CreatedUtc = Utc(r, "CreatedUtc"),
    };

    public async Task InsertSurvivorAsync(Survivor s) => s.Id = (int)await InsertAsync(
        "INSERT INTO dbo.Survivors (Nickname, PassHash, RecoveryHash, Email, IsSample, CreatedUtc) VALUES (@n, @p, @r, @e, @s, @c);",
        cmd => { P(cmd, "@n", s.Nickname); P(cmd, "@p", s.PassHash); P(cmd, "@r", s.RecoveryHash); P(cmd, "@e", s.Email); P(cmd, "@s", s.IsSample); P(cmd, "@c", s.CreatedUtc); });

    public async Task<Survivor?> GetSurvivorAsync(int id) =>
        (await QueryAsync($"SELECT {SurvivorCols} FROM dbo.Survivors WHERE Id = @id", MapSurvivor, ("@id", id))).FirstOrDefault();

    public async Task<Survivor?> GetSurvivorByNicknameAsync(string nickname) =>
        (await QueryAsync($"SELECT {SurvivorCols} FROM dbo.Survivors WHERE Nickname = @n", MapSurvivor, ("@n", nickname))).FirstOrDefault();

    public Task UpdateSurvivorSecretsAsync(int id, string passHash, string recoveryHash) =>
        ExecAsync("UPDATE dbo.Survivors SET PassHash = @p, RecoveryHash = @r WHERE Id = @id;", ("@p", passHash), ("@r", recoveryHash), ("@id", id));

    // ------------------------------------------------------------------ organisations + officers

    private const string OrgCols = "Id, Name, Initials, Color, ServicesJson, StatesJson, Description, ResponseNote, Languages, IsActive, IsSample";

    private static Organisation MapOrg(DbDataReader r) => new()
    {
        Id = Str(r, "Id")!, Name = Str(r, "Name")!, Initials = Str(r, "Initials")!, Color = Str(r, "Color")!,
        Services = L(Str(r, "ServicesJson")), States = L(Str(r, "StatesJson")), Description = Str(r, "Description")!,
        ResponseNote = Str(r, "ResponseNote")!, Languages = Str(r, "Languages")!,
        IsActive = r.GetBoolean(r.GetOrdinal("IsActive")), IsSample = r.GetBoolean(r.GetOrdinal("IsSample")),
    };

    public async Task<IReadOnlyList<Organisation>> GetOrganisationsAsync() =>
        await QueryAsync($"SELECT {OrgCols} FROM dbo.Organisations ORDER BY Name", MapOrg);

    public async Task<Organisation?> GetOrganisationAsync(string id) =>
        (await QueryAsync($"SELECT {OrgCols} FROM dbo.Organisations WHERE Id = @id", MapOrg, ("@id", id))).FirstOrDefault();

    public Task SaveOrganisationAsync(Organisation o) => SaveOrganisationAsync(o, null, null);

    private async Task SaveOrganisationAsync(Organisation o, DbConnection? conn, DbTransaction? tx)
    {
        DbConnection? owned = null;
        var c = conn ?? (owned = await db.OpenAsync());
        try
        {
            await using var cmd = Cmd(c, """
                UPDATE dbo.Organisations SET Name = @name, Initials = @ini, Color = @color, ServicesJson = @svc, StatesJson = @st,
                    Description = @desc, ResponseNote = @resp, Languages = @lang, IsActive = @act, UpdatedUtc = SYSUTCDATETIME()
                WHERE Id = @id;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.Organisations (Id, Name, Initials, Color, ServicesJson, StatesJson, Description, ResponseNote, Languages, IsActive, IsSample)
                    VALUES (@id, @name, @ini, @color, @svc, @st, @desc, @resp, @lang, @act, @sample);
                """, tx);
            P(cmd, "@id", o.Id); P(cmd, "@name", o.Name); P(cmd, "@ini", o.Initials); P(cmd, "@color", o.Color); P(cmd, "@svc", J(o.Services));
            P(cmd, "@st", J(o.States)); P(cmd, "@desc", o.Description); P(cmd, "@resp", o.ResponseNote); P(cmd, "@lang", o.Languages);
            P(cmd, "@act", o.IsActive); P(cmd, "@sample", o.IsSample);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { if (owned is not null) await owned.DisposeAsync(); }
    }

    private const string OfficerCols = "Id, OrgId, Name, Username, PassHash, IsActive, IsSample";

    private static Officer MapOfficer(DbDataReader r) => new()
    {
        Id = r.GetInt32(r.GetOrdinal("Id")), OrgId = Str(r, "OrgId")!, Name = Str(r, "Name")!, Username = Str(r, "Username")!,
        PassHash = Str(r, "PassHash")!, IsActive = r.GetBoolean(r.GetOrdinal("IsActive")), IsSample = r.GetBoolean(r.GetOrdinal("IsSample")),
    };

    public async Task<IReadOnlyList<Officer>> GetOfficersAsync(string? orgId) => orgId is null
        ? await QueryAsync($"SELECT {OfficerCols} FROM dbo.Officers ORDER BY Name", MapOfficer)
        : await QueryAsync($"SELECT {OfficerCols} FROM dbo.Officers WHERE OrgId = @o ORDER BY Name", MapOfficer, ("@o", orgId));

    public async Task<Officer?> GetOfficerAsync(int id) =>
        (await QueryAsync($"SELECT {OfficerCols} FROM dbo.Officers WHERE Id = @id", MapOfficer, ("@id", id))).FirstOrDefault();

    public async Task<Officer?> GetOfficerByUsernameAsync(string username) =>
        (await QueryAsync($"SELECT {OfficerCols} FROM dbo.Officers WHERE Username = @u", MapOfficer, ("@u", username))).FirstOrDefault();

    public Task InsertOfficerAsync(Officer o) => InsertOfficerAsync(o, null, null);

    private async Task InsertOfficerAsync(Officer o, DbConnection? c, DbTransaction? tx) => o.Id = (int)await InsertAsync(
        "INSERT INTO dbo.Officers (OrgId, Name, Username, PassHash, IsActive, IsSample) VALUES (@o, @n, @u, @p, @a, @s);",
        cmd => { P(cmd, "@o", o.OrgId); P(cmd, "@n", o.Name); P(cmd, "@u", o.Username); P(cmd, "@p", o.PassHash); P(cmd, "@a", o.IsActive); P(cmd, "@s", o.IsSample); },
        c, tx);

    public Task SetOfficerActiveAsync(int id, bool active) =>
        ExecAsync("UPDATE dbo.Officers SET IsActive = @a WHERE Id = @id;", ("@a", active), ("@id", id));

    // ------------------------------------------------------------------ reports

    private const string ReportCols = """
        Id, Code, PinHash, SurvivorId, TypesJson, Place, State, IncidentDate, Frequency, Responsible, Narrative, Witnesses, SupportJson,
        IdentityMode, OrgId, Status, Urgent, IsSample, CreatedUtc, UpdatedUtc
        """;

    private static Report MapReport(DbDataReader r)
    {
        var di = r.GetOrdinal("IncidentDate");
        return new Report
        {
            Id = r.GetInt64(r.GetOrdinal("Id")), Code = Str(r, "Code")!, PinHash = Str(r, "PinHash")!, SurvivorId = IntN(r, "SurvivorId"),
            Types = L(Str(r, "TypesJson")), Place = Str(r, "Place")!, State = Str(r, "State"),
            IncidentDate = r.IsDBNull(di) ? null : DateOnly.FromDateTime(r.GetDateTime(di)),
            Frequency = Str(r, "Frequency")!, Responsible = Str(r, "Responsible")!, Narrative = Str(r, "Narrative"), Witnesses = Str(r, "Witnesses")!,
            Support = L(Str(r, "SupportJson")), Identity = Enum.Parse<IdentityMode>(Str(r, "IdentityMode")!), OrgId = Str(r, "OrgId"),
            Status = Enum.Parse<ReportStatus>(Str(r, "Status")!), Urgent = r.GetBoolean(r.GetOrdinal("Urgent")),
            IsSample = r.GetBoolean(r.GetOrdinal("IsSample")), CreatedUtc = Utc(r, "CreatedUtc"), UpdatedUtc = Utc(r, "UpdatedUtc"),
        };
    }

    public Task InsertReportAsync(Report r) => InsertReportAsync(r, null, null);

    private async Task InsertReportAsync(Report r, DbConnection? c, DbTransaction? tx) => r.Id = await InsertAsync("""
        INSERT INTO dbo.Reports (Code, PinHash, SurvivorId, TypesJson, Place, State, IncidentDate, Frequency, Responsible, Narrative, Witnesses,
            SupportJson, IdentityMode, OrgId, Status, Urgent, IsSample, CreatedUtc, UpdatedUtc)
        VALUES (@code, @pin, @sid, @types, @place, @state, @date, @freq, @resp, @narr, @wit, @sup, @ident, @org, @status, @urgent, @sample, @created, @updated);
        """, cmd =>
        {
            P(cmd, "@code", r.Code); P(cmd, "@pin", r.PinHash); P(cmd, "@sid", r.SurvivorId, DbType.Int32); P(cmd, "@types", J(r.Types));
            P(cmd, "@place", r.Place); P(cmd, "@state", r.State, DbType.String);
            P(cmd, "@date", r.IncidentDate?.ToDateTime(TimeOnly.MinValue), DbType.Date);
            P(cmd, "@freq", r.Frequency); P(cmd, "@resp", r.Responsible); P(cmd, "@narr", r.Narrative, DbType.String); P(cmd, "@wit", r.Witnesses);
            P(cmd, "@sup", J(r.Support)); P(cmd, "@ident", r.Identity.ToString()); P(cmd, "@org", r.OrgId, DbType.String);
            P(cmd, "@status", r.Status.ToString()); P(cmd, "@urgent", r.Urgent); P(cmd, "@sample", r.IsSample);
            P(cmd, "@created", r.CreatedUtc); P(cmd, "@updated", r.UpdatedUtc);
        }, c, tx);

    public async Task<Report?> GetReportAsync(long id) =>
        (await QueryAsync($"SELECT {ReportCols} FROM dbo.Reports WHERE Id = @id", MapReport, ("@id", id))).FirstOrDefault();

    public async Task<Report?> GetReportByCodeAsync(string code) =>
        (await QueryAsync($"SELECT {ReportCols} FROM dbo.Reports WHERE Code = @c", MapReport, ("@c", code))).FirstOrDefault();

    public async Task<IReadOnlyList<Report>> GetReportsForSurvivorAsync(int survivorId) =>
        await QueryAsync($"SELECT {ReportCols} FROM dbo.Reports WHERE SurvivorId = @s ORDER BY CreatedUtc DESC", MapReport, ("@s", survivorId));

    public async Task<IReadOnlyList<Report>> GetReportsForOrgAsync(string orgId) =>
        await QueryAsync($"SELECT {ReportCols} FROM dbo.Reports WHERE OrgId = @o ORDER BY CreatedUtc DESC", MapReport, ("@o", orgId));

    public async Task<IReadOnlyList<Report>> GetAllReportsAsync() =>
        await QueryAsync($"SELECT {ReportCols} FROM dbo.Reports ORDER BY CreatedUtc DESC", MapReport);

    public Task UpdateReportStatusAsync(long id, ReportStatus status, string? orgId, DateTime utc) =>
        ExecAsync("UPDATE dbo.Reports SET Status = @s, OrgId = @o, UpdatedUtc = @u WHERE Id = @id;",
            ("@s", status.ToString()), ("@o", orgId), ("@u", utc), ("@id", id));

    public Task AddEventAsync(ReportEvent e) => AddEventAsync(e, null, null);

    private async Task AddEventAsync(ReportEvent e, DbConnection? c, DbTransaction? tx) => e.Id = await InsertAsync(
        "INSERT INTO dbo.ReportEvents (ReportId, Kind, Note, ByWho, Utc) VALUES (@r, @k, @n, @b, @u);",
        cmd => { P(cmd, "@r", e.ReportId); P(cmd, "@k", e.Kind); P(cmd, "@n", e.Note, DbType.String); P(cmd, "@b", e.By); P(cmd, "@u", e.Utc); }, c, tx);

    public async Task<IReadOnlyList<ReportEvent>> GetEventsAsync(long reportId) => await QueryAsync(
        "SELECT Id, ReportId, Kind, Note, ByWho, Utc FROM dbo.ReportEvents WHERE ReportId = @r ORDER BY Utc, Id",
        r => new ReportEvent
        {
            Id = r.GetInt64(0), ReportId = r.GetInt64(1), Kind = r.GetString(2), Note = r.IsDBNull(3) ? null : r.GetString(3),
            By = r.GetString(4), Utc = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc),
        }, ("@r", reportId));

    // ------------------------------------------------------------------ evidence

    private static Evidence MapEvidence(DbDataReader r) => new()
    {
        Id = r.GetInt64(0), ReportId = r.GetInt64(1), FileName = r.GetString(2), ContentType = r.GetString(3), Size = r.GetInt64(4),
        StoredName = r.GetString(5), Utc = DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc),
    };

    private const string EvidenceCols = "Id, ReportId, FileName, ContentType, SizeBytes, StoredName, Utc";

    public async Task InsertEvidenceAsync(Evidence e) => e.Id = await InsertAsync(
        "INSERT INTO dbo.Evidence (ReportId, FileName, ContentType, SizeBytes, StoredName, Utc) VALUES (@r, @f, @t, @s, @n, @u);",
        cmd => { P(cmd, "@r", e.ReportId); P(cmd, "@f", e.FileName); P(cmd, "@t", e.ContentType); P(cmd, "@s", e.Size); P(cmd, "@n", e.StoredName); P(cmd, "@u", e.Utc); });

    public async Task<Evidence?> GetEvidenceAsync(long id) =>
        (await QueryAsync($"SELECT {EvidenceCols} FROM dbo.Evidence WHERE Id = @id", MapEvidence, ("@id", id))).FirstOrDefault();

    public async Task<IReadOnlyList<Evidence>> GetEvidenceForReportAsync(long reportId) =>
        await QueryAsync($"SELECT {EvidenceCols} FROM dbo.Evidence WHERE ReportId = @r ORDER BY Id", MapEvidence, ("@r", reportId));

    // ------------------------------------------------------------------ conversations

    private const string ConvCols = "Id, SurvivorId, ReportId, OrgId, IsSample, CreatedUtc, LastUtc, SurvivorReadUtc, OrgReadUtc";

    private static Conversation MapConv(DbDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")), SurvivorId = IntN(r, "SurvivorId"), ReportId = LongN(r, "ReportId"), OrgId = Str(r, "OrgId")!,
        IsSample = r.GetBoolean(r.GetOrdinal("IsSample")), CreatedUtc = Utc(r, "CreatedUtc"), LastUtc = Utc(r, "LastUtc"),
        SurvivorReadUtc = Utc(r, "SurvivorReadUtc"), OrgReadUtc = Utc(r, "OrgReadUtc"),
    };

    public Task InsertConversationAsync(Conversation c) => InsertConversationAsync(c, null, null);

    private async Task InsertConversationAsync(Conversation cv, DbConnection? c, DbTransaction? tx) => cv.Id = await InsertAsync("""
        INSERT INTO dbo.Conversations (SurvivorId, ReportId, OrgId, IsSample, CreatedUtc, LastUtc, SurvivorReadUtc, OrgReadUtc)
        VALUES (@s, @r, @o, @sample, @c, @l, @sr, @or);
        """, cmd =>
        {
            P(cmd, "@s", cv.SurvivorId, DbType.Int32); P(cmd, "@r", cv.ReportId, DbType.Int64); P(cmd, "@o", cv.OrgId); P(cmd, "@sample", cv.IsSample);
            P(cmd, "@c", cv.CreatedUtc); P(cmd, "@l", cv.LastUtc); P(cmd, "@sr", cv.SurvivorReadUtc); P(cmd, "@or", cv.OrgReadUtc);
        }, c, tx);

    public async Task<Conversation?> GetConversationAsync(long id) =>
        (await QueryAsync($"SELECT {ConvCols} FROM dbo.Conversations WHERE Id = @id", MapConv, ("@id", id))).FirstOrDefault();

    public async Task<Conversation?> FindConversationAsync(int? survivorId, long? reportId, string orgId) =>
        (await QueryAsync($"""
            SELECT TOP 1 {ConvCols} FROM dbo.Conversations
            WHERE OrgId = @o
              AND ((@s IS NULL AND SurvivorId IS NULL) OR SurvivorId = @s)
              AND ((@r IS NULL AND ReportId IS NULL) OR ReportId = @r)
            ORDER BY Id
            """, MapConv, ("@o", orgId), ("@s", survivorId), ("@r", reportId))).FirstOrDefault();

    public async Task<IReadOnlyList<Conversation>> GetConversationsForSurvivorAsync(int survivorId) =>
        await QueryAsync($"SELECT {ConvCols} FROM dbo.Conversations WHERE SurvivorId = @s ORDER BY LastUtc DESC", MapConv, ("@s", survivorId));

    public async Task<IReadOnlyList<Conversation>> GetConversationsForReportAsync(long reportId) =>
        await QueryAsync($"SELECT {ConvCols} FROM dbo.Conversations WHERE ReportId = @r ORDER BY LastUtc DESC", MapConv, ("@r", reportId));

    public async Task<IReadOnlyList<Conversation>> GetConversationsForOrgAsync(string orgId) =>
        await QueryAsync($"SELECT {ConvCols} FROM dbo.Conversations WHERE OrgId = @o ORDER BY LastUtc DESC", MapConv, ("@o", orgId));

    public Task InsertMessageAsync(Message m) => InsertMessageAsync(m, null, null);

    private async Task InsertMessageAsync(Message m, DbConnection? c, DbTransaction? tx) => m.Id = await InsertAsync("""
        UPDATE dbo.Conversations SET LastUtc = CASE WHEN @u > LastUtc THEN @u ELSE LastUtc END WHERE Id = @c;
        INSERT INTO dbo.Messages (ConversationId, Sender, OfficerId, Body, Utc) VALUES (@c, @s, @o, @b, @u);
        """, cmd => { P(cmd, "@c", m.ConversationId); P(cmd, "@s", m.From.ToString()); P(cmd, "@o", m.OfficerId, DbType.Int32); P(cmd, "@b", m.Body); P(cmd, "@u", m.Utc); },
        c, tx);

    private static Message MapMessage(DbDataReader r) => new()
    {
        Id = r.GetInt64(0), ConversationId = r.GetInt64(1), From = Enum.Parse<Sender>(r.GetString(2)), OfficerId = r.IsDBNull(3) ? null : r.GetInt32(3),
        Body = r.GetString(4), Utc = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc),
    };

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(long conversationId, long afterId) => await QueryAsync(
        "SELECT Id, ConversationId, Sender, OfficerId, Body, Utc FROM dbo.Messages WHERE ConversationId = @c AND Id > @a ORDER BY Id",
        MapMessage, ("@c", conversationId), ("@a", afterId));

    public async Task<Message?> GetLastMessageAsync(long conversationId) => (await QueryAsync(
        "SELECT TOP 1 Id, ConversationId, Sender, OfficerId, Body, Utc FROM dbo.Messages WHERE ConversationId = @c ORDER BY Id DESC",
        MapMessage, ("@c", conversationId))).FirstOrDefault();

    public async Task<int> CountUnreadAsync(long conversationId, Sender notFrom, DateTime sinceUtc) => (await QueryAsync(
        "SELECT COUNT(*) FROM dbo.Messages WHERE ConversationId = @c AND Sender <> @n AND Sender <> N'System' AND Utc > @u",
        r => r.GetInt32(0), ("@c", conversationId), ("@n", notFrom.ToString()), ("@u", sinceUtc))).Single();

    public Task MarkReadAsync(long conversationId, bool bySurvivor, DateTime utc) => ExecAsync(bySurvivor
        ? "UPDATE dbo.Conversations SET SurvivorReadUtc = @u WHERE Id = @c;"
        : "UPDATE dbo.Conversations SET OrgReadUtc = @u WHERE Id = @c;", ("@u", utc), ("@c", conversationId));

    // ------------------------------------------------------------------ seed

    public async Task<bool> IsEmptyAsync() =>
        (await QueryAsync("SELECT COUNT(*) FROM dbo.Organisations", r => r.GetInt32(0))).Single() == 0;

    public async Task SeedAsync(SeedSet seed)
    {
        await using var c = await db.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        foreach (var o in seed.Organisations) await SaveOrganisationAsync(o, c, tx);
        foreach (var o in seed.Officers) await InsertOfficerAsync(o, c, tx);
        var survivorIds = new Dictionary<string, int>();
        foreach (var s in seed.Survivors)
        {
            s.Id = (int)await InsertAsync(
                "INSERT INTO dbo.Survivors (Nickname, PassHash, RecoveryHash, Email, IsSample, CreatedUtc) VALUES (@n, @p, @r, @e, @s, @c);",
                cmd => { P(cmd, "@n", s.Nickname); P(cmd, "@p", s.PassHash); P(cmd, "@r", s.RecoveryHash); P(cmd, "@e", s.Email, DbType.String); P(cmd, "@s", s.IsSample); P(cmd, "@c", s.CreatedUtc); },
                c, tx);
            survivorIds[s.Nickname] = s.Id;
        }
        foreach (var sr in seed.Reports)
        {
            var r = sr.Report;
            r.SurvivorId = sr.SurvivorNickname is null ? null : survivorIds[sr.SurvivorNickname];
            await InsertReportAsync(r, c, tx);
            foreach (var e in sr.Events) { e.ReportId = r.Id; await AddEventAsync(e, c, tx); }
            if (sr.Conversation is { } conv)
            {
                var cv = conv.Conversation;
                cv.ReportId = r.Id; cv.SurvivorId = r.SurvivorId;
                await InsertConversationAsync(cv, c, tx);
                var officerId = seed.Officers.First(o => o.OrgId == cv.OrgId).Id;
                foreach (var m in conv.Messages)
                {
                    m.ConversationId = cv.Id;
                    if (m.From == Sender.Officer) m.OfficerId = officerId;
                    await InsertMessageAsync(m, c, tx);
                }
            }
        }
        await tx.CommitAsync();
    }
}
