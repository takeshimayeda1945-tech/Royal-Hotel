namespace Intouch_67173986_L003.Models;

/// <summary>What the guest picked on the booking form.</summary>
public record PriceRequest(
    DateTime CheckIn,
    DateTime CheckOut,
    int Guests,
    bool Breakfast,
    bool ExtraBed,
    int SpaSessions);

public record DiscountLine(string Name, decimal Amount);

public class PriceQuote
{
    public int Nights { get; init; }
    public decimal PricePerNight { get; init; }

    /// <summary>ค่าห้องพัก = ราคาห้อง × จำนวนคืน</summary>
    public decimal RoomTotal { get; init; }

    public decimal BreakfastFee { get; init; }
    public decimal ExtraBedFee { get; init; }
    public decimal SpaFee { get; init; }
    public decimal ServiceTotal => BreakfastFee + ExtraBedFee + SpaFee;

    /// <summary>Room + services, before discounts.</summary>
    public decimal Subtotal => RoomTotal + ServiceTotal;

    public List<DiscountLine> Discounts { get; init; } = new();
    public decimal DiscountTotal => Discounts.Sum(d => d.Amount);

    /// <summary>ราคาหลังหักส่วนลด (the VAT base).</summary>
    public decimal AfterDiscount => Subtotal - DiscountTotal;

    /// <summary>ภาษี 7% ของราคาหลังหักส่วนลด</summary>
    public decimal Tax { get; init; }

    public decimal Total => AfterDiscount + Tax;
}

/// <summary>
/// The one place where booking prices are calculated (checkout page, live quote and the saved booking).
///
///   คืนที่เข้าพัก   = วันออก − วันเข้า
///   ค่าห้องพัก     = ราคาห้อง × จำนวนคืน
///   ค่าบริการเสริม  = อาหารเช้า 300 × คน × คืน + เตียงเสริม 500 × คืน + สปา 1,500 × ครั้ง
///   ส่วนลด        = โปรโมชั่นตามเงื่อนไข (ใช้ร่วมกันได้ทุกข้อ)
///   ภาษี 7%       = (ค่าห้อง + ค่าบริการ − ส่วนลด) × 7%
///   ยอดสุทธิ       = ค่าห้อง + ค่าบริการ − ส่วนลด + ภาษี
/// </summary>
public static class PriceCalculator
{
    public const int EarlyBirdDays = 30;
    public const decimal EarlyBirdPercent = 10m;
    public const decimal BirthdayPercent = 10m;
    public const decimal WeekendDeluxePercent = 10m;
    public const decimal MemberMinimumSpend = 10000m;
    public const decimal MemberDiscount = 1000m;

    public const string EarlyBirdName = "จองล่วงหน้า 30 วัน ลด 10%";
    public const string BirthdayName = "เช็คอินในเดือนเกิด ลด 10%";
    public const string WeekendDeluxeName = "ห้อง Deluxe คืนวันศุกร์–เสาร์ ลด 10%";
    public const string MemberName = "สมาชิกจองครบ 10,000 บาท ลด 1,000 บาท";

    /// <summary>The promotion rules, for the Promotions page.</summary>
    public static readonly (string Title, string Badge, string Condition)[] Rules =
    {
        (EarlyBirdName, "−10%", $"เช็คอินห่างจากวันที่จองอย่างน้อย {EarlyBirdDays} วัน · ลด 10% จากค่าห้องพัก"),
        (BirthdayName, "−10%", "วันเช็คอินอยู่ในเดือนเกิดของสมาชิก (ตามวันเกิดที่ลงทะเบียนไว้) · ลด 10% จากค่าห้องพัก"),
        (WeekendDeluxeName, "−10%", "เฉพาะห้องประเภท Deluxe · ลด 10% ของราคาห้องเฉพาะคืนวันศุกร์และคืนวันเสาร์"),
        (MemberName, "−฿1,000", "สมาชิกที่มียอดค่าห้อง + ค่าบริการเสริม (ก่อนหักส่วนลดและภาษี) ตั้งแต่ 10,000 บาทขึ้นไป"),
    };

    /// <param name="room">The room being booked (price and room type come from the database).</param>
    /// <param name="request">Dates, guests and extra services from the form.</param>
    /// <param name="member">The signed-in member (every booking needs an account), or null for a visitor.</param>
    /// <param name="bookingDate">The day the booking is made (today).</param>
    public static PriceQuote Calculate(HotelRoom room, PriceRequest request, CyberUser? member, DateTime bookingDate)
    {
        var checkIn = request.CheckIn.Date;
        var checkOut = request.CheckOut.Date;
        var nights = Math.Max((checkOut - checkIn).Days, 0);
        var guests = Math.Max(request.Guests, 0);
        var spaSessions = room.HasSpa ? Math.Clamp(request.SpaSessions, 0, BookingOptions.MaxSpaSessions) : 0;

        var roomTotal = room.Price * nights;
        var breakfast = request.Breakfast ? BookingOptions.BreakfastPerPersonPerNight * guests * nights : 0m;
        var extraBed = request.ExtraBed ? BookingOptions.ExtraBedPerNight * nights : 0m;
        var spa = BookingOptions.SpaPerSession * spaSessions;
        var subtotal = roomTotal + breakfast + extraBed + spa;

        var discounts = new List<DiscountLine>();
        if (nights > 0)
        {
            // 1) Booked at least 30 days before check-in.
            if ((checkIn - bookingDate.Date).Days >= EarlyBirdDays)
            {
                discounts.Add(new DiscountLine(EarlyBirdName, Percent(roomTotal, EarlyBirdPercent)));
            }

            // 2) Check-in falls in the member's birthday month.
            if (member != null && member.Birthday.Month == checkIn.Month)
            {
                discounts.Add(new DiscountLine(BirthdayName, Percent(roomTotal, BirthdayPercent)));
            }

            // 3) Deluxe rooms: 10% off every Friday and Saturday night.
            if (room.IsDeluxe)
            {
                var weekendNights = Enumerable.Range(0, nights)
                    .Select(offset => checkIn.AddDays(offset).DayOfWeek)
                    .Count(day => day is DayOfWeek.Friday or DayOfWeek.Saturday);
                if (weekendNights > 0)
                {
                    discounts.Add(new DiscountLine(
                        $"{WeekendDeluxeName} ({weekendNights} คืน)",
                        Percent(room.Price * weekendNights, WeekendDeluxePercent)));
                }
            }

            // 4) Members spending 10,000 baht or more get 1,000 baht off.
            if (member != null && subtotal >= MemberMinimumSpend)
            {
                discounts.Add(new DiscountLine(MemberName, MemberDiscount));
            }
        }

        // The discounts can never make the price negative.
        var discountTotal = discounts.Sum(d => d.Amount);
        if (discountTotal > subtotal && discounts.Count > 0)
        {
            var last = discounts[^1];
            discounts[^1] = last with { Amount = Math.Max(last.Amount - (discountTotal - subtotal), 0) };
            discountTotal = subtotal;
        }

        var tax = Percent(subtotal - discountTotal, BookingOptions.TaxPercent);

        return new PriceQuote
        {
            Nights = nights,
            PricePerNight = room.Price,
            RoomTotal = roomTotal,
            BreakfastFee = breakfast,
            ExtraBedFee = extraBed,
            SpaFee = spa,
            Discounts = discounts,
            Tax = tax
        };
    }

    private static decimal Percent(decimal amount, decimal percent)
    {
        return Math.Round(amount * percent / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
