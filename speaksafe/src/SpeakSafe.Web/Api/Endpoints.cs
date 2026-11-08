using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SpeakSafe.Core;

namespace SpeakSafe.Web.Api;

public static class Policies
{
    public const string Survivor = "survivor", Reporter = "reporter", Participant = "participant", Officer = "officer", Admin = "admin";
}

public static class Endpoints
{
    private static IResult Ok(object? data) => Results.Json(new { ok = true, data });

    public static Actor ActorOf(HttpContext ctx)
    {
        var u = ctx.User;
        if (u.Identity?.IsAuthenticated != true) return Actor.Nobody;
        return new Actor(u.FindFirstValue(ClaimTypes.Role) ?? "", u.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", u.FindFirstValue(ClaimTypes.Name) ?? "");
    }

    private static async Task SignIn(HttpContext ctx, string role, string id, string name)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Name, name)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/health", async (ISpeakStore store, IConfiguration cfg) =>
        {
            var orgs = await store.GetOrganisationsAsync();
            return Results.Json(new
            {
                status = "ok", store = cfg["Store"] ?? "SqlServer", organisations = orgs.Count,
                version = typeof(Endpoints).Assembly.GetName().Version?.ToString(3),
            });
        });

        var api = app.MapGroup("/api/v1");

        // ------------------------------------------------------------- public

        api.MapGet("/meta", (IConfiguration cfg) =>
        {
            object? demo = null;
            if (cfg.GetValue("Demo:ShowLogins", false))
                demo = new
                {
                    password = cfg["Seed:SamplePassword"] ?? "demo1234",
                    survivor = SeedData.SampleSurvivor,
                    officers = SeedData.SampleOfficers.Select(o => new { o.Username, o.Name, o.OrgId }),
                    track = new[] { new { code = SeedData.SampleCode, pin = SeedData.SamplePin }, new { code = SeedData.SampleAnonCode, pin = SeedData.SampleAnonPin } },
                };
            return Ok(new
            {
                types = Catalog.Types, places = Catalog.Places, frequencies = Catalog.Frequencies, responsible = Catalog.Responsible,
                witnesses = Catalog.Witnesses, support = Catalog.Support, services = Catalog.Services, states = Catalog.States,
                supportToServices = Catalog.SupportToServices, allStates = Catalog.AllStates,
                maxNarrative = ReportService.MaxNarrative, maxEvidence = ReportService.MaxEvidencePerReport,
                maxEvidenceMb = ReportService.MaxEvidenceBytes / 1024 / 1024, evidenceTypes = ReportService.EvidenceTypes,
                adminEnabled = !string.IsNullOrEmpty(cfg["Admin:Pin"]),
                demo,
            });
        });

        api.MapGet("/orgs", async (string? service, string? state, OrgService orgs) =>
            Ok((await orgs.PublicListAsync(service, state)).Select(PublicOrg)));

        // ------------------------------------------------------------- auth

        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (RegisterDto dto, AccountService acc, HttpContext ctx) =>
        {
            var (s, recovery) = await acc.RegisterAsync(dto.Nickname, dto.Passphrase, dto.Email);
            await SignIn(ctx, Actor.SurvivorRole, s.Id.ToString(), s.Nickname);
            return Ok(new { role = Actor.SurvivorRole, name = s.Nickname, recoveryCode = recovery });
        });

        auth.MapPost("/signin", async (SignInDto dto, AccountService acc, HttpContext ctx) =>
        {
            var s = await acc.SignInAsync(dto.Nickname, dto.Passphrase);
            await SignIn(ctx, Actor.SurvivorRole, s.Id.ToString(), s.Nickname);
            return Ok(new { role = Actor.SurvivorRole, name = s.Nickname });
        });

        auth.MapPost("/recover", async (RecoverDto dto, AccountService acc, HttpContext ctx) =>
        {
            var (s, recovery) = await acc.RecoverAsync(dto.Nickname, dto.RecoveryCode, dto.NewPassphrase);
            await SignIn(ctx, Actor.SurvivorRole, s.Id.ToString(), s.Nickname);
            return Ok(new { role = Actor.SurvivorRole, name = s.Nickname, recoveryCode = recovery });
        });

        auth.MapPost("/track", async (TrackDto dto, AccountService acc, HttpContext ctx) =>
        {
            var r = await acc.TrackAsync(dto.Code, dto.Pin);
            await SignIn(ctx, Actor.TrackerRole, r.Id.ToString(), r.Code);
            return Ok(new { role = Actor.TrackerRole, name = r.Code });
        });

        auth.MapPost("/officer", async (OfficerSignInDto dto, AccountService acc, ISpeakStore store, HttpContext ctx) =>
        {
            var o = await acc.OfficerSignInAsync(dto.Username, dto.Passphrase);
            await SignIn(ctx, Actor.OfficerRole, $"{o.Id}:{o.OrgId}", o.Name);
            var org = await store.GetOrganisationAsync(o.OrgId);
            return Ok(new { role = Actor.OfficerRole, name = o.Name, org = org?.Name });
        });

        auth.MapPost("/admin", async (AdminSignInDto dto, IConfiguration cfg, HttpContext ctx) =>
        {
            var pin = cfg["Admin:Pin"];
            if (string.IsNullOrEmpty(pin)) throw new SpeakException("Coordinator sign-in is off. Set Admin:Pin in appsettings.", 403);
            if (!Hasher.FixedEquals(dto.Pin ?? "", pin)) throw new SpeakException("PIN is wrong.", 401);
            await SignIn(ctx, Actor.AdminRole, "admin", "Coordinator");
            return Ok(new { role = Actor.AdminRole, name = "Coordinator" });
        });

        api.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(null);
        });

        api.MapGet("/me", async (HttpContext ctx, ISpeakStore store) =>
        {
            var a = ActorOf(ctx);
            if (a.Role == "") return Ok(null);
            string? org = a.IsOfficer ? (await store.GetOrganisationAsync(a.OfficerOrgId))?.Name : null;
            return Ok(new { role = a.Role, name = a.Name, org });
        });

        // ------------------------------------------------------------- reports

        api.MapPost("/reports", async (ReportDto dto, ReportService reports, ISpeakStore store, HttpContext ctx) =>
        {
            var actor = ActorOf(ctx);
            if (!actor.IsSurvivor) actor = Actor.Nobody;
            var (r, pin) = await reports.CreateAsync(actor, dto.ToNew());
            // Without an account, the browser becomes the tracker of this report so evidence can be attached right away.
            if (!actor.IsSurvivor) await SignIn(ctx, Actor.TrackerRole, r.Id.ToString(), r.Code);
            var org = r.OrgId is null ? null : await store.GetOrganisationAsync(r.OrgId);
            return Ok(new { code = r.Code, pin, status = r.Status, org = org?.Name, urgent = r.Urgent });
        }).RequireRateLimiting("report");

        api.MapGet("/reports/mine", async (HttpContext ctx, ISpeakStore store, ReportService reports) =>
        {
            var a = ActorOf(ctx);
            var list = await store.GetReportsForSurvivorAsync(a.SurvivorId);
            var orgs = (await store.GetOrganisationsAsync()).ToDictionary(o => o.Id);
            return Ok(list.Select(r => Summary(r, orgs)));
        }).RequireAuthorization(Policies.Survivor);

        api.MapGet("/reports/{code}", async (string code, HttpContext ctx, ISpeakStore store, ReportService reports) =>
        {
            var a = ActorOf(ctx);
            var r = await store.GetReportByCodeAsync(Codes.NormalizeCode(code));
            if (r is null || !reports.CanSee(a, r)) throw new SpeakException("Report not found.", 404);
            return Ok(await DetailAsync(r, a, store, reports));
        }).RequireAuthorization(Policies.Participant);

        api.MapPost("/reports/{code}/share", async (string code, ShareDto dto, HttpContext ctx, ReportService reports) =>
        {
            var r = await reports.ShareAsync(ActorOf(ctx), code, dto.OrgId);
            return Ok(new { code = r.Code, status = r.Status });
        }).RequireAuthorization(Policies.Reporter);

        api.MapPost("/reports/{code}/close", async (string code, HttpContext ctx, ReportService reports) =>
        {
            await reports.CloseByReporterAsync(ActorOf(ctx), code);
            return Ok(null);
        }).RequireAuthorization(Policies.Reporter);

        api.MapPost("/reports/{code}/evidence", async (string code, HttpContext ctx, ReportService reports) =>
        {
            if (!ctx.Request.HasFormContentType) throw new SpeakException("Send the file as multipart/form-data.");
            var form = await ctx.Request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? throw new SpeakException("No file was attached.");
            if (file.Length > ReportService.MaxEvidenceBytes) throw new SpeakException("Each file must be 10 MB or smaller.");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var e = await reports.AddEvidenceAsync(ActorOf(ctx), code, file.FileName, file.ContentType, ms.ToArray());
            return Ok(new { e.Id, e.FileName, e.Size, e.ContentType });
        }).RequireAuthorization(Policies.Reporter).RequireRateLimiting("report");

        api.MapGet("/evidence/{id:long}", async (long id, HttpContext ctx, ReportService reports) =>
        {
            var f = await reports.ReadEvidenceAsync(ActorOf(ctx), id);
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.File(f.Data, "application/octet-stream", f.Meta.FileName);
        }).RequireAuthorization(Policies.Participant);

        // ------------------------------------------------------------- conversations

        api.MapGet("/conversations", async (HttpContext ctx, ChatService chat) =>
            Ok(await chat.ListAsync(ActorOf(ctx)))).RequireAuthorization(Policies.Participant);

        api.MapPost("/conversations", async (StartConversationDto dto, HttpContext ctx, ChatService chat) =>
        {
            var c = await chat.StartAsync(ActorOf(ctx), dto.OrgId);
            return Ok(new { id = c.Id });
        }).RequireAuthorization(Policies.Survivor);

        api.MapGet("/conversations/{id:long}/messages", async (long id, long? after, HttpContext ctx, ChatService chat) =>
        {
            var (_, msgs) = await chat.MessagesAsync(ActorOf(ctx), id, after ?? 0);
            return Ok(msgs);
        }).RequireAuthorization(Policies.Participant);

        api.MapPost("/conversations/{id:long}/messages", async (long id, SendDto dto, HttpContext ctx, ChatService chat) =>
            Ok(await chat.SendAsync(ActorOf(ctx), id, dto.Text))).RequireAuthorization(Policies.Participant).RequireRateLimiting("chat");

        // ------------------------------------------------------------- organisation staff

        api.MapGet("/org/cases", async (HttpContext ctx, ISpeakStore store, ReportService reports, ChatService chat) =>
        {
            var a = ActorOf(ctx);
            var list = await store.GetReportsForOrgAsync(a.OfficerOrgId);
            var convs = (await chat.ListAsync(a)).Where(c => c.ReportCode is not null).ToDictionary(c => c.ReportCode!);
            var result = new List<object>();
            foreach (var r in list)
            {
                convs.TryGetValue(r.Code, out var c);
                result.Add(new
                {
                    r.Code, r.Status, statusLabel = Catalog.StatusLabel(r.Status), types = r.Types.Select(Catalog.TypeLabel),
                    place = Catalog.PlaceLabel(r.Place), r.State, r.Urgent, r.CreatedUtc, r.UpdatedUtc,
                    reporter = await reports.ReporterLabelAsync(r), conversationId = c?.Id, unread = c?.Unread ?? 0,
                });
            }
            return Ok(result);
        }).RequireAuthorization(Policies.Officer);

        api.MapPost("/org/cases/{code}/status", async (string code, StatusDto dto, HttpContext ctx, ReportService reports) =>
        {
            if (!Enum.TryParse<ReportStatus>(dto.Status, true, out var s)) throw new SpeakException("Unknown status.");
            await reports.SetStatusByOfficerAsync(ActorOf(ctx), code, s, dto.Note);
            return Ok(null);
        }).RequireAuthorization(Policies.Officer);

        // ------------------------------------------------------------- coordinator

        var admin = api.MapGroup("/admin").RequireAuthorization(Policies.Admin);

        admin.MapGet("/overview", async (OrgService orgs) =>
        {
            var o = await orgs.OverviewAsync();
            return Ok(new
            {
                o.Reports, o.Urgent, o.ByStatus, o.ByType, o.ByPlace, o.ByState,
                organisations = o.Organisations.Select(l => new { org = AdminOrg(l.Org), l.Officers, l.OpenCases, l.ClosedCases }),
            });
        });

        admin.MapPost("/orgs", async (OrgInput dto, OrgService orgs) => Ok(AdminOrg(await orgs.SaveAsync(dto))));

        admin.MapGet("/orgs/{id}/officers", async (string id, ISpeakStore store) =>
            Ok((await store.GetOfficersAsync(id)).Select(o => new { o.Id, o.Name, o.Username, o.IsActive, o.IsSample })));

        admin.MapPost("/orgs/{id}/officers", async (string id, OfficerDto dto, OrgService orgs) =>
        {
            var (o, pass) = await orgs.AddOfficerAsync(id, dto.Name, dto.Username);
            return Ok(new { o.Id, o.Name, o.Username, passphrase = pass });
        });

        admin.MapPost("/officers/{id:int}/active", async (int id, ActiveDto dto, ISpeakStore store) =>
        {
            if (await store.GetOfficerAsync(id) is null) throw new SpeakException("Officer not found.", 404);
            await store.SetOfficerActiveAsync(id, dto.Active);
            return Ok(null);
        });
    }

    // ------------------------------------------------------------- shapes

    private static object PublicOrg(Organisation o) => new
    {
        o.Id, o.Name, o.Initials, o.Color, o.Services, o.States, o.Description, o.ResponseNote, o.Languages, demo = o.IsSample,
    };

    private static object AdminOrg(Organisation o) => new
    {
        o.Id, o.Name, o.Initials, o.Color, o.Services, o.States, o.Description, o.ResponseNote, o.Languages, o.IsActive, demo = o.IsSample,
    };

    private static object Summary(Report r, Dictionary<string, Organisation> orgs) => new
    {
        r.Code, r.Status, statusLabel = Catalog.StatusLabel(r.Status), types = r.Types.Select(Catalog.TypeLabel),
        place = Catalog.PlaceLabel(r.Place), r.State, r.Urgent, r.CreatedUtc, r.UpdatedUtc,
        org = r.OrgId is not null && orgs.TryGetValue(r.OrgId, out var o) ? o.Name : null,
    };

    private static async Task<object> DetailAsync(Report r, Actor a, ISpeakStore store, ReportService reports)
    {
        var org = r.OrgId is null ? null : await store.GetOrganisationAsync(r.OrgId);
        var events = await store.GetEventsAsync(r.Id);
        var evidence = await store.GetEvidenceForReportAsync(r.Id);
        var convs = await store.GetConversationsForReportAsync(r.Id);
        return new
        {
            r.Code, r.Status, statusLabel = Catalog.StatusLabel(r.Status), types = r.Types.Select(Catalog.TypeLabel),
            place = Catalog.PlaceLabel(r.Place), r.State, incidentDate = r.IncidentDate?.ToString("yyyy-MM-dd"), r.Frequency, r.Responsible,
            r.Witnesses, r.Support, identity = r.Identity, r.Urgent, r.CreatedUtc, r.UpdatedUtc,
            narrative = reports.ReadNarrative(r),
            org = org is null ? null : new { org.Id, org.Name },
            reporter = a.IsOfficer ? await reports.ReporterLabelAsync(r) : null,
            events = events.Select(e => new { e.Kind, e.Note, e.By, e.Utc }),
            evidence = evidence.Select(e => new { e.Id, e.FileName, e.Size, e.ContentType, e.Utc }),
            conversationId = convs.FirstOrDefault()?.Id,
            canShare = !a.IsOfficer && r.OrgId is null && r.Status != ReportStatus.Closed,
            canClose = r.Status != ReportStatus.Closed,
        };
    }
}
