using System.Collections.Concurrent;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class DiscoveryAgent(
    IDiscoveryRepository discoveryRepository,
    IDiscoveryScopeValidator scopeValidator,
    IDiscoveryProbe probe,
    IEnumerable<IDatabaseFingerprintDetector> detectors,
    IDiscoveryResultClassifier classifier,
    IOptions<DiscoveryOptions> options,
    ILogger<DiscoveryAgent> logger) : IDiscoveryAgent
{
    public async Task ExecuteAsync(Guid discoveryJobId, CancellationToken cancellationToken = default)
    {
        if (!await discoveryRepository.TryMarkRunningAsync(discoveryJobId, cancellationToken))
        {
            logger.LogInformation("Discovery job {DiscoveryJobId} is not pending and was ignored.", discoveryJobId);
            return;
        }

        try
        {
            var job = await discoveryRepository.GetByIdAsync(discoveryJobId, cancellationToken)
                ?? throw new InvalidOperationException("Discovery job was not found.");
            var engineFilter = ParseEngineFilter(job.SupportedEngineFilter);

            // Defense in depth: scope is validated again in the worker immediately before probing.
            var endpoints = await scopeValidator.ValidateAndPlanAsync(job.ScopeInput, engineFilter, cancellationToken);
            var results = new ConcurrentBag<DiscoveredDatabase>();
            var orderedDetectors = detectors.OrderBy(detector => detector.Order).ToArray();
            using var rateGate = new SemaphoreSlim(1, 1);
            var nextProbeAt = DateTimeOffset.UtcNow;
            var interval = TimeSpan.FromMilliseconds(1000d / Math.Max(1, options.Value.MaxProbesPerSecond));

            await Parallel.ForEachAsync(
                endpoints,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(1, options.Value.MaxConcurrency)
                },
                async (endpoint, token) =>
                {
                    await WaitForRateLimitAsync(rateGate, interval, () => nextProbeAt, value => nextProbeAt = value, token);
                    var probeResult = await probe.ProbeAsync(endpoint, token);
                    if (!probeResult.IsOpen)
                    {
                        return;
                    }

                    var fingerprint = orderedDetectors
                        .Select(detector => detector.Detect(probeResult))
                        .FirstOrDefault(candidate => candidate is not null);
                    if (fingerprint is null)
                    {
                        return;
                    }

                    var classification = classifier.Classify(fingerprint, engineFilter);
                    results.Add(new DiscoveredDatabase
                    {
                        DiscoveryJobId = discoveryJobId,
                        Host = endpoint.Host,
                        Port = endpoint.Port,
                        DetectedService = classification.Service,
                        DetectedEngine = classification.Engine,
                        Confidence = classification.Confidence,
                        Status = classification.Status,
                        Reason = classification.Reason
                    });
                });

            await discoveryRepository.CompleteAsync(discoveryJobId, results.ToArray(), cancellationToken);
            logger.LogInformation(
                "Discovery job {DiscoveryJobId} completed. Open database services: {ResultCount}.",
                discoveryJobId,
                results.Count);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            await discoveryRepository.FailAsync(discoveryJobId, SanitizeError(exception), cancellationToken);
            throw;
        }
    }

    private static async Task WaitForRateLimitAsync(
        SemaphoreSlim gate,
        TimeSpan interval,
        Func<DateTimeOffset> getNext,
        Action<DateTimeOffset> setNext,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var delay = getNext() - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            setNext(DateTimeOffset.UtcNow + interval);
        }
        finally
        {
            gate.Release();
        }
    }

    private static HashSet<DatabaseEngine> ParseEngineFilter(string value)
    {
        var engines = new HashSet<DatabaseEngine>();
        foreach (var item in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (DatabaseEngineExtensions.TryParseApiString(item, out var engine))
            {
                engines.Add(engine);
            }
        }

        return engines;
    }

    private static string SanitizeError(Exception exception) => exception switch
    {
        InvalidOperationException => exception.Message[..Math.Min(exception.Message.Length, 512)],
        _ => "Discovery processing failed. Review worker logs for details."
    };
}
