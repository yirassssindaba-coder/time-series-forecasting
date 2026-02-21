using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using TimeSeriesForecast.Core.Forecasting;

namespace TimeSeriesForecast.Core.Common;

public static class CsvTimeSeriesReader
{
    public static async Task<IReadOnlyList<ForecastEngine.ForecastPoint>> ReadAsync(Stream csvStream)
    {
        using var reader = new StreamReader(csvStream, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var csv = new CsvReader(reader, config);
        var list = new List<ForecastEngine.ForecastPoint>();
        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            var dateStr = csv.GetField("date") ?? csv.GetField("timestamp");
            var yStr = csv.GetField("y") ?? csv.GetField("value");

            if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(yStr))
                continue;

            if (!DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ts))
                continue;

            if (!double.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;

            list.Add(new ForecastEngine.ForecastPoint(ts, y));
        }

        return list.OrderBy(p => p.Timestamp).ToList();
    }
}
