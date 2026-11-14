using System.Text.Json;

namespace Treesi.TestSupport;

public static class LinuxVirtualDisplay
{
    public static void Require()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("An owned Linux virtual display is required.");
        var temporary = Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? throw new InvalidOperationException("Runner temp is missing.");
        var token = Environment.GetEnvironmentVariable("TREESI_LINUX_DISPLAY_TOKEN") ?? throw new InvalidOperationException("The virtual display capability is missing.");
        RequireChild(temporary, token);
        if ((File.GetAttributes(token) & FileAttributes.ReparsePoint) != 0 || new FileInfo(token).Length > 4096
            || (File.GetUnixFileMode(token) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
            throw new InvalidOperationException("Invalid virtual display capability file.");
        using var document = JsonDocument.Parse(File.ReadAllText(token));
        var root = document.RootElement;
        var display = root.GetProperty("display").GetString();
        var authority = root.GetProperty("authority").GetString()!;
        var pid = root.GetProperty("serverPid").GetInt32();
        RequireChild(temporary, authority);
        if ((File.GetAttributes(authority) & FileAttributes.ReparsePoint) != 0 || (File.GetUnixFileMode(authority) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0)
            throw new InvalidOperationException("The display authority is not private and regular.");
        if (root.GetProperty("schemaVersion").GetString() != "1" || display != Environment.GetEnvironmentVariable("DISPLAY")
            || authority != Environment.GetEnvironmentVariable("XAUTHORITY") || !File.Exists(authority)
            || Path.GetFileName(File.ResolveLinkTarget($"/proc/{pid}/exe", true)?.FullName) != "Xvfb")
            throw new InvalidOperationException("The display server does not match the capability.");
        var arguments = File.ReadAllText($"/proc/{pid}/cmdline").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (!arguments.Contains(display) || !arguments.Contains("-nolisten") || !arguments.Contains("tcp") || !arguments.Contains("-auth") || !arguments.Contains(authority))
            throw new InvalidOperationException("The owned Xvfb arguments are invalid.");
    }

    public static string RequireChild(string root, string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("The lab path is outside its owned root.");
        return Path.GetFullPath(path);
    }
}
