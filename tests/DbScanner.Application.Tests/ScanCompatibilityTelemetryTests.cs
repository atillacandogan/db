using System.Diagnostics.Metrics;
using DbScanner.Application.Models;
using DbScanner.Application.Observability;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ScanCompatibilityTelemetryTests
{
    [Fact]
    public void LegacySelection_EmitsLowCardinalityFallbackMetric()
    {
        long fallbackCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ScanCompatibilityTelemetry.MeterName
                && instrument.Name == "dbscanner.scan_check.compatibility_fallbacks")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "surface" && Equals(tag.Value, "contract-selection"))
                {
                    fallbackCount += measurement;
                }
            }
        });
        listener.Start();

        var resolved = ScanCheckSelectionCompatibility.Resolve(null, ["banner"]);

        Assert.Equal(["banner"], resolved);
        Assert.Equal(1, fallbackCount);
    }

    [Fact]
    public void ConflictingSelection_EmitsConflictMetricAndFailsClosed()
    {
        long conflictCount = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ScanCompatibilityTelemetry.MeterName
                && instrument.Name == "dbscanner.scan_check.compatibility_conflicts")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "surface" && Equals(tag.Value, "contract-selection"))
                {
                    conflictCount += measurement;
                }
            }
        });
        listener.Start();

        Assert.Throws<InvalidOperationException>(() =>
            ScanCheckSelectionCompatibility.Resolve(["generic"], ["legacy"]));
        Assert.Equal(1, conflictCount);
    }
}
