using System.Text;

namespace TraceCapsule.Core.Recording;

/// <summary>Shared helper for turning a body stream/byte array into the text TraceCapsule
/// stores, used by both the ASP.NET Core middleware and the outbound HTTP recorder so the
/// truncation and binary-detection rules are consistent everywhere.</summary>
public static class BodyCapture
{
    public static string? FromBytes(byte[] bytes, int maxBytes)
    {
        if (bytes.Length == 0) return null;
        if (LooksBinary(bytes))
        {
            return $"<binary, {bytes.Length} bytes>";
        }
        var take = Math.Min(bytes.Length, maxBytes);
        var text = Encoding.UTF8.GetString(bytes, 0, take);
        return take < bytes.Length ? text + "...(truncated)" : text;
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var sampleLength = Math.Min(bytes.Length, 512);
        var suspicious = 0;
        for (var i = 0; i < sampleLength; i++)
        {
            var b = bytes[i];
            if (b == 0) return true;
            if (b < 0x09 || (b > 0x0D && b < 0x20)) suspicious++;
        }
        return suspicious > sampleLength / 10;
    }
}
