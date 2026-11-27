using System.Security.Cryptography;
using System.Text.Json;

namespace AirNgin.Simulator;

public sealed record Pair(string key, string value);
public sealed record Incoming(string Text, Pair? Operation);
public static class Wire
{
    public static Incoming Command(string serial, byte[] payload)
    {
        if (payload.Length is 0 or > 4096) throw new InvalidDataException("اندازهٔ فرمان نامعتبر است.");
        using var outer = JsonDocument.Parse(payload);
        var body = outer.RootElement;
        string Read(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException("مقدار باید متن باشد.");
        if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Count() != 2 || Read(body, "deviceSerial") != serial)
            throw new InvalidDataException("فرمان برای این سریال نیست یا قالب نادرست است.");
        return ParseInput(Read(body, "data"));
    }
    public static Incoming ParseInput(string text)
    {
        Profiles.CheckText(text, 4096, true);
        if (!text.AsSpan().TrimStart().StartsWith("{")) return new(text, null);
        try
        {
            using var inner = JsonDocument.Parse(text);
            var obj = inner.RootElement;
            if (obj.EnumerateObject().Count() == 2 &&
                (obj.TryGetProperty("type", out var key) || obj.TryGetProperty("key", out key)) &&
                key.ValueKind == JsonValueKind.String && obj.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
            {
                var op = new Pair(key.GetString()!, value.GetString()!);
                return new(text, op);
            }
        }
        catch (JsonException) { /* Custom text may contain literal braces. */ }
        return new(text, null);
    }
    public static byte[] Sample(string serial, Pair command) =>
        JsonSerializer.SerializeToUtf8Bytes(new { deviceSerial = serial, data = JsonSerializer.Serialize(command) });
    public static byte[] Report(Pair result) => JsonSerializer.SerializeToUtf8Bytes(new[] { result });
}
public sealed class Envelope(byte[] key, string serial) : IDisposable
{
    private readonly Dictionary<string, long> seen = new();
    public byte[] Seal(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[data.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, cipher, tag);
        return JsonSerializer.SerializeToUtf8Bytes(new { nonce, ciphertext = cipher, tag, version = "1.0", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), deviceSerial = serial, more = "" });
    }
    public byte[] Open(byte[] data)
    {
        using var doc = JsonDocument.Parse(data);
        var e = doc.RootElement;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ts = e.GetProperty("timestamp").GetInt64();
        if (ts < now - 60 || ts > now + 60 || e.GetProperty("version").GetString() != "1.0" ||
            (e.TryGetProperty("deviceSerial", out var sn) && !string.IsNullOrEmpty(sn.GetString()) && sn.GetString() != serial))
            throw new InvalidDataException("زمان، نسخه یا سریال بستهٔ AES نامعتبر است.");
        var nonce = e.GetProperty("nonce").GetBytesFromBase64(); var cipher = e.GetProperty("ciphertext").GetBytesFromBase64();
        var tag = e.GetProperty("tag").GetBytesFromBase64();
        if (nonce.Length != 12 || tag.Length != 16 || cipher.Length is 0 or > 4096) throw new InvalidDataException("اندازهٔ بستهٔ AES نامعتبر است.");
        string id = Convert.ToHexString(nonce);
        foreach (var old in seen.Where(x => now - x.Value > 130).Select(x => x.Key).ToArray()) seen.Remove(old);
        if (seen.ContainsKey(id) || seen.Count >= 64) throw new InvalidDataException("بستهٔ تکراری یا پنجرهٔ AES پر است.");
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, plain);
        seen.Add(id, now);
        return plain;
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(key);
}
