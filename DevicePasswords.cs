using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AirNgin.Simulator;

public static class DevicePasswords
{
    private const string Service = "com.airngin.hardware-simulator.mqtt";

    // Keep a single credential per profile, but only use it with the original MQTT identity.
    private static byte[] Binding(Device d) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { d.Id, Host = d.Host.ToUpperInvariant(), d.Port, d.Username, d.ClientId, d.Project, d.Serial }));

    public static string Read(Device device)
    {
        var account = device.Id.ToString("N");
        byte[]? data = OperatingSystem.IsMacOS() ? Mac.Read(account) : Windows.Read(account);
        if (data is null) return "";
        try
        {
            var binding = Binding(device);
            if (data.Length <= binding.Length || !CryptographicOperations.FixedTimeEquals(binding, data.AsSpan(0, binding.Length))) return "";
            return Encoding.UTF8.GetString(data.AsSpan(binding.Length));
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    public static void Save(Device device, string password)
    {
        var account = device.Id.ToString("N");
        byte[]? data = null;
        try
        {
            if (password.Length != 0)
            {
                var binding = Binding(device);
                data = new byte[binding.Length + Encoding.UTF8.GetByteCount(password)];
                binding.CopyTo(data, 0);
                Encoding.UTF8.GetBytes(password, data.AsSpan(binding.Length));
            }
            if (OperatingSystem.IsMacOS()) Mac.Set(account, data);
            else if (OperatingSystem.IsWindows()) Windows.Set(account, data);
            else throw new PlatformNotSupportedException();
        }
        finally { if (data != null) CryptographicOperations.ZeroMemory(data); }
    }

    private static IOException Error(int code) => new($"دسترسی به مخزن رمزهای سیستم ممکن نشد (کد {code}). دسترسی شبیه‌ساز به مخزن رمزهای حساب خود را بررسی کنید.");

    private static class Mac
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const int NotFound = -25300;
        private static readonly byte[] Name = Encoding.UTF8.GetBytes(Service);

        [DllImport(Security)] private static extern int SecKeychainFindGenericPassword(nint chain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint size, out nint data, out nint item);
        [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")] private static extern int FindItem(nint chain, uint serviceLength, byte[] service, uint accountLength, byte[] account, nint size, nint data, out nint item);
        [DllImport(Security)] private static extern int SecKeychainAddGenericPassword(nint chain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint length, byte[] data, nint item);
        [DllImport(Security)] private static extern int SecKeychainItemModifyAttributesAndData(nint item, nint attributes, uint size, byte[] data);
        [DllImport(Security)] private static extern int SecKeychainItemDelete(nint item);
        [DllImport(Security)] private static extern int SecKeychainItemFreeContent(nint attributes, nint data);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(nint item);

        internal static byte[]? Read(string account)
        {
            var id = Encoding.UTF8.GetBytes(account);
            int status = SecKeychainFindGenericPassword(0, (uint)Name.Length, Name, (uint)id.Length, id, out uint size, out var data, out var item);
            if (status == NotFound) return null;
            if (status != 0) throw Error(status);
            try
            {
                var bytes = new byte[checked((int)size)];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { SecKeychainItemFreeContent(0, data); CFRelease(item); }
        }

        internal static void Set(string account, byte[]? data)
        {
            var id = Encoding.UTF8.GetBytes(account);
            int status = FindItem(0, (uint)Name.Length, Name, (uint)id.Length, id, 0, 0, out var item);
            if (status == NotFound)
            {
                if (data == null) return;
                status = SecKeychainAddGenericPassword(0, (uint)Name.Length, Name, (uint)id.Length, id, (uint)data.Length, data, 0);
            }
            else if (status == 0)
            {
                try { status = data == null ? SecKeychainItemDelete(item) : SecKeychainItemModifyAttributesAndData(item, 0, (uint)data.Length, data); }
                finally { CFRelease(item); }
            }
            if (status != 0) throw Error(status);
        }
    }

    private static class Windows
    {
        private const uint Generic = 1, LocalMachine = 2;
        private const int NotFound = 1168;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            internal uint Flags, Type;
            internal string? Target, Comment;
            internal System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            internal uint Size;
            internal nint Blob;
            internal uint Persist, AttributeCount;
            internal nint Attributes, TargetAlias, Username;
        }

        [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern bool CredReadW(string target, uint type, uint flags, out nint credential);
        [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern bool CredWriteW(ref Credential credential, uint flags);
        [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern bool CredDeleteW(string target, uint type, uint flags);
        [DllImport("advapi32")] private static extern void CredFree(nint credential);

        internal static byte[]? Read(string account)
        {
            if (!CredReadW(Service + "/" + account, Generic, 0, out var ptr))
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == NotFound) return null;
                throw Error(error);
            }
            try
            {
                var entry = Marshal.PtrToStructure<Credential>(ptr);
                var bytes = new byte[checked((int)entry.Size)];
                Marshal.Copy(entry.Blob, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { CredFree(ptr); }
        }

        internal static void Set(string account, byte[]? data)
        {
            var target = Service + "/" + account;
            if (data == null)
            {
                if (!CredDeleteW(target, Generic, 0))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != NotFound) throw Error(error);
                }
                return;
            }
            if (data.Length > 2560) throw new InvalidDataException("طول رمز از ظرفیت مخزن رمزهای ویندوز بیشتر است.");
            var pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var entry = new Credential { Type = Generic, Target = target, Persist = LocalMachine, Size = (uint)data.Length, Blob = pinned.AddrOfPinnedObject() };
                if (!CredWriteW(ref entry, 0)) throw Error(Marshal.GetLastPInvokeError());
            }
            finally { pinned.Free(); }
        }
    }
}
