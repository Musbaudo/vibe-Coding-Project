namespace SpeakSafe.Core;

public enum ReportStatus { Submitted, Referred, InProgress, Closed }
public enum IdentityMode { Anonymous, ShareNickname }
public enum Sender { System, Survivor, Officer }

public sealed class Survivor
{
    public int Id { get; set; }
    public string Nickname { get; set; } = "";
    public string PassHash { get; set; } = "";
    public string RecoveryHash { get; set; } = "";
    public string? Email { get; set; }
    public DateTime CreatedUtc { get; set; }
    public bool IsSample { get; set; }
}

public sealed class Organisation
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Initials { get; set; } = "";
    public string Color { get; set; } = "#0b7a4b";
    public List<string> Services { get; set; } = [];
    /// <summary>State names, or "All states".</summary>
    public List<string> States { get; set; } = [];
    public string Description { get; set; } = "";
    public string ResponseNote { get; set; } = "";
    public string Languages { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsSample { get; set; }

    public bool Covers(string? state) =>
        string.IsNullOrEmpty(state) || States.Contains(Catalog.AllStates) || States.Contains(state);
}

public sealed class Officer
{
    public int Id { get; set; }
    public string OrgId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string PassHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsSample { get; set; }
}

public sealed class Report
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string PinHash { get; set; } = "";
    public int? SurvivorId { get; set; }
    public List<string> Types { get; set; } = [];
    public string Place { get; set; } = "";
    public string? State { get; set; }
    public DateOnly? IncidentDate { get; set; }
    public string Frequency { get; set; } = "";
    public string Responsible { get; set; } = "";
    /// <summary>Ciphertext. Use <see cref="ICipher"/> to read.</summary>
    public string? Narrative { get; set; }
    public string Witnesses { get; set; } = "";
    public List<string> Support { get; set; } = [];
    public IdentityMode Identity { get; set; }
    public string? OrgId { get; set; }
    public ReportStatus Status { get; set; }
    public bool Urgent { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool IsSample { get; set; }
}

public sealed class ReportEvent
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public string Kind { get; set; } = "";
    public string? Note { get; set; }
    public string By { get; set; } = "";
    public DateTime Utc { get; set; }
}

public sealed class Evidence
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string StoredName { get; set; } = "";
    public DateTime Utc { get; set; }
}

public sealed class Conversation
{
    public long Id { get; set; }
    public int? SurvivorId { get; set; }
    /// <summary>Set when the conversation belongs to a report (also the only way an account-less reporter can chat).</summary>
    public long? ReportId { get; set; }
    public string OrgId { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime LastUtc { get; set; }
    public DateTime SurvivorReadUtc { get; set; }
    public DateTime OrgReadUtc { get; set; }
    public bool IsSample { get; set; }
}

public sealed class Message
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public Sender From { get; set; }
    public int? OfficerId { get; set; }
    /// <summary>Ciphertext. Use <see cref="ICipher"/> to read.</summary>
    public string Body { get; set; } = "";
    public DateTime Utc { get; set; }
}

/// <summary>Who is making a call. Built from the auth cookie in the web layer.</summary>
public sealed record Actor(string Role, string Id, string Name)
{
    public const string SurvivorRole = "survivor", TrackerRole = "tracker", OfficerRole = "officer", AdminRole = "admin";
    public static readonly Actor Nobody = new("", "", "");

    public bool IsSurvivor => Role == SurvivorRole;
    public bool IsTracker => Role == TrackerRole;
    public bool IsOfficer => Role == OfficerRole;
    public bool IsAdmin => Role == AdminRole;
    public int SurvivorId => IsSurvivor ? int.Parse(Id) : 0;
    public long TrackedReportId => IsTracker ? long.Parse(Id) : 0;
    public int OfficerId => IsOfficer ? int.Parse(Id.Split(':')[0]) : 0;
    /// <summary>Officer actor Id is "officerId:orgId".</summary>
    public string OfficerOrgId => IsOfficer ? Id.Split(':')[1] : "";
}

public sealed class SpeakException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
