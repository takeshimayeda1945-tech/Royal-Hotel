using System.ComponentModel.DataAnnotations.Schema;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// A reservation. The price breakdown is stored as it was quoted when the booking was made, so
/// later changes to room prices or promotion rules never change what the guest has to pay.
/// </summary>
public class HotelBooking
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public int RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public DateTime CheckIn { get; set; }
    public DateTime CheckOut { get; set; }
    public int Guests { get; set; } = 2;
    public int Nights { get; set; }
    public string BedType { get; set; } = BookingOptions.DoubleBed;

    // ----- extra services -----
    public bool IncludeBreakfast { get; set; }
    public bool ExtraBed { get; set; }
    /// <summary>True when at least one spa session was booked (kept for older rows).</summary>
    public bool IncludeSpa { get; set; }
    public int SpaSessions { get; set; }

    /// <summary>
    /// The guest's note for the hotel (high floor, late arrival, allergies …); empty when none.
    /// Written when booking (online or at the front desk) and shown to the front desk.
    /// </summary>
    public string SpecialRequest { get; set; } = string.Empty;

    // ----- price breakdown (snapshot) -----
    public decimal PricePerNight { get; set; }
    public decimal RoomTotal { get; set; }
    public decimal ServiceTotal { get; set; }
    public decimal Discount { get; set; }
    /// <summary>Promotions that were applied, one per line.</summary>
    public string DiscountDetails { get; set; } = string.Empty;
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    // ----- payment -----
    /// <summary>Empty until the guest chooses a method on the payment page.</summary>
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = Models.PaymentStatus.Unpaid;
    /// <summary>Transfer reference, or the masked card number. Full card numbers are never stored.</summary>
    public string PaymentReference { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }

    // ----- stay -----
    public string Status { get; set; } = BookingStatus.Confirmed;
    /// <summary>Why the booking was cancelled (<see cref="CancelReasons"/>); empty otherwise.</summary>
    public string CancelReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CheckedOutAt { get; set; }

    /// <summary>The guest can cancel a booking until (and including) its check-in day, before checking in.</summary>
    [NotMapped]
    public bool CanCancel => Status == BookingStatus.Confirmed && CheckIn.Date >= DateTime.Today;

    /// <summary>
    /// Booked online but no payment method chosen yet. Such a booking keeps the room only until
    /// <see cref="PaymentChoiceDeadline"/>; then it is cancelled.
    /// </summary>
    [NotMapped]
    public bool AwaitsPaymentChoice =>
        Status == BookingStatus.Confirmed &&
        PaymentStatus == Models.PaymentStatus.Unpaid &&
        PaymentMethod.Length == 0;

    [NotMapped]
    public DateTime PaymentChoiceDeadline => CreatedAt.AddMinutes(BookingOptions.PaymentChoiceMinutes);

    /// <summary>Still waiting for a payment method although the deadline has passed (about to be cancelled).</summary>
    public bool IsPaymentChoiceOverdue(DateTime now) => AwaitsPaymentChoice && now >= PaymentChoiceDeadline;

    /// <summary>
    /// The guest still has to pick a payment method (or switch from cash to paying online).
    /// Not possible any more once the stay is over, or once the time to choose a method has run out.
    /// </summary>
    [NotMapped]
    public bool CanPayOnline =>
        Status == BookingStatus.Confirmed &&
        PaymentStatus == Models.PaymentStatus.Unpaid &&
        CheckOut.Date > DateTime.Today &&
        !IsPaymentChoiceOverdue(DateTime.Now);

    /// <summary>The guest is expected today (or is late: the check-in day has passed but the stay has not ended).</summary>
    [NotMapped]
    public bool IsArrivalDue =>
        Status == BookingStatus.Confirmed &&
        CheckIn.Date <= DateTime.Today &&
        CheckOut.Date > DateTime.Today;

    /// <summary>The whole stay is over but the guest never checked in (no-show).</summary>
    [NotMapped]
    public bool IsNoShow => Status == BookingStatus.Confirmed && CheckOut.Date <= DateTime.Today;

    /// <summary>A no-show that the front desk has closed (cancelled without a refund).</summary>
    [NotMapped]
    public bool IsClosedNoShow => Status == BookingStatus.Cancelled && CancelReason == CancelReasons.NoShow;

    /// <summary>
    /// A transfer that staff or finance can still confirm or reject: the booking is going ahead, or it
    /// is a closed no-show (its money is kept, so the transfer is still checked).
    /// </summary>
    [NotMapped]
    public bool CanCheckTransfer => PaymentStatus == Models.PaymentStatus.Pending && (Status != BookingStatus.Cancelled || IsClosedNoShow);

    [NotMapped]
    public bool CanCheckIn =>
        Status == BookingStatus.Confirmed &&
        PaymentStatus == Models.PaymentStatus.Paid &&
        CheckIn.Date <= DateTime.Today &&
        CheckOut.Date > DateTime.Today;

    [NotMapped]
    public bool CanCheckOut => Status == BookingStatus.CheckedIn;

    /// <summary>The breakdown for display; the services line is split back from the stored flags.</summary>
    [NotMapped]
    public IEnumerable<string> DiscountLines =>
        DiscountDetails.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The applied promotions as name and amount (saved as "name: amount", one per line).</summary>
    [NotMapped]
    public IEnumerable<(string Name, decimal Amount)> DiscountItems =>
        DiscountLines.Select(line =>
        {
            var separator = line.LastIndexOf(':');
            if (separator <= 0)
            {
                return (line, 0m);
            }

            var amount = decimal.TryParse(line[(separator + 1)..], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0m;
            return (line[..separator], amount);
        });

    [NotMapped]
    public string ServicesText
    {
        get
        {
            var parts = new List<string> { BedType };
            if (IncludeBreakfast) parts.Add("อาหารเช้า");
            if (ExtraBed) parts.Add("เตียงเสริม");
            if (SpaSessions > 0) parts.Add($"สปา {SpaSessions} ครั้ง");
            else if (IncludeSpa) parts.Add("สปา");
            return string.Join(" · ", parts);
        }
    }
}
