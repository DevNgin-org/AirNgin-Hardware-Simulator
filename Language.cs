using Avalonia.Media;

namespace AirNgin.Simulator;

internal static class Language
{
    private static volatile bool english;
    public static bool English => english;
    public static FlowDirection Direction => english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
    public static string SettingsFile => Path.Combine(Profiles.Folder, "language.txt");

    public static void Load(string file) =>
        english = File.Exists(file) && File.ReadAllText(file).Trim() == "en";

    public static void Save(bool useEnglish, string file)
    {
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, useEnglish ? "en" : "fa");
            File.Move(temp, file, true);
            english = useEnglish;
        }
        finally { File.Delete(temp); }
    }

    // Only the original names of bundled profiles have localized display aliases.
    public static string DisplayName(Device device) => (device.Id.ToString(), device.Name) switch
    {
        ("00000000-0000-4600-8000-000000000001", "چراغ اتاق") => Strings.Lamp,
        ("00000000-0000-4600-8000-000000000002", "دیمر پذیرایی") when english => "Living room dimmer",
        ("00000000-0000-4600-8000-000000000003", "کلید آزمایشی Analyzer") when english => "Analyzer demo switch",
        _ => device.Name
    };
}
