using AirNgin.Simulator;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

static void Check(bool expected, string message)
{
    if (!expected) throw new Exception(message);
}
// Two separate invocations exercise the operating system vault across a process restart.
if (args is ["vault-write" or "vault-read", var testId])
{
    var device = Device.Sample() with { Id = Guid.Parse(testId), Host = "fixture.invalid" };
    const string initial = "synthetic-رمز-123";
    if (args[0] == "vault-write")
    {
        Check(DevicePasswords.Read(device).Length == 0, "test requires an unused fixture ID");
        DevicePasswords.Save(device, initial);
    }
    else
    {
        try
        {
            Check(DevicePasswords.Read(device) == initial, "restart did not restore fixture password");
            Check(DevicePasswords.Read(device with { Host = "another.invalid" }) == "", "broker isolation");
            Check(DevicePasswords.Read(device with { Username = "another" }) == "", "account isolation");
            Check(DevicePasswords.Read(device with { Id = Guid.NewGuid() }) == "", "device isolation");
            DevicePasswords.Save(device, "updated-fixture");
            Check(DevicePasswords.Read(device) == "updated-fixture", "password update");
            Check(!JsonSerializer.Serialize(device).Contains("updated-fixture"), "password in export");
        }
        finally { DevicePasswords.Save(device, ""); }
        Check(DevicePasswords.Read(device) == "", "credential removal");
        Console.WriteLine("PASS: saved password restored across processes, isolated, updated and removed.");
    }
    return;
}
var demoProfiles = Profiles.Read(Path.Combine(AppContext.BaseDirectory, "tester-demo.json"));
var demoSwitch = demoProfiles.Single(d => d.Rules.Any(r => r.CustomInput));
var demoRuntime = new DeviceRuntime(demoSwitch);
Check(demoSwitch.Rules.Count == 2, "demo has both switch states");
foreach (var rule in demoSwitch.Rules)
{
    var reply = await demoRuntime.PreviewAsync(new Pair("command", rule.Match!), default);
    Check(reply?.value == rule.Value && JsonDocument.Parse(reply.value).RootElement.GetProperty("data").GetString() != rule.Match,
        "Analyzer demo reports its own response frame");
}
var root = Directory.CreateTempSubdirectory("airngin-simulator-checks-").FullName;
var portProbe = new TcpListener(IPAddress.Loopback, 0);
portProbe.Start();
var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
var first = Device.Sample() with { Serial = "lamp-1", Host = "localhost", Port = port, ClientId = "fixture-one", Username = "fixture" };
first.Rules.Add(new("ch1", null, ReplyMode.Silent, "", "", 0));
var second = first with
{
    Id = Guid.NewGuid(),
    Name = "دستگاه دوم",
    Serial = "چراغ-اتاق",
    ClientId = "fixture-two",
    Aes = true,
    Rules = [new("level", null, ReplyMode.Echo, "", "position", 15)]
};
string profileFile = Path.Combine(root, "demo.json");
Profiles.Write(profileFile, [first, second]);
Check(Profiles.Read(profileFile).Select(d => d.Serial).SequenceEqual(new[] { "lamp-1", "چراغ-اتاق" }), "free-form serial round trip");
Profiles.Validate([first with { Serial = "7" }]);
var localOnly = new DeviceRuntime(first with { Serial = "اتاق/#" });
Check((await localOnly.PreviewAsync(new("ch1", "on"), default))?.value == "on", "local serial with topic characters");
try { localOnly.Start("local-test", ""); throw new Exception("wildcard subscription accepted"); } catch (InvalidDataException) { }
try { Profiles.Validate([first, first]); throw new Exception("duplicate device accepted"); } catch (InvalidDataException) { }
var one = new DeviceRuntime(first); var two = new DeviceRuntime(second);
Check((await one.PreviewAsync(new("ch1", "on"), default))?.value == "on", "exact before wildcard");
Check((await one.PreviewAsync(new("ch1", "get"), default))?.value == "on", "stateful read");
Check(await one.PreviewAsync(new("ch1", "unknown"), default) == null, "silent fallback");
try { Wire.Command(first.Serial, Wire.Sample(second.Serial, new("ch1", "on"))); throw new Exception("wrong serial accepted"); } catch (InvalidDataException) { }
using var caKey = RSA.Create(2048);
var caReq = new CertificateRequest("CN=AirNgin fixture root", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
using var ca = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
var caPath = Path.Combine(root, "fixture.pem"); File.WriteAllText(caPath, ca.ExportCertificatePem());
using var tlsKey = RSA.Create(2048);
var request = new CertificateRequest("CN=localhost", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection() { new Oid("1.3.6.1.5.5.7.3.1") }, false));
using var leaf = request.Create(ca, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
using var cert = leaf.CopyWithPrivateKey(tlsKey);
var factory = new MqttFactory();
var options = new MqttServerOptionsBuilder().WithoutDefaultEndpoint().WithEncryptedEndpoint()
    .WithEncryptedEndpointBoundIPAddress(IPAddress.Loopback).WithEncryptedEndpointBoundIPV6Address(IPAddress.IPv6Loopback)
    .WithEncryptedEndpointPort(port).WithEncryptionCertificate(cert).Build();
var replies = Channel.CreateUnbounded<(string Client, string Topic, byte[] Data)>();
MqttServer NewBroker()
{
    var broker = factory.CreateMqttServer(options);
    broker.ValidatingConnectionAsync += e => { if (e.UserName != "fixture" || e.Password != "local-test") e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword; return Task.CompletedTask; };
    broker.InterceptingPublishAsync += e =>
    {
        if (e.ApplicationMessage.Topic.Contains("/DeviceToServer"))
            replies.Writer.TryWrite((e.ClientId, e.ApplicationMessage.Topic, e.ApplicationMessage.PayloadSegment.ToArray()));
        return Task.CompletedTask;
    };
    return broker;
}
var aesKey = RandomNumberGenerator.GetBytes(32);
using var sender = new Envelope(aesKey.ToArray(), second.Serial);
async Task Ready()
{
    using var deadline = new CancellationTokenSource(20000);
    try { while (one.Status != "آنلاین" || two.Status != "آنلاین") await Task.Delay(50, deadline.Token); }
    catch { throw new Exception("Connect: " + string.Join(" | ", one.Events.Concat(two.Events).Select(e => e.Message))); }
}
async Task Send(MqttServer broker, Device d, byte[] data) =>
    await broker.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
        .WithTopic(d.Input).WithPayload(data).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build()));
async Task<(string Client, string Topic, byte[] Data)> Reply()
{
    using var deadline = new CancellationTokenSource(4000);
    return await replies.Reader.ReadAsync(deadline.Token);
}
const string switchSerial = "DEMO0000000003";
var frames = new[] {
        ("01E230303030303030303033010004000200FF6666", "01E230303030303030303033011104000200FF6527"),
        ("01E230303030303030303033010004000200002626", "01E230303030303030303033011104000200002567") };
var switchProfile = first with { Id = Guid.NewGuid(), Serial = switchSerial, ClientId = "fixture-switch",
    Rules = frames.Select(f => new Rule("command", f.Item1, ReplyMode.Custom, JsonSerializer.Serialize(new { data = f.Item2 }), "", 0)).ToList() };
const string customOn = "01E230303030303030303034010004000200FF2780";
const string customOff = "01E2303030303030303030340100040002000067C0";
const string customText = "  خط اول\nخط دوم  ";
Rule Literal(string input, string output) => new Rule("", input, ReplyMode.Custom, output, "", 0) { CustomInput = true };
var customRules = new[] {
    Literal(customOn, "{\"data\":\"01E230303030303030303034011104000200FF24C1\"}"),
    Literal(customOff, "{\"data\":\"01E230303030303030303034011104000200006481\"}"),
    Literal(customText, "  پاسخ\n") };
switchProfile.Rules.AddRange(customRules);
switchProfile.Rules.Add(new("command", null, ReplyMode.Silent, "", "", 0));
Profiles.Write(profileFile, [switchProfile]);
Check(Profiles.Read(profileFile).Single().Rules.SequenceEqual(switchProfile.Rules), "custom rule persistence");
var manualProfile = first with { Id = Guid.NewGuid(), Serial = "", ClientId = "fixture-manual", Rules = [] };
Profiles.Validate([manualProfile]);
var manualDevice = new DeviceRuntime(manualProfile);
var simulatedSwitch = new DeviceRuntime(switchProfile);
using var broker = NewBroker();
try
{
    await broker.StartAsync();
    one.Start("local-test", "", caPath); two.Start("local-test", Convert.ToBase64String(aesKey), caPath); await Ready();
    await Send(broker, first, Wire.Sample(first.Serial, new("ch1", "get")));
    var p = await Reply();
    Check(p.Client == first.ClientId && p.Topic == first.Output && Encoding.UTF8.GetString(p.Data) == "[{\"key\":\"ch1\",\"value\":\"off\"}]", "real plain reply or separation from preview");
    var encrypted = sender.Seal(Wire.Sample(second.Serial, new("level", "42")));
    await Send(broker, second, encrypted); var q = await Reply();
    Check(q.Client == second.ClientId && q.Topic == second.Output && Encoding.UTF8.GetString(sender.Open(q.Data)) == "[{\"key\":\"position\",\"value\":\"42\"}]", "encrypted response routing");
    await Send(broker, second, encrypted);
    await Send(broker, first, Wire.Sample(second.Serial, new("ch1", "on")));
    await Send(broker, first, Wire.Sample(first.Serial, new("ch1", "other")));
    await Task.Delay(350); Check(!replies.Reader.TryRead(out _), "replay / wrong serial / silent generated reply");
    simulatedSwitch.Start("local-test", "", caPath);
    using (var limit = new CancellationTokenSource(10000))
        while (simulatedSwitch.Status != "آنلاین") await Task.Delay(25, limit.Token);
    // Synthetic device frames with a Modbus CRC for both switch states.
    foreach (var (cmd, report) in frames)
    {
        var down = JsonSerializer.SerializeToUtf8Bytes(new { deviceSerial = switchSerial, data = JsonSerializer.Serialize(new { type = "command", value = cmd }) });
        await Send(broker, switchProfile, down);
        var received = await Reply();
        Check(received.Client == switchProfile.ClientId && received.Topic == switchProfile.Output, "JS reply routing");
        Check(Encoding.UTF8.GetString(received.Data) == JsonSerializer.Serialize(new { data = report }), "JS report payload");
        var preview = await simulatedSwitch.PreviewAsync(new Pair("command", cmd), default);
        Check(preview?.value == JsonSerializer.Serialize(new { data = report }), "local JS preview");
    }
    foreach (var rule in customRules)
    {
        string input = rule.Match!;
        string data = input == customText ? input : JsonSerializer.Serialize(new { type = "command", value = input });
        await Send(broker, switchProfile, JsonSerializer.SerializeToUtf8Bytes(new { deviceSerial = switchSerial, data }));
        var reply = await Reply();
        Check(reply.Client == switchProfile.ClientId && Encoding.UTF8.GetString(reply.Data) == rule.Value, "custom input wire match");
        Check((await simulatedSwitch.PreviewCustomAsync(input, default))?.value == rule.Value, "literal preview");
        Check((await simulatedSwitch.PreviewCustomAsync(data, default))?.value == rule.Value, "wrapped preview");
    }
    Check(await simulatedSwitch.PreviewCustomAsync(customText.Trim(), default) == null, "custom input whitespace is significant");
    manualDevice.Start("local-test", "", caPath);
    using (var limit = new CancellationTokenSource(10000))
        while (!manualDevice.CanSend) await Task.Delay(25, limit.Token);
    foreach (var payload in new[] { "  متن دلخواه بدون سریال  ", "{\n  \"data\": \"example\"\n}" })
    {
        await manualDevice.SendCustomAsync(payload).WaitAsync(TimeSpan.FromSeconds(4));
        var packet = await Reply();
        Check(packet.Client == manualProfile.ClientId && packet.Topic == manualProfile.Output &&
            packet.Data.SequenceEqual(Encoding.UTF8.GetBytes(payload)), "literal manual text without serial");
    }
    await manualDevice.StopAsync();
    try { await manualDevice.SendCustomAsync("offline"); throw new Exception("offline send succeeded"); } catch (InvalidOperationException) { }
    await simulatedSwitch.StopAsync();
    await broker.StopAsync();
    using var restart = NewBroker();
    await restart.StartAsync(); await Task.Delay(300); await Ready();
    await Send(restart, first, Wire.Sample(first.Serial, new("ch1", "on")));
    Check((await Reply()).Client == first.ClientId, "resubscribe after disconnect");
    await one.StopAsync(); await two.StopAsync(); await restart.StopAsync();
    Console.WriteLine("PASS: profile persistence, local rules, 2 simultaneous TLS clients, authentication, PLAIN/AES, replay, serial isolation, state, silent rules and broker restart; custom Zooril JS on/off and byte-exact manual MQTT text without serial.");
}
finally
{
    await manualDevice.StopAsync(); await simulatedSwitch.StopAsync(); await one.StopAsync(); await two.StopAsync();
    File.Delete(caPath); File.Delete(profileFile); Directory.Delete(root);
}
