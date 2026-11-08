namespace SpeakSafe.Core;

public sealed class DuplicateException : Exception;

public interface ISpeakStore
{
    // survivors
    /// <summary>Inserts and assigns Id. Throws <see cref="DuplicateException"/> if the nickname is taken.</summary>
    Task InsertSurvivorAsync(Survivor s);
    Task<Survivor?> GetSurvivorAsync(int id);
    Task<Survivor?> GetSurvivorByNicknameAsync(string nickname);
    Task UpdateSurvivorSecretsAsync(int id, string passHash, string recoveryHash);

    // organisations + officers
    Task<IReadOnlyList<Organisation>> GetOrganisationsAsync();
    Task<Organisation?> GetOrganisationAsync(string id);
    /// <summary>Insert or update by Id.</summary>
    Task SaveOrganisationAsync(Organisation o);
    Task<IReadOnlyList<Officer>> GetOfficersAsync(string? orgId);
    Task<Officer?> GetOfficerAsync(int id);
    Task<Officer?> GetOfficerByUsernameAsync(string username);
    /// <summary>Inserts and assigns Id. Throws <see cref="DuplicateException"/> if the username is taken.</summary>
    Task InsertOfficerAsync(Officer o);
    Task SetOfficerActiveAsync(int id, bool active);

    // reports
    /// <summary>Inserts and assigns Id. Throws <see cref="DuplicateException"/> if Code exists.</summary>
    Task InsertReportAsync(Report r);
    Task<Report?> GetReportAsync(long id);
    Task<Report?> GetReportByCodeAsync(string code);
    Task<IReadOnlyList<Report>> GetReportsForSurvivorAsync(int survivorId);
    Task<IReadOnlyList<Report>> GetReportsForOrgAsync(string orgId);
    Task<IReadOnlyList<Report>> GetAllReportsAsync();
    Task UpdateReportStatusAsync(long id, ReportStatus status, string? orgId, DateTime utc);
    Task AddEventAsync(ReportEvent e);
    Task<IReadOnlyList<ReportEvent>> GetEventsAsync(long reportId);

    // evidence
    Task InsertEvidenceAsync(Evidence e);
    Task<Evidence?> GetEvidenceAsync(long id);
    Task<IReadOnlyList<Evidence>> GetEvidenceForReportAsync(long reportId);

    // conversations
    Task InsertConversationAsync(Conversation c);
    Task<Conversation?> GetConversationAsync(long id);
    Task<Conversation?> FindConversationAsync(int? survivorId, long? reportId, string orgId);
    Task<IReadOnlyList<Conversation>> GetConversationsForSurvivorAsync(int survivorId);
    Task<IReadOnlyList<Conversation>> GetConversationsForReportAsync(long reportId);
    Task<IReadOnlyList<Conversation>> GetConversationsForOrgAsync(string orgId);
    Task InsertMessageAsync(Message m);
    Task<IReadOnlyList<Message>> GetMessagesAsync(long conversationId, long afterId);
    Task<Message?> GetLastMessageAsync(long conversationId);
    Task<int> CountUnreadAsync(long conversationId, Sender notFrom, DateTime sinceUtc);
    Task MarkReadAsync(long conversationId, bool bySurvivor, DateTime utc);
}
