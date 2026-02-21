namespace TimeSeriesForecast.Api.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "TimeSeriesForecast";
    public string Audience { get; set; } = "TimeSeriesForecast";
    public string Key { get; set; } = "CHANGE_ME";
}
