using System.Text.RegularExpressions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Scanning;

public sealed class InstalledNseScriptSyncService(
    INseScriptRepository nseScriptRepository,
    IOptions<NseScriptDiscoveryOptions> options,
    ILogger<InstalledNseScriptSyncService> logger) : INseScriptSyncService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(150);
    private static readonly SemaphoreSlim SyncLock = new(1, 1);

    public async Task<NseScriptSyncResult> SyncInstalledScriptsAsync(CancellationToken cancellationToken = default)
    {
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            var scriptDirectory = options.Value.ScriptDirectory;
            if (!Directory.Exists(scriptDirectory))
            {
                throw new DirectoryNotFoundException($"NSE script discovery directory was not found: {scriptDirectory}");
            }

            var files = Directory
                .EnumerateFiles(scriptDirectory, "*.nse", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No NSE scripts were found in {scriptDirectory}; existing installed records were preserved.");
            }

            var allScripts = await nseScriptRepository.ListAsync(cancellationToken);
            var manualScripts = allScripts
                .Where(script => script.Source == NseScriptSource.Manual)
                .ToArray();
            var installedScripts = await nseScriptRepository.ListInstalledForSyncAsync(cancellationToken);
            var installedById = installedScripts.ToDictionary(script => script.Id, StringComparer.OrdinalIgnoreCase);
            var installedByName = installedScripts.ToDictionary(script => script.NseScriptName, StringComparer.OrdinalIgnoreCase);
            var discoveredInstalledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var addedCount = 0;
            var updatedCount = 0;
            var unchangedCount = 0;
            var skippedCount = 0;
            var failedCount = 0;
            var uninstalledCount = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var scriptName = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(scriptName) || !IsValidScriptName(scriptName))
                {
                    logger.LogWarning("Skipped NSE script with an invalid file name: {ScriptFile}", file);
                    skippedCount++;
                    continue;
                }

                var installedScript = installedByName.GetValueOrDefault(scriptName)
                    ?? installedById.GetValueOrDefault(scriptName);
                if (installedScript is not null)
                {
                    // The source file exists even when its metadata cannot be parsed. Do not
                    // mark an existing record as uninstalled because one file is malformed.
                    discoveredInstalledIds.Add(installedScript.Id);
                }

                if (installedScript is null && manualScripts.Any(script =>
                        string.Equals(script.Id, scriptName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(script.NseScriptName, scriptName, StringComparison.OrdinalIgnoreCase)))
                {
                    logger.LogWarning(
                        "Installed NSE script {ScriptName} was not imported because a manual record uses the same id or script name.",
                        scriptName);
                    skippedCount++;
                    continue;
                }

                try
                {
                    var metadata = ReadMetadata(file, scriptName);
                    var supportedEngines = InferSupportedEngines(scriptName);

                    if (installedScript is not null)
                    {
                        if (ApplyInstalledMetadata(installedScript, scriptName, metadata, supportedEngines))
                        {
                            updatedCount++;
                        }
                        else
                        {
                            unchangedCount++;
                        }

                        continue;
                    }

                    var script = new NseScript
                    {
                        Id = scriptName,
                        ExecutorType = ScanExecutorTypes.NmapNse,
                        NseScriptName = scriptName,
                        DisplayName = ToDisplayName(scriptName),
                        Description = metadata.Description,
                        SupportedEngines = string.Join(",", supportedEngines),
                        Categories = JoinCategories(metadata.Categories),
                        DefaultEnabled = false,
                        RequiresCredentials = RequiresCredentials(metadata.Categories),
                        IsIntrusive = IsIntrusive(metadata.Categories),
                        ParserType = ParserType.GenericOutput,
                        MatcherType = MatcherType.Contains,
                        MatcherExpression = scriptName,
                        Severity = Severity.Info,
                        Weight = 0,
                        ApprovalStatus = ScriptApprovalStatus.Draft,
                        Source = NseScriptSource.Installed,
                        IsInstalled = true
                    };

                    script.Matchers.Add(new NseScriptMatcher
                    {
                        NseScriptId = script.Id,
                        MatcherExpression = script.MatcherExpression,
                        MatcherType = script.MatcherType,
                        ParserType = script.ParserType,
                        Engine = ParsePrimaryEngine(supportedEngines)
                    });

                    await nseScriptRepository.AddAsync(script, cancellationToken);
                    installedById.Add(script.Id, script);
                    installedByName.Add(script.NseScriptName, script);
                    discoveredInstalledIds.Add(script.Id);
                    addedCount++;
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or RegexMatchTimeoutException)
                {
                    failedCount++;
                    logger.LogError(
                        exception,
                        "Failed to parse NSE script {ScriptName} from {ScriptFile}; synchronization will continue.",
                        scriptName,
                        file);
                }
            }

            foreach (var script in installedScripts.Where(script => !discoveredInstalledIds.Contains(script.Id)))
            {
                if (script.IsInstalled != false)
                {
                    script.IsInstalled = false;
                    updatedCount++;
                    uninstalledCount++;
                }
            }

            if (addedCount > 0 || updatedCount > 0)
            {
                await nseScriptRepository.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation(
                "NSE script sync completed. Directory: {ScriptDirectory}, discovered: {DiscoveredCount}, added: {AddedCount}, updated: {UpdatedCount}, unchanged: {UnchangedCount}, skipped: {SkippedCount}, failed: {FailedCount}, uninstalled: {UninstalledCount}",
                scriptDirectory,
                files.Length,
                addedCount,
                updatedCount,
                unchangedCount,
                skippedCount,
                failedCount,
                uninstalledCount);

            return new NseScriptSyncResult(
                scriptDirectory,
                files.Length,
                addedCount,
                updatedCount,
                unchangedCount,
                skippedCount,
                failedCount,
                uninstalledCount);
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private static bool ApplyInstalledMetadata(
        NseScript script,
        string scriptName,
        InstalledNseScriptMetadata metadata,
        IReadOnlyList<string> supportedEngines)
    {
        var changed = false;
        changed |= SetNullableIfChanged(
            script.ExecutorType,
            ScanExecutorTypes.NmapNse,
            value => script.ExecutorType = value);
        changed |= SetIfChanged(script.NseScriptName, scriptName, value => script.NseScriptName = value);
        changed |= SetIfChanged(script.DisplayName, ToDisplayName(scriptName), value => script.DisplayName = value);
        changed |= SetIfChanged(script.Description, metadata.Description, value => script.Description = value);
        changed |= SetIfChanged(script.SupportedEngines, string.Join(",", supportedEngines), value => script.SupportedEngines = value);
        changed |= SetNullableIfChanged(script.Categories, JoinCategories(metadata.Categories), value => script.Categories = value);

        var requiresCredentials = RequiresCredentials(metadata.Categories);
        if (script.RequiresCredentials != requiresCredentials)
        {
            script.RequiresCredentials = requiresCredentials;
            changed = true;
        }

        var isIntrusive = IsIntrusive(metadata.Categories);
        if (script.IsIntrusive != isIntrusive)
        {
            script.IsIntrusive = isIntrusive;
            changed = true;
        }

        if (script.Source != NseScriptSource.Installed)
        {
            script.Source = NseScriptSource.Installed;
            changed = true;
        }

        if (script.IsInstalled != true)
        {
            script.IsInstalled = true;
            changed = true;
        }

        var primaryMatcher = script.Matchers.FirstOrDefault();
        var primaryEngine = ParsePrimaryEngine(supportedEngines);
        if (primaryMatcher is not null && primaryMatcher.Engine != primaryEngine)
        {
            primaryMatcher.Engine = primaryEngine;
            changed = true;
        }

        return changed;
    }

    private static bool SetIfChanged(string current, string next, Action<string> setter)
    {
        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            return false;
        }

        setter(next);
        return true;
    }

    private static bool SetNullableIfChanged(string? current, string? next, Action<string?> setter)
    {
        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            return false;
        }

        setter(next);
        return true;
    }

    private static string? JoinCategories(IReadOnlyList<string> categories) =>
        categories.Count == 0 ? null : string.Join(",", categories);

    private static bool RequiresCredentials(IReadOnlyList<string> categories) =>
        categories.Contains("auth", StringComparer.OrdinalIgnoreCase)
        || categories.Contains("brute", StringComparer.OrdinalIgnoreCase);

    private static bool IsIntrusive(IReadOnlyList<string> categories) =>
        categories.Contains("intrusive", StringComparer.OrdinalIgnoreCase)
        || categories.Contains("dos", StringComparer.OrdinalIgnoreCase)
        || categories.Contains("exploit", StringComparer.OrdinalIgnoreCase);

    private static InstalledNseScriptMetadata ReadMetadata(string file, string scriptName)
    {
        var content = File.ReadAllText(file);
        var description = ExtractDescription(content)
            ?? $"Installed Nmap NSE script: {scriptName}.";
        var categories = ExtractCategories(content);

        return new InstalledNseScriptMetadata(CleanText(description), categories);
    }

    private static string? ExtractDescription(string content)
    {
        var blockMatch = Regex.Match(
            content,
            @"description\s*=\s*\[\[(.*?)\]\]",
            RegexOptions.Singleline,
            RegexTimeout);
        if (blockMatch.Success)
        {
            return blockMatch.Groups[1].Value;
        }

        var stringMatch = Regex.Match(
            content,
            "description\\s*=\\s*\"([^\"]+)\"",
            RegexOptions.Singleline,
            RegexTimeout);
        return stringMatch.Success ? stringMatch.Groups[1].Value : null;
    }

    private static IReadOnlyList<string> ExtractCategories(string content)
    {
        var match = Regex.Match(
            content,
            @"categories\s*=\s*\{(.*?)\}",
            RegexOptions.Singleline,
            RegexTimeout);
        if (!match.Success)
        {
            return [];
        }

        return Regex.Matches(match.Groups[1].Value, "\"([^\"]+)\"", RegexOptions.None, RegexTimeout)
            .Cast<Match>()
            .Select(item => item.Groups[1].Value.Trim().ToLowerInvariant())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CleanText(string value)
    {
        var text = Regex.Replace(value, @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();
        return text.Length <= 2048 ? text : string.Concat(text.AsSpan(0, 2045), "...");
    }

    private static IReadOnlyList<string> InferSupportedEngines(string scriptName)
    {
        if (scriptName.StartsWith("mysql-", StringComparison.OrdinalIgnoreCase)
            || scriptName.Contains("mysql", StringComparison.OrdinalIgnoreCase))
        {
            return ["mysql"];
        }

        if (scriptName.StartsWith("pgsql-", StringComparison.OrdinalIgnoreCase)
            || scriptName.Contains("postgres", StringComparison.OrdinalIgnoreCase)
            || scriptName.Contains("postgresql", StringComparison.OrdinalIgnoreCase))
        {
            return ["postgresql"];
        }

        if (scriptName.StartsWith("ms-sql-", StringComparison.OrdinalIgnoreCase)
            || scriptName.StartsWith("mssql-", StringComparison.OrdinalIgnoreCase)
            || scriptName.StartsWith("sqlserver-", StringComparison.OrdinalIgnoreCase))
        {
            return ["mssql"];
        }

        if (scriptName.StartsWith("oracle-", StringComparison.OrdinalIgnoreCase)
            || scriptName.StartsWith("tns-", StringComparison.OrdinalIgnoreCase)
            || scriptName.Contains("oracle", StringComparison.OrdinalIgnoreCase))
        {
            return ["oracle"];
        }

        return ["generic"];
    }

    private static DatabaseEngine ParsePrimaryEngine(IReadOnlyList<string> supportedEngines)
    {
        if (supportedEngines.Contains("postgresql", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.PostgreSql;
        }

        if (supportedEngines.Contains("mysql", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.MySql;
        }

        if (supportedEngines.Contains("mssql", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.MsSql;
        }

        if (supportedEngines.Contains("oracle", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.Oracle;
        }

        return DatabaseEngine.Generic;
    }

    private static string ToDisplayName(string scriptName) =>
        string.Join(
            " ",
            scriptName
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..]));

    private static bool IsValidScriptName(string scriptName) =>
        Regex.IsMatch(scriptName, @"^[A-Za-z][A-Za-z0-9.-]*$", RegexOptions.None, RegexTimeout);

    private sealed record InstalledNseScriptMetadata(string Description, IReadOnlyList<string> Categories);
}
