using System.Security.Cryptography;
using System.Text;

namespace PKeetDashboard.API.Services;

public static class RazorpaySignature
{
    public static bool VerifyPayment(string orderId, string paymentId, string signature, string keySecret)
    {
        if (string.IsNullOrWhiteSpace(orderId) ||
            string.IsNullOrWhiteSpace(paymentId) ||
            string.IsNullOrWhiteSpace(signature) ||
            string.IsNullOrWhiteSpace(keySecret))
        {
            return false;
        }

        var payload = $"{orderId}|{paymentId}";
        var expected = ComputeHmacHex(payload, keySecret);
        return FixedTimeEqualsHex(expected, signature.Trim());
    }

    private static string ComputeHmacHex(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEqualsHex(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a.ToLowerInvariant());
        var bb = Encoding.UTF8.GetBytes(b.ToLowerInvariant());
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
