using System.Collections.Concurrent;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace AirNgin.Simulator;

public sealed record Entry(DateTimeOffset Time, string Kind, string Message);
public sealed class Behavior(Device device)
{
    private readonly ConcurrentDictionary<string, string> values = new();
    public IEnumerable<KeyValuePair<string, string>> Values => values.OrderBy(x => x.Key, StringComparer.Ordinal);
    public Rule? Select(Incoming command)
    {
        var literal = device.Rules.Find(r => r.CustomInput && r.Match == command.Text);
        if (literal != null || command.Operation == null) return literal;
        var op = command.Operation;
        bool standard = !string.IsNullOrWhiteSpace(op.key) && !op.key.Contains('\0') && !op.value.Contains('\0') &&
            Encoding.UTF8.GetByteCount(op.key) <= 64 && Encoding.UTF8.GetByteCount(op.value) <= 512;
        return device.Rules.Find(r => standard && !r.CustomInput && r.Key == op.key && r.Match == op.value) ??
            device.Rules.Find(r => r.CustomInput && r.Match == op.value) ??
            device.Rules.Find(r => standard && !r.CustomInput && r.Key == op.key && r.Match == null);
    }
    public Pair? Apply(Rule rule, Incoming command)
    {
        if (rule.Mode == ReplyMode.Silent) return null;
        if (rule.Mode == ReplyMode.Custom) return new Pair("", rule.Value);
        var value = rule.Mode switch
        {
            ReplyMode.Echo => rule.CustomInput ? rule.Match! : command.Operation!.value,
            ReplyMode.Fixed => rule.Value,
            ReplyMode.Current => values.GetValueOrDefault(rule.OutputKey, rule.Value),
            _ => throw new InvalidDataException("نوع پاسخ ناشناخته است.")
        };
        var result = new Pair(rule.OutputKey, value);
        values[result.key] = result.value;
        return result;
    }
}
public sealed class DeviceRuntime(Device device)
{
    public Device Profile => device;
    private readonly object gate = new();
    private readonly Queue<Entry> events = new();
    private readonly Behavior preview = new(device), hardware = new(device);
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private sealed record Packet(byte[] Bytes, TaskCompletionSource? Sent = null);
    private ChannelWriter<Packet>? outgoing;
    private Task? loop;
    public bool CanSend { get { lock (gate) return outgoing != null; } }
    public Task SendCustomAsync(string text)
    {
        Profiles.CheckText(text, 4096, true);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (outgoing == null) throw new InvalidOperationException("ابتدا دستگاه را به سرور وصل کنید.");
            if (!outgoing.TryWrite(new Packet(Encoding.UTF8.GetBytes(text), sent)))
                throw new IOException("صف ارسال پر است؛ دوباره تلاش کنید.");
        }
        return sent.Task;
    }
    private string status = "متوقف";
    public string Status { get { lock (gate) return status; } }
    public bool Running { get { lock (gate) return lifetime != null; } }
    public Entry[] Events { get { lock (gate) return events.Reverse().ToArray(); } }
    public void Log(string kind, string message)
    {
        lock (gate) { if (events.Count >= 200) events.Dequeue(); events.Enqueue(new(DateTimeOffset.Now, kind, message)); }
    }
    private void State(string value) { lock (gate) status = value; }
    public Task<Pair?> PreviewAsync(Pair input, CancellationToken cancel) =>
        Preview(Wire.Command(device.Serial, Wire.Sample(device.Serial, input)), cancel);
    public Task<Pair?> PreviewCustomAsync(string input, CancellationToken cancel) =>
        Preview(Wire.ParseInput(input), cancel);
    private async Task<Pair?> Preview(Incoming cmd, CancellationToken cancel)
    {
        await previewGate.WaitAsync(cancel);
        try { return (await Respond(preview, cmd, "تست محلی", cancel))?.Result; }
        finally { previewGate.Release(); }
    }
    private async Task<(Pair Result, byte[] Bytes)?> Respond(Behavior behavior, Incoming cmd, string source, CancellationToken cancel)
    {
        Log(source, cmd.Operation is { } op ? $"{op.key} ← {op.value}" : cmd.Text);
        var rule = behavior.Select(cmd);
        if (rule == null) { Log(source, "برای این فرمان قانونی تعریف نشده است."); return null; }
        if (rule.DelayMs > 0) await Task.Delay(rule.DelayMs, cancel);
        var reply = behavior.Apply(rule, cmd);
        if (reply == null) { Log(source, "طبق قانون، پاسخی ارسال نمی‌شود."); return null; }
        var bytes = rule.Mode == ReplyMode.Custom ? Encoding.UTF8.GetBytes(reply.value) : Wire.Report(reply);
        if (source == "تست محلی") Log(source, "پاسخ: " + Encoding.UTF8.GetString(bytes));
        return (reply, bytes);
    }
    public void Start(string password, string aesKey, string caFile = "")
    {
        if (string.IsNullOrWhiteSpace(device.ClientId) || string.IsNullOrWhiteSpace(device.Username) || string.IsNullOrEmpty(password))
            throw new InvalidDataException("Client ID، نام کاربری و رمز MQTT همین دستگاه را وارد کنید.");
        if (device.Aes && string.IsNullOrEmpty(device.Serial)) throw new InvalidDataException("برای پوشش AES سریال دستگاه لازم است.");
        if (device.Serial.IndexOfAny(['/', '+', '#']) >= 0)
            throw new InvalidDataException("این سریال برای تست محلی قابل استفاده است. برای اتصال، سریالی وارد کنید که شامل /، + یا # نباشد؛ این نشانه‌ها در مسیر MQTT کاربرد دارند.");
        var trust = new X509Certificate2Collection();
        if (!string.IsNullOrEmpty(caFile))
        {
            trust.ImportFromPemFile(caFile);
            if (trust.Count == 0) throw new InvalidDataException("فایل PEM شامل گواهی CA نیست.");
        }
        byte[]? secret = null;
        if (device.Aes && (string.IsNullOrEmpty(aesKey) || (secret = Convert.FromBase64String(aesKey)).Length != 32))
            throw new InvalidDataException("کلید AES باید ۳۲ بایت با نمایش Base64 باشد.");
        lock (gate)
        {
            if (lifetime != null) throw new InvalidOperationException("این دستگاه در حال اجراست.");
            var cts = lifetime = new CancellationTokenSource();
            status = "در حال اتصال";
            loop = Task.Run(() => Run(password, secret, trust, cts.Token));
        }
    }
    public async Task StopAsync()
    {
        CancellationTokenSource? cts; Task? task;
        lock (gate) { cts = lifetime; task = loop; if (cts != null) { status = "در حال توقف"; cts.Cancel(); } }
        if (task != null) await task;
        lock (gate) { if (cts != null && lifetime == cts) { lifetime = null; loop = null; status = "متوقف"; cts.Dispose(); } }
    }
    private async Task Run(string password, byte[]? secret, X509Certificate2Collection trust, CancellationToken stop)
    {
        using var crypto = secret == null ? null : new Envelope(secret, device.Serial);
        int retry = 2;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Session(password, crypto, trust, () => retry = 2, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception e) { Log("اتصال", e.GetBaseException().Message); }
            if (stop.IsCancellationRequested) break;
            State($"اتصال مجدد در {retry} ثانیه");
            try { await Task.Delay(TimeSpan.FromSeconds(retry), stop); } catch (OperationCanceledException) { break; }
            retry = Math.Min(30, retry * 2);
        }
    }
    private async Task Session(string password, Envelope? crypto, X509Certificate2Collection trust, Action connected, CancellationToken stop)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var ct = connection.Token;
        var queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(64) { SingleReader = true });
        client.DisconnectedAsync += _ => { connection.Cancel(); return Task.CompletedTask; };
        client.ApplicationMessageReceivedAsync += args =>
        {
            var msg = args.ApplicationMessage;
            if (msg.Topic == device.Input && !msg.Retain && msg.PayloadSegment.Count is > 0 and <= 8192 &&
                !queue.Writer.TryWrite(new Packet(msg.PayloadSegment.ToArray())))
                Log("دریافت", "صف پر است؛ فرمان اجرا نشد.");
            return Task.CompletedTask;
        };
        var options = new MqttClientOptionsBuilder().WithTcpServer(device.Host, device.Port)
            .WithProtocolVersion(MqttProtocolVersion.V311).WithCleanSession().WithClientId(device.ClientId)
            .WithCredentials(device.Username, password).WithTlsOptions(t => { t.UseTls(); if (trust.Count > 0) t.WithTrustChain(trust); })
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30)).WithTimeout(TimeSpan.FromSeconds(12)).Build();
        State("در حال اتصال");
        try
        {
            var conn = await client.ConnectAsync(options, ct);
            if (conn.ResultCode != MqttClientConnectResultCode.Success) throw new IOException(conn.ResultCode.ToString());
            if (device.Serial.Length > 0)
            {
                var sub = await client.SubscribeAsync(factory.CreateSubscribeOptionsBuilder().WithTopicFilter(f => f.WithTopic(device.Input)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)).Build(), ct);
                if (sub.Items.Count != 1 || (int)sub.Items.First().ResultCode >= 128) throw new IOException("بروکر مجوز دریافت فرمان را نداد.");
                Log("اتصال", "عضویت تأیید شد: " + device.Input);
            }
            lock (gate) outgoing = queue.Writer;
            State("آنلاین"); connected();
            async Task Publish(byte[] bytes)
            {
                var ack = await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(device.Output)
                    .WithPayload(crypto?.Seal(bytes) ?? bytes).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false).Build(), ct);
                if (!ack.IsSuccess) throw new IOException("بروکر پیام را تأیید نکرد.");
                Log("تأیید بروکر", Encoding.UTF8.GetString(bytes));
            }
            await foreach (var packet in queue.Reader.ReadAllAsync(ct))
            {
                if (packet.Sent is { } sent)
                {
                    try { await Publish(packet.Bytes); sent.TrySetResult(); }
                    catch (Exception ex) { sent.TrySetException(ex); throw; }
                    continue;
                }
                var data = packet.Bytes;
                Incoming cmd;
                try { cmd = Wire.Command(device.Serial, crypto?.Open(data) ?? data); }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or
                    KeyNotFoundException or FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
                { Log("رد پیام", "فرمان نامعتبر، تکراری یا متعلق به دستگاه دیگری است."); continue; }
                var reply = await Respond(hardware, cmd, "فرمان سرور", ct);
                if (reply is { } report) await Publish(report.Bytes);
            }
        }
        finally
        {
            lock (gate) outgoing = null;
            queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out var pending))
                pending.Sent?.TrySetException(new IOException("اتصال پیش از ارسال پیام قطع شد."));
            if (client.IsConnected)
            {
                using var deadline = new CancellationTokenSource(1500);
                try { await client.DisconnectAsync(new MqttClientDisconnectOptions(), deadline.Token); } catch (Exception) {/* Closing the socket completes cleanup. */}
            }
        }
    }
}
