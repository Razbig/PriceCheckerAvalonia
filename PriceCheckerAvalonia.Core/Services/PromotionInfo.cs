using System.Globalization;

namespace PriceCheckerAvalonia.Core.Services;

public class PromotionInfo
{
    public double Day { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool IsActive { get; set; }

    public static PromotionInfo Parse(string? memo)
    {
        var result = new PromotionInfo();

        if (string.IsNullOrWhiteSpace(memo))
            return result;

        if (memo.Contains("Card=", StringComparison.OrdinalIgnoreCase))
            return result;

        // $Day=
        var dayStart = memo.IndexOf("$Day=", StringComparison.Ordinal);

        if (dayStart >= 0)
        {
            dayStart += 5;

            var dayEnd = memo.IndexOf(';', dayStart);

            if (dayEnd > dayStart &&
                double.TryParse(
                    memo[dayStart..dayEnd],
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var day))
            {
                result.Day = day;
            }
        }

        // $DayPeriod=
        var periodStart = memo.IndexOf("$DayPeriod=", StringComparison.Ordinal);

        if (periodStart >= 0)
        {
            periodStart += "$DayPeriod=".Length;

            var periodEnd = memo.IndexOf(';', periodStart);

            if (periodEnd > periodStart)
            {
                var period = memo[periodStart..periodEnd];
                var parts = period.Split(',');

                if (parts.Length >= 2)
                {
                    if (DateTime.TryParseExact(
                        parts[0],
                        "dd-MM-yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dateFrom))
                    {
                        result.DateFrom = dateFrom;
                    }

                    if (DateTime.TryParseExact(
                        parts[1],
                        "dd-MM-yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dateTo))
                    {
                        result.DateTo = dateTo;
                    }
                }
            }
        }

        result.IsActive =
            result.DateFrom.HasValue &&
            result.DateTo.HasValue &&
            DateTime.Now >= result.DateFrom.Value &&
            DateTime.Now <= result.DateTo.Value.AddDays(1) &&
            result.DateTo.Value < new DateTime(2030, 1, 1);

        return result;
    }
}