using System.Text.Json;

namespace Treesi.Packaging;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["verify-graph", var baselineDesktop, var baselineCli, var restoredDesktop, var restoredCli])
                DependencyNotices.VerifyGraph(DependencyNotices.ReadLocks([baselineDesktop, baselineCli]), DependencyNotices.ReadLocks([restoredDesktop, restoredCli]));
            else if (args is ["notices", var cache, var sources, var destination, var desktopLock, var cliLock])
                DependencyNotices.Collect(cache, DependencyNotices.ReadLocks([desktopLock, cliLock]), sources, destination);
            else if (args is ["windows-installer", var candidate, var nsisLicense, var template, var installerWork])
                WindowsInstaller.Generate(candidate, nsisLicense, template, installerWork);
            else if (args is ["runtime-notices", var runtimeCache, var runtimeSources, var runtimeDestination, var runtimeRid])
            {
                if (runtimeRid is not ("win-x64" or "linux-x64" or "osx-arm64" or "osx-x64")) throw new ArgumentException("Unsupported runtime.");
                var id = "Microsoft.NETCore.App.Runtime." + runtimeRid;
                var directory = PackageFiles.Within(runtimeCache, id.ToLowerInvariant() + "/11.0.0");
                using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, ".nupkg.metadata")));
                DependencyNotices.Collect(runtimeCache, [new(id, "11.0.0", metadata.RootElement.GetProperty("contentHash").GetString()!)], runtimeSources, runtimeDestination);
                File.WriteAllText(Path.Combine(runtimeDestination, "SCOPE.txt"), "SDK-selected .NET 11.0.0 runtime pack. Content hash comes from NuGet restore metadata, not the application lock graph. Original notices are extracted from the integrity-checked archive.\n");
            }
            else if (args is ["assemble", var desktop, var cli, var legal, var root, var output, var commit, var rid, var version, var bundleBuildVersion])
            {
                if (Directory.Exists(root) || !System.Text.RegularExpressions.Regex.IsMatch(commit, "\\A[0-9a-f]{40}\\z")
                    || rid is not ("win-x64" or "linux-x64" or "osx-arm64" or "osx-x64")) throw new InvalidDataException("Invalid or occupied package destination or build identity.");
                Directory.CreateDirectory(root);
                PortableLayout.Stage(desktop, cli, legal, root, rid, version, bundleBuildVersion, commit);
                PackageFiles.WriteInventory(root, commit, rid, version);
                PackageFiles.Archive(root, output, rid != "linux-x64");
                File.WriteAllText(output + ".sha256", PackageFiles.Hash(output) + "  " + Path.GetFileName(output) + "\n");
                File.WriteAllText(output + ".report.json", JsonSerializer.Serialize(new
                {
                    sourceCommit = commit,
                    runtimeIdentifier = rid,
                    productVersion = version,
                    bundleBuildVersion = rid.StartsWith("osx-", StringComparison.Ordinal) ? bundleBuildVersion : null,
                    archiveBytes = new FileInfo(output).Length,
                    extractedBytes = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length),
                    sha256 = PackageFiles.Hash(output),
                    signing = "no OS publisher signature; not notarized"
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else throw new ArgumentException("Expected verify-graph, notices or assemble with the documented arguments.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Xml.XmlException or JsonException or NuGet.Packaging.Signing.SignatureException)
        { Console.Error.WriteLine(exception.Message); return 1; }
    }
}
