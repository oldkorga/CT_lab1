using RRC.Abstractions;

namespace RRC.Student;

public sealed class DirovskijAdaptiveRequestPolicy : IRequestPolicy
{
    private const double TargetQuantile = 0.90;
    private const double SafetyFactor = 1.1;
    private const double MinTimeoutMs = 5.0;
    private const double MaxTimeoutMs = 30_000.0;
    private const double InitialTimeoutMs = 500.0;
    private const int WindowSize = 256;
    private const int MinSamplesForAdaptation = 20;
    private const int MaxAttempts = 3;

    private readonly Queue<double> recentLatenciesMs = new(WindowSize + 1);
    private readonly double[] scratch = new double[WindowSize];
    private double timeoutMs = InitialTimeoutMs;

    public TimeSpan GetTimeout(RequestContext context, Random random) =>
        TimeSpan.FromMilliseconds(timeoutMs);

    public RetryDecision OnResult(RequestObservation observation, Random random)
    {
        double elapsedMs = observation.Elapsed.TotalMilliseconds;

        if (observation.Outcome == RequestOutcome.Timeout)
        {
            AddSample(Math.Max(elapsedMs, timeoutMs));
            timeoutMs = Math.Min(MaxTimeoutMs, timeoutMs * 2.0);
            return observation.Attempt < MaxAttempts
                ? RetryDecision.RetryAfter(TimeSpan.Zero)
                : RetryDecision.NoRetry();
        }

        AddSample(elapsedMs);
        if (recentLatenciesMs.Count >= MinSamplesForAdaptation)
            timeoutMs = Math.Clamp(Percentile(TargetQuantile) * SafetyFactor, MinTimeoutMs, MaxTimeoutMs);

        return RetryDecision.NoRetry();
    }

    private void AddSample(double value)
    {
        if (recentLatenciesMs.Count == WindowSize)
            recentLatenciesMs.Dequeue();
        recentLatenciesMs.Enqueue(value);
    }

    private double Percentile(double quantile)
    {
        int count = recentLatenciesMs.Count;
        int index = 0;
        foreach (double sample in recentLatenciesMs)
            scratch[index++] = sample;

        Array.Sort(scratch, 0, count);
        int position = (int)Math.Ceiling(count * quantile) - 1;
        if (position < 0) position = 0;
        if (position >= count) position = count - 1;
        return scratch[position];
    }
}
