using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using static AirNgin.Simulator.Ui;

namespace AirNgin.Simulator;

internal static class Editors
{
    static readonly string[] Modes = ["بازگرداندن مقدار فرمان", "پاسخ ثابت", "خواندن آخرین وضعیت", "بدون پاسخ", "کاستوم (متن دلخواه)"];
    public static bool UsesAnalyzerFormat(Rule rule) => rule.CustomInput || rule.Mode == ReplyMode.Custom;
    public static string Describe(Rule r) => $"{(r.CustomInput ? "ورودی کاستوم" : r.Key)}  ·  {(r.Match == null ? "هر مقدار" : (r.Match.Length == 0 ? "رشتهٔ خالی" : r.Match))}  →  {Modes[(int)r.Mode]}" +
        (r.Mode is ReplyMode.Fixed or ReplyMode.Current or ReplyMode.Custom ? "  " + r.Value : "") +
        (r.ReplyKey.Length > 0 ? "  [" + r.ReplyKey + "]" : "") + (r.DelayMs > 0 ? $"  ·  {r.DelayMs} ms" : "");
    static async Task<Rule?> RuleDialog(Window parent, Rule? current, bool analyzer)
    {
        var w = Dialog(analyzer ? "قانون پاسخ Analyzer" : "قانون ارتباط مستقیم", 600, 690); var body = Stack(12); var err = Text("", 13, Brushes.Firebrick);
        var key = Box(current?.Key, "ch1"); var match = Box(current?.Match, "on");
        var input = new CheckBox { Content = "ورودی کاستوم", IsChecked = current?.CustomInput ?? analyzer };
        var keyField = Field("کلید عملیات", key);
        var matchField = Field("اگر مقدار ورودی برابر بود با", match);
        var customHint = Text("متن فرمان را عیناً بچسبانید. اگر در رویداد فرمان یک رشتهٔ HEX می‌بینید، همان رشته را با حفظ حروف و فاصله‌ها کپی کنید.", 13, Muted);
        var any = new CheckBox { Content = "هر مقدار ورودی", IsChecked = current is { Match: null } };
        var mode = new ComboBox { ItemsSource = Modes, SelectedIndex = (int)(current?.Mode ?? (analyzer ? ReplyMode.Custom : ReplyMode.Echo)), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var value = Box(current?.Value); var dest = Box(current?.ReplyKey, "پیش‌فرض: همان کلید ورودی");
        var delay = new NumericUpDown { Minimum = 0, Maximum = 60000, Increment = 100, Value = current?.DelayMs ?? 0, FormatString = "0" };
        var valLabel = Field("مقدار پاسخ", value);
        var destLabel = Field("کلید پاسخ", dest);
        void Sync()
        {
            bool literal = input.IsChecked == true;
            keyField.IsVisible = any.IsVisible = !literal;
            customHint.IsVisible = literal;
            match.AcceptsReturn = literal;
            match.MinHeight = literal ? 64 : 35;
            match.IsEnabled = literal || any.IsChecked != true;
            ((TextBlock)matchField.Children[0]).Text = literal ? "متن دقیق ورودی" : "اگر مقدار ورودی برابر بود با";
            dest.Watermark = literal ? "کلید خروجی" : "پیش‌فرض: همان کلید ورودی";
            bool custom = mode.SelectedIndex == (int)ReplyMode.Custom;
            valLabel.IsVisible = mode.SelectedIndex is 1 or 2 || custom;
            value.AcceptsReturn = custom;
            value.MinHeight = custom ? 76 : 35;
            destLabel.IsVisible = !custom && mode.SelectedIndex != (int)ReplyMode.Silent;
            ((TextBlock)valLabel.Children[0]).Text = mode.SelectedIndex == 2 ? "مقدار اولیه، تا پیش از اولین تغییر" : custom ? "متن کامل پاسخ" : "مقدار پاسخ";
            dest.IsEnabled = mode.SelectedIndex != 3;
        }
        input.IsCheckedChanged += (_, _) =>
        {
            if (input.IsChecked == true && current == null && mode.SelectedIndex == (int)ReplyMode.Echo) mode.SelectedIndex = (int)ReplyMode.Custom;
            Sync();
        };
        any.IsCheckedChanged += (_, _) => Sync(); mode.SelectionChanged += (_, _) => Sync(); Sync();
        var incoming = Section("فرمان دریافتی", analyzer
            ? "فرمانی را وارد کنید که اسکریپت تولید فرمان برای این دستگاه می‌سازد. برای روشن و خاموش دو قانون جدا تعریف کنید."
            : "کلید عملیات دستگاه را وارد کنید. برای چراغ می‌توانید کلید ch1 و مقدار on را انتخاب کنید.");
        foreach (var c in new Control[]{input,keyField,any,matchField,customHint}) incoming.Children.Add(c);
        var outgoing = Section("پاسخ دستگاه", analyzer
            ? "متن کامل گزارش را مطابق ورودی Analyzer بنویسید. اگر دستگاه از CRC استفاده می‌کند، CRC فریم پاسخ را هم در متن بگذارید."
            : "انتخاب کنید دستگاه پس از دریافت فرمان، چه وضعیتی به اپ گزارش کند.");
        var responseHelp = Text("", 13, Muted);
        void ExplainMode() => responseHelp.Text = mode.SelectedIndex switch
        {
            0 => "برای دیمر یا کلید، همان مقدار دریافتی در پاسخ قرار می‌گیرد.",
            1 => "برای هر فرمان منطبق، همین مقدار گزارش می‌شود.",
            2 => "آخرین مقدار کلید پاسخ خوانده می‌شود. تا اولین تغییر، مقدار اولیه گزارش می‌شود.",
            3 => "فرمان در رویدادها ثبت می‌شود و دستگاه ساکت می‌ماند.",
            _ => "متن یا JSON دقیقاً همان‌طور که نوشته‌اید ارسال می‌شود. کلید پاسخ لازم نیست."
        };
        mode.SelectionChanged += (_, _) => ExplainMode(); ExplainMode();
        foreach (var c in new Control[]{Field("نوع پاسخ",mode),responseHelp,valLabel,destLabel,Field("تأخیر پاسخ (میلی‌ثانیه)",delay)}) outgoing.Children.Add(c);
        body.Children.Add(Card(incoming)); body.Children.Add(Card(outgoing)); body.Children.Add(err);
        Form(w, body, Row(Button("ثبت قانون", () =>
        {
            try
            {
                var literal = input.IsChecked == true;
                var r = new Rule(literal ? "" : Val(key).Trim(), !literal && any.IsChecked == true ? null : Val(match), (ReplyMode)mode.SelectedIndex, Val(value), mode.SelectedIndex == (int)ReplyMode.Custom ? "" : Val(dest).Trim(), (int)(delay.Value ?? 0)) { CustomInput = literal };
                Profiles.Validate([Device.Sample() with { Rules = [r] }]); w.Close(r);
            }
            catch (Exception e) { err.Text = e.Message; }
        }, true), Button("انصراف", () => w.Close())));
        return await w.ShowDialog<Rule?>(parent);
    }

    private static StackPanel PasswordField(TextBox input)
    {
        var field = Field("رمز عبور MQTT", input);
        var reveal = new CheckBox { Content = "نمایش رمز" };
        var copy = new Button { Content = "کپی رمز", IsVisible = false };
        var feedback = Text("", 12, Muted);
        reveal.IsCheckedChanged += (_, _) =>
        {
            input.RevealPassword = reveal.IsChecked == true;
            copy.IsVisible = input.RevealPassword;
            feedback.Text = "";
        };
        copy.Click += async (_, _) =>
        {
            try
            {
                var clipboard = TopLevel.GetTopLevel(input)?.Clipboard;
                if (clipboard == null) throw new InvalidOperationException();
                await clipboard.SetTextAsync(Val(input));
                feedback.Text = "کپی شد";
            }
            catch { feedback.Text = "کپی رمز ممکن نشد."; }
        };
        field.Children.Add(Row(reveal, copy, feedback));
        return field;
    }

    public static async Task<(Device Profile, string Password)?> DeviceDialog(Window parent, Device source, IEnumerable<Device> others, string savedPassword, bool running, bool showRules = false)
    {
        var w = Dialog("مشخصات دستگاه · " + source.Name, 700, 730); var p = Stack(12); var err = Text("", 13, Brushes.Firebrick);
        if (running) p.Children.Add(Text("ذخیرهٔ تغییرات، اتصال همین دستگاه را متوقف می‌کند.", 13, Muted));
        var name = Box(source.Name, latin: false); var serial = Box(source.Serial, "lamp-1"); var project = Box(source.Project);
        var host = Box(source.Host); var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = source.Port, FormatString = "0" };
        var client = Box(source.ClientId, "Client ID دریافتی از پروژه"); var user = Box(source.Username);
        var password = Box(savedPassword);
        password.PasswordChar = '●';
        // An existing password belongs to the original broker and identity.
        bool inheritedPassword = savedPassword.Length > 0;
        password.TextChanged += (_, _) =>
        {
            if (Val(password) != savedPassword) inheritedPassword = false;
        };
        void ClearInheritedPassword()
        {
            if (!inheritedPassword) return;
            inheritedPassword = false;
            password.Text = "";
        }
        foreach (var identityField in new[] { host, user, client, project, serial })
            identityField.TextChanged += (_, _) =>
            {
                if (Val(host) != source.Host || Val(user) != source.Username || Val(client) != source.ClientId ||
                    Val(project) != source.Project || Val(serial) != source.Serial) ClearInheritedPassword();
            };
        port.ValueChanged += (_, _) => { if (port.Value != source.Port) ClearInheritedPassword(); };
        var aes = new CheckBox { Content = "رمزنگاری لایهٔ کاربرد AES-256-GCM", IsChecked = source.Aes };
        var rules = source.Rules.ToList();
        var list = new ListBox { Height = 128 };
        void Refresh(int i) { list.ItemsSource = rules.Select(Describe).ToArray(); if (i >= 0 && i < rules.Count) list.SelectedIndex = i; }
        Refresh(0);
        async Task Edit(bool add, bool analyzer = false)
        {
            var i = list.SelectedIndex; if (!add && i < 0) return;
            var result = await RuleDialog(w, add ? null : rules[i], add ? analyzer : UsesAnalyzerFormat(rules[i])); if (result == null) return;
            if (rules.Where((_, n) => add || n != i).Any(r => r.Key == result.Key && r.Match == result.Match)) { err.Text = "قانونی با همین کلید و شرط وجود دارد."; return; }
            if (add) { rules.Add(result); i = rules.Count - 1; } else rules[i] = result;
            err.Text = ""; Refresh(i);
        }
        var deviceFields = Section("مشخصات دستگاه", "نام نمایشی برای پیدا کردن دستگاه در شبیه‌ساز است. برای دریافت فرمان اپ، کد پروژه و سریال ثبت‌شده در پنل را وارد کنید.");
        deviceFields.Children.Add(Field("نام نمایشی",name));
        deviceFields.Children.Add(Columns(Field("سریال دستگاه",serial),Field("کد پروژه",project)));
        var network = Section("اتصال به MQTT", "میزبان، شناسهٔ اتصال و حساب MQTT همین دستگاه را از پنل بردارید. برای آزمایش محلی نیازی به اتصال ندارید.");
        foreach (var c in new Control[]{Columns(Field("میزبان MQTT",host),Field("پورت TLS",port)),Columns(Field("شناسهٔ اتصال (Client ID)",client),Field("نام کاربری MQTT",user)),
            PasswordField(password),Text("رمز روی همین رایانه ذخیره می‌شود. برای پاک کردن آن، فیلد را خالی و دستگاه را ذخیره کنید.",13,Muted),
            aes,Text("AES را برای دستگاهی فعال کنید که در پنل با رمزنگاری لایهٔ کاربرد ثبت شده است.",13,Muted)}) network.Children.Add(c);
        var settings = Stack(12); settings.Children.Add(Card(deviceFields)); settings.Children.Add(Card(network));
        var responses = Section("فرمان‌ها و پاسخ‌های دستگاه", "در ارتباط مستقیم، کلید و مقدار عملیات را تعریف کنید. برای Analyzer، متن فرمان و گزارش مورد انتظار اسکریپت را وارد کنید. هر دستگاه می‌تواند هر دو نوع قانون را داشته باشد.");
        responses.Children.Add(Row(Button("قانون مستقیم جدید",async()=>await Edit(true)),Button("قانون Analyzer جدید",async()=>await Edit(true,true))));
        responses.Children.Add(list);
        responses.Children.Add(Row(Button("ویرایش قانون انتخابی",async()=>await Edit(false)),
            Button("حذف قانون انتخابی",()=>{var i=list.SelectedIndex;if(i>=0){rules.RemoveAt(i);Refresh(Math.Min(i,rules.Count-1));}})));
        responses.Children.Add(Text("پس از ثبت قانون، «ذخیرهٔ دستگاه» را بزنید تا تغییرات برای دفعهٔ بعد هم بماند.",13,Muted));
        var tabs = new TabControl { ItemsSource = new TabItem[] {
            new() { Header = "مشخصات و اتصال", Content = settings },
            new() { Header = "قوانین پاسخ", Content = Card(responses) }
        }, SelectedIndex = showRules ? 1 : 0 };
        p.Children.Add(tabs); p.Children.Add(err);
        Form(w, p, Row(Button("ذخیرهٔ دستگاه", () =>
        {
            try
            {
                var result = new Device(source.Id, Val(name).Trim(), Val(serial).Trim(), Val(project).Trim(), Val(host).Trim(),
                    (int)(port.Value ?? 8883), Val(client).Trim(), Val(user).Trim(), aes.IsChecked == true, rules);
                Profiles.Validate(others.Append(result)); w.Close((result, Val(password)));
            }
            catch (Exception e) { err.Text = e.Message; }
        }, true), Button("انصراف", () => w.Close())));
        return await w.ShowDialog<(Device, string)?>(parent);
    }

    public static async Task<(string Password, string Key, string CaFile)?> Credentials(Window parent, Device d, string savedPassword)
    {
        var w = Dialog("اتصال " + d.Name, 490, d.Aes ? 480 : 390); var p = Stack(12);
        var pwd = Box(savedPassword); pwd.PasswordChar = '●'; var key = Box(); key.PasswordChar = '●';
        p.Children.Add(Text(d.Host + ":" + d.Port + "   ·   " + d.Serial, 15, null, true));
        p.Children.Add(PasswordField(pwd));
        if (d.Aes) p.Children.Add(Field("کلید Base64 دستگاه", key));
        string caFile = "";
        var ca = Text("اعتماد به گواهی‌های سیستم", 12, Muted);
        p.Children.Add(Row(Button("انتخاب CA خصوصی (اختیاری)", async () =>
        {
            var files = await w.StorageProvider.OpenFilePickerAsync(new() { Title = "انتخاب گواهی CA با قالب PEM" });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string path) { caFile = path; ca.Text = Path.GetFileName(path); }
        }), ca));
        Form(w, p, Row(Button("اتصال به بروکر", () => w.Close((Val(pwd), Val(key), caFile)), true), Button("انصراف", () => w.Close())));
        return await w.ShowDialog<(string, string, string)?>(parent);
    }

    public static async Task<bool> Confirm(Window parent, string message)
    {
        var w = Dialog("حذف دستگاه", 420, 185); var p = Stack(); p.Children.Add(Text(message));
        Form(w, p, Row(Button("حذف", () => w.Close(true)), Button("انصراف", () => w.Close(false))));
        return await w.ShowDialog<bool>(parent);
    }
}
