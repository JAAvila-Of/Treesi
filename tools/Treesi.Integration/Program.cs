using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Treesi.Integration;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsLinux()) throw new DesktopRegistrationException("unsupported_platform", "Desktop menu registration is available only on Linux.");
            if (GetEffectiveUserId() == 0) throw new DesktopRegistrationException("elevated_user", "Run this helper as the desktop user without sudo.");
            var registration = new DesktopRegistration(DesktopRegistration.ResolveDataHome(Environment.GetEnvironmentVariable("HOME") ?? "", Environment.GetEnvironmentVariable("XDG_DATA_HOME")));
            DesktopRegistrationState state;
            switch (args)
            {
                case ["status"]: state = registration.Status(); break;
                case ["unregister"]: state = registration.Unregister(); break;
                case ["register"]:
                    var (root, commit) = PackageIdentity();
                    state = registration.Register(root, commit); break;
                default: throw new DesktopRegistrationException("usage", "Expected register, status or unregister.");
            }
            Write(new { schemaVersion = "1", ok = true, data = state, error = (object?)null });
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Write(new { schemaVersion = "1", ok = false, data = (object?)null, error = new { code = (error as DesktopRegistrationException)?.Code ?? "integration_error", message = error.Message } });
            return 1;
        }
    }

    [SupportedOSPlatform("linux")]
    private static (string Root, string Commit) PackageIdentity()
    {
        var root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        DesktopRegistration.RefuseLink(root, true);
        var manifest = Path.Combine(root, "package-manifest.json");
        var desktop = Path.Combine(root, "Treesi.Desktop");
        DesktopRegistration.RefuseLink(manifest, false); DesktopRegistration.RefuseLink(desktop, false);
        if ((File.GetUnixFileMode(desktop) & UnixFileMode.UserExecute) == 0) throw new InvalidDataException("The Desktop executable has no user execute permission.");
        if (new FileInfo(manifest).Length > 4 * 1024 * 1024) throw new InvalidDataException("Package inventory exceeds its limit.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var value = document.RootElement;
        var commit = value.GetProperty("sourceCommit").GetString()!;
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        if (value.GetProperty("schemaVersion").GetString() != "1" || value.GetProperty("runtimeIdentifier").GetString() != "linux-x64" || !version.EndsWith("+" + commit, StringComparison.Ordinal))
            throw new InvalidDataException("Package identity does not match this integration helper.");
        var member = value.GetProperty("files").EnumerateArray().Single(entry => entry.GetProperty("path").GetString() == "Treesi.Desktop");
        using var stream = File.OpenRead(desktop);
        if (stream.Length != member.GetProperty("bytes").GetInt64() || Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() != member.GetProperty("sha256").GetString())
            throw new InvalidDataException("The Desktop executable differs from the package inventory.");
        return (root, commit);
    }

    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEffectiveUserId();
}
