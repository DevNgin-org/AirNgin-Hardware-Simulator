using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirNgin.Simulator;

public enum ReplyMode { Echo, Fixed, Current, Silent, Custom }
public sealed record Rule(string Key, string? Match, ReplyMode Mode, string Value, string ReplyKey, int DelayMs)
{
    public bool CustomInput { get; init; }
    [JsonIgnore] public string OutputKey => string.IsNullOrEmpty(ReplyKey) ? Key : ReplyKey;
}
public sealed record Device(Guid Id, string Name, string Serial, string Project, string Host, int Port,
    string ClientId, string Username, bool Aes, List<Rule> Rules)
{
    [JsonIgnore] public string Input => $"{Project}/ServerToDevice/{Serial}" + (Aes ? "/AES" : "");
    [JsonIgnore] public string Output => $"{Project}/DeviceToServer" + (Aes ? "/AES" : "");
    public static Device Sample() => new(Guid.Parse("00000000-0000-4600-8000-000000000001"), "چراغ اتاق", "DEMO0000000001", "DEMO", "mqtt.airngin.com",
        8883, "", "", false, [new("ch1", "on", ReplyMode.Fixed, "on", "", 0),
        new("ch1", "off", ReplyMode.Fixed, "off", "", 0), new("ch1", "get", ReplyMode.Current, "off", "", 0)]);
}
public sealed record Library(int Format, List<Device> Devices);
public static class Profiles
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirNgin", "HardwareSimulator");
    public static string Database => Path.Combine(Folder, "devices.json");

    public static void CheckText(string? text, int limit, bool empty = false)
    {
        if (text is null || (!empty && string.IsNullOrWhiteSpace(text)) || text.Contains('\0') || Encoding.UTF8.GetByteCount(text) > limit)
            throw new InvalidDataException($"متن خالی یا نامعتبر است؛ حداکثر {limit} بایت مجاز است.");
    }
    public static void Validate(IEnumerable<Device> devices)
    {
        var ids = new HashSet<Guid>();
        var addresses = new HashSet<string>();
        var clients = new HashSet<string>();
        foreach (var p in devices)
        {
            if (p is null || p.Id == Guid.Empty || !ids.Add(p.Id)) throw new InvalidDataException("شناسهٔ دستگاه تکراری یا نامعتبر است.");
            CheckText(p.Name, 120); CheckText(p.Project, 128); CheckText(p.ClientId, 256, true); CheckText(p.Username, 256, true);
            if (p.Serial is null || p.Serial.Contains('\0')) throw new InvalidDataException("سریال دستگاه را وارد کنید.");
            if (p.Project.Any(c => c is '/' or '#' or '+' || char.IsWhiteSpace(c)) || Uri.CheckHostName(p.Host) == UriHostNameType.Unknown || p.Port is < 1 or > 65535)
                throw new InvalidDataException("کد پروژه، نام میزبان یا پورت معتبر نیست.");
            var broker = p.Host.ToLowerInvariant() + ":" + p.Port + "/";
            if ((p.Serial.Length > 0 && !addresses.Add(broker + p.Project + "/" + p.Serial)) || (p.ClientId.Length > 0 && !clients.Add(broker + p.ClientId)))
                throw new InvalidDataException("این سریال در پروژه یا Client ID روی این بروکر قبلاً تعریف شده است.");
            var patterns = new HashSet<(string, string?)>();
            if (p.Rules is null) throw new InvalidDataException("فهرست عملیات موجود نیست.");
            foreach (var r in p.Rules)
            {
                if (r is null) throw new InvalidDataException("قانون خالی است.");
                CheckText(r.Key, 64, r.CustomInput); CheckText(r.Value, r.Mode == ReplyMode.Custom ? 4096 : 512, true); CheckText(r.ReplyKey, 64, true);
                if (r.CustomInput)
                {
                    if (r.Key != "" || string.IsNullOrEmpty(r.Match)) throw new InvalidDataException("متن دقیق ورودی کاستوم را وارد کنید.");
                    CheckText(r.Match, 4096, true);
                    if (r.Mode is not (ReplyMode.Custom or ReplyMode.Silent)) CheckText(r.ReplyKey, 64);
                }
                else if (r.Match != null) CheckText(r.Match, 512, true);
                if (!Enum.IsDefined(r.Mode) || r.DelayMs is < 0 or > 60000 || !patterns.Add((r.Key, r.Match)))
                    throw new InvalidDataException("شرط عملیات تکراری، نوع پاسخ نامعتبر یا تأخیر خارج از محدودهٔ ۰ تا ۶۰۰۰۰ است.");
            }
        }
    }
    public static List<Device> Read(string path)
    {
        if (new FileInfo(path).Length > 4_000_000) throw new InvalidDataException("فایل پروفایل بیش از حد بزرگ است.");
        var doc = JsonSerializer.Deserialize<Library>(File.ReadAllText(path), Json);
        if (doc?.Format != 1 || doc.Devices is null) throw new InvalidDataException("قالب فایل پروفایل پشتیبانی نمی‌شود.");
        Validate(doc.Devices);
        return doc.Devices;
    }
    public static void Write(string path, List<Device> devices)
    {
        Validate(devices);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Library(1, devices), Json);
            if (bytes.Length > 4_000_000) throw new InvalidDataException("مجموع پروفایل‌ها بیش از حد بزرگ است.");
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }
}
