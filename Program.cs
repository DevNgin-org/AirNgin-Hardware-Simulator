using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

namespace AirNgin.Simulator;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = AppBuilder.Configure<App>();
        if (OperatingSystem.IsMacOS()) builder.UseAvaloniaNative();
        else if (OperatingSystem.IsWindows()) builder.UseWin32();
        else throw new PlatformNotSupportedException();
        builder.UseSkia().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class App : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Light; }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            FileStream? lease = null;
            try
            {
                Directory.CreateDirectory(Profiles.Folder);
                lease = new FileStream(Path.Combine(Profiles.Folder, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                desktop.MainWindow = new Dashboard();
            }
            catch (Exception e)
            {
                desktop.MainWindow = new Window
                {
                    Title = "AirNgin",
                    Width = 540,
                    Height = 180,
                    Content = Ui.Wrap(
                    "باز کردن شبیه‌ساز ممکن نشد. اگر پنجرهٔ دیگری باز است از همان استفاده کنید.\n" + e.Message, 20)
                };
            }
            desktop.Exit += (_, _) => lease?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
