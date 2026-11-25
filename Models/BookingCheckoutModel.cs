using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// Booking form (step 1 of 2: dates, guests and extra services). Properties marked [BindNever]
/// are always filled in by the server from the database, so a tampered form can never change
/// the room, the price or the total.
/// </summary>
public class BookingCheckoutModel
{
    public int RoomId { get; set; }

    [Required(ErrorMessage = "กรุณาเลือกวันเช็คอิน")]
    public string CheckIn { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณาเลือกวันเช็คเอาท์")]
    public string CheckOut { get; set; } = string.Empty;

    [Range(1, BookingOptions.MaxGuests, ErrorMessage = "จำนวนผู้เข้าพักต้องอยู่ระหว่าง {1}–{2} คน")]
    public int Guests { get; set; } = RoomSearchModel.DefaultGuests;

    [Required(ErrorMessage = "กรุณากรอกชื่อผู้เข้าพัก")]
    [StringLength(100, ErrorMessage = "ชื่อผู้เข้าพักต้องไม่เกิน {1} ตัวอักษร")]
    public string GuestName { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    [StringLength(200, ErrorMessage = "อีเมลต้องไม่เกิน {1} ตัวอักษร")]
    public string ContactEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณาเลือกประเภทเตียง")]
    public string BedType { get; set; } = BookingOptions.DoubleBed;

    public bool Breakfast { get; set; }
    public bool ExtraBed { get; set; }

    [Range(0, BookingOptions.MaxSpaSessions, ErrorMessage = "จำนวนครั้งสปาต้องอยู่ระหว่าง {1}–{2} ครั้ง")]
    public int SpaSessions { get; set; }

    /// <summary>Optional note for the hotel. Cleaned and length-checked by the controller (BookingRules.CleanSpecialRequest).</summary>
    public string? SpecialRequest { get; set; }

    // ----- Filled in by the server only -----
    [BindNever] public HotelRoom Room { get; set; } = new();
    [BindNever] public PriceQuote Quote { get; set; } = new();
}
