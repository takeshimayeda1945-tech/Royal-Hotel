using System.Globalization;
using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Infrastructure;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

/// <summary>Session helpers and the role check shared by every controller.</summary>
public abstract class AppControllerBase : Controller
{
    protected readonly ApplicationDbContext _context;

    protected AppControllerBase(ApplicationDbContext context)
    {
        _context = context;
    }

    protected static string NormalizeText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    protected static string NormalizeEmail(string? value)
    {
        return NormalizeText(value).ToLowerInvariant();
    }

    /// <summary>
    /// The signed-in user's id. The session is checked against the database before every action
    /// (Infrastructure/CurrentUserFilter), so the id, name and role are always up to date.
    /// </summary>
    protected int? CurrentUserId =>
        int.TryParse(HttpContext.Session.GetString(SessionKeys.UserId), out var userId) ? userId : null;

    protected string? CurrentRole => HttpContext.Session.GetString(SessionKeys.UserRole);

    protected string CurrentUserName => HttpContext.Session.GetString(SessionKeys.UserName) ?? string.Empty;

    protected bool IsSignedIn => CurrentUserId != null;

    protected string CurrentRequestUrl => Request.PathBase + Request.Path + Request.QueryString;

    protected void SetSessionUser(CyberUser user)
    {
        HttpContext.Session.SetString(SessionKeys.UserId, user.Id.ToString(CultureInfo.InvariantCulture));
        HttpContext.Session.SetString(SessionKeys.UserName, user.Name);
        HttpContext.Session.SetString(SessionKeys.UserRole, user.Role);
    }

    protected string? GetSafeReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
    }

    /// <summary>
    /// Returns a redirect when the visitor's role is not one of <paramref name="roles"/> (to the
    /// login page when nobody is signed in, otherwise to the home page with a message);
    /// returns null when access is allowed.
    /// </summary>
    protected IActionResult? DenyUnless(params string[] roles)
    {
        if (UserRoles.IsIn(CurrentRole, roles))
        {
            return null;
        }

        if (CurrentUserId == null)
        {
            var returnUrl = HttpMethods.IsGet(Request.Method) ? CurrentRequestUrl : null;
            return RedirectToAction("Login", "Home", new { returnUrl });
        }

        TempData["ErrorMessage"] = "บัญชีของคุณไม่มีสิทธิ์เข้าหน้านี้";
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Redirects to a posted local return URL, or to the given action.</summary>
    protected IActionResult RedirectBack(string? returnUrl, string action, string? controller = null)
    {
        return GetSafeReturnUrl(returnUrl) is { } url ? LocalRedirect(url) : RedirectToAction(action, controller);
    }

    /// <summary>The first page a user sees after logging in, by role.</summary>
    protected IActionResult RedirectToRoleHome(string? role)
    {
        var (controller, action) = RolePortals.HomePageOf(role);
        return RedirectToAction(action, controller);
    }

    /// <summary>
    /// After a failed save: true when the e-mail now belongs to another account, i.e. someone
    /// registered it in the moment between our check and the save (the unique index refused it).
    /// </summary>
    protected Task<bool> EmailTakenAsync(string email, int exceptUserId = 0)
    {
        return _context.Users.AsNoTracking().AnyAsync(u => u.Id != exceptUserId && u.Email.ToLower() == email);
    }

    /// <summary>
    /// Adds a line to the audit log for the signed-in user. It is written by the next SaveChanges,
    /// together with the change it describes (call it before saving), so one never exists without the other.
    /// </summary>
    protected void Audit(string action, string entityType, int? entityId, string details)
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        var ipText = ip == null ? string.Empty : (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
        _context.AuditLogs.Add(AuditLog.Create(action, entityType, entityId, details,
            CurrentUserId, CurrentUserName, CurrentRole ?? string.Empty, ipText));
    }

    protected void AddErrorIfNone(string key, string message)
    {
        if (ModelState.GetValidationState(key) != ModelValidationState.Invalid)
        {
            ModelState.AddModelError(key, message);
        }
    }

    // ------------------------------------------------------------------ bookings (customer checkout + front-desk walk-in)

    /// <summary>True when another booking still holds the room on one of the requested nights.</summary>
    protected Task<bool> IsRoomBookedAsync(int roomId, DateTime checkIn, DateTime checkOut)
    {
        return _context.HotelBookings.Where(b => b.RoomId == roomId).Overlapping(checkIn, checkOut).AnyAsync();
    }

    /// <summary>
    /// Locks the room row until the current transaction ends. Two bookings for the same room made
    /// at the same moment are handled one after the other, so the second one sees the first
    /// booking and is refused instead of creating a double booking. Call it as the first statement
    /// of the transaction: MySQL takes the transaction's snapshot at the first plain read, so the
    /// availability check after it also sees a booking committed while we waited for the lock.
    /// </summary>
    protected async Task LockRoomAsync(int roomId)
    {
        if (_context.IsMySql)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM HotelRooms WHERE Id = {roomId} FOR UPDATE");
        }

        // SQLite: the transaction itself already holds the database write lock.
    }

    /// <summary>
    /// Locks one booking row until the current transaction ends (same idea as <see cref="LockRoomAsync"/>),
    /// so a payment cannot be saved on a booking that is being cancelled at the same moment, e.g. by
    /// <see cref="PaymentDeadlineService"/>. Read the booking after this call.
    /// </summary>
    protected async Task LockBookingAsync(int bookingId)
    {
        if (_context.IsMySql)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM HotelBookings WHERE Id = {bookingId} FOR UPDATE");
        }
    }

    /// <summary>
    /// Cancels online bookings whose time to choose a payment method has run out. The background
    /// service does it every minute; pages that show bookings call it first so they are never behind.
    /// </summary>
    protected Task<int> CancelOverduePaymentChoicesAsync()
    {
        return PaymentDeadlineService.CancelOverdueAsync(_context, DateTime.Now);
    }

    /// <summary>Checks the stay dates of a booking form (not in the past, at least 1 night, at most MaxNights).</summary>
    protected bool ValidateStayDates(string? checkInText, string? checkOutText, out DateTime checkIn, out DateTime checkOut,
        string checkInKey = "CheckIn", string checkOutKey = "CheckOut")
    {
        var isValid = true;

        if (!DateInput.TryParse(checkInText, out checkIn))
        {
            AddErrorIfNone(checkInKey, "รูปแบบวันเช็คอินไม่ถูกต้อง");
            isValid = false;
        }
        else if (checkIn < DateTime.Today)
        {
            ModelState.AddModelError(checkInKey, "วันเช็คอินต้องไม่เป็นวันที่ผ่านมาแล้ว");
            isValid = false;
        }

        if (!DateInput.TryParse(checkOutText, out checkOut))
        {
            AddErrorIfNone(checkOutKey, "รูปแบบวันเช็คเอาท์ไม่ถูกต้อง");
            isValid = false;
        }
        else if (isValid && checkOut <= checkIn)
        {
            ModelState.AddModelError(checkOutKey, "วันเช็คเอาท์ต้องอยู่หลังวันเช็คอินอย่างน้อย 1 คืน");
            isValid = false;
        }
        else if (isValid && (checkOut - checkIn).Days > BookingOptions.MaxNights)
        {
            ModelState.AddModelError(checkOutKey, $"จองได้สูงสุด {BookingOptions.MaxNights} คืนต่อครั้ง");
            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Passwords are trimmed (log-in trims them too), so the minimum length is checked after
    /// trimming, on every form that sets a password.
    /// </summary>
    protected void CheckNewPasswordLength(string password, string key)
    {
        if (password.Length < AccountRules.MinPasswordLength)
        {
            AddErrorIfNone(key, $"รหัสผ่านต้องมีอย่างน้อย {AccountRules.MinPasswordLength} ตัวอักษร");
        }
    }
}
