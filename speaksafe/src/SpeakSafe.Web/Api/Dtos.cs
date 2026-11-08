using SpeakSafe.Core;

namespace SpeakSafe.Web.Api;

public sealed record RegisterDto(string? Nickname, string? Passphrase, string? Email);
public sealed record SignInDto(string? Nickname, string? Passphrase);
public sealed record RecoverDto(string? Nickname, string? RecoveryCode, string? NewPassphrase);
public sealed record TrackDto(string? Code, string? Pin);
public sealed record OfficerSignInDto(string? Username, string? Passphrase);
public sealed record AdminSignInDto(string? Pin);

public sealed record ReportDto(
    List<string>? Types, string? Place, string? State, string? IncidentDate, string? Frequency, string? Responsible,
    string? Narrative, string? Witnesses, List<string>? Support, string? Identity, string? OrgId)
{
    public NewReport ToNew()
    {
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(IncidentDate))
        {
            if (!DateOnly.TryParseExact(IncidentDate, "yyyy-MM-dd", out var d)) throw new SpeakException("Date must look like 2026-11-08.");
            date = d;
        }
        var identity = string.Equals(Identity, "share", StringComparison.OrdinalIgnoreCase) || string.Equals(Identity, nameof(IdentityMode.ShareNickname), StringComparison.OrdinalIgnoreCase)
            ? IdentityMode.ShareNickname : IdentityMode.Anonymous;
        return new NewReport(Types, Place, State, date, Frequency, Responsible, Narrative, Witnesses, Support, identity, OrgId);
    }
}

public sealed record ShareDto(string? OrgId);
public sealed record StartConversationDto(string? OrgId);
public sealed record SendDto(string? Text);
public sealed record StatusDto(string? Status, string? Note);
public sealed record OfficerDto(string? Name, string? Username);
public sealed record ActiveDto(bool Active);
