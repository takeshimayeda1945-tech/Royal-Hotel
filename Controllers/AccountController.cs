using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

/// <summary>บัญชีของฉัน: every signed-in user (any role) can edit their details and change their password.</summary>
public class AccountController : AppControllerBase
{
    public AccountController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return RedirectToAction("Login", "Home", new { returnUrl = CurrentRequestUrl });
        }

        var page = await BuildPageAsync(user);
        page.Profile = new ProfileFormModel { Name = user.Name, PhoneNumber = user.PhoneNumber };
        return View(page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile([Bind(Prefix = "Profile")] ProfileFormModel model)
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return RedirectToAction("Login", "Home");
        }

        model.Name = NormalizeText(model.Name);
        model.PhoneNumber = NormalizeText(model.PhoneNumber);

        if (!ModelState.IsValid)
        {
            var page = await BuildPageAsync(user);
            page.Profile = model;
            return View(nameof(Index), page);
        }

        user.Name = model.Name;
        user.PhoneNumber = model.PhoneNumber;
        await _context.SaveChangesAsync();

        // The header shows the name from the session.
        SetSessionUser(user);
        TempData["SuccessMessage"] = "บันทึกข้อมูลส่วนตัวเรียบร้อย";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordModel model)
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return RedirectToAction("Login", "Home");
        }

        var current = NormalizeText(model.CurrentPassword);
        var newPassword = NormalizeText(model.NewPassword);
        const string prefix = "Password.";

        CheckNewPasswordLength(newPassword, prefix + nameof(model.NewPassword));

        if (ModelState.IsValid && PasswordSecurity.Verify(user.Password, current) == PasswordCheck.Failed)
        {
            ModelState.AddModelError(prefix + nameof(model.CurrentPassword), "รหัสผ่านปัจจุบันไม่ถูกต้อง");
        }
        else if (ModelState.IsValid && newPassword == current)
        {
            ModelState.AddModelError(prefix + nameof(model.NewPassword), "รหัสผ่านใหม่ต้องไม่ซ้ำกับรหัสผ่านเดิม");
        }

        if (!ModelState.IsValid)
        {
            var page = await BuildPageAsync(user);
            page.Profile = new ProfileFormModel { Name = user.Name, PhoneNumber = user.PhoneNumber };
            // Password boxes are never filled back in.
            return View(nameof(Index), page);
        }

        user.Password = PasswordSecurity.Hash(newPassword);
        Audit(AuditActions.PasswordChanged, AuditEntities.User, user.Id, $"{user.Name} ({user.Email}) เปลี่ยนรหัสผ่านของตัวเอง");
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "เปลี่ยนรหัสผ่านเรียบร้อย ครั้งต่อไปให้เข้าสู่ระบบด้วยรหัสผ่านใหม่";
        return RedirectToAction(nameof(Index));
    }

    private async Task<CyberUser?> LoadUserAsync()
    {
        return CurrentUserId is int userId ? await _context.Users.FindAsync(userId) : null;
    }

    private async Task<AccountPageModel> BuildPageAsync(CyberUser user)
    {
        var page = new AccountPageModel { User = user };
        if (user.Role == UserRoles.Customer)
        {
            // Only members book rooms; the page shows no booking figures to employees.
            page.BookingCount = await _context.HotelBookings.CountAsync(b => b.UserId == user.Id);
            page.UpcomingCount = await _context.HotelBookings.Where(b => b.UserId == user.Id).StillActive(DateTime.Today).CountAsync();
        }

        return page;
    }
}
