namespace SpeakSafe.Core;

/// <summary>Fixed option lists shared by the API, the UI and validation.</summary>
public static class Catalog
{
    public const string AllStates = "All states";
    public const string AssaultType = "assault";

    public sealed record Option(string Id, string Label, string? Detail = null);

    public static readonly Option[] Types =
    [
        new("verbal", "Comments or jokes", "Sexual remarks, jokes, whistling or gestures"),
        new("touch", "Unwanted touching", "Groping, brushing against you, hugging or kissing"),
        new("quid", "Sex for favours", "Demands for sex in exchange for grades, a job, promotion or money"),
        new("online", "Online harassment", "Unwanted messages, sexual images, or sharing your images"),
        new("stalk", "Stalking or following", "Being followed, watched or contacted again and again"),
        new(AssaultType, "Sexual assault or rape", "Forced or non-consensual sexual contact"),
    ];

    public static readonly Option[] Places =
    [
        new("work", "Workplace"), new("school", "School or university"), new("public", "Public place or transport"),
        new("online", "Online"), new("home", "Home or community"), new("other", "Somewhere else"),
    ];

    public static readonly string[] Frequencies = ["Once", "A few times", "It is still happening"];

    public static readonly string[] Responsible =
    [
        "Prefer not to say", "Boss or supervisor", "Colleague", "Lecturer or teacher", "Fellow student",
        "Client or customer", "Stranger", "Family member or someone I know", "Online account",
    ];

    public static readonly string[] Witnesses = ["Not sure", "Yes", "No"];

    public static readonly string[] Support =
        ["Legal advice", "Counselling", "Medical care", "Help reporting to my employer or school", "Just keep a record"];

    public static readonly string[] Services =
        ["Human rights", "Legal aid", "Counselling", "Medical care", "Campus support", "Workplace support"];

    public static readonly string[] States =
    [
        "Abia", "Adamawa", "Akwa Ibom", "Anambra", "Bauchi", "Bayelsa", "Benue", "Borno", "Cross River", "Delta", "Ebonyi",
        "Edo", "Ekiti", "Enugu", "FCT Abuja", "Gombe", "Imo", "Jigawa", "Kaduna", "Kano", "Katsina", "Kebbi", "Kogi", "Kwara",
        "Lagos", "Nasarawa", "Niger", "Ogun", "Ondo", "Osun", "Oyo", "Plateau", "Rivers", "Sokoto", "Taraba", "Yobe", "Zamfara",
    ];

    /// <summary>Which organisation services fit each support request.</summary>
    public static readonly Dictionary<string, string[]> SupportToServices = new()
    {
        ["Legal advice"] = ["Legal aid", "Human rights"],
        ["Counselling"] = ["Counselling"],
        ["Medical care"] = ["Medical care"],
        ["Help reporting to my employer or school"] = ["Workplace support", "Campus support"],
    };

    public static string TypeLabel(string id) => Types.FirstOrDefault(t => t.Id == id)?.Label ?? id;
    public static string PlaceLabel(string id) => Places.FirstOrDefault(p => p.Id == id)?.Label ?? id;

    public static string StatusLabel(ReportStatus s) => s switch
    {
        ReportStatus.Submitted => "Submitted",
        ReportStatus.Referred => "Sent to organisation",
        ReportStatus.InProgress => "Organisation is helping",
        ReportStatus.Closed => "Closed",
        _ => s.ToString(),
    };
}
