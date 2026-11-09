using System.Globalization;
using System.Text.RegularExpressions;

namespace BMThuDiem;

public static class Gram
{
    public static bool IsKingbird(string raw) =>
        Regex.IsMatch(raw.Trim(), @"^\.?\d{2}\s+\d{6}");

    /// <summary>075000= là 570 g. Sáu số viết ngược, kết thúc bằng dấu =.</summary>
    public static bool TryGrams(string raw, out int grams)
    {
        grams = 0;
        var m = Regex.Match(raw.Trim(), @"^(\d{6})\s*=");
        if (!m.Success) return false;
        var rev = new string(m.Groups[1].Value.Reverse().ToArray());
        return int.TryParse(rev, NumberStyles.None, CultureInfo.InvariantCulture, out grams) && grams >= 0;
    }
}
