namespace Zitac.Proxmox.Steps;

// Sort keys for [CategoryClassification] on steps. The designer puts Step Information at -10,
// Inputs at 100 and Outcomes / Testing at 1000; categories without a key get -1 and are
// sorted by name. These put Authentication and then Settings right after Step Information.
internal static class StepCategories
{
    public const string Authentication = "Authentication";
    public const string Settings = "Settings";

    public const int AuthenticationSortKey = -3;
    public const int SettingsSortKey = -2;
}
