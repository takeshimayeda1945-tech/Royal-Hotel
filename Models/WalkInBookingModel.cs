using System.ComponentModel.DataAnnotations;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// Front-desk booking for a guest who walks in (or phones) without booking online. The guest does
/// not need an account; if the e-mail belongs to a member, the booking is linked to that member
/// and member promotions apply. Room price and totals always come from the server.
/// </summary>
public class WalkInBookingModel
{
    [Range(1, int.MaxValue, ErrorMessage = "กรุณาเลือกห้องพัก")]
    public int RoomId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกวันเช็คอิน")]
    public string CheckIn { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณาเลือกวันเช็คเอาท์")]
    public string CheckOut { get; set; } = string.Empty;

    [Range(1, BookingOptions.MaxGuests, ErrorMessage = "จำนวนผู้เข้าพักต้องอยู่ระหว่าง {1}–{2} คน")]
    public int Guests { get; set; } = 1;

    [Required(ErrorMessage = "กรุณากรอกชื่อผู้เข้าพัก")]
    [StringLength(100, ErrorMessage = "ชื่อผู้เข้าพักต้องไม่เกิน {1} ตัวอักษร")]
    public string GuestName { get; set; } = string.Empty;

    /// <summary>Optional. When it matches a member account, the booking is linked to that member.</summary>
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    [StringLength(200, ErrorMessage = "อีเมลต้องไม่เกิน {1} ตัวอักษร")]
    public string? ContactEmail { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกประเภทเตียง")]
    public string BedType { get; set; } = BookingOptions.DoubleBed;

    public bool Breakfast { get; set; }
    public bool ExtraBed { get; set; }

    [Range(0, BookingOptions.MaxSpaSessions, ErrorMessage = "จำนวนครั้งสปาต้องอยู่ระหว่าง {1}–{2} ครั้ง")]
    public int SpaSessions { get; set; }

    /// <summary>Optional note from the guest. Cleaned and length-checked by the controller (BookingRules.CleanSpecialRequest).</summary>
    public string? SpecialRequest { get; set; }

    // ----- payment at the desk -----
    public string PaymentMethod { get; set; } = BookingOptions.Cash;

    /// <summary>The money was received now (cash counted, card approved, transfer seen).</summary>
    public bool PaidNow { get; set; } = true;

    /// <summary>Optional slip / approval / transfer number. Never a full card number.</summary>
    [StringLength(50, ErrorMessage = "เลขอ้างอิงต้องไม่เกิน {1} ตัวอักษร")]
    public string? PaymentReference { get; set; }

    /// <summary>Check the guest in straight away (only for a paid stay that starts today).</summary>
    public bool CheckInNow { get; set; } = true;
}

/// <summary>Live price box of the walk-in form (Staff/WalkInQuote).</summary>
public class WalkInQuoteModel
{
    public PriceQuote Quote { get; set; } = new();
    public HotelRoom? Room { get; set; }
    public bool DatesValid { get; set; }
    public bool RoomTaken { get; set; }
    /// <summary>The stay starts today and housekeeping has not finished this room yet (open cleaning job).</summary>
    public bool RoomBeingCleaned { get; set; }
    /// <summary>The member found by the e-mail, or null for a guest without an account.</summary>
    public string? MemberName { get; set; }
}
