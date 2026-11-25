using System.Globalization;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// Converts between DateTime and the "yyyy-MM-dd" value used by &lt;input type="date"&gt;.
/// Always uses the invariant (Gregorian) calendar, so a machine whose regional format is Thai
/// (Buddhist calendar) can never turn 2026 into 2569 or 1483.
/// </summary>
public static class DateInput
{
    public const string Format = "yyyy-MM-dd";

    public static string ToValue(DateTime date)
    {
        return date.ToString(Format, CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? value, out DateTime date)
    {
        return DateTime.TryParseExact(value?.Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}

/// <summary>Free text typed by users (special requests, review comments).</summary>
public static class TextInput
{
    /// <summary>
    /// Several lines of text as they are saved: line breaks kept (never more than one empty line in a
    /// row), tabs and other control characters removed, spaces trimmed. Empty when nothing was written.
    /// Check the length after this (Windows line breaks then count once, as in the browser's counter).
    /// </summary>
    public static string CleanMultiline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(line => new string(line.Select(c => c == '\t' ? ' ' : c).Where(c => !char.IsControl(c)).ToArray()).Trim());
        return System.Text.RegularExpressions.Regex.Replace(string.Join("\n", lines), "\n{3,}", "\n\n").Trim();
    }
}

/// <summary>Consistent display formats for money, dates and percentages.</summary>
public static class DisplayText
{
    /// <summary>฿3,200 for whole baht, otherwise always two decimals: ฿5,392.80 (not ฿5,392.8).</summary>
    public static string Money(decimal amount)
    {
        var format = amount == decimal.Truncate(amount) ? "#,0" : "#,0.00";
        return "฿" + amount.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string Date(DateTime date)
    {
        return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Rounded to whole baht (฿3,177,847) for the big summary tiles; tables keep the exact amount.</summary>
    public static string MoneyWhole(decimal amount)
    {
        return "฿" + Math.Round(amount, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture);
    }

    /// <summary>Date and time, e.g. 24/11/2026 14:05.</summary>
    public static string Timestamp(DateTime value)
    {
        return value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Time of day only, e.g. 14:05 (for deadlines a few minutes away).</summary>
    public static string Clock(DateTime value)
    {
        return value.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public static string Percent(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }
}

/// <summary>
/// An amount in Thai words as written on receipts, e.g. 16,852.50 →
/// "หนึ่งหมื่นหกพันแปดร้อยห้าสิบสองบาทห้าสิบสตางค์", 1,200 → "หนึ่งพันสองร้อยบาทถ้วน".
/// </summary>
public static class ThaiBahtText
{
    private static readonly string[] Digits = { "ศูนย์", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า" };
    private static readonly string[] Places = { "", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน" };

    public static string Convert(decimal amount)
    {
        amount = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var baht = decimal.Truncate(amount);
        var satang = (int)((amount - baht) * 100);

        if (baht == 0 && satang == 0)
        {
            return "ศูนย์บาทถ้วน";
        }

        var text = baht > 0 ? Number(baht) + "บาท" : string.Empty;
        return text + (satang > 0 ? Number(satang) + "สตางค์" : "ถ้วน");
    }

    /// <summary>A whole number in words; every six digits are a group of "ล้าน" (million).</summary>
    private static string Number(decimal value)
    {
        if (value < 1_000_000)
        {
            return BelowMillion((int)value, hasHigherGroup: false);
        }

        var millions = decimal.Truncate(value / 1_000_000);
        var rest = (int)(value - millions * 1_000_000);
        return Number(millions) + "ล้าน" + (rest > 0 ? BelowMillion(rest, hasHigherGroup: true) : string.Empty);
    }

    /// <summary>
    /// 0–999,999 in words. Tens: 1 → "สิบ" (not "หนึ่งสิบ"), 2 → "ยี่สิบ". Units: 1 after any higher digit
    /// is "เอ็ด" (11 สิบเอ็ด, 101 หนึ่งร้อยเอ็ด, 1,000,001 หนึ่งล้านเอ็ด); a plain 1 is "หนึ่ง".
    /// </summary>
    private static string BelowMillion(int value, bool hasHigherGroup)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[i] - '0';
            var place = digits.Length - 1 - i;
            if (digit == 0)
            {
                continue;
            }

            if (place == 1 && digit == 1)
            {
                text.Append("สิบ");
            }
            else if (place == 1 && digit == 2)
            {
                text.Append("ยี่สิบ");
            }
            else if (place == 0 && digit == 1 && (value > 9 || hasHigherGroup))
            {
                text.Append("เอ็ด");
            }
            else
            {
                text.Append(Digits[digit]).Append(Places[place]);
            }
        }

        return text.ToString();
    }
}
