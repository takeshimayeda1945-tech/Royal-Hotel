using System.Globalization;

namespace Intouch_67173986_L003.Models;

/// <summary>One priced line of a receipt (room nights, breakfast, extra bed, spa).</summary>
public record ReceiptLine(string Description, int Quantity, string Unit, decimal UnitPrice, decimal Amount);

/// <summary>
/// Receipt (ใบเสร็จรับเงิน) of a paid booking, built from the price breakdown saved with the booking,
/// so it always shows what the guest was charged even if room prices or rules change later.
/// Nothing is stored: the receipt number comes from the payment date and the booking number.
/// </summary>
public static class BookingReceipt
{
    /// <summary>
    /// The hotel holds the money: paid (also a closed no-show, which keeps its payment). A cancelled
    /// booking that was refunded, or whose transfer was never confirmed, has no receipt.
    /// </summary>
    public static bool IsAvailable(HotelBooking booking) => booking.PaymentStatus == PaymentStatus.Paid;

    /// <summary>When the money was received (bookings paid before PaidAt was kept use the booking time).</summary>
    public static DateTime IssuedAt(HotelBooking booking) => booking.PaidAt ?? booking.CreatedAt;

    /// <summary>e.g. RC-20261124-000123: payment date and booking number (unique, one receipt per booking).</summary>
    public static string NumberOf(HotelBooking booking) =>
        string.Create(CultureInfo.InvariantCulture, $"RC-{IssuedAt(booking):yyyyMMdd}-{booking.Id:D6}");

    /// <summary>
    /// The room line and one line per extra service. If the saved service total does not match the
    /// service prices (rules changed after booking), the services are shown as one line instead.
    /// </summary>
    public static List<ReceiptLine> Lines(HotelBooking booking)
    {
        var nights = Math.Max(booking.Nights, 1);
        if (booking.RoomTotal <= 0)
        {
            // Saved before the price breakdown existed: the stay as one line (total before tax).
            var amount = booking.Total - booking.Tax;
            var perNight = booking.PricePerNight > 0 ? booking.PricePerNight : Math.Round(amount / nights, 2);
            return new() { new($"ค่าห้องพัก {booking.RoomName}", nights, "คืน", perNight, amount) };
        }

        var lines = new List<ReceiptLine>
        {
            new($"ค่าห้องพัก {booking.RoomName}", nights, "คืน", booking.PricePerNight, booking.RoomTotal)
        };

        var services = new List<ReceiptLine>();
        if (booking.IncludeBreakfast)
        {
            var portions = booking.Guests * nights;
            services.Add(new($"อาหารเช้า ({booking.Guests} คน × {nights} คืน)", portions, "ที่",
                BookingOptions.BreakfastPerPersonPerNight, BookingOptions.BreakfastPerPersonPerNight * portions));
        }

        if (booking.ExtraBed)
        {
            services.Add(new("เตียงเสริม", nights, "คืน", BookingOptions.ExtraBedPerNight, BookingOptions.ExtraBedPerNight * nights));
        }

        if (booking.SpaSessions > 0)
        {
            services.Add(new("สปา", booking.SpaSessions, "ครั้ง", BookingOptions.SpaPerSession, BookingOptions.SpaPerSession * booking.SpaSessions));
        }

        if (services.Sum(s => s.Amount) == booking.ServiceTotal)
        {
            lines.AddRange(services);
        }
        else if (booking.ServiceTotal > 0)
        {
            lines.Add(new($"ค่าบริการเสริม ({booking.ServicesText})", 1, "รายการ", booking.ServiceTotal, booking.ServiceTotal));
        }

        return lines;
    }
}

public class ReceiptViewModel
{
    public required HotelBooking Booking { get; init; }

    /// <summary>Where the "back" button goes (the booking page for a guest, the work list for staff).</summary>
    public required string BackUrl { get; init; }
    public required string BackLabel { get; init; }

    /// <summary>True when a staff member opened it (shows who the booking belongs to).</summary>
    public bool ForStaff { get; init; }

    private List<ReceiptLine>? _lines;

    public string Number => BookingReceipt.NumberOf(Booking);
    public DateTime IssuedAt => BookingReceipt.IssuedAt(Booking);
    public List<ReceiptLine> Lines => _lines ??= BookingReceipt.Lines(Booking);
    public decimal Subtotal => Lines.Sum(l => l.Amount);
    /// <summary>Total before VAT (after discounts).</summary>
    public decimal BeforeTax => Booking.Total - Booking.Tax;
    public string AmountInWords => ThaiBahtText.Convert(Booking.Total);
}
