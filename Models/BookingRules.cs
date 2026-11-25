using System.Globalization;

namespace Intouch_67173986_L003.Models;

/// <summary>Booking rules shared by the customer pages and the front-desk pages.</summary>
public static class BookingRules
{
    /// <summary>
    /// A new, unpaid booking with the price breakdown saved exactly as quoted. Used by the
    /// customer checkout and by the front desk's walk-in booking.
    /// </summary>
    /// <param name="userId">The member who books, or null for a walk-in guest without an account.</param>
    /// <param name="specialRequest">Already cleaned with <see cref="CleanSpecialRequest"/>.</param>
    public static HotelBooking NewBooking(HotelRoom room, PriceRequest request, PriceQuote quote, string bedType,
        string guestName, string contactEmail, int? userId, string specialRequest = "")
    {
        return new HotelBooking
        {
            UserId = userId,
            GuestName = guestName,
            ContactEmail = contactEmail,
            RoomId = room.Id,
            RoomName = room.Name,
            CheckIn = request.CheckIn.Date,
            CheckOut = request.CheckOut.Date,
            Guests = request.Guests,
            Nights = quote.Nights,
            BedType = bedType,
            IncludeBreakfast = request.Breakfast,
            ExtraBed = request.ExtraBed,
            SpaSessions = room.HasSpa ? request.SpaSessions : 0,
            IncludeSpa = room.HasSpa && request.SpaSessions > 0,
            SpecialRequest = specialRequest,
            PricePerNight = quote.PricePerNight,
            RoomTotal = quote.RoomTotal,
            ServiceTotal = quote.ServiceTotal,
            Discount = quote.DiscountTotal,
            DiscountDetails = string.Join("\n", quote.Discounts.Select(d =>
                $"{d.Name}: {d.Amount.ToString("0.##", CultureInfo.InvariantCulture)}")),
            Tax = quote.Tax,
            Total = quote.Total,
            PaymentMethod = string.Empty,
            PaymentStatus = PaymentStatus.Unpaid,
            Status = BookingStatus.Confirmed,
            CreatedAt = DateTime.Now
        };
    }

    /// <summary>The special request as it is saved (see <see cref="TextInput.CleanMultiline"/>).</summary>
    public static string CleanSpecialRequest(string? text) => TextInput.CleanMultiline(text);

    public static string SpecialRequestTooLongMessage =>
        $"คำขอพิเศษต้องไม่เกิน {BookingOptions.MaxSpecialRequestLength} ตัวอักษร";

    /// <summary>
    /// Cancels the booking. Money that was paid (or a transfer still waiting to be checked) is
    /// handed to finance for a refund. Returns true when a refund is needed.
    /// A no-show (<see cref="CancelReasons.NoShow"/>) is never refunded: a guest may cancel until the
    /// check-in day, so one who neither came nor cancelled keeps nothing back. The payment is left as
    /// it is (paid stays paid; a transfer waiting to be checked is still checked as usual).
    /// </summary>
    /// <param name="reason">One of <see cref="CancelReasons"/>.</param>
    public static bool Cancel(HotelBooking booking, string reason)
    {
        booking.Status = BookingStatus.Cancelled;
        booking.CancelReason = reason;

        if (reason == CancelReasons.NoShow)
        {
            return false;
        }

        if (booking.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Pending)
        {
            booking.PaymentStatus = PaymentStatus.RefundPending;
            return true;
        }

        return false;
    }
}
