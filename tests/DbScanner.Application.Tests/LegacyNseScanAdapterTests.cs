using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class LegacyNseScanAdapterTests
{
    [Fact]
    public void Nse_script_round_trip_preserves_catalog_and_security_metadata()
    {
        var legacy = new NseScript
        {
            Id = "pg-auth-001",
            NseScriptName = "pgsql-brute",
            DisplayName = "PostgreSQL Authentication",
            Description = "Checks PostgreSQL authentication behavior.",
            SupportedEngines = "postgresql, generic,postgresql",
            Categories = "auth,safe",
            DefaultEnabled = true,
            RequiresCredentials = true,
            IsIntrusive = false,
            AllowedArgsSchema = "{\"type\":\"object\"}",
            ParserType = ParserType.StructuredXml,
            MatcherType = MatcherType.XPath,
            MatcherExpression = "//script[@id='pgsql-brute']",
            Severity = Severity.High,
            Weight = 42,
            RemediationTemplate = "Disable weak credentials.",
            DocUrl = "https://example.test/checks/pg-auth-001",
            ApprovalStatus = ScriptApprovalStatus.Approved,
            Source = NseScriptSource.Installed,
            IsInstalled = true
        };

        var generic = legacy.ToScanCheckDefinition();
        var roundTrip = generic.ToNseScript(legacy.Source, legacy.IsInstalled);

        Assert.Equal(ScanExecutorTypes.NmapNse, generic.ExecutorType);
        Assert.Equal(new[] { DatabaseEngine.PostgreSql, DatabaseEngine.Generic }, generic.SupportedEngines);
        Assert.Equal(CheckApprovalStatus.Approved, generic.ApprovalStatus);

        Assert.Equal(legacy.Id, roundTrip.Id);
        Assert.Equal(legacy.NseScriptName, roundTrip.NseScriptName);
        Assert.Equal(legacy.DisplayName, roundTrip.DisplayName);
        Assert.Equal(legacy.Description, roundTrip.Description);
        Assert.Equal("postgresql,generic", roundTrip.SupportedEngines);
        Assert.Equal(legacy.Categories, roundTrip.Categories);
        Assert.Equal(legacy.DefaultEnabled, roundTrip.DefaultEnabled);
        Assert.Equal(legacy.RequiresCredentials, roundTrip.RequiresCredentials);
        Assert.Equal(legacy.IsIntrusive, roundTrip.IsIntrusive);
        Assert.Equal(legacy.AllowedArgsSchema, roundTrip.AllowedArgsSchema);
        Assert.Equal(legacy.ParserType, roundTrip.ParserType);
        Assert.Equal(legacy.MatcherType, roundTrip.MatcherType);
        Assert.Equal(legacy.MatcherExpression, roundTrip.MatcherExpression);
        Assert.Equal(legacy.Severity, roundTrip.Severity);
        Assert.Equal(legacy.Weight, roundTrip.Weight);
        Assert.Equal(legacy.RemediationTemplate, roundTrip.RemediationTemplate);
        Assert.Equal(legacy.DocUrl, roundTrip.DocUrl);
        Assert.Equal(legacy.ApprovalStatus, roundTrip.ApprovalStatus);
        Assert.Equal(legacy.Source, roundTrip.Source);
        Assert.Equal(legacy.IsInstalled, roundTrip.IsInstalled);
    }

    [Fact]
    public void Script_result_round_trip_preserves_identity_xml_output_and_status()
    {
        var legacy = new ScanScriptExecutionResult(
            "script-42",
            "pgsql-brute",
            "<script id=\"pgsql-brute\" output=\"safe\" />",
            "{\"status\":\"safe\"}",
            7,
            ProbeResultStatus.InsufficientPrivileges);

        var generic = legacy.ToCheckExecutionResult();
        var roundTrip = generic.ToLegacyScanScriptExecutionResult();

        Assert.Equal(legacy.NseScriptId, generic.CheckId);
        Assert.Equal(ScanExecutorTypes.NmapNse, generic.ExecutorType);
        Assert.Equal(ScanPayloadFormat.Xml, generic.PayloadFormat);
        Assert.Equal(legacy, roundTrip);
    }

    [Fact]
    public void Composite_result_round_trip_preserves_scan_xml_and_all_check_results()
    {
        var legacy = new ScanExecutionResult(
            "<nmaprun><host /></nmaprun>",
            "{\"host\":\"db.internal\"}",
            new[]
            {
                new ScanScriptExecutionResult(
                    "script-a",
                    "pgsql-info",
                    "<script id=\"pgsql-info\" />",
                    "version=16",
                    0,
                    ProbeResultStatus.Passed),
                new ScanScriptExecutionResult(
                    "script-b",
                    "pgsql-config",
                    null,
                    "setting=secure",
                    1,
                    ProbeResultStatus.Failed)
            });

        var generic = legacy.ToCompositeScanExecutionResult();
        var roundTrip = generic.ToLegacyScanExecutionResult();

        Assert.Single(generic.Artifacts);
        Assert.Equal(ScanPayloadFormat.Xml, generic.Artifacts[0].PayloadFormat);
        Assert.Equal(legacy.RawXml, generic.Artifacts[0].RawPayload);
        Assert.Equal(legacy.StructuredOutput, roundTrip.StructuredOutput);
        Assert.Equal(legacy.RawXml, roundTrip.RawXml);
        Assert.Equal(legacy.ScriptResults, roundTrip.ScriptResults);
    }

    [Fact]
    public void Non_xml_result_is_rejected_by_legacy_adapter_instead_of_being_mislabeled()
    {
        var generic = new CheckExecutionResult(
            "api-check",
            "External API check",
            ScanExecutorTypes.ExternalApi,
            "{\"healthy\":true}",
            ScanPayloadFormat.Json,
            "healthy=true",
            0,
            ProbeResultStatus.Passed);

        var error = Assert.Throws<InvalidOperationException>(
            generic.ToLegacyScanScriptExecutionResult);

        Assert.Contains("legacy XML contract", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Composite_with_mixed_executors_is_rejected_by_single_executor_legacy_adapter()
    {
        var composite = new CompositeScanExecutionResult(
            "{}",
            new[]
            {
                new CheckExecutionResult(
                    "nse-check",
                    "NSE check",
                    ScanExecutorTypes.NmapNse,
                    "<script />",
                    ScanPayloadFormat.Xml,
                    "ok",
                    0),
                new CheckExecutionResult(
                    "api-check",
                    "API check",
                    ScanExecutorTypes.ExternalApi,
                    "<legacy-compatible-shape />",
                    ScanPayloadFormat.Xml,
                    "ok",
                    0)
            },
            new[]
            {
                new ScanExecutionArtifact(
                    ScanExecutorTypes.NmapNse,
                    "<nmaprun />",
                    ScanPayloadFormat.Xml)
            });

        var error = Assert.Throws<InvalidOperationException>(
            () => composite.ToLegacyScanExecutionResult());

        Assert.Contains(ScanExecutorTypes.ExternalApi, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execution_context_keeps_existing_domain_and_protected_authentication_models()
    {
        var target = new Target
        {
            Name = "Production replica",
            Host = "db.internal",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };
        var scan = new Scan { TargetId = target.Id, Target = target };
        var authentication = new DatabaseAuthentication("app", "scanner", "secret");

        var context = new ScanExecutionContext(scan, target, authentication);

        Assert.Same(scan, context.Scan);
        Assert.Same(target, context.Target);
        Assert.Same(authentication, context.Authentication);
    }

    [Fact]
    public void Nse_adapter_rejects_unknown_engine_instead_of_silently_changing_scope()
    {
        var legacy = new NseScript
        {
            Id = "unknown-engine",
            NseScriptName = "unknown-engine",
            DisplayName = "Unknown engine",
            Description = "Invalid fixture",
            SupportedEngines = "postgresql,db2"
        };

        var error = Assert.Throws<InvalidOperationException>(() => legacy.ToScanCheckDefinition());

        Assert.Contains("db2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Executor_identifier_validation_allows_extensions_without_expanding_a_central_list()
    {
        Assert.True(ScanExecutorTypes.IsValid("vendor-custom-scanner"));
        Assert.False(ScanExecutorTypes.IsValid("../../bin/scanner"));
        Assert.False(ScanExecutorTypes.IsValid("Vendor Scanner"));
    }
}
