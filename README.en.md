# AirNgin Hardware Simulator

[فارسی](README.md) · **English**

Simulate one device or a fleet on macOS and Windows. Each device has its own MQTT connection, rules and state. Use the **Language** menu at the top of the window to choose **English** or **فارسی**. The layout changes immediately and your choice is saved on this computer.

## Install

Download the ZIP for your computer from [Releases](https://github.com/DevNgin-org/AirNgin-Hardware-Simulator/releases). Extract it before starting the application. Each build includes .NET.

| Computer | Download | Open |
|---|---|---|
| Apple Silicon Mac | `HardwareSimulator-osx-arm64.zip` | `AirNgin Hardware Simulator.app` |
| Intel Mac | `HardwareSimulator-osx-x64.zip` | `AirNgin Hardware Simulator.app` |
| 64-bit Windows on Intel or AMD | `HardwareSimulator-win-x64.zip` | `AirNgin.HardwareSimulator.exe` |

Quit the old version before opening an update. Profiles live in your account’s application data folder. The Mac build is signed locally and has no Apple notarization. The Windows build has no commercial signature.

## Try both methods offline

- **Direct connection.** Select **Room light**, choose the rule for `ch1` with `on`, then select **Run local test**. The expected reply is `[{"key":"ch1","value":"on"}]`. Run the `get` rule next to read the state you just set.
- **Via Analyzer.** Select **Import profiles**, open the bundled `tester-demo.json` and select **Analyzer demo switch**. Choose either switch rule to load its incoming command. Run the test to see the JSON report the simulated device would send. The sample frames correspond to the demo serial. Use your own device model’s commands and reports when configuring another device.

The tabs group saved rules by format. An online device checks all its rules regardless of which tab is open. The local test exercises matching and reply generation. To verify server-side script execution and the state displayed in the mobile app, run the connected test below.

## Configure a direct device

Open **Configure response rules → New direct rule**. Enter the operation key and input value, or select **Match any input value**. With an ordinary keyed command, an empty reply key reuses the incoming key.

| Reply type | Example use |
|---|---|
| Echo command value | Report the requested dimmer level or switch state. |
| Fixed reply | Return the configured value whenever the condition matches. |
| Read last state | Answer `get` with the last value for the reply key. Until the first change, return the configured initial value. |
| No reply | Log the incoming command and simulate a silent device. |
| Custom (literal text) | Write the complete report in the format expected by the receiving script. |

Use **Reply delay** to model latency in milliseconds. Select **Save rule**, then **Save device**.

## Configure a device with custom data

Choose **Via Analyzer → Configure response rules → New Analyzer rule**. Custom input and a literal reply are preselected. Your device model determines the command and report formats.

| Example | Exact input text | Complete reply text |
|---|---|---|
| A human-readable command | `LIGHT:ON` | `STATE:ON` |
| Strings known only to the device protocol | `@Q7!` | `!R1` |
| A protocol frame, such as Modbus represented as hexadecimal text | The complete incoming frame as displayed | The reply frame in the format your Analyzer accepts, including its checksum if required |

The first two rows are illustrative pairs you can try offline. For a live test, copy the actual command value from **Server command** into **Exact input text**. For a structured command you can also match the complete inner `data` text. Case, spaces and newlines count. The simulator sends literal replies as UTF-8 text. Include a JSON wrapper, serial or checksum when your device model requires it.

Define separate rules for commands that need different replies. Matching checks the full custom text first, then an exact key/value pair, then a custom rule matching the operation value, then a keyed rule accepting any value. Save the rule and the device before testing.

## Connect a device and test from the app

Create the project in the AirNgin mobile app. Open [AirNgin Console](https://console.airngin.com/), select that same project and add the device to obtain its MQTT connection details. If the device already belongs to the project, **remove it from that project and add it again through the console**. Enter the connection details from the console in the simulator. Offline tests can use the bundled demo profiles.

1. Open **Edit device and operations → Details and connection**. Enter the registered project code, device serial, MQTT host, TLS port, client ID, username and password. Use a distinct client ID for each device.
2. Select **Connect to server**. Enable application encryption for devices registered with AES and supply the Base64 device key when connecting. If your broker uses a private CA, choose its PEM certificate.
3. When the status reads **Online**, send a command from a mobile account with access to that device. Keep the simulator running. Saving changes to an online device disconnects it. Reconnect to continue.
4. Read **Device events**. **Server command** records the inbound command. If no rule matches, compare the exact input with the event. **Broker acknowledgement** confirms the broker received the report. If the mobile state stays unchanged, check the Analyzer’s expected input and the device identity it returns.

**Manual send** lets an online device publish a complete text or JSON report at any time. A non-AES device can send manual reports with a blank serial. A serial is needed to subscribe to app commands on the dedicated device topic and to use AES.

**Passwords and sharing.** Each device’s MQTT password is saved in macOS Keychain or Windows Credential Manager. Use **Show password** to view or copy it. To remove it, clear the field and save the device. **Export profiles** shares the devices and rules. MQTT passwords and AES keys are excluded. Testers using the mobile app need project access to the relevant devices.

## Build and test from source

Install the .NET 10 SDK. From the project directory, run:

```sh
dotnet run --project HardwareSimulator.csproj
dotnet run --project tests/Checks.csproj
```

The integration tests start a temporary TLS broker on loopback. They exercise simultaneous clients, direct and custom replies, AES, reconnects and profile persistence. The standard topics are `<project>/ServerToDevice/<serial>` and `<project>/DeviceToServer`. AES uses the `/AES` suffix.

On a Mac, run `bash packaging/build.sh osx-arm64`, `bash packaging/build.sh osx-x64` or `bash packaging/build.sh win-x64` to create a self-contained ZIP. The default destination is `dist`. Each ZIP includes the demo profiles and both guides.
