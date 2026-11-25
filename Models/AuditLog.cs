using System.Globalization;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// One line of the audit log (บันทึกการกระทำ): who did what to which record, and when.
/// Written in the same save as the change it describes, so a change never happens without its line.
/// Lines are only added, never changed or deleted by the site. Name and role are copies, so the
/// line still reads correctly after the account is renamed, given another role, or deleted.
/// </summary>
public class AuditLog
{
    public const int MaxDetailsLength = 1000;
    public const string SystemRole = "System";

    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Who did it; null for the system itself (automatic jobs) or once the account is deleted.</summary>
    public int? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;

    /// <summary>One of <see cref="AuditActions"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>One of <see cref="AuditEntities"/>, with the id of that record.</summary>
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }

    /// <summary>What changed, in Thai (e.g. "ราคา ฿3,200 → ฿3,500").</summary>
    public string Details { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public static AuditLog Create(string action, string entityType, int? entityId, string details,
        int? userId, string userName, string userRole, string ipAddress)
    {
        details = details.Trim();
        return new AuditLog
        {
            CreatedAt = DateTime.Now,
            UserId = userId,
            UserName = Cut(userName, 100),
            UserRole = Cut(userRole, 50),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = Cut(details, MaxDetailsLength),
            IpAddress = Cut(ipAddress, 45)
        };
    }

    /// <summary>A line written by the site itself (no user), e.g. the payment deadline service.</summary>
    public static AuditLog BySystem(string action, string entityType, int? entityId, string details) =>
        Create(action, entityType, entityId, details, null, "ระบบอัตโนมัติ", SystemRole, string.Empty);

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>Kinds of record an audit line points to.</summary>
public static class AuditEntities
{
    public const string Booking = "Booking";
    public const string Room = "Room";
    public const string User = "User";
    public const string Shift = "Shift";
    public const string CleaningTask = "CleaningTask";
    public const string Review = "Review";
    public const string Report = "Report";

    public static readonly string[] All = { Booking, Room, User, Shift, CleaningTask, Review, Report };

    public static string ThaiName(string? entity) => entity switch
    {
        Booking => "การจอง",
        Room => "ห้องพัก",
        User => "ผู้ใช้",
        Shift => "เวรแม่บ้าน",
        CleaningTask => "งานทำความสะอาด",
        Review => "รีวิว",
        Report => "รายงาน",
        _ => entity ?? string.Empty
    };
}

/// <summary>What was done. Grouped for the filter of the audit log page.</summary>
public static class AuditActions
{
    // ----- bookings and stays -----
    public const string WalkInBooking = "WalkInBooking";
    public const string CheckIn = "CheckIn";
    public const string CheckOut = "CheckOut";
    public const string StaffCancel = "StaffCancel";
    public const string NoShowClosed = "NoShowClosed";
    public const string GuestCancel = "GuestCancel";
    public const string AutoCancel = "AutoCancel";

    // ----- money -----
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string CashReceived = "CashReceived";
    public const string PaymentRejected = "PaymentRejected";
    public const string RefundPaid = "RefundPaid";
    public const string RefundClosed = "RefundClosed";
    public const string CsvExported = "CsvExported";

    // ----- housekeeping -----
    public const string CleaningRequested = "CleaningRequested";
    public const string CleaningStarted = "CleaningStarted";
    public const string CleaningDone = "CleaningDone";
    public const string ShiftAdded = "ShiftAdded";
    public const string ShiftDeleted = "ShiftDeleted";

    // ----- rooms and accounts -----
    public const string RoomCreated = "RoomCreated";
    public const string RoomUpdated = "RoomUpdated";
    public const string RoomDeleted = "RoomDeleted";
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserRoleChanged = "UserRoleChanged";
    public const string UserPasswordReset = "UserPasswordReset";
    public const string UserDeleted = "UserDeleted";
    public const string PasswordChanged = "PasswordChanged";

    // ----- reviews -----
    public const string ReviewHidden = "ReviewHidden";
    public const string ReviewShown = "ReviewShown";

    public record Group(string Key, string Label, string[] Actions);

    public static readonly Group[] Groups =
    {
        new("booking", "การจองและการเข้าพัก", new[] { WalkInBooking, CheckIn, CheckOut, StaffCancel, NoShowClosed, GuestCancel, AutoCancel }),
        new("money", "การชำระเงินและการเงิน", new[] { PaymentConfirmed, CashReceived, PaymentRejected, RefundPaid, RefundClosed, CsvExported }),
        new("housekeeping", "แม่บ้านและเวร", new[] { CleaningRequested, CleaningStarted, CleaningDone, ShiftAdded, ShiftDeleted }),
        new("admin", "ห้องพักและบัญชีผู้ใช้", new[] { RoomCreated, RoomUpdated, RoomDeleted, UserCreated, UserUpdated, UserRoleChanged, UserPasswordReset, UserDeleted, PasswordChanged }),
        new("review", "รีวิว", new[] { ReviewHidden, ReviewShown }),
    };

    public static readonly string[] All = Groups.SelectMany(g => g.Actions).ToArray();

    public static string ThaiName(string? action) => action switch
    {
        WalkInBooking => "จองให้แขก walk-in",
        CheckIn => "เช็คอิน",
        CheckOut => "เช็คเอาท์",
        StaffCancel => "พนักงานยกเลิกการจอง",
        NoShowClosed => "ปิดรายการไม่มาเข้าพัก",
        GuestCancel => "ลูกค้ายกเลิกการจอง",
        AutoCancel => "ยกเลิกอัตโนมัติ (ไม่เลือกวิธีชำระ)",
        PaymentConfirmed => "ยืนยันยอดโอน",
        CashReceived => "รับชำระเงินสด",
        PaymentRejected => "ปฏิเสธการแจ้งโอน",
        RefundPaid => "บันทึกการคืนเงิน",
        RefundClosed => "ปิดรายการคืนเงิน (ไม่พบยอดโอน)",
        CsvExported => "ดาวน์โหลด CSV",
        CleaningRequested => "แจ้งทำความสะอาด",
        CleaningStarted => "รับงานทำความสะอาด",
        CleaningDone => "ทำความสะอาดเสร็จ",
        ShiftAdded => "เพิ่มเวรแม่บ้าน",
        ShiftDeleted => "ลบเวรแม่บ้าน",
        RoomCreated => "เพิ่มห้องพัก",
        RoomUpdated => "แก้ไขห้องพัก",
        RoomDeleted => "ลบห้องพัก",
        UserCreated => "เพิ่มผู้ใช้",
        UserUpdated => "แก้ไขข้อมูลผู้ใช้",
        UserRoleChanged => "เปลี่ยนบทบาทผู้ใช้",
        UserPasswordReset => "ตั้งรหัสผ่านใหม่ให้ผู้ใช้",
        UserDeleted => "ลบผู้ใช้",
        PasswordChanged => "เปลี่ยนรหัสผ่านตัวเอง",
        ReviewHidden => "ซ่อนรีวิว",
        ReviewShown => "แสดงรีวิวอีกครั้ง",
        _ => action ?? string.Empty
    };

    /// <summary>Colour of the action label (money and deletions stand out).</summary>
    public static string CssClass(string? action) => action switch
    {
        PaymentConfirmed or CashReceived or RefundPaid => "status-confirmed",
        PaymentRejected or RefundClosed or StaffCancel or NoShowClosed or GuestCancel or AutoCancel or
            RoomDeleted or UserDeleted or ShiftDeleted or ReviewHidden => "status-cancelled",
        UserRoleChanged or UserPasswordReset or PasswordChanged or CsvExported => "status-warning",
        _ => "status-info"
    };
}

/// <summary>Short descriptions used in audit lines.</summary>
public static class AuditText
{
    /// <summary>"#123 สมชาย ใจดี · Royal Deluxe 24/11/2026–27/11/2026 · ฿10,272"</summary>
    public static string Booking(HotelBooking b) =>
        $"#{b.Id.ToString(CultureInfo.InvariantCulture)} {b.GuestName} · {b.RoomName} " +
        $"{DisplayText.Date(b.CheckIn)}–{DisplayText.Date(b.CheckOut)} · {DisplayText.Money(b.Total)}";

    /// <summary>Adds "label: old → new" when the value changed.</summary>
    public static void Change(List<string> changes, string label, string before, string after)
    {
        if (before != after)
        {
            changes.Add($"{label}: {(before.Length > 0 ? before : "(ว่าง)")} → {(after.Length > 0 ? after : "(ว่าง)")}");
        }
    }
}
