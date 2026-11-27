using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using static AirNgin.Simulator.Ui;

namespace AirNgin.Simulator;

internal static class Editors
{
    static string[] Modes => [Strings.Echo, Strings.Fixed, Strings.Current, Strings.Silent, Strings.CustomReply];
    public static bool UsesAnalyzerFormat(Rule rule) => rule.CustomInput || rule.Mode == ReplyMode.Custom;
    public static string Describe(Rule r) => $"{(r.CustomInput ? Strings.CustomInput : r.Key)}  ·  {(r.Match == null ? Strings.Any : (r.Match.Length == 0 ? Strings.Empty : r.Match))}  →  {Modes[(int)r.Mode]}" +
        (r.Mode is ReplyMode.Fixed or ReplyMode.Current or ReplyMode.Custom ? "  " + r.Value : "") +
        (r.ReplyKey.Length > 0 ? "  [" + r.ReplyKey + "]" : "") + (r.DelayMs > 0 ? $"  ·  {r.DelayMs} ms" : "");
    static async Task<Rule?> RuleDialog(Window parent, Rule? current, bool analyzer)
    {
        var w = Dialog(analyzer ? Strings.AnalyzerRule : Strings.DirectRule, 600, 690); var body = Stack(12); var err = Text("", 13, Brushes.Firebrick);
        var key = Box(current?.Key, "ch1"); var match = Box(current?.Match, "on");
        var input = new CheckBox { Content = Strings.CustomInput, IsChecked = current?.CustomInput ?? analyzer };
        var keyField = Field(Strings.OperationKey, key);
        var matchField = Field(Strings.IfEqual, match);
        var customHint = Text(Strings.PasteHelp, 13, Muted);
        var any = new CheckBox { Content = Strings.AnyInput, IsChecked = current is { Match: null } };
        var mode = new ComboBox { ItemsSource = Modes, SelectedIndex = (int)(current?.Mode ?? (analyzer ? ReplyMode.Custom : ReplyMode.Echo)), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var value = Box(current?.Value); var dest = Box(current?.ReplyKey, Strings.SameKey);
        var delay = new NumericUpDown { Minimum = 0, Maximum = 60000, Increment = 100, Value = current?.DelayMs ?? 0, FormatString = "0" };
        var valLabel = Field(Strings.ReplyValue, value);
        var destLabel = Field(Strings.ReplyKey, dest);
        void Sync()
        {
            bool literal = input.IsChecked == true;
            keyField.IsVisible = any.IsVisible = !literal;
            customHint.IsVisible = literal;
            match.AcceptsReturn = literal;
            match.MinHeight = literal ? 64 : 35;
            match.IsEnabled = literal || any.IsChecked != true;
            ((TextBlock)matchField.Children[0]).Text = literal ? Strings.ExactInput : Strings.IfEqual;
            dest.Watermark = literal ? Strings.OutputKey : Strings.SameKey;
            bool custom = mode.SelectedIndex == (int)ReplyMode.Custom;
            valLabel.IsVisible = mode.SelectedIndex is 1 or 2 || custom;
            value.AcceptsReturn = custom;
            value.MinHeight = custom ? 76 : 35;
            destLabel.IsVisible = !custom && mode.SelectedIndex != (int)ReplyMode.Silent;
            ((TextBlock)valLabel.Children[0]).Text = mode.SelectedIndex == 2 ? Strings.Initial : custom ? Strings.FullReply : Strings.ReplyValue;
            dest.IsEnabled = mode.SelectedIndex != 3;
        }
        input.IsCheckedChanged += (_, _) =>
        {
            if (input.IsChecked == true && current == null && mode.SelectedIndex == (int)ReplyMode.Echo) mode.SelectedIndex = (int)ReplyMode.Custom;
            Sync();
        };
        any.IsCheckedChanged += (_, _) => Sync(); mode.SelectionChanged += (_, _) => Sync(); Sync();
        var incoming = Section(Strings.Incoming, analyzer
            ? Strings.AnalyzerInputHelp
            : Strings.DirectInputHelp);
        foreach (var c in new Control[]{input,keyField,any,matchField,customHint}) incoming.Children.Add(c);
        var outgoing = Section(Strings.DeviceReply, analyzer
            ? Strings.AnalyzerReplyHelp
            : Strings.DirectReplyHelp);
        var responseHelp = Text("", 13, Muted);
        void ExplainMode() => responseHelp.Text = mode.SelectedIndex switch
        {
            0 => Strings.EchoHelp,
            1 => Strings.FixedHelp,
            2 => Strings.CurrentHelp,
            3 => Strings.SilentHelp,
            _ => Strings.LiteralHelp
        };
        mode.SelectionChanged += (_, _) => ExplainMode(); ExplainMode();
        foreach (var c in new Control[]{Field(Strings.ReplyType,mode),responseHelp,valLabel,destLabel,Field(Strings.Delay,delay)}) outgoing.Children.Add(c);
        body.Children.Add(Card(incoming)); body.Children.Add(Card(outgoing)); body.Children.Add(err);
        Form(w, body, Row(Button(Strings.SaveRule, () =>
        {
            try
            {
                var literal = input.IsChecked == true;
                var r = new Rule(literal ? "" : Val(key).Trim(), !literal && any.IsChecked == true ? null : Val(match), (ReplyMode)mode.SelectedIndex, Val(value), mode.SelectedIndex == (int)ReplyMode.Custom ? "" : Val(dest).Trim(), (int)(delay.Value ?? 0)) { CustomInput = literal };
                Profiles.Validate([Device.Sample() with { Rules = [r] }]); w.Close(r);
            }
            catch (Exception e) { err.Text = e.Message; }
        }, true), Button(Strings.Cancel, () => w.Close())));
        return await w.ShowDialog<Rule?>(parent);
    }

    private static StackPanel PasswordField(TextBox input)
    {
        var field = Field(Strings.Password, input);
        var reveal = new CheckBox { Content = Strings.Reveal };
        var copy = new Button { Content = Strings.Copy, IsVisible = false };
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
                feedback.Text = Strings.Copied;
            }
            catch { feedback.Text = Strings.CopyFailed; }
        };
        field.Children.Add(Row(reveal, copy, feedback));
        return field;
    }

    public static async Task<(Device Profile, string Password)?> DeviceDialog(Window parent, Device source, IEnumerable<Device> others, string savedPassword, bool running, bool showRules = false)
    {
        var w = Dialog(Strings.DetailsPrefix + Language.DisplayName(source), 700, 730); var p = Stack(12); var err = Text("", 13, Brushes.Firebrick);
        if (running) p.Children.Add(Text(Strings.WillDisconnect, 13, Muted));
        var name = Box(source.Name, latin: false); var serial = Box(source.Serial, "lamp-1"); var project = Box(source.Project);
        var host = Box(source.Host); var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = source.Port, FormatString = "0" };
        var client = Box(source.ClientId, Strings.ClientHint); var user = Box(source.Username);
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
        var aes = new CheckBox { Content = Strings.Encryption, IsChecked = source.Aes };
        var rules = source.Rules.ToList();
        var list = new ListBox { Height = 128 };
        void Refresh(int i) { list.ItemsSource = rules.Select(Describe).ToArray(); if (i >= 0 && i < rules.Count) list.SelectedIndex = i; }
        Refresh(0);
        async Task Edit(bool add, bool analyzer = false)
        {
            var i = list.SelectedIndex; if (!add && i < 0) return;
            var result = await RuleDialog(w, add ? null : rules[i], add ? analyzer : UsesAnalyzerFormat(rules[i])); if (result == null) return;
            if (rules.Where((_, n) => add || n != i).Any(r => r.Key == result.Key && r.Match == result.Match)) { err.Text = Strings.DuplicateRule; return; }
            if (add) { rules.Add(result); i = rules.Count - 1; } else rules[i] = result;
            err.Text = ""; Refresh(i);
        }
        var deviceFields = Section(Strings.Settings, Strings.SettingsHelp);
        deviceFields.Children.Add(Field(Strings.DisplayName,name));
        deviceFields.Children.Add(Columns(Field(Strings.Serial,serial),Field(Strings.Project,project)));
        var network = Section(Strings.MqttHeading, Strings.MqttHelp);
        network.Children.Add(Text(Strings.RegisterAgain, 13, Muted));
        network.Children.Add(new HyperlinkButton { Content = Strings.OpenConsole, NavigateUri = new Uri("https://console.airngin.com/") });
        foreach (var c in new Control[]{Columns(Field(Strings.Host,host),Field(Strings.Port,port)),Columns(Field(Strings.ClientId,client),Field(Strings.Username,user)),
            PasswordField(password),Text(Strings.PasswordHelp,13,Muted),
            aes,Text(Strings.AesHelp,13,Muted)}) network.Children.Add(c);
        var settings = Stack(12); settings.Children.Add(Card(deviceFields)); settings.Children.Add(Card(network));
        var responses = Section(Strings.Commands, Strings.RulesHelp);
        responses.Children.Add(Row(Button(Strings.AddDirect,async()=>await Edit(true)),Button(Strings.AddAnalyzer,async()=>await Edit(true,true))));
        responses.Children.Add(list);
        responses.Children.Add(Row(Button(Strings.EditRule,async()=>await Edit(false)),
            Button(Strings.RemoveRule,()=>{var i=list.SelectedIndex;if(i>=0){rules.RemoveAt(i);Refresh(Math.Min(i,rules.Count-1));}})));
        responses.Children.Add(Text(Strings.SaveHint,13,Muted));
        var tabs = new TabControl { ItemsSource = new TabItem[] {
            new() { Header = Strings.SettingsTab, Content = settings },
            new() { Header = Strings.RulesTab, Content = Card(responses) }
        }, SelectedIndex = showRules ? 1 : 0 };
        p.Children.Add(tabs); p.Children.Add(err);
        Form(w, p, Row(Button(Strings.SaveDevice, () =>
        {
            try
            {
                var result = new Device(source.Id, Val(name).Trim(), Val(serial).Trim(), Val(project).Trim(), Val(host).Trim(),
                    (int)(port.Value ?? 8883), Val(client).Trim(), Val(user).Trim(), aes.IsChecked == true, rules);
                Profiles.Validate(others.Append(result)); w.Close((result, Val(password)));
            }
            catch (Exception e) { err.Text = e.Message; }
        }, true), Button(Strings.Cancel, () => w.Close())));
        return await w.ShowDialog<(Device, string)?>(parent);
    }

    public static async Task<(string Password, string Key, string CaFile)?> Credentials(Window parent, Device d, string savedPassword)
    {
        var w = Dialog(Strings.ConnectPrefix + Language.DisplayName(d), 490, d.Aes ? 480 : 390); var p = Stack(12);
        var pwd = Box(savedPassword); pwd.PasswordChar = '●'; var key = Box(); key.PasswordChar = '●';
        p.Children.Add(Text(d.Host + ":" + d.Port + "   ·   " + d.Serial, 15, null, true));
        p.Children.Add(PasswordField(pwd));
        if (d.Aes) p.Children.Add(Field(Strings.AesKey, key));
        string caFile = "";
        var ca = Text(Strings.SystemTrust, 12, Muted);
        p.Children.Add(Row(Button(Strings.ChooseCa, async () =>
        {
            var files = await w.StorageProvider.OpenFilePickerAsync(new() { Title = Strings.PickPem });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string path) { caFile = path; ca.Text = Path.GetFileName(path); }
        }), ca));
        Form(w, p, Row(Button(Strings.ConnectBroker, () => w.Close((Val(pwd), Val(key), caFile)), true), Button(Strings.Cancel, () => w.Close())));
        return await w.ShowDialog<(string, string, string)?>(parent);
    }

    public static async Task<bool> Confirm(Window parent, string message)
    {
        var w = Dialog(Strings.RemoveDevice, 420, 185); var p = Stack(); p.Children.Add(Text(message));
        Form(w, p, Row(Button(Strings.Delete, () => w.Close(true)), Button(Strings.Cancel, () => w.Close(false))));
        return await w.ShowDialog<bool>(parent);
    }
}
