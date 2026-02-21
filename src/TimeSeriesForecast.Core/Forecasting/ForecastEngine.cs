using System.Text.Json;

namespace TimeSeriesForecast.Core.Forecasting;

public static class ForecastEngine
{
    public sealed record ForecastPoint(DateTimeOffset Timestamp, double Value);

    public static IReadOnlyList<ForecastPoint> ExponentialSmoothing(
        IReadOnlyList<ForecastPoint> history,
        int horizon,
        double alpha = 0.35)
    {
        if (history.Count == 0) throw new ArgumentException("history is empty");
        alpha = Math.Clamp(alpha, 0.001, 0.999);

        double level = history[0].Value;
        for (int i = 1; i < history.Count; i++)
        {
            level = alpha * history[i].Value + (1.0 - alpha) * level;
        }

        var step = InferStep(history);
        var start = history[^1].Timestamp;
        var result = new List<ForecastPoint>(capacity: horizon);
        for (int h = 1; h <= horizon; h++)
        {
            result.Add(new ForecastPoint(start.Add(step * h), level));
        }
        return result;
    }

    public static IReadOnlyList<ForecastPoint> SeasonalNaive(
        IReadOnlyList<ForecastPoint> history,
        int horizon,
        int seasonLength = 7)
    {
        if (history.Count == 0) throw new ArgumentException("history is empty");
        seasonLength = Math.Max(1, seasonLength);

        var step = InferStep(history);
        var start = history[^1].Timestamp;
        var result = new List<ForecastPoint>(capacity: horizon);

        for (int h = 1; h <= horizon; h++)
        {
            int idx = history.Count - seasonLength + ((h - 1) % seasonLength);
            idx = Math.Clamp(idx, 0, history.Count - 1);
            result.Add(new ForecastPoint(start.Add(step * h), history[idx].Value));
        }
        return result;
    }

    public static (double mae, double rmse) Evaluate(IReadOnlyList<double> actual, IReadOnlyList<double> predicted)
    {
        if (actual.Count != predicted.Count) throw new ArgumentException("length mismatch");
        if (actual.Count == 0) return (0, 0);

        double sumAbs = 0;
        double sumSq = 0;
        for (int i = 0; i < actual.Count; i++)
        {
            var err = actual[i] - predicted[i];
            sumAbs += Math.Abs(err);
            sumSq += err * err;
        }
        return (sumAbs / actual.Count, Math.Sqrt(sumSq / actual.Count));
    }

    public static TimeSpan InferStep(IReadOnlyList<ForecastPoint> history)
    {
        if (history.Count < 2) return TimeSpan.FromDays(1);
        // median step (robust for minor gaps)
        var diffs = new List<long>();
        for (int i = 1; i < history.Count; i++)
        {
            diffs.Add((history[i].Timestamp - history[i - 1].Timestamp).Ticks);
        }
        diffs.Sort();
        long medianTicks = diffs[diffs.Count / 2];
        if (medianTicks <= 0) medianTicks = TimeSpan.FromDays(1).Ticks;
        return TimeSpan.FromTicks(medianTicks);
    }

    public static string ToJson(IReadOnlyList<ForecastPoint> points)
        => JsonSerializer.Serialize(points);
}
