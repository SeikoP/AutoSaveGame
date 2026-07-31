using System.Text.RegularExpressions;

namespace AutoSaveGame.Core.Services;

public sealed partial class PathTemplateService
{
    private static readonly string[] SupportedVariables =
        ["APPDATA", "LOCALAPPDATA", "USERPROFILE", "PROGRAMDATA"];

    private readonly IReadOnlyDictionary<string, string> environmentPaths;

    public PathTemplateService(IReadOnlyDictionary<string, string> environmentPaths)
    {
        ArgumentNullException.ThrowIfNull(environmentPaths);

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in SupportedVariables)
        {
            if (!environmentPaths.TryGetValue(variable, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[variable] = NormalizeRoot(value);
        }

        this.environmentPaths = normalized;
    }

    public string Collapse(string absolutePath)
    {
        var normalizedPath = NormalizeAbsolutePath(absolutePath);
        var match = environmentPaths
            .OrderByDescending(pair => pair.Value.Length)
            .FirstOrDefault(pair => IsWithinRoot(normalizedPath, pair.Value));

        if (string.IsNullOrEmpty(match.Key))
        {
            return normalizedPath;
        }

        var suffix = normalizedPath[match.Value.Length..];
        return $"%{match.Key}%{suffix}";
    }

    public string Expand(string pathTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);

        var variables = VariablePattern().Matches(pathTemplate);
        if (variables.Count == 0)
        {
            return NormalizeAbsolutePath(pathTemplate);
        }

        var first = variables[0];
        if (first.Index != 0 || variables.Count != 1)
        {
            throw new InvalidOperationException("The path template must start with one supported variable.");
        }

        var variable = first.Groups[1].Value;
        if (!environmentPaths.TryGetValue(variable, out var root))
        {
            throw new InvalidOperationException($"Unsupported path variable: {variable}.");
        }

        var suffix = pathTemplate[first.Length..].TrimStart(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var expanded = Path.GetFullPath(Path.Combine(root, suffix));

        if (!IsWithinRoot(expanded, root))
        {
            throw new InvalidOperationException(
                $"The expanded path escapes the {variable} root.");
        }

        return expanded;
    }

    private static string NormalizeAbsolutePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException("The save path must be absolute.");
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRoot(string path)
    {
        var fullPath = NormalizeAbsolutePath(path);
        var pathRoot = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithinRoot(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(
            root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("%([A-Za-z0-9_]+)%", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
