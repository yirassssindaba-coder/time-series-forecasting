using System.Security.Cryptography;
using System.Text;

namespace TimeSeriesForecast.Api.Security;

public interface IFileSigner
{
    string Sign(Guid fileId, DateTimeOffset expiresAt);
    bool Verify(Guid fileId, DateTimeOffset expiresAt, string signature);
}

public sealed class HmacFileSigner : IFileSigner
{
    private readonly byte[] _key;

    public HmacFileSigner(JwtOptions opts)
    {
        _key = Encoding.UTF8.GetBytes(opts.Key);
    }

    public string Sign(Guid fileId, DateTimeOffset expiresAt)
    {
        var payload = $"{fileId}:{expiresAt.ToUnixTimeSeconds()}";
        var sig = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(sig).ToLowerInvariant();
    }

    public bool Verify(Guid fileId, DateTimeOffset expiresAt, string signature)
    {
        var expected = Sign(fileId, expiresAt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
    }
}
