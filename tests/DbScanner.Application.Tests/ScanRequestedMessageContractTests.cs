using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Contracts.Scans;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ScanRequestedMessageContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OldProducerMessage_IsConsumedByNewResolver()
    {
        var legacyMessage = new LegacyScanRequestedMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "postgresql",
            "db.internal",
            5432,
            DateTimeOffset.Parse("2026-10-04T00:00:00+00:00"),
            ["banner"]);
        var json = JsonSerializer.Serialize(legacyMessage, JsonOptions);

        var currentMessage = JsonSerializer.Deserialize<ScanRequestedMessage>(json, JsonOptions)!;
        var selectedCheckIds = ScanRequestedMessageCompatibility.ResolveSelection(currentMessage);

        Assert.Equal(["banner"], selectedCheckIds);
        Assert.Null(currentMessage.SelectedCheckIds);
    }

    [Fact]
    public void NewProducerMessage_EmitsOnlyGenericSelection()
    {
        var currentMessage = ScanRequestedMessageCompatibility.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "postgresql",
            "db.internal",
            5432,
            DateTimeOffset.Parse("2026-10-04T00:00:00+00:00"),
            ["banner"],
            protectedCredentials: null);
        var json = JsonSerializer.Serialize(currentMessage, JsonOptions);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("selectedNseScriptIds", out _));
        var consumed = JsonSerializer.Deserialize<ScanRequestedMessage>(json, JsonOptions)!;
        Assert.Equal(["banner"], ScanRequestedMessageCompatibility.ResolveSelection(consumed));
    }

    [Fact]
    public void NewProducer_requires_consumer_upgrade_before_generic_only_messages_are_published()
    {
        var message = ScanRequestedMessageCompatibility.Create(
            Guid.NewGuid(), Guid.NewGuid(), "postgresql", "db.internal", 5432,
            DateTimeOffset.UtcNow, ["banner"], protectedCredentials: null);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var oldConsumer = JsonSerializer.Deserialize<LegacyScanRequestedMessage>(json, JsonOptions)!;

        // The old consumer would use its default plan: it cannot see the selection.
        Assert.Null(oldConsumer.SelectedNseScriptIds);
        var newConsumer = JsonSerializer.Deserialize<ScanRequestedMessage>(json, JsonOptions)!;
        Assert.Equal(["banner"], ScanRequestedMessageCompatibility.ResolveSelection(newConsumer));
    }

    [Fact]
    public void NewProducer_PreservesSelectionWithoutLegacyDualWrite()
    {
        var message = ScanRequestedMessageCompatibility.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "mysql",
            "db.internal",
            3306,
            DateTimeOffset.UtcNow,
            ["native-version", "permissions"],
            protectedCredentials: null);

        Assert.Null(message.SelectedNseScriptIds);
        Assert.Equal(
            ["native-version", "permissions"],
            ScanRequestedMessageCompatibility.ResolveSelection(message));
    }

    private sealed record LegacyScanRequestedMessage(
        Guid ScanId,
        Guid TargetId,
        string Engine,
        string Host,
        int Port,
        DateTimeOffset RequestedAt,
        IReadOnlyList<string>? SelectedNseScriptIds = null,
        string? ProtectedCredentials = null);
}
