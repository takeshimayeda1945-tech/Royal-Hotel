using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intouch_67173986_L003.Models;

/// <summary>Payment form (step 2 of 2). Only the method and its details are posted.</summary>
public class PaymentModel
{
    public int BookingId { get; set; }
    public string PaymentMethod { get; set; } = BookingOptions.QrCode;

    // QR Code transfer
    public string? TransferReference { get; set; }

    // Debit / credit card (validated, never stored except the last 4 digits)
    public string? CardName { get; set; }
    public string? CardNumber { get; set; }
    public string? CardExpiry { get; set; }
    public string? CardCvv { get; set; }

    [BindNever] public HotelBooking Booking { get; set; } = new();
}
