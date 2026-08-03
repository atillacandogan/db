using System.ComponentModel;
using System.Diagnostics;
using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure;

internal sealed class NmapScannerRuntime(
    IOptions<ScannerOptions> options,
    IMySqlEmptyPasswordProbe mySqlEmptyPasswordProbe,
    ILogger<NmapScannerRuntime> logger) : IScannerRuntime
{
    private const string MySqlInfoScriptName = "mysql-info";
    private const string LegacyMySqlEmptyPasswordScriptName = "mysql-empty-password";

    public async Task<ScanExecutionResult> RunAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication? authentication = null,
        CancellationToken cancellationToken = default)
    {
        var routedEmptyPasswordScripts = target.Engine == DatabaseEngine.MySql
            ? scripts.Where(IsLegacyMySqlEmptyPasswordScript).ToArray()
            : [];

        var nmapScripts = scripts
            .Except(routedEmptyPasswordScripts)
            .ToArray();

        var scriptNames = nmapScripts
            .Select(script => script.NseScriptName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (routedEmptyPasswordScripts.Length > 0
            && !scriptNames.Contains(MySqlInfoScriptName, StringComparer.OrdinalIgnoreCase))
        {
            // mysql-info is a preflight probe here. It is not persisted unless the
            // user actually selected it in the scan scope.
            scriptNames.Add(MySqlInfoScriptName);
        }

        try
        {
            var result = await ExecuteAsync(target, scriptNames, cancellationToken);
            var nmapResult = ParseResult(result.Xml, nmapScripts, result.ExitCode);

            if (routedEmptyPasswordScripts.Length == 0)
            {
                return nmapResult;
            }

            var authPlugin = ParseMySqlAuthPlugin(result.Xml);
            IReadOnlyList<ScanScriptExecutionResult> emptyPasswordResults;

            if (ShouldUseLegacyMySqlEmptyPasswordScript(authPlugin))
            {
                logger.LogInformation(
                    "MySQL target {TargetId} advertises {AuthPlugin}; legacy mysql-empty-password NSE will be used.",
                    target.Id,
                    authPlugin);

                var legacyScriptNames = routedEmptyPasswordScripts
                    .Select(script => script.NseScriptName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var legacyExecution = await ExecuteAsync(target, legacyScriptNames, cancellationToken);
                emptyPasswordResults = ParseResult(
                    legacyExecution.Xml,
                    routedEmptyPasswordScripts,
                    legacyExecution.ExitCode).ScriptResults;
            }
            else
            {
                logger.LogInformation(
                    "MySQL target {TargetId} advertises {AuthPlugin}; the MySQL 8.4-compatible empty-password probe will be used.",
                    target.Id,
                    authPlugin ?? "an unknown authentication plugin");

                var probeResults = new List<ScanScriptExecutionResult>();
                foreach (var script in routedEmptyPasswordScripts)
                {
                    probeResults.Add(await mySqlEmptyPasswordProbe.RunAsync(
                        target,
                        script,
                        cancellationToken));
                }

                emptyPasswordResults = probeResults;
            }

            return MergeResults(nmapResult, emptyPasswordResults);
        }
        catch (Exception exception) when (CanUseSampleOutput(exception))
        {
            logger.LogWarning(
                exception,
                "Nmap runtime was unavailable for scan {ScanId}. Sample XML output will be used.",
                scan.Id);

            var sampleXml = GetSampleXml(target);
            return ParseResult(sampleXml, scripts, 0);
        }
    }

    internal static string? ParseMySqlAuthPlugin(string xml)
    {
        var document = XDocument.Parse(xml);
        var mysqlInfo = document
            .Descendants("script")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("id")?.Value,
                MySqlInfoScriptName,
                StringComparison.OrdinalIgnoreCase));

        return mysqlInfo?
            .Descendants("elem")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("key")?.Value,
                "Auth Plugin Name",
                StringComparison.OrdinalIgnoreCase))?
            .Value
            .Trim();
    }

    internal static bool ShouldUseLegacyMySqlEmptyPasswordScript(string? authPlugin) =>
        string.Equals(authPlugin, "mysql_native_password", StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyMySqlEmptyPasswordScript(NseScript script) =>
        string.Equals(
            script.NseScriptName,
            LegacyMySqlEmptyPasswordScriptName,
            StringComparison.OrdinalIgnoreCase);

    private static ScanExecutionResult MergeResults(
        ScanExecutionResult nmapResult,
        IReadOnlyList<ScanScriptExecutionResult> additionalResults)
    {
        var allResults = nmapResult.ScriptResults
            .Concat(additionalResults)
            .ToArray();

        var additionalOutput = additionalResults
            .Select(result => $"{result.ScriptName}={result.NormalizedOutput}");
        var structuredOutput = string.Join(
            "; ",
            new[] { nmapResult.StructuredOutput }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Concat(additionalOutput));

        return new ScanExecutionResult(
            nmapResult.RawXml,
            structuredOutput,
            allResults);
    }

    private async Task<(string Xml, int ExitCode)> ExecuteAsync(
        Target target,
        IReadOnlyCollection<string> scriptNames,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.Value.ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-Pn");
        process.StartInfo.ArgumentList.Add("-sV");
        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add(target.Port.ToString());
        process.StartInfo.ArgumentList.Add("-oX");
        process.StartInfo.ArgumentList.Add("-");

        if (scriptNames.Count > 0)
        {
            process.StartInfo.ArgumentList.Add("--script");
            process.StartInfo.ArgumentList.Add(string.Join(",", scriptNames));
        }

        process.StartInfo.ArgumentList.Add(target.Host);
        process.Start();

        var timeout = TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds));
        var waitForExitTask = process.WaitForExitAsync(cancellationToken);
        var completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(timeout, cancellationToken));

        if (completedTask != waitForExitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill failures after timeout.
            }

            throw new TimeoutException($"Nmap execution exceeded the configured timeout of {timeout.TotalSeconds:0} seconds.");
        }

        await waitForExitTask;

        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError)
                    ? $"Nmap exited with code {process.ExitCode}."
                    : $"Nmap exited with code {process.ExitCode}: {standardError.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidOperationException("Nmap completed without XML output.");
        }

        return (standardOutput, process.ExitCode);
    }

    private static ScanExecutionResult ParseResult(
        string xml,
        IReadOnlyCollection<NseScript> scripts,
        int exitCode)
    {
        var document = XDocument.Parse(xml);
        var portElement = document.Descendants("port").FirstOrDefault();

        var state = portElement?.Element("state")?.Attribute("state")?.Value;
        var serviceElement = portElement?.Element("service");
        var serviceName = serviceElement?.Attribute("name")?.Value;
        var product = serviceElement?.Attribute("product")?.Value;
        var version = serviceElement?.Attribute("version")?.Value;

        var scriptElements = document.Descendants("script").ToArray();
        var scriptResults = new List<ScanScriptExecutionResult>();

        foreach (var script in scripts)
        {
            var scriptElement = scriptElements.FirstOrDefault(element =>
                string.Equals(element.Attribute("id")?.Value, script.NseScriptName, StringComparison.OrdinalIgnoreCase));

            if (scriptElement is null)
            {
                continue;
            }

            var rawOutput = scriptElement.ToString(SaveOptions.DisableFormatting);
            var normalizedOutput = scriptElement.Attribute("output")?.Value
                ?? string.Join("; ", scriptElement.Descendants("elem").Select(element => $"{element.Attribute("key")?.Value}={element.Value}"));

            scriptResults.Add(new ScanScriptExecutionResult(
                script.Id,
                script.NseScriptName,
                rawOutput,
                normalizedOutput,
                exitCode));
        }

        var structuredSegments = new[]
        {
            state is null ? null : $"state={state}",
            serviceName is null ? null : $"service={serviceName}",
            product is null ? null : $"product={product}",
            version is null ? null : $"version={version}"
        }
        .Where(segment => !string.IsNullOrWhiteSpace(segment))
        .Concat(scriptResults.Select(result => $"{result.ScriptName}={result.NormalizedOutput}"))
        .ToArray();

        return new ScanExecutionResult(
            xml,
            string.Join("; ", structuredSegments),
            scriptResults);
    }

    private bool CanUseSampleOutput(Exception exception) =>
        options.Value.UseSampleOutputWhenUnavailable
        && exception is Win32Exception or InvalidOperationException;

    private static string GetSampleXml(Target target)
    {
        var serviceName = target.Engine switch
        {
            DatabaseEngine.PostgreSql => "postgresql",
            DatabaseEngine.MySql => "mysql",
            DatabaseEngine.MsSql => "ms-sql-s",
            DatabaseEngine.Oracle => "oracle-tns",
            _ => "tcpwrapped"
        };

        var product = target.Engine switch
        {
            DatabaseEngine.PostgreSql => "PostgreSQL DB",
            DatabaseEngine.MySql => "MySQL",
            DatabaseEngine.MsSql => "Microsoft SQL Server",
            DatabaseEngine.Oracle => "Oracle Database Listener",
            _ => "Unknown"
        };

        var version = target.Engine switch
        {
            DatabaseEngine.PostgreSql => "16.3",
            DatabaseEngine.MySql => "8.4.0",
            DatabaseEngine.MsSql => "16.0",
            DatabaseEngine.Oracle => "23ai",
            _ => "unknown"
        };

        var scriptBlock = target.Engine switch
        {
            DatabaseEngine.PostgreSql =>
                $"""
                      <script id="banner" output="PostgreSQL server ready; ssl disabled; version {version}" />
                """,
            DatabaseEngine.MySql =>
                $"""
                      <script id="banner" output="MySQL server ready; version {version}" />
                      <script id="mysql-info" output="Protocol: 10; Version: {version}; Capabilities: compression,ssl" />
                """,
            DatabaseEngine.MsSql =>
                $"""
                      <script id="banner" output="Microsoft SQL Server ready; version {version}" />
                      <script id="ms-sql-info" output="Microsoft SQL Server; version {version}" />
                """,
            DatabaseEngine.Oracle =>
                $"""
                      <script id="banner" output="Oracle Database Listener ready; version {version}" />
                      <script id="oracle-tns-version" output="Oracle TNS Listener; version {version}; service=FREEPDB1" />
                """,
            _ =>
                """
                      <script id="banner" output="Database service detected" />
                """
        };

        return
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <nmaprun scanner="nmap" args="sample" start="{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}">
               <host>
                 <status state="up" reason="user-set" />
                 <address addr="{target.Host}" addrtype="ipv4" />
                 <ports>
                   <port protocol="tcp" portid="{target.Port}">
                     <state state="open" reason="syn-ack" reason_ttl="0" />
                     <service name="{serviceName}" product="{product}" version="{version}" method="probed" conf="10" />
             {scriptBlock}
                   </port>
                 </ports>
               </host>
             </nmaprun>
             """;
    }
}
