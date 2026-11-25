using Intouch_67173986_L003.Models;

namespace Intouch_67173986_L003.Data;

/// <summary>Booking filters used by several pages, so they always mean the same thing.</summary>
public static class BookingQueries
{
    /// <summary>
    /// Bookings that keep the room occupied on at least one night of [from, to): booked or
    /// checked in (not cancelled, not already checked out) with overlapping dates.
    /// </summary>
    public static IQueryable<HotelBooking> Overlapping(this IQueryable<HotelBooking> bookings, DateTime from, DateTime to)
    {
        return bookings.Where(b =>
            (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn) &&
            b.CheckIn < to &&
            b.CheckOut > from);
    }

    /// <summary>Bookings that are still running or still to come (they block deleting a user).</summary>
    public static IQueryable<HotelBooking> StillActive(this IQueryable<HotelBooking> bookings, DateTime today)
    {
        return bookings.Where(b =>
            b.Status == BookingStatus.CheckedIn ||
            (b.Status == BookingStatus.Confirmed && b.CheckOut > today));
    }

    /// <summary>Guests expected today, including late arrivals whose stay has not ended yet.</summary>
    public static IQueryable<HotelBooking> ArrivalsDue(this IQueryable<HotelBooking> bookings, DateTime today)
    {
        return bookings.Where(b => b.Status == BookingStatus.Confirmed && b.CheckIn <= today && b.CheckOut > today);
    }

    /// <summary>Guests in the hotel whose check-out day has come (today or already passed).</summary>
    public static IQueryable<HotelBooking> DeparturesDue(this IQueryable<HotelBooking> bookings, DateTime today)
    {
        return bookings.Where(b => b.Status == BookingStatus.CheckedIn && b.CheckOut <= today);
    }

    /// <summary>The stay is over but the guest never checked in (no-show): staff should close it.</summary>
    public static IQueryable<HotelBooking> NoShows(this IQueryable<HotelBooking> bookings, DateTime today)
    {
        return bookings.Where(b => b.Status == BookingStatus.Confirmed && b.CheckOut <= today);
    }

    /// <summary>
    /// Booked online, but no payment method was chosen within BookingOptions.PaymentChoiceMinutes
    /// (same rule as <see cref="HotelBooking.IsPaymentChoiceOverdue"/>): these get cancelled.
    /// </summary>
    public static IQueryable<HotelBooking> PaymentChoiceOverdue(this IQueryable<HotelBooking> bookings, DateTime now)
    {
        var bookedBefore = now.AddMinutes(-BookingOptions.PaymentChoiceMinutes);
        return bookings.Where(b =>
            b.Status == BookingStatus.Confirmed &&
            b.PaymentStatus == PaymentStatus.Unpaid &&
            b.PaymentMethod == "" &&
            b.CreatedAt <= bookedBefore);
    }
}
