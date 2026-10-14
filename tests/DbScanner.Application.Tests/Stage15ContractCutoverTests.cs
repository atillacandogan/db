using System.Text.Json;
using DbScanner.Api.Mapping;
using DbScanner.Application.Models;
using DbScanner.Contracts.Scans;
using DbScanner.Domain.Entities;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class Stage15ContractCutoverTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Historical_and_generic_scan_rows_expose_the_same_generic_identity(bool genericRow)
    {
        var scan = new Scan();
        scan.ScriptResults.Add(new ScanScriptResult
        {
            ScanId = scan.Id,
            NseScriptId = "banner",
            ScanCheckId = genericRow ? "banner" : null,
            ScriptName = "banner",
            NormalizedOutput = "version=16",
            RawXml = "<script id=\"banner\" />",
            ExitCode = 0
        });

        var response = scan.ToDto();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));
        var result = json.RootElement.GetProperty("scriptResults")[0];

        Assert.Equal("banner", result.GetProperty("scanCheckId").GetString());
        Assert.False(result.TryGetProperty("nseScriptId", out _));
        Assert.Equal("version=16", result.GetProperty("normalizedOutput").GetString());
    }

    [Fact]
    public void Conflicting_historical_row_cannot_be_silently_mapped_to_a_different_check()
    {
        var scan = new Scan();
        scan.ScriptResults.Add(new ScanScriptResult
        {
            NseScriptId = "old-check",
            ScanCheckId = "different-check",
            ScriptName = "banner"
        });

        Assert.Throws<InvalidOperationException>(() => scan.ToDto());
    }

    [Fact]
    public void Explicit_empty_selection_survives_new_message_serialization()
    {
        var message = ScanRequestedMessageCompatibility.Create(
            Guid.NewGuid(), Guid.NewGuid(), "postgresql", "db.internal", 5432,
            DateTimeOffset.UtcNow, [], "protected-not-plaintext");
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var consumed = JsonSerializer.Deserialize<ScanRequestedMessage>(json, JsonOptions)!;

        Assert.Empty(ScanRequestedMessageCompatibility.ResolveSelection(consumed)!);
        Assert.Equal("protected-not-plaintext", consumed.ProtectedCredentials);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("selectedNseScriptIds", out _));
    }

    [Fact]
    public void Unspecified_selection_preserves_default_plan_semantics()
    {
        var message = ScanRequestedMessageCompatibility.Create(
            Guid.NewGuid(), Guid.NewGuid(), "postgresql", "db.internal", 5432,
            DateTimeOffset.UtcNow, null, null);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var consumed = JsonSerializer.Deserialize<ScanRequestedMessage>(json, JsonOptions)!;

        Assert.Null(ScanRequestedMessageCompatibility.ResolveSelection(consumed));
    }

    [Fact]
    public void Queued_dual_generation_message_with_conflicting_selection_is_rejected()
    {
        var message = new ScanRequestedMessage(
            Guid.NewGuid(), Guid.NewGuid(), "postgresql", "db.internal", 5432,
            DateTimeOffset.UtcNow,
            SelectedNseScriptIds: ["legacy-only"],
            SelectedCheckIds: ["new-only"]);
        var consumed = JsonSerializer.Deserialize<ScanRequestedMessage>(
            JsonSerializer.Serialize(message, JsonOptions), JsonOptions)!;

        Assert.Throws<InvalidOperationException>(() =>
            ScanRequestedMessageCompatibility.ResolveSelection(consumed));
    }
}
