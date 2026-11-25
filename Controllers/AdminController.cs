using System.Globalization;
using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

public record MonthStat(string Label, decimal Revenue, int Bookings);
public record RoomStat(string RoomName, int Bookings, int Nights, decimal Revenue);

/// <summary>Admin (and Super Admin): revenue dashboard and housekeeping shifts.</summary>
public class AdminController : AppControllerBase
{
    private const int DashboardMonths = 12;

    public AdminController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // Totals are added up in memory so the same code also works on SQLite (no SUM over decimals there).
        var bookings = await _context.HotelBookings.AsNoTracking().ToListAsync();
        var paid = bookings.Where(b => b.PaymentStatus == PaymentStatus.Paid).ToList();
        var notCancelled = bookings.Where(b => b.Status != BookingStatus.Cancelled).ToList();
        var activeRooms = await _context.HotelRooms.CountAsync(r => r.IsActive);

        ViewBag.Revenue = paid.Sum(b => b.Total);
        ViewBag.RevenueThisMonth = paid.Where(b => (b.PaidAt ?? b.CreatedAt) >= monthStart).Sum(b => b.Total);
        ViewBag.BookingCount = bookings.Count;
        ViewBag.BookingsThisMonth = bookings.Count(b => b.CreatedAt >= monthStart);
        ViewBag.CancelledCount = bookings.Count(b => b.Status == BookingStatus.Cancelled);
        ViewBag.InHouse = bookings.Count(b => b.Status == BookingStatus.CheckedIn);
        ViewBag.ActiveRooms = activeRooms;
        ViewBag.ArrivalsToday = bookings.Count(b => b.IsArrivalDue);
        ViewBag.NoShows = bookings.Count(b => b.IsNoShow);

        // Occupancy this month = booked room-nights in the month ÷ (open rooms × days in the month).
        var occupiedNights = notCancelled.Sum(b =>
            Math.Max(0, ((b.CheckOut < monthEnd ? b.CheckOut : monthEnd) - (b.CheckIn > monthStart ? b.CheckIn : monthStart)).Days));
        var availableNights = activeRooms * (monthEnd - monthStart).Days;
        ViewBag.OccupiedNights = occupiedNights;
        ViewBag.AvailableNights = availableNights;
        ViewBag.OccupancyPercent = availableNights > 0 ? Math.Min(100m, occupiedNights * 100m / availableNights) : 0m;
        ViewBag.PendingPayments = bookings.Count(b => b.PaymentStatus == PaymentStatus.Pending);
        ViewBag.OpenCleaning = await _context.HousekeepingTasks.CountAsync(t => t.Status != HousekeepingTaskStatus.Done);
        ViewBag.UserCount = await _context.Users.CountAsync();

        ViewBag.StatusCounts = BookingStatus.All
            .Select(status => (Status: status, Count: bookings.Count(b => b.Status == status)))
            .ToList();

        // Revenue by the month it was received; bookings by the month they were made.
        var months = new List<MonthStat>();
        for (var i = DashboardMonths - 1; i >= 0; i--)
        {
            var start = monthStart.AddMonths(-i);
            var end = start.AddMonths(1);
            months.Add(new MonthStat(
                start.ToString("MMM yy", CultureInfo.InvariantCulture),
                paid.Where(b => (b.PaidAt ?? b.CreatedAt) >= start && (b.PaidAt ?? b.CreatedAt) < end).Sum(b => b.Total),
                bookings.Count(b => b.CreatedAt >= start && b.CreatedAt < end)));
        }

        ViewBag.Months = months;
        // Bookings and nights leave out cancelled stays; revenue is every payment kept for the room
        // (also a closed no-show, whose money is not refunded), so the rows add up to the total revenue.
        ViewBag.RoomStats = bookings
            .Where(b => b.Status != BookingStatus.Cancelled || b.PaymentStatus == PaymentStatus.Paid)
            .GroupBy(b => b.RoomName)
            .Select(g => new RoomStat(
                g.Key,
                g.Count(b => b.Status != BookingStatus.Cancelled),
                g.Where(b => b.Status != BookingStatus.Cancelled).Sum(b => b.Nights),
                g.Where(b => b.PaymentStatus == PaymentStatus.Paid).Sum(b => b.Total)))
            .OrderByDescending(r => r.Revenue)
            .ThenByDescending(r => r.Bookings)
            .ToList();
        ViewBag.RecentBookings = bookings
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Take(8)
            .ToList();

        var visibleStars = await _context.RoomReviews.Where(r => !r.IsHidden).Select(r => r.Rating).ToListAsync();
        ViewBag.ReviewSummary = visibleStars.Count > 0 ? new RatingSummary(visibleStars.Average(), visibleStars.Count) : null;
        ViewBag.HiddenReviews = await _context.RoomReviews.CountAsync(r => r.IsHidden);
        ViewBag.RecentAudit = await _context.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(6)
            .ToListAsync();
        return View();
    }

    // ------------------------------------------------------------------ audit log

    public const int AuditPageSize = 50;

    /// <summary>บันทึกการกระทำ: who did what and when, newest first, with filters.</summary>
    [HttpGet]
    [ActionName("AuditLog")]
    public async Task<IActionResult> AuditLogPage(AuditLogFilter filter, int page = 1)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        // Unknown values are dropped, so an edited link never breaks the page.
        var group = AuditActions.Groups.FirstOrDefault(g => g.Key == filter.Group);
        filter.Group = group?.Key;
        filter.Entity = AuditEntities.All.FirstOrDefault(e => e == filter.Entity);
        if (filter.Entity == null)
        {
            filter.Id = null;
        }

        filter.Q = NormalizeText(filter.Q);
        var hasFrom = DateInput.TryParse(filter.From, out var from);
        var hasTo = DateInput.TryParse(filter.To, out var to);
        filter.From = hasFrom ? DateInput.ToValue(from) : null;
        filter.To = hasTo ? DateInput.ToValue(to) : null;

        var query = _context.AuditLogs.AsNoTracking().AsQueryable();
        if (group != null)
        {
            query = query.Where(a => group.Actions.Contains(a.Action));
        }

        if (filter.Entity != null)
        {
            query = query.Where(a => a.EntityType == filter.Entity);
            if (filter.Id != null)
            {
                query = query.Where(a => a.EntityId == filter.Id);
            }
        }

        if (filter.UserId != null)
        {
            query = query.Where(a => a.UserId == filter.UserId);
        }

        if (hasFrom)
        {
            query = query.Where(a => a.CreatedAt >= from);
        }

        if (hasTo)
        {
            var end = to.AddDays(1);
            query = query.Where(a => a.CreatedAt < end);
        }

        if (filter.Q.Length > 0)
        {
            // "#123" (or just 123) also finds the lines about record 123.
            var keyword = filter.Q.ToLower();
            var number = int.TryParse(filter.Q.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
            query = query.Where(a => a.EntityId == number || a.Details.ToLower().Contains(keyword) || a.UserName.ToLower().Contains(keyword));
        }

        var total = await query.CountAsync();
        page = Paging.Clamp(page, AuditPageSize, total);
        var today = DateTime.Today;
        return View("AuditLog", new AuditLogPageModel
        {
            Lines = await query
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                .Skip((page - 1) * AuditPageSize).Take(AuditPageSize)
                .ToListAsync(),
            Filter = filter,
            Paging = new Paging(page, AuditPageSize, total),
            People = await _context.Users.AsNoTracking()
                .Where(u => _context.AuditLogs.Any(a => a.UserId == u.Id))
                .OrderBy(u => u.Name)
                .ToListAsync(),
            TodayCount = await _context.AuditLogs.CountAsync(a => a.CreatedAt >= today),
            TotalCount = await _context.AuditLogs.CountAsync()
        });
    }

    // ------------------------------------------------------------------ reviews

    public const int ReviewPageSize = 30;

    /// <summary>Every review (newest first): hide rude or advertising ones, show them again.</summary>
    [HttpGet]
    public async Task<IActionResult> Reviews(string? show, int? stars, int page = 1)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        show = show is "visible" or "hidden" ? show : null;
        stars = stars is >= ReviewRules.MinRating and <= ReviewRules.MaxRating ? stars : null;

        var query = _context.RoomReviews.AsNoTracking().AsQueryable();
        if (show != null)
        {
            var hidden = show == "hidden";
            query = query.Where(r => r.IsHidden == hidden);
        }

        if (stars != null)
        {
            query = query.Where(r => r.Rating == stars);
        }

        var total = await query.CountAsync();
        page = Paging.Clamp(page, ReviewPageSize, total);
        var reviews = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * ReviewPageSize).Take(ReviewPageSize)
            .ToListAsync();
        var roomNames = await _context.HotelRooms.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);
        var visibleStars = await _context.RoomReviews.Where(r => !r.IsHidden).Select(r => r.Rating).ToListAsync();

        return View(new ReviewAdminPageModel
        {
            Rows = reviews.Select(r => new ReviewRow(r, roomNames.GetValueOrDefault(r.RoomId, $"#{r.RoomId}"))).ToList(),
            Paging = new Paging(page, ReviewPageSize, total),
            Show = show,
            Stars = stars,
            VisibleCount = visibleStars.Count,
            HiddenCount = await _context.RoomReviews.CountAsync(r => r.IsHidden),
            Overall = visibleStars.Count > 0 ? new RatingSummary(visibleStars.Average(), visibleStars.Count) : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> HideReview(int id, string? returnUrl) => SetReviewHiddenAsync(id, true, returnUrl);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ShowReview(int id, string? returnUrl) => SetReviewHiddenAsync(id, false, returnUrl);

    private async Task<IActionResult> SetReviewHiddenAsync(int id, bool hide, string? returnUrl)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        var review = await _context.RoomReviews.FindAsync(id);
        if (review == null)
        {
            TempData["ErrorMessage"] = "ไม่พบรีวิวนี้";
        }
        else if (review.IsHidden == hide)
        {
            TempData["ErrorMessage"] = hide ? $"รีวิว #{id} ถูกซ่อนอยู่แล้ว" : $"รีวิว #{id} แสดงอยู่แล้ว";
        }
        else
        {
            review.IsHidden = hide;
            var room = await _context.HotelRooms.AsNoTracking().Where(r => r.Id == review.RoomId).Select(r => r.Name).FirstOrDefaultAsync();
            var excerpt = review.Comment.Length > 80 ? review.Comment[..80] + "…" : review.Comment;
            Audit(hide ? AuditActions.ReviewHidden : AuditActions.ReviewShown, AuditEntities.Review, review.Id,
                $"ห้อง {room} · {review.Rating} ดาว · {review.ReviewerName} (การจอง #{review.BookingId})" +
                (excerpt.Length > 0 ? $": “{excerpt.Replace('\n', ' ')}”" : ""));
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = hide
                ? $"ซ่อนรีวิว #{id} แล้ว — ไม่แสดงและไม่นับในคะแนนของห้อง {room}"
                : $"แสดงรีวิว #{id} อีกครั้งแล้ว";
        }

        return RedirectBack(returnUrl, nameof(Reviews));
    }

    // ------------------------------------------------------------------ housekeeping shifts

    [HttpGet]
    public async Task<IActionResult> Shifts()
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        var today = DateTime.Today;
        ViewBag.Housekeepers = await _context.Users
            .Where(u => u.Role == UserRoles.Housekeeper)
            .OrderBy(u => u.Name)
            .ToListAsync();
        ViewBag.Today = today;
        // Every shift from today on (a shift planned weeks ahead must still be visible and deletable),
        // in the order of the day: morning, afternoon, night (see ShiftNames.Order).
        var shifts = (await _context.HousekeepingShifts
                .Where(s => s.Date >= today)
                .OrderBy(s => s.Date)
                .ThenBy(s => s.Id)
                .Take(300)
                .ToListAsync())
            .OrderBy(s => s.Date)
            .ThenBy(s => ShiftNames.Order(s.ShiftName))
            .ThenBy(s => s.UserName)
            .ToList();
        return View(shifts);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddShift(int userId, string? date, string? shiftName, string? note)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRoles.Housekeeper);
        note = NormalizeText(note);

        if (user == null)
        {
            TempData["ErrorMessage"] = "กรุณาเลือกแม่บ้าน";
        }
        else if (!DateInput.TryParse(date, out var shiftDate) || shiftDate < DateTime.Today || shiftDate > DateTime.Today.AddYears(1))
        {
            TempData["ErrorMessage"] = "กรุณาเลือกวันที่ตั้งแต่วันนี้ ถึงไม่เกิน 1 ปีข้างหน้า";
        }
        else if (shiftName == null || !ShiftNames.All.Contains(shiftName))
        {
            TempData["ErrorMessage"] = "กรุณาเลือกช่วงเวร";
        }
        else if (note.Length > 200)
        {
            TempData["ErrorMessage"] = "หมายเหตุต้องไม่เกิน 200 ตัวอักษร";
        }
        else if (await _context.HousekeepingShifts.AnyAsync(s => s.UserId == userId && s.Date == shiftDate && s.ShiftName == shiftName))
        {
            TempData["ErrorMessage"] = $"{user.Name} มีเวรนี้อยู่แล้ว";
        }
        else
        {
            var shift = new HousekeepingShift
            {
                UserId = user.Id,
                UserName = user.Name,
                Date = shiftDate,
                ShiftName = shiftName,
                Note = note
            };
            _context.HousekeepingShifts.Add(shift);

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                await _context.SaveChangesAsync();
                Audit(AuditActions.ShiftAdded, AuditEntities.Shift, shift.Id,
                    $"{user.Name} · {DisplayText.Date(shiftDate)} {shiftName}" + (note.Length > 0 ? $" · {note}" : ""));
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                TempData["SuccessMessage"] = $"เพิ่มเวร {shiftName} วันที่ {DisplayText.Date(shiftDate)} ให้ {user.Name} แล้ว";
            }
            catch (DbUpdateException)
            {
                // Two admins added the same shift at once: the unique index
                // IX_HousekeepingShifts_UserId_Date_ShiftName keeps only one of them.
                if (!await _context.HousekeepingShifts.AsNoTracking()
                        .AnyAsync(s => s.UserId == userId && s.Date == shiftDate && s.ShiftName == shiftName))
                {
                    throw;
                }

                TempData["ErrorMessage"] = $"{user.Name} มีเวรนี้อยู่แล้ว";
            }
        }

        return RedirectToAction(nameof(Shifts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShift(int id)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        var shift = await _context.HousekeepingShifts.FindAsync(id);
        if (shift != null)
        {
            _context.HousekeepingShifts.Remove(shift);
            Audit(AuditActions.ShiftDeleted, AuditEntities.Shift, shift.Id,
                $"{shift.UserName} · {DisplayText.Date(shift.Date)} {shift.ShiftName}");
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"ลบเวรของ {shift.UserName} วันที่ {DisplayText.Date(shift.Date)} แล้ว";
        }

        return RedirectToAction(nameof(Shifts));
    }
}
