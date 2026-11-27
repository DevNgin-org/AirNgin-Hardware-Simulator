using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using static AirNgin.Simulator.Ui;

namespace AirNgin.Simulator;

internal sealed class Dashboard : Window
{
    private readonly Dictionary<Guid, DeviceRuntime> devices = [];
    private readonly ListBox fleet = new() { MinHeight = 80 };
    private readonly StackPanel detail = Stack(14);
    private readonly TextBlock notice = Text("", 13, Brushes.Firebrick);
    private TextBlock status = Text("", 14, Teal, true);
    private TextBlock logText = Text("", 13);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly CancellationTokenSource closing = new();
    private DeviceRuntime? selected;
    private Button? connect, delete, manualSend;
    private string snapshot = "";
    private bool shutdown;

    public Dashboard()
    {
        FontFamily = PersianFont;
        Title = "AirNgin • شبیه‌ساز سخت‌افزار"; Width = 1080; Height = 740; MinWidth = 740; MinHeight = 510;
        FlowDirection = FlowDirection.RightToLeft; Background = Ui.Canvas;
        var profiles = File.Exists(Profiles.Database) ? Profiles.Read(Profiles.Database) : [Device.Sample()];
        foreach (var p in profiles) devices.Add(p.Id, new(p));
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(22) };
        var head = Stack(3); head.Children.Add(Text("شبیه‌ساز سخت‌افزار", 27, Ink, true)); head.Children.Add(Text("دستگاه را انتخاب کنید تا فرمان‌های اپ و پاسخ دستگاه را آزمایش کنید.", 13, Muted));
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("248,16,*"), Margin = new Thickness(0, 20, 0, 8) };
        var side = new DockPanel(); var top = Stack(9); top.Children.Add(Text("دستگاه‌ها", 18, Ink, true));
        top.Children.Add(Button("＋  دستگاه جدید", () => Do(() => Edit(null)), true));
        DockPanel.SetDock(top, Dock.Top); side.Children.Add(top);
        var files = Row(Button("ورود پروفایل", () => Do(Import)), Button("خروجی پروفایل", () => Do(Export)));
        DockPanel.SetDock(files, Dock.Bottom); side.Children.Add(files); side.Children.Add(fleet);
        layout.Children.Add(Card(side)); var scroll = new ScrollViewer { Content = detail };
        Grid.SetColumn(scroll, 2); layout.Children.Add(scroll); root.Children.Add(head); Grid.SetRow(layout, 1); root.Children.Add(layout);
        Grid.SetRow(notice, 2); root.Children.Add(notice); Content = root;
        fleet.SelectionChanged += (_, _) => { if (fleet.SelectedItem is ListBoxItem { Tag: Guid id } && devices.TryGetValue(id, out var r)) Show(r); };
        Reload(profiles.FirstOrDefault()?.Id);
        timer.Tick += (_, _) => Refresh(); timer.Start();
        Closing += async (_, e) =>
        {
            if (shutdown) return;
            e.Cancel = true;
            if (closing.IsCancellationRequested) return;
            closing.Cancel(); IsEnabled = false; timer.Stop();
            await Task.WhenAll(devices.Values.Select(r => r.StopAsync()));
            shutdown = true; Close();
        };
    }
    private async void Do(Func<Task> action)
    {
        notice.Text = "";
        try { await action(); }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        catch (Exception e) { notice.Text = e.GetBaseException().Message; }
    }
    private void Reload(Guid? id)
    {
        var items = devices.Values.Select(r =>
        {
            var row = new DockPanel();
            var editDevice = Button("ویرایش", () => Do(() => Edit(r)));
            DockPanel.SetDock(editDevice, Dock.Left);
            row.Children.Add(editDevice);
            row.Children.Add(Text(DeviceLabel(r), 12));
            return new ListBoxItem { Tag = r.Profile.Id, Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 8) };
        }).ToList();
        fleet.ItemsSource = items;
        var chosen = items.Find(x => Equals(x.Tag, id)) ?? items.FirstOrDefault();
        if (chosen != null) { fleet.SelectedItem = chosen; Show(devices[(Guid)chosen.Tag!]); }
        else { selected = null; detail.Children.Clear(); detail.Children.Add(Card(Text("برای شروع، یک دستگاه اضافه یا پروفایل دمو را وارد کنید."))); }
    }
    private static string DeviceLabel(DeviceRuntime r) => r.Profile.Name + "\n" + r.Profile.Serial + "  ·  " + r.Status;
    private void Show(DeviceRuntime r)
    {
        selected = r; snapshot = ""; notice.Text = ""; detail.Children.Clear(); status = Text("", 14, Teal, true); logText = Text("", 13); var p = r.Profile;
        var identity = Stack(6); identity.Children.Add(Text(p.Name, 24, Ink, true));
        identity.Children.Add(Text($"پروژه {p.Project}    ·    سریال {p.Serial}", 14, Muted)); identity.Children.Add(status);
        connect = Button("اتصال", () => Do(() => Toggle(r)), true);
        var edit = Button("ویرایش دستگاه و عملیات", () => Do(() => Edit(r)));
        delete = Button("حذف", () => Do(() => Remove(r)));
        identity.Children.Add(Row(connect, edit, delete)); detail.Children.Add(Card(identity));
        TabItem RulePage(bool analyzer)
        {
            var title = analyzer ? "از طریق Analyzer" : "ارتباط مستقیم";
            var panel = Section(title, analyzer
                ? "برای دستگاهی که فرمان و گزارش اختصاصی دارد. فرمان تولیدشده در سرور را با ورودی قانون تطبیق دهید و گزارش مورد انتظار اسکریپت تحلیل را برگردانید."
                : "برای دستگاهی که عملیات را با کلید و مقدار می‌خواند. مثلاً وقتی اپ ch1 را با مقدار on می‌فرستد، دستگاه وضعیت روشن را گزارش می‌کند.");
            var saved = p.Rules.Where(rule => Editors.UsesAnalyzerFormat(rule) == analyzer).ToArray();
            var picker = new ComboBox { ItemsSource = saved.Select(Editors.Describe).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 210 };
            if (saved.Length == 0)
                panel.Children.Add(Text("هنوز قانونی برای این روش ثبت نشده است.", 14, Muted));
            else panel.Children.Add(Field("قانون برای آزمایش", picker));
            panel.Children.Add(Button("تنظیم قوانین پاسخ", () => Do(() => Edit(r, true))));
            var trial = Section("آزمایش محلی", "فرمان را اجرا کنید تا پاسخ همین دستگاه را پیش از اتصال به سرور ببینید.");
            var key = Box(hint: "ch1"); var value = Box(hint: "on");
            var fields = Columns(Field("کلید فرمان", key), Field("مقدار ورودی", value));
            var raw = Box(hint: "HEX، متن یا JSON فرمان"); raw.AcceptsReturn = true; raw.MinHeight = 58;
            var rawField = Field("متن دقیق ورودی", raw);
            var custom = new CheckBox { Content = "ورودی کاستوم", IsChecked = analyzer };
            var result = Text("", 14, Teal);
            void ShowFormat() { rawField.IsVisible = custom.IsChecked == true; fields.IsVisible = !rawField.IsVisible; result.Text = ""; }
            custom.IsCheckedChanged += (_, _) => ShowFormat(); ShowFormat();
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0) return;
                var rule = saved[picker.SelectedIndex];
                custom.IsChecked = rule.CustomInput;
                key.Text = rule.Key; value.Text = raw.Text = rule.Match ?? "";
                result.Text = "";
            };
            if (saved.Length > 0) picker.SelectedIndex = 0;
            trial.Children.Add(custom); trial.Children.Add(fields); trial.Children.Add(rawField);
            trial.Children.Add(Button("اجرای آزمایش محلی", () => Do(async () =>
            {
                var reply = custom.IsChecked == true ? await r.PreviewCustomAsync(Val(raw), closing.Token)
                    : await r.PreviewAsync(new Pair(Val(key), Val(value)), closing.Token);
                result.Text = reply == null ? "پاسخی تولید نشد. دلیل را در رویدادهای همین دستگاه ببینید."
                    : "پاسخ دستگاه  " + (reply.key.Length == 0 ? reply.value : System.Text.Encoding.UTF8.GetString(Wire.Report(reply)));
                Refresh();
            })));
            trial.Children.Add(result);
            var page = Stack(10); page.Children.Add(Card(panel)); page.Children.Add(Card(trial));
            return new TabItem { Header = title, Content = page };
        }
        var manual = Section("ارسال دستی به سرور", "با دستگاه آنلاین، متن گزارش را مستقیماً روی تاپیک ارسال دستگاه منتشر کنید. این دکمه منتظر فرمان اپ نمی‌ماند.");
        var payload = Box(hint: "متن یا JSON کامل گزارش دستگاه");
        payload.AcceptsReturn = true; payload.MinHeight = 80;
        manual.Children.Add(payload);
        manualSend = Button("ارسال متن به سرور", () => Do(async () => { await r.SendCustomAsync(Val(payload)); Refresh(); }));
        manual.Children.Add(manualSend);
        detail.Children.Add(new TabControl { ItemsSource = new TabItem[] { RulePage(false), RulePage(true),
            new() { Header = "ارسال دستی", Content = Card(manual) } },
            SelectedIndex = p.Rules.Count > 0 && p.Rules.All(Editors.UsesAnalyzerFormat) ? 1 : 0 });
        var logs = Section("رویدادهای همین دستگاه", "«فرمان سرور» دریافت فرمان را نشان می‌دهد. «تأیید بروکر» یعنی بروکر گزارش دستگاه را پذیرفته است. برای بررسی نتیجهٔ تحلیل، وضعیت را در اپ ببینید.");
        logs.Children.Add(logText); detail.Children.Add(Card(logs)); Refresh();
    }
    private void Refresh()
    {
        if (selected == null) return;
        status.Text = selected.Status;
        if (connect != null) connect.Content = selected.Running ? "قطع اتصال" : "اتصال به سرور";
        if (manualSend != null) manualSend.IsEnabled = selected.CanSend;
        if (delete != null) delete.IsEnabled = !selected.Running;
        foreach (var it in fleet.Items.OfType<ListBoxItem>()) if (it.Tag is Guid id && devices.TryGetValue(id, out var dev))
        {
            if (it.Content is DockPanel row && row.Children[1] is TextBlock label)
            {
                var line = DeviceLabel(dev);
                if (line != label.Text) label.Text = line;
            }
        }
        var entries = selected.Events;
        var latest = entries.Length == 0 ? "هنوز رویدادی ثبت نشده است." : string.Join("\n\n", entries.Select(e => $"{e.Time:HH:mm:ss}   {e.Kind}   ·   {e.Message}"));
        if (latest != snapshot) { logText.Text = latest; snapshot = latest; }
    }
    private async Task Toggle(DeviceRuntime r)
    {
        if (r.Running) { await r.StopAsync(); Refresh(); return; }
        if (string.IsNullOrWhiteSpace(r.Profile.ClientId) || string.IsNullOrWhiteSpace(r.Profile.Username))
            throw new InvalidDataException("ابتدا در «ویرایش دستگاه و عملیات»، Client ID و نام کاربری MQTT دستگاه ثبت‌شده را وارد کنید.");
        var previous = DevicePasswords.Read(r.Profile);
        var credentials = await Editors.Credentials(this, r.Profile, previous);
        if (credentials is not { } c) return;
        r.Start(c.Password, c.Key, c.CaFile);
        try { if (c.Password != previous) DevicePasswords.Save(r.Profile, c.Password); }
        catch { await r.StopAsync(); throw; }
        Refresh();
    }

    private List<Device> All() => devices.Values.Select(r => r.Profile).ToList();

    private async Task Edit(DeviceRuntime? runtime, bool showRules = false)
    {
        var original = runtime?.Profile ?? Device.Sample() with { Id = Guid.NewGuid(), Name = "دستگاه جدید", Serial = "", Project = "", Rules = [] };
        var others = All().Where(p => p.Id != original.Id).ToList();
        var previous = runtime == null ? "" : DevicePasswords.Read(original);
        var result = await Editors.DeviceDialog(this, original, others, previous, runtime?.Running == true, showRules);
        if (result is not { } saved) return;
        var updated = saved.Profile;
        if (runtime != null && previous == saved.Password && original == (updated with { Rules = original.Rules })
            && original.Rules.SequenceEqual(updated.Rules)) return;
        Commit(original, previous, updated, saved.Password, others.Append(updated).ToList());
        if (runtime != null) await runtime.StopAsync();
        devices[updated.Id] = new(updated);
        Reload(updated.Id);
    }

    private static void Commit(Device old, string previous, Device updated, string password, List<Device> profiles)
    {
        bool updateSecret = previous.Length != 0 || password.Length != 0;
        if (updateSecret) DevicePasswords.Save(updated, password);
        try { Profiles.Write(Profiles.Database, profiles); }
        catch
        {
            if (updateSecret) DevicePasswords.Save(old, previous);
            throw;
        }
    }

    private async Task Remove(DeviceRuntime r)
    {
        if (!await Editors.Confirm(this, "دستگاه «" + r.Profile.Name + "» از این شبیه‌ساز حذف شود؟")) return;
        Commit(r.Profile, DevicePasswords.Read(r.Profile), r.Profile, "", All().Where(p => p.Id != r.Profile.Id).ToList());
        devices.Remove(r.Profile.Id); Reload(null);
    }
    private static readonly FilePickerFileType JsonFile = new("پروفایل شبیه‌ساز") { Patterns = ["*.json"] };
    private async Task Export()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "خروجی تنظیمات دمو", SuggestedFileName = "airngin-demo.json", DefaultExtension = "json", FileTypeChoices = [JsonFile] });
        if (file == null) return;
        var path = file.TryGetLocalPath() ?? throw new IOException("یک فایل محلی انتخاب کنید.");
        Profiles.Write(path, All()); notice.Text = "پروفایل‌ها بدون رمز اتصال ذخیره شدند.";
    }
    private async Task Import()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "ورود تنظیمات دمو", FileTypeFilter = [JsonFile] });
        if (files.Count == 0) return;
        var incoming = Profiles.Read(files[0].TryGetLocalPath() ?? throw new IOException("یک فایل محلی انتخاب کنید."));
        var fresh = incoming.Where(p => !devices.ContainsKey(p.Id)).ToList();
        if (incoming.Any(p => devices.TryGetValue(p.Id, out var old) && System.Text.Json.JsonSerializer.Serialize(p) != System.Text.Json.JsonSerializer.Serialize(old.Profile)))
            throw new InvalidDataException("یک شناسه با تنظیمات متفاوت از قبل موجود است. ابتدا پروفایل قبلی را بررسی کنید.");
        Profiles.Write(Profiles.Database, All().Concat(fresh).ToList());
        foreach (var p in fresh) devices.Add(p.Id, new(p)); Reload(fresh.FirstOrDefault()?.Id ?? selected?.Profile.Id);
        notice.Text = $"{fresh.Count} دستگاه جدید وارد شد.";
    }
}
