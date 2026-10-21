using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.CSharp.RuntimeBinder;

namespace QuadExplorer;

public sealed record ExplorerFavoritesResult(
    IReadOnlyList<string> Paths, string? Warning, string Source)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class ExplorerFavorites
{
    private const string QuickAccess = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

    // Only reads Shell properties. Never invokes verbs, resolves links, or saves shortcuts.
    public static ExplorerFavoritesResult Read()
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            shell = Activator.CreateInstance(shellType)!;
            ReadFolder(shell, QuickAccess, legacyLinks: false, paths, seen, warnings);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                var links = Path.Combine(profile, "Links");
                if (Directory.Exists(links))
                    ReadFolder(shell, links, legacyLinks: true, paths, seen, warnings);
            }
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            warnings.Add("Windows Explorer favorites could not be read.");
        }
        finally
        {
            Release(shell);
        }

        return new ExplorerFavoritesResult(paths.AsReadOnly(),
            warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct()),
            "Windows Explorer pinned Quick Access folders and legacy Links folder shortcuts")
        { Warnings = warnings.Distinct().ToArray() };
    }

    private static void ReadFolder(object shell, string location, bool legacyLinks,
        List<string> paths, HashSet<string> seen, List<string> warnings)
    {
        object? folder = null;
        object? items = null;
        try
        {
            folder = ((dynamic)shell).NameSpace(location);
            if (folder is null)
            {
                warnings.Add(legacyLinks
                    ? "Legacy Explorer Links were unavailable."
                    : "Explorer Quick Access was unavailable.");
                return;
            }

            items = ((dynamic)folder).Items();
            int count = ((dynamic)items).Count;
            for (var index = 0; index < count && paths.Count < 100; index++)
            {
                object? item = null;
                object? link = null;
                try
                {
                    item = ((dynamic)items).Item(index);
                    string? path;
                    if (legacyLinks)
                    {
                        string shortcutPath = ((dynamic)item).Path;
                        if (!((bool)((dynamic)item).IsLink)
                            || !string.Equals(Path.GetExtension(shortcutPath), ".lnk",
                                StringComparison.OrdinalIgnoreCase))
                            continue;
                        link = ((dynamic)item).GetLink;
                        path = ((dynamic)link).Path;
                    }
                    else
                    {
                        if (!((bool)((dynamic)item).IsFileSystem)
                            || !((bool)((dynamic)item).IsFolder))
                            continue;

                        // This Shell property is not a documented cross-version contract.
                        // Unknown pin state is skipped, never mistaken for a user favorite.
                        // FolderItem.Verbs() names are localized and cannot reliably replace it.
                        object? pinned = ((dynamic)item).ExtendedProperty("System.Home.IsPinned");
                        if (pinned is not bool isPinned)
                        {
                            warnings.Add("Some Explorer folders had no readable pin state and were skipped.");
                            continue;
                        }
                        if (!isPinned)
                            continue;
                        path = ((dynamic)item).Path;
                    }

                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
                        || !Directory.Exists(path))
                        continue;
                    var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                    if (seen.Add(normalized))
                        paths.Add(normalized);
                }
                catch (Exception exception) when (IsReadFailure(exception))
                {
                    warnings.Add(legacyLinks
                        ? "Some legacy Explorer Links could not be read and were skipped."
                        : "Some Explorer folders could not be read and were skipped.");
                }
                finally
                {
                    Release(link);
                    Release(item);
                }
            }
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            warnings.Add(legacyLinks
                ? "Legacy Explorer Links could not be read."
                : "Explorer Quick Access could not be read.");
        }
        finally
        {
            Release(items);
            Release(folder);
        }
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is COMException or RuntimeBinderException or IOException
            or UnauthorizedAccessException or SecurityException or ArgumentException
            or InvalidCastException or NotSupportedException;

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
