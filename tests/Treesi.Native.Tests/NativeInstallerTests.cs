using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using JAAvila.FluentOperations;
using Microsoft.Win32;
using NUnit.Framework;
using Treesi.TestSupport;

namespace Treesi.Native.Tests;

[TestFixture, NonParallelizable, Category("NativeInstaller"), SupportedOSPlatform("windows")]
public sealed class NativeInstallerTests
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TreesiPreview";

    [OneTimeSetUp]
    public void RequireDisposableWindows() => IsolatedExecution.Require("native-ui");

    [Test]
    public async Task PerUserInstallAndBoundedUninstallPreserveForeignFilesAndPreferences()
    {
        IsolatedExecution.Require("native-ui");
        var installer = Environment.GetEnvironmentVariable("TREESI_NATIVE_INSTALLER") ?? throw new InvalidOperationException("The isolated installer path is missing.");
        var runnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? throw new InvalidOperationException("The runner temp directory is missing.");
        if (!Path.GetFullPath(installer).StartsWith(Path.GetFullPath(runnerTemp).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The installer must be inside the disposable runner temp directory.");
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        var installation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Treesi Preview");
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Treesi Preview.lnk");
        if (Directory.Exists(installation) || File.Exists(shortcut) || registry.OpenSubKey(Key) is not null)
            throw new InvalidOperationException("The VM must not contain an earlier Treesi Preview installation.");
        var originalUserPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var originalMachinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        var lab = Directory.CreateTempSubdirectory("treesi-installer-lab-").FullName;
        using var repository = new SyntheticRepository();
        var originalIndex = Hash(Path.Combine(repository.Root, ".git", "index"));
        var originalFile = File.ReadAllText(repository.SamplePath);
        var preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Treesi", "installer-test-sentinel.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        if (File.Exists(preferences)) throw new InvalidOperationException("The isolated preferences sentinel already exists.");
        File.WriteAllText(preferences, "preserve saved preferences");
        try
        {
            ManifestLevel(installer).Test().Be("asInvoker");
            var foreign = Directory.CreateDirectory(Child(lab, "foreign target with spaces")).FullName;
            File.WriteAllText(Path.Combine(foreign, "keep.txt"), "foreign data");
            (await Run(installer, "/S", "/D=" + foreign)).Code.Test().Be(0);
            File.Exists(Path.Combine(foreign, "Treesi.Desktop.exe")).Test().BeFalse();
            File.ReadAllText(Path.Combine(foreign, "keep.txt")).Test().Be("foreign data");
            File.Exists(shortcut).Test().BeTrue();
            using (var entry = registry.OpenSubKey(Key)) ((string?)entry!.GetValue("InstallLocation")).Test().Be(installation);
            var cli = Path.Combine(installation, "Treesi.Cli.exe");
            var capabilities = await Run(cli, "capabilities"); capabilities.Code.Test().Be(0);
            using (var envelope = JsonDocument.Parse(capabilities.Output)) envelope.RootElement.GetProperty("ok").GetBoolean().Test().BeTrue();
            var inspection = await Run(cli, "inspect", repository.Root); inspection.Code.Test().Be(0);
            using (var envelope = JsonDocument.Parse(inspection.Output)) envelope.RootElement.GetProperty("ok").GetBoolean().Test().BeTrue();
            using (var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(installation, "installation-files.json"))))
                foreach (var file in inventory.RootElement.GetProperty("files").EnumerateArray())
                    Hash(Child(installation, file.GetProperty("path").GetString()!)).Test().Be(file.GetProperty("sha256").GetString());
            var desktopHash = Hash(Path.Combine(installation, "Treesi.Desktop.exe"));
            (await Run(installer, "/S")).Code.Test().Be(2);
            Hash(Path.Combine(installation, "Treesi.Desktop.exe")).Test().Be(desktopHash);
            var uninstall = Child(lab, "uninstall-probe.exe");
            File.Copy(Path.Combine(installation, "Uninstall.exe"), uninstall);
            using (File.Open(cli, FileMode.Open, FileAccess.Read, FileShare.Read))
                (await Run(uninstall, "/S", "_?=" + installation)).Code.Test().Be(2);
            Hash(Path.Combine(installation, "Treesi.Desktop.exe")).Test().Be(desktopHash);
            var marker = Path.Combine(installation, "installation.ini");
            var originalMarker = File.ReadAllText(marker);
            File.WriteAllText(marker, "[Identity]\r\nProduct=OtherProduct\r\nCommit=other\r\n");
            (await Run(uninstall, "/S", "_?=" + installation)).Code.Test().Be(2);
            File.WriteAllText(marker, originalMarker);

            var thirdParty = Child(installation, "third-party");
            var backup = Child(lab, "original-third-party");
            Directory.Move(thirdParty, backup);
            var linked = false;
            try
            {
                var junction = await Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d", "/c", "mklink", "/J", thirdParty, foreign);
                junction.Code.Test().Be(0); linked = true;
                (await Run(uninstall, "/S", "_?=" + installation)).Code.Test().Be(2);
                File.ReadAllText(Path.Combine(foreign, "keep.txt")).Test().Be("foreign data");
                Hash(Path.Combine(installation, "Treesi.Desktop.exe")).Test().Be(desktopHash);
            }
            finally
            {
                if (linked) Directory.Delete(thirdParty);
                Directory.Move(backup, thirdParty);
            }
            var unknown = Path.Combine(installation, "user-note.keep");
            var nested = Path.Combine(thirdParty, "user-note.keep");
            File.WriteAllText(unknown, "preserve unknown root file"); File.WriteAllText(nested, "preserve unknown nested file");
            (await Run(uninstall, "/S", "_?=" + foreign)).Code.Test().Be(0);
            File.Exists(Path.Combine(installation, "Treesi.Desktop.exe")).Test().BeFalse();
            File.Exists(shortcut).Test().BeFalse();
            using (var entry = registry.OpenSubKey(Key)) (entry is null).Test().BeTrue();
            File.ReadAllText(unknown).Test().Be("preserve unknown root file");
            File.ReadAllText(nested).Test().Be("preserve unknown nested file");
            File.ReadAllText(Path.Combine(foreign, "keep.txt")).Test().Be("foreign data");
            File.ReadAllText(preferences).Test().Be("preserve saved preferences");
            Hash(Path.Combine(repository.Root, ".git", "index")).Test().Be(originalIndex);
            File.ReadAllText(repository.SamplePath).Test().Be(originalFile);
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User).Test().Be(originalUserPath);
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine).Test().Be(originalMachinePath);
            File.Delete(unknown); File.Delete(nested);
            Directory.Delete(thirdParty); Directory.Delete(installation);
        }
        finally
        {
            File.Delete(preferences);
            if (Directory.GetFileSystemEntries(Path.GetDirectoryName(preferences)!).Length == 0) Directory.Delete(Path.GetDirectoryName(preferences)!);
        }
    }

    private static string Child(string root, string relative)
    {
        var child = Path.GetFullPath(Path.Combine(root, relative));
        if (!child.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The test child escapes its owned directory.");
        return child;
    }

    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(); }

    private static async Task<(int Code, string Output)> Run(string executable, params string[] arguments)
    {
        IsolatedExecution.Require("native-ui");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        // NSIS requires /D= and _?= to be the final, unquoted remainder of its command line.
        if (arguments is ["/S", var target] && (target.StartsWith("_?=", StringComparison.Ordinal) || target.StartsWith("/D=", StringComparison.Ordinal)))
            start.Arguments = "/S " + target;
        else
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The owned test process did not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        finally { if (!process.HasExited) process.Kill(true); }
        var text = await output; var stderr = await error;
        if (process.ExitCode != 0) TestContext.Progress.WriteLine($"Owned probe exited {process.ExitCode}: {stderr}");
        return (process.ExitCode, text);
    }

    private static string ManifestLevel(string executable)
    {
        var module = LoadLibraryEx(executable, IntPtr.Zero, 0x22);
        if (module == IntPtr.Zero) throw new InvalidOperationException("Installer resources could not be inspected.");
        try
        {
            var resource = FindResource(module, (IntPtr)1, (IntPtr)24);
            var size = SizeofResource(module, resource);
            if (size is 0 or > 65536) throw new InvalidOperationException("Installer manifest is missing or excessive.");
            var bytes = new byte[size]; Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, bytes.Length);
            using var stream = new MemoryStream(bytes);
            return XDocument.Load(stream).Descendants().Single(element => element.Name.LocalName == "requestedExecutionLevel").Attribute("level")!.Value;
        }
        finally { FreeLibrary(module); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
}
