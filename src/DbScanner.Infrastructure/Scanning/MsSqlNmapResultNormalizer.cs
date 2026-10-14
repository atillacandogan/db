using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Infrastructure.Scanning;

internal static class MsSqlNmapResultNormalizer
{
    private const string MsSqlInfoScriptName = "ms-sql-info";

    internal static ScanExecutionResult NormalizeMsSqlInfoResult(
        ScanExecutionResult result,
        IReadOnlyCollection<NseScript> scripts)
    {
        var msSqlInfoScript = scripts.FirstOrDefault(script =>
            string.Equals(
                script.NseScriptName,
                MsSqlInfoScriptName,
                StringComparison.OrdinalIgnoreCase));

        if (msSqlInfoScript is null || string.IsNullOrWhiteSpace(result.RawXml))
        {
            return result;
        }

        var currentResult = result.ScriptResults.FirstOrDefault(scriptResult =>
            string.Equals(
                scriptResult.ScriptName,
                MsSqlInfoScriptName,
                StringComparison.OrdinalIgnoreCase));

        if (currentResult is not null
            && !currentResult.NormalizedOutput.Contains(
                "ERROR:",
                StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(result.RawXml);
        }
        catch
        {
            return result;
        }

        var serviceElement = document
            .Descendants("port")
            .Select(port => port.Element("service"))
            .FirstOrDefault(service =>
                service is not null
                && IsMsSqlService(service));

        if (serviceElement is null)
        {
            return result;
        }

        var serviceName = serviceElement.Attribute("name")?.Value;
        var product = serviceElement.Attribute("product")?.Value;
        var version = serviceElement.Attribute("version")?.Value;
        var normalizedOutput = string.Join(
            "; ",
            new[]
            {
                serviceName is null ? null : $"service={serviceName}",
                product is null ? null : $"product={product}",
                version is null ? null : $"version={version}",
                "source=nmap-service-detection"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var normalizedResult = new ScanScriptExecutionResult(
            msSqlInfoScript.Id,
            msSqlInfoScript.NseScriptName,
            serviceElement.ToString(SaveOptions.DisableFormatting),
            normalizedOutput,
            0);
        var scriptResults = result.ScriptResults
            .Where(scriptResult => !string.Equals(
                scriptResult.ScriptName,
                MsSqlInfoScriptName,
                StringComparison.OrdinalIgnoreCase))
            .Append(normalizedResult)
            .ToArray();
        var structuredSegments = result.StructuredOutput
            .Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !segment.StartsWith(
                $"{MsSqlInfoScriptName}=ERROR:",
                StringComparison.OrdinalIgnoreCase))
            .Append($"{MsSqlInfoScriptName}={normalizedOutput}");

        return result with
        {
            StructuredOutput = string.Join("; ", structuredSegments),
            ScriptResults = scriptResults
        };
    }

    private static bool IsMsSqlService(XElement service)
    {
        var serviceName = service.Attribute("name")?.Value ?? string.Empty;
        var product = service.Attribute("product")?.Value ?? string.Empty;
        var tunnel = service.Attribute("tunnel")?.Value ?? string.Empty;

        return serviceName.Equals("ms-sql-s", StringComparison.OrdinalIgnoreCase)
            || serviceName.Equals("mssql", StringComparison.OrdinalIgnoreCase)
            || serviceName.Contains("sqlserver", StringComparison.OrdinalIgnoreCase)
            || product.Contains("Microsoft SQL Server", StringComparison.OrdinalIgnoreCase)
            || product.Contains("SQL Server", StringComparison.OrdinalIgnoreCase)
            || product.Contains("TDS", StringComparison.OrdinalIgnoreCase)
            || tunnel.Contains("tds", StringComparison.OrdinalIgnoreCase);
    }

}
