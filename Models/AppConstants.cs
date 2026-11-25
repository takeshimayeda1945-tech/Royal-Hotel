namespace Intouch_67173986_L003.Models;

/// <summary>Keys used to keep the signed-in user in the session.</summary>
public static class SessionKeys
{
    public const string UserId = "UserId";
    public const string UserName = "UserName";
    public const string UserRole = "UserRole";
}

public static class UserRoles
{
    public const string Customer = "Customer";
    public const string Staff = "Staff";
    public const string Housekeeper = "Housekeeper";
    public const string Finance = "Finance";
    public const string Admin = "Admin";
    public const string SuperAdmin = "Super Admin";

    public static readonly string[] All = { Customer, Staff, Housekeeper, Finance, Admin, SuperAdmin };

    // ----- who may open which back-office page -----
    public static readonly string[] BookingManagers = { Staff, Admin, SuperAdmin };
    public static readonly string[] PaymentCheckers = { Staff, Finance, Admin, SuperAdmin };
    public static readonly string[] FinanceTeam = { Finance, Admin, SuperAdmin };
    public static readonly string[] HousekeepingTeam = { Housekeeper, Admin, SuperAdmin };
    public static readonly string[] Managers = { Admin, SuperAdmin };
    /// <summary>May ask housekeeping to clean a room (the front desk too, e.g. for a guest request).</summary>
    public static readonly string[] CleaningRequesters = { Housekeeper, Staff, Admin, SuperAdmin };

    public static bool IsIn(string? role, IEnumerable<string> roles)
    {
        return role != null && roles.Contains(role);
    }

    public static string ThaiName(string? role) => role switch
    {
        Customer => "ลูกค้า (สมาชิก)",
        Staff => "พนักงานต้อนรับ",
        Housekeeper => "แม่บ้าน",
        Finance => "ฝ่ายการเงิน",
        Admin => "ผู้ดูแลระบบ",
        SuperAdmin => "ผู้ดูแลระบบสูงสุด",
        _ => role ?? string.Empty
    };
}

/// <summary>Where the stay is: booked → checked in → checked out (or cancelled).</summary>
public static class BookingStatus
{
    public const string Confirmed = "Confirmed";
    public const string CheckedIn = "CheckedIn";
    public const string CheckedOut = "CheckedOut";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = { Confirmed, CheckedIn, CheckedOut, Cancelled };

    /// <summary>
    /// Statuses that keep the room occupied. A guest who checked out (even early) frees the room,
    /// and a cancelled booking never holds it.
    /// </summary>
    public static readonly string[] HoldsRoom = { Confirmed, CheckedIn };

    public static string ThaiName(string? status) => status switch
    {
        Confirmed => "จองแล้ว",
        CheckedIn => "เข้าพักอยู่",
        CheckedOut => "เช็คเอาท์แล้ว",
        Cancelled => "ยกเลิก",
        _ => status ?? string.Empty
    };

    public static string CssClass(string? status) => status switch
    {
        Confirmed => "status-confirmed",
        CheckedIn => "status-info",
        CheckedOut => "status-completed",
        Cancelled => "status-cancelled",
        _ => "status-completed"
    };
}

/// <summary>Why a booking was cancelled (empty: not cancelled, or cancelled before reasons were kept).</summary>
public static class CancelReasons
{
    public const string Guest = "Guest";
    public const string Staff = "Staff";
    /// <summary>The front desk closed a booking whose guest never came. Never refunded (see BookingRules.Cancel).</summary>
    public const string NoShow = "NoShow";
    /// <summary>Cancelled by the system: no payment method was chosen in time (see BookingOptions.PaymentChoiceMinutes).</summary>
    public const string PaymentTimeout = "PaymentTimeout";

    public static readonly string[] All = { Guest, Staff, NoShow, PaymentTimeout };

    public static string ThaiName(string? reason) => reason switch
    {
        Guest => "ลูกค้ายกเลิกเอง",
        Staff => "พนักงานยกเลิก",
        NoShow => "แขกไม่มาเข้าพัก (ไม่คืนเงิน)",
        PaymentTimeout => $"ยกเลิกอัตโนมัติ: ไม่ได้เลือกวิธีชำระเงินภายใน {BookingOptions.PaymentChoiceMinutes} นาที",
        _ => reason ?? string.Empty
    };
}

/// <summary>Where the money is. Kept separate from <see cref="BookingStatus"/>.</summary>
public static class PaymentStatus
{
    /// <summary>Nothing paid yet (no method chosen, or cash to be paid at the front desk).</summary>
    public const string Unpaid = "Unpaid";
    /// <summary>Customer says the QR transfer was made; staff or finance must check the slip.</summary>
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    /// <summary>A paid booking was cancelled; finance has to return the money.</summary>
    public const string RefundPending = "RefundPending";
    public const string Refunded = "Refunded";

    public static readonly string[] All = { Unpaid, Pending, Paid, RefundPending, Refunded };

    public static string ThaiName(string? status) => status switch
    {
        Unpaid => "ยังไม่ชำระ",
        Pending => "รอตรวจสอบ",
        Paid => "ชำระแล้ว",
        RefundPending => "รอคืนเงิน",
        Refunded => "คืนเงินแล้ว",
        _ => status ?? string.Empty
    };

    public static string CssClass(string? status) => status switch
    {
        Paid => "status-confirmed",
        Pending or RefundPending => "status-warning",
        Refunded => "status-completed",
        _ => "status-cancelled"
    };
}

public static class RoomTypes
{
    public const string Standard = "Standard";
    public const string Deluxe = "Deluxe";
    public const string Suite = "Suite";

    public static readonly string[] All = { Standard, Deluxe, Suite };
}

/// <summary>Business rules shared by the booking pages and the admin room editor.</summary>
public static class BookingOptions
{
    // ----- extra services -----
    /// <summary>Breakfast: 300 baht per guest, charged for every night of the stay.</summary>
    public const decimal BreakfastPerPersonPerNight = 300m;
    /// <summary>Extra bed: 500 baht per night.</summary>
    public const decimal ExtraBedPerNight = 500m;
    /// <summary>Spa: 1,500 baht per session.</summary>
    public const decimal SpaPerSession = 1500m;
    public const int MaxSpaSessions = 10;

    /// <summary>VAT, added after the discounts.</summary>
    public const decimal TaxPercent = 7m;

    public const int MaxNights = 30;
    public const int MaxGuests = 10;

    /// <summary>Longest special request a guest can write for the front desk (characters).</summary>
    public const int MaxSpecialRequestLength = 500;

    public const string SingleBed = "เตียงเดี่ยว";
    public const string DoubleBed = "เตียงคู่";
    public static readonly string[] BedTypes = { SingleBed, DoubleBed };

    // ----- payment -----
    /// <summary>
    /// After booking online, the guest has this many minutes to choose a payment method (paying at the
    /// hotel in cash counts). Otherwise the booking is cancelled and the room is free again
    /// (Infrastructure/PaymentDeadlineService.cs), so a booking left at the payment step cannot keep a room.
    /// </summary>
    public const int PaymentChoiceMinutes = 30;

    public const string Cash = "เงินสด";
    public const string QrCode = "QR Code";
    public const string DebitCard = "บัตรเดบิต";
    public const string CreditCard = "บัตรเครดิต";
    public static readonly string[] PaymentMethods = { Cash, QrCode, DebitCard, CreditCard };
    public static readonly string[] CardPaymentMethods = { DebitCard, CreditCard };

    public static bool IsCardPayment(string? paymentMethod)
    {
        return paymentMethod != null && CardPaymentMethods.Contains(paymentMethod);
    }
}

public static class HousekeepingTaskStatus
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Done = "Done";

    public static readonly string[] All = { Pending, InProgress, Done };

    public static string ThaiName(string? status) => status switch
    {
        Pending => "รอทำความสะอาด",
        InProgress => "กำลังทำ",
        Done => "เสร็จแล้ว",
        _ => status ?? string.Empty
    };

    public static string CssClass(string? status) => status switch
    {
        Pending => "status-cancelled",
        InProgress => "status-warning",
        _ => "status-confirmed"
    };
}

public static class ShiftNames
{
    public const string Morning = "เช้า 07:00–15:00";
    public const string Afternoon = "บ่าย 15:00–23:00";
    public const string Night = "ดึก 23:00–07:00";

    public static readonly string[] All = { Morning, Afternoon, Night };

    /// <summary>
    /// Position of the shift in the day (morning 0, afternoon 1, night 2). Sorting by the name
    /// itself puts night first: Thai text sorts by its letters, and "เช้า" starts with a vowel.
    /// </summary>
    public static int Order(string? shiftName)
    {
        var index = Array.IndexOf(All, shiftName);
        return index < 0 ? All.Length : index;
    }
}
