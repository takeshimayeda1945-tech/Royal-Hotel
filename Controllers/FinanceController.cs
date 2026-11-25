using System.Globalization;
using System.Text;
using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

/// <summary>ฝ่ายการเงิน: money received, transfers to check and refunds to pay back.</summary>
public class FinanceController : AppControllerBase
{
    public FinanceController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (DenyUnless(UserRoles.FinanceTeam) is { } denied)
        {
            return denied;
        }

        // Totals are added up in memory so the same code also works on SQLite (no SUM over decimals there).
        var bookings = await _context.HotelBookings.AsNoTracking().ToListAsync();
        var paid = bookings.Where(b => b.PaymentStatus == PaymentStatus.Paid).ToList();
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        ViewBag.ReceivedTotal = paid.Sum(b => b.Total);
        ViewBag.ReceivedThisMonth = paid.Where(b => (b.PaidAt ?? b.CreatedAt) >= monthStart).Sum(b => b.Total);
        ViewBag.TaxTotal = paid.Sum(b => b.Tax);
        ViewBag.ByMethod = paid
            .GroupBy(b => b.PaymentMethod.Length > 0 ? b.PaymentMethod : "ไม่ระบุ")
            .Select(g => (Method: g.Key, Count: g.Count(), Amount: g.Sum(b => b.Total)))
            .OrderByDescending(g => g.Amount)
            .ToList();
        ViewBag.PendingTransfers = bookings
            .Where(b => b.PaymentStatus == PaymentStatus.Pending)
            .OrderBy(b => b.CreatedAt)
            .ToList();
        // Money still to come: bookings that are going ahead (stays that ended without the guest are no-shows).
        ViewBag.UnpaidTotal = bookings
            .Where(b => (b.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Pending) &&
                        (b.Status == BookingStatus.CheckedIn || (b.Status == BookingStatus.Confirmed && b.CheckOut > today)))
            .Sum(b => b.Total);
        ViewBag.Refunds = bookings
            .Where(b => b.PaymentStatus == PaymentStatus.RefundPending)
            .OrderBy(b => b.CreatedAt)
            .ToList();
        ViewBag.RefundedTotal = bookings.Where(b => b.PaymentStatus == PaymentStatus.Refunded).Sum(b => b.Total);
        ViewBag.RecentPayments = paid
            .OrderByDescending(b => b.PaidAt ?? b.CreatedAt)
            .Take(20)
            .ToList();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRefunded(int id)
    {
        if (DenyUnless(UserRoles.FinanceTeam) is { } denied)
        {
            return denied;
        }

        var booking = await _context.HotelBookings.FindAsync(id);
        if (booking == null || booking.PaymentStatus != PaymentStatus.RefundPending)
        {
            TempData["ErrorMessage"] = $"การจอง #{id} ไม่มีรายการรอคืนเงิน";
        }
        else
        {
            booking.PaymentStatus = PaymentStatus.Refunded;
            Audit(AuditActions.RefundPaid, AuditEntities.Booking, booking.Id,
                $"{AuditText.Booking(booking)} · คืนเงิน {DisplayText.Money(booking.Total)} ({booking.PaymentMethod})");
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"บันทึกการคืนเงิน {DisplayText.Money(booking.Total)} ให้การจอง #{id} แล้ว";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// A booking was cancelled while its transfer was still waiting to be checked, and finance
    /// cannot find the money: there is nothing to give back, so the refund request is closed.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseRefund(int id)
    {
        if (DenyUnless(UserRoles.FinanceTeam) is { } denied)
        {
            return denied;
        }

        var booking = await _context.HotelBookings.FindAsync(id);
        if (booking == null || booking.PaymentStatus != PaymentStatus.RefundPending)
        {
            TempData["ErrorMessage"] = $"การจอง #{id} ไม่มีรายการรอคืนเงิน";
        }
        else if (booking.PaidAt != null)
        {
            TempData["ErrorMessage"] = $"การจอง #{id} ได้รับเงินแล้ว ({DisplayText.Timestamp(booking.PaidAt.Value)}) ต้องคืนเงินให้ลูกค้า";
        }
        else
        {
            booking.PaymentStatus = PaymentStatus.Unpaid;
            Audit(AuditActions.RefundClosed, AuditEntities.Booking, booking.Id,
                $"{AuditText.Booking(booking)} · ไม่พบยอดโอน (อ้างอิง {booking.PaymentReference}) จึงไม่มีเงินต้องคืน");
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"ปิดรายการคืนเงินของการจอง #{id} แล้ว (ไม่พบยอดโอน จึงไม่มีเงินต้องคืน)";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Every booking with its amounts and payment, as a CSV file that opens in Excel (Thai text included).</summary>
    [HttpGet]
    public async Task<IActionResult> ExportCsv()
    {
        if (DenyUnless(UserRoles.FinanceTeam) is { } denied)
        {
            return denied;
        }

        var bookings = await _context.HotelBookings.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
        var csv = new StringBuilder();
        AppendRow(csv, "เลขที่การจอง", "วันที่จอง", "ผู้เข้าพัก", "อีเมล", "ห้อง", "เช็คอิน", "เช็คเอาท์", "คืน", "ผู้เข้าพัก (คน)",
            "ค่าห้อง", "ค่าบริการเสริม", "ส่วนลด", "ภาษี", "ยอดสุทธิ", "วิธีชำระ", "สถานะการชำระ", "เลขอ้างอิง", "วันที่รับเงิน", "สถานะการจอง",
            "เหตุผลที่ยกเลิก");

        foreach (var b in bookings)
        {
            AppendRow(csv,
                b.Id.ToString(CultureInfo.InvariantCulture),
                DisplayText.Timestamp(b.CreatedAt),
                b.GuestName,
                b.ContactEmail,
                b.RoomName,
                DisplayText.Date(b.CheckIn),
                DisplayText.Date(b.CheckOut),
                b.Nights.ToString(CultureInfo.InvariantCulture),
                b.Guests.ToString(CultureInfo.InvariantCulture),
                Amount(b.RoomTotal),
                Amount(b.ServiceTotal),
                Amount(b.Discount),
                Amount(b.Tax),
                Amount(b.Total),
                b.PaymentMethod,
                PaymentStatus.ThaiName(b.PaymentStatus),
                b.PaymentReference,
                b.PaidAt is { } paidAt ? DisplayText.Timestamp(paidAt) : string.Empty,
                BookingStatus.ThaiName(b.Status),
                // A cancelled booking that is still paid is a closed no-show (money kept): the reason says so.
                b.Status == BookingStatus.Cancelled ? CancelReasons.ThaiName(b.CancelReason) : string.Empty);
        }

        // The BOM tells Excel the file is UTF-8, otherwise Thai text shows as garbage.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        var fileName = $"royalhotel-bookings-{DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";

        // The file holds every guest's name and e-mail: who took a copy is worth knowing.
        Audit(AuditActions.CsvExported, AuditEntities.Report, null, $"{fileName} · {bookings.Count:N0} รายการ");
        await _context.SaveChangesAsync();
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static void AppendRow(StringBuilder csv, params string[] cells)
    {
        csv.Append(string.Join(",", cells.Select(CsvCell))).Append("\r\n");
    }

    private static string CsvCell(string value)
    {
        // A cell that starts with = + - @ would run as a formula in Excel (guest names are typed by users).
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
