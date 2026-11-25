using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

/// <summary>User management (Super Admin only) and rooms &amp; prices (Admin and Super Admin).</summary>
public class SuperAdminController : AppControllerBase
{
    public SuperAdminController(ApplicationDbContext context) : base(context)
    {
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Only the known roles are accepted; anything else becomes Customer.</summary>
    private static string NormalizeRole(string? value)
    {
        var role = NormalizeText(value);
        return UserRoles.All.Contains(role) ? role : UserRoles.Customer;
    }

    private static void NormalizeUserForm(UserFormModel model)
    {
        model.Name = NormalizeText(model.Name);
        model.Email = NormalizeEmail(model.Email);
        model.PhoneNumber = NormalizeText(model.PhoneNumber);
        model.Password = NormalizeText(model.Password);
        model.Role = NormalizeRole(model.Role);
    }

    private Task<List<CyberUser>> LoadUsersAsync()
    {
        return _context.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync();
    }

    private Task<List<HotelRoom>> LoadRoomsAsync()
    {
        return _context.HotelRooms.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    private static HotelRoom NewRoom()
    {
        return new HotelRoom { Capacity = 2, BedType = BookingOptions.DoubleBed, IsActive = true };
    }

    private static bool IsValidImageUrl(string url)
    {
        if (url.StartsWith('/') && !url.StartsWith("//"))
        {
            return true; // site-relative path, e.g. /img/room.jpg
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    // ------------------------------------------------------------------ dashboard

    /// <summary>The overview moved to the Admin dashboard (revenue, bookings, rooms).</summary>
    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Dashboard", "Admin");
    }

    // ------------------------------------------------------------------ users

    [HttpGet]
    public IActionResult AddUser()
    {
        if (DenyUnless(UserRoles.SuperAdmin) is { } denied)
        {
            return denied;
        }

        return View(new UserFormModel { Role = UserRoles.Customer });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUser(UserFormModel model)
    {
        if (DenyUnless(UserRoles.SuperAdmin) is { } denied)
        {
            return denied;
        }

        NormalizeUserForm(model);
        if (string.IsNullOrEmpty(model.Password))
        {
            AddErrorIfNone(nameof(model.Password), "กรุณากรอกรหัสผ่าน");
        }
        else
        {
            CheckNewPasswordLength(model.Password, nameof(model.Password));
        }

        if (ModelState.IsValid && await _context.Users.AnyAsync(u => u.Email.ToLower() == model.Email))
        {
            ModelState.AddModelError(nameof(model.Email), "อีเมลนี้มีผู้ใช้งานแล้ว");
        }

        if (!ModelState.IsValid)
        {
            model.Password = null; // never send a password back to the browser
            return View(model);
        }

        var user = new CyberUser
        {
            Name = model.Name,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            Birthday = model.Birthday!.Value.Date,
            Password = PasswordSecurity.Hash(model.Password!),
            Role = model.Role
        };

        _context.Users.Add(user);
        try
        {
            // The account and its log line are saved together (the line needs the new account's id).
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.SaveChangesAsync();
            Audit(AuditActions.UserCreated, AuditEntities.User, user.Id,
                $"{user.Name} ({user.Email}) · บทบาท {UserRoles.ThaiName(user.Role)}");
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            // The same e-mail was saved by someone else a moment ago (unique index on Email).
            if (!await EmailTakenAsync(model.Email))
            {
                throw;
            }

            ModelState.AddModelError(nameof(model.Email), "อีเมลนี้มีผู้ใช้งานแล้ว");
            model.Password = null;
            return View(model);
        }

        TempData["SuccessMessage"] = $"เพิ่มผู้ใช้ {user.Name} ({UserRoles.ThaiName(user.Role)}) เรียบร้อย";
        return RedirectToAction(nameof(UserList));
    }

    [HttpGet]
    public async Task<IActionResult> UserList(int? editId, string? role, string? q)
    {
        if (DenyUnless(UserRoles.SuperAdmin) is { } denied)
        {
            return denied;
        }

        // The view gets every user (for the per-role counts) and filters the table itself.
        var users = await LoadUsersAsync();
        var editUser = editId.HasValue ? users.FirstOrDefault(u => u.Id == editId.Value) : null;
        if (editId.HasValue && editUser == null)
        {
            TempData["ErrorMessage"] = "ไม่พบผู้ใช้ที่ต้องการแก้ไข";
        }

        ViewBag.EditUser = editUser != null ? UserFormModel.From(editUser) : null;
        ViewBag.CurrentUserId = CurrentUserId;
        ViewBag.RoleFilter = role != null && UserRoles.All.Contains(role) ? role : null;
        ViewBag.Query = NormalizeText(q);
        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateUser(UserFormModel model)
    {
        if (DenyUnless(UserRoles.SuperAdmin) is { } denied)
        {
            return denied;
        }

        var existingUser = await _context.Users.FindAsync(model.Id);
        if (existingUser == null)
        {
            TempData["ErrorMessage"] = "ไม่พบผู้ใช้นี้";
            return RedirectToAction(nameof(UserList));
        }

        NormalizeUserForm(model);

        // The password box is left empty on purpose: blank means "keep the current password".
        if (!string.IsNullOrEmpty(model.Password))
        {
            CheckNewPasswordLength(model.Password, nameof(model.Password));
        }

        var isCurrentUser = existingUser.Id == CurrentUserId;
        if (isCurrentUser && model.Role != UserRoles.SuperAdmin)
        {
            ModelState.AddModelError(nameof(model.Role), "ไม่สามารถเอาสิทธิ์ Super Admin ออกจากบัญชีของตัวเองได้");
        }

        if (ModelState.IsValid &&
            await _context.Users.AnyAsync(u => u.Id != model.Id && u.Email.ToLower() == model.Email))
        {
            ModelState.AddModelError(nameof(model.Email), "อีเมลนี้มีผู้ใช้งานแล้ว");
        }

        if (!ModelState.IsValid)
        {
            model.Password = null;
            ViewBag.EditUser = model;
            ViewBag.CurrentUserId = CurrentUserId;
            return View(nameof(UserList), await LoadUsersAsync());
        }

        var wasHousekeeper = existingUser.Role == UserRoles.Housekeeper;
        var oldRole = existingUser.Role;
        var who = $"{model.Name} ({model.Email})";
        var changes = new List<string>();
        AuditText.Change(changes, "ชื่อ", existingUser.Name, model.Name);
        AuditText.Change(changes, "อีเมล", existingUser.Email, model.Email);
        AuditText.Change(changes, "เบอร์โทร", existingUser.PhoneNumber, model.PhoneNumber);
        AuditText.Change(changes, "วันเกิด", DisplayText.Date(existingUser.Birthday), DisplayText.Date(model.Birthday!.Value.Date));
        if (changes.Count > 0)
        {
            Audit(AuditActions.UserUpdated, AuditEntities.User, existingUser.Id, $"{who} · {string.Join(" · ", changes)}");
        }

        if (oldRole != model.Role)
        {
            Audit(AuditActions.UserRoleChanged, AuditEntities.User, existingUser.Id,
                $"{who} · {UserRoles.ThaiName(oldRole)} → {UserRoles.ThaiName(model.Role)}");
        }

        if (!string.IsNullOrEmpty(model.Password))
        {
            // Never the password itself: only that a new one was set.
            Audit(AuditActions.UserPasswordReset, AuditEntities.User, existingUser.Id, $"{who} · ตั้งรหัสผ่านใหม่");
        }

        existingUser.Name = model.Name;
        existingUser.Email = model.Email;
        existingUser.PhoneNumber = model.PhoneNumber;
        existingUser.Birthday = model.Birthday!.Value.Date;
        existingUser.Role = model.Role;
        if (!string.IsNullOrEmpty(model.Password))
        {
            existingUser.Password = PasswordSecurity.Hash(model.Password);
        }

        // The user row and the copies of the name on the duty shifts change together, or not at all.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Someone who is no longer a housekeeper keeps no future duty shifts.
        var removedShifts = 0;
        if (wasHousekeeper && model.Role != UserRoles.Housekeeper)
        {
            var futureShifts = await _context.HousekeepingShifts
                .Where(s => s.UserId == existingUser.Id && s.Date >= DateTime.Today)
                .ToListAsync();
            _context.HousekeepingShifts.RemoveRange(futureShifts);
            removedShifts = futureShifts.Count;
        }
        else
        {
            // Shift rows keep a copy of the name for the duty table.
            await _context.HousekeepingShifts
                .Where(s => s.UserId == existingUser.Id && s.UserName != existingUser.Name)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserName, existingUser.Name));
        }

        try
        {
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            // The new e-mail was taken by another account a moment ago (unique index on Email).
            await transaction.RollbackAsync();
            if (!await EmailTakenAsync(model.Email, model.Id))
            {
                throw;
            }

            ModelState.AddModelError(nameof(model.Email), "อีเมลนี้มีผู้ใช้งานแล้ว");
            model.Password = null;
            ViewBag.EditUser = model;
            ViewBag.CurrentUserId = CurrentUserId;
            return View(nameof(UserList), await LoadUsersAsync());
        }

        if (isCurrentUser)
        {
            // Keep the name shown in the header in sync.
            SetSessionUser(existingUser);
        }

        TempData["SuccessMessage"] = $"แก้ไขข้อมูลผู้ใช้ {existingUser.Name} เรียบร้อย" +
                                     (removedShifts > 0 ? $" (ลบเวรแม่บ้านที่ยังไม่ถึง {removedShifts} รายการ)" : "");
        return RedirectToAction(nameof(UserList));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (DenyUnless(UserRoles.SuperAdmin) is { } denied)
        {
            return denied;
        }

        if (id == CurrentUserId)
        {
            TempData["ErrorMessage"] = "ไม่สามารถลบบัญชีที่กำลังใช้งานอยู่ได้";
            return RedirectToAction(nameof(UserList));
        }

        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            TempData["ErrorMessage"] = "ไม่พบผู้ใช้นี้";
            return RedirectToAction(nameof(UserList));
        }

        var activeBookings = await _context.HotelBookings.Where(b => b.UserId == id).StillActive(DateTime.Today).CountAsync();
        if (activeBookings > 0)
        {
            TempData["ErrorMessage"] = $"ลบผู้ใช้ {user.Name} ไม่ได้ เพราะยังมีการจองที่ยังไม่สิ้นสุด {activeBookings} รายการ — ยกเลิกหรือเช็คเอาท์ก่อน";
            return RedirectToAction(nameof(UserList));
        }

        // Past bookings stay for the accounts (with the guest name and e-mail saved on them); the
        // database's foreign keys do the same, this also covers the old SQLite file that has none.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.HotelBookings.Where(b => b.UserId == id)
            .ExecuteUpdateAsync(b => b.SetProperty(x => x.UserId, (int?)null));
        await _context.HousekeepingTasks.Where(t => t.AssignedToUserId == id)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.AssignedToUserId, (int?)null));
        await _context.HousekeepingShifts.Where(s => s.UserId == id).ExecuteDeleteAsync();
        // Reviews and audit lines stay, with the name that was copied onto them.
        await _context.RoomReviews.Where(r => r.UserId == id)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.UserId, (int?)null));
        await _context.AuditLogs.Where(a => a.UserId == id)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.UserId, (int?)null));
        _context.Users.Remove(user);
        Audit(AuditActions.UserDeleted, AuditEntities.User, id,
            $"{user.Name} ({user.Email}) · บทบาท {UserRoles.ThaiName(user.Role)}");
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["SuccessMessage"] = $"ลบผู้ใช้ {user.Name} เรียบร้อย";
        return RedirectToAction(nameof(UserList));
    }

    // ------------------------------------------------------------------ rooms

    [HttpGet]
    public async Task<IActionResult> Rooms(int? editId)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        var rooms = await LoadRoomsAsync();
        HotelRoom? editRoom = null;
        if (editId.HasValue)
        {
            editRoom = rooms.FirstOrDefault(r => r.Id == editId.Value);
            if (editRoom == null)
            {
                TempData["ErrorMessage"] = "ไม่พบห้องพักที่ต้องการแก้ไข";
            }
        }

        ViewBag.EditRoom = editRoom ?? NewRoom();
        ViewBag.BookingCounts = await LoadBookingCountsAsync();
        return View(rooms);
    }

    private async Task<Dictionary<int, int>> LoadBookingCountsAsync()
    {
        return await _context.HotelBookings
            .GroupBy(b => b.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoomId, x => x.Count);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRoom(HotelRoom model)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        // Empty optional text boxes arrive as null; store them as empty text (the columns are NOT NULL).
        model.Name = NormalizeText(model.Name);
        model.Area = NormalizeText(model.Area);
        model.ImageUrl = NormalizeText(model.ImageUrl);
        model.Tag = NormalizeText(model.Tag);
        model.Description = NormalizeText(model.Description);
        model.BedType = NormalizeText(model.BedType);
        model.RoomType = NormalizeText(model.RoomType);

        // With nullable reference types the binder marks every empty optional field as "required",
        // so the room is validated by hand instead.
        ModelState.Clear();
        if (model.Name.Length is 0 or > 100)
        {
            ModelState.AddModelError(string.Empty, "กรุณากรอกชื่อห้องพัก (ไม่เกิน 100 ตัวอักษร)");
        }
        else if (await _context.HotelRooms.AnyAsync(r => r.Id != model.Id && r.Name.ToLower() == model.Name.ToLower()))
        {
            ModelState.AddModelError(string.Empty, $"มีห้องชื่อ \"{model.Name}\" อยู่แล้ว — ตั้งชื่อห้องไม่ให้ซ้ำกัน");
        }

        if (model.Price <= 0 || model.Price > 1_000_000)
        {
            ModelState.AddModelError(string.Empty, "ราคาต่อคืนต้องมากกว่า 0 และไม่เกิน 1,000,000 บาท");
        }

        if (model.Capacity < 1 || model.Capacity > BookingOptions.MaxGuests)
        {
            ModelState.AddModelError(string.Empty, $"จำนวนผู้เข้าพักต้องอยู่ระหว่าง 1–{BookingOptions.MaxGuests} คน");
        }

        if (!RoomTypes.All.Contains(model.RoomType))
        {
            ModelState.AddModelError(string.Empty, "กรุณาเลือกประเภทห้องจากรายการ");
        }

        if (!BookingOptions.BedTypes.Contains(model.BedType))
        {
            ModelState.AddModelError(string.Empty, "กรุณาเลือกประเภทเตียงจากรายการ");
        }

        if (model.ImageUrl.Length > 500 || (model.ImageUrl.Length > 0 && !IsValidImageUrl(model.ImageUrl)))
        {
            ModelState.AddModelError(string.Empty, "URL รูปภาพต้องขึ้นต้นด้วย http://, https:// หรือ / และยาวไม่เกิน 500 ตัวอักษร");
        }

        if (model.Area.Length > 100 || model.Tag.Length > 50 || model.Description.Length > 1000)
        {
            ModelState.AddModelError(string.Empty, "พื้นที่ไม่เกิน 100, ป้ายกำกับไม่เกิน 50 และรายละเอียดไม่เกิน 1,000 ตัวอักษร");
        }

        HotelRoom? existing = null;
        if (model.Id != 0)
        {
            existing = await _context.HotelRooms.FindAsync(model.Id);
            if (existing == null)
            {
                TempData["ErrorMessage"] = "ไม่พบห้องพักที่ต้องการแก้ไข";
                return RedirectToAction(nameof(Rooms));
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.EditRoom = model;
            ViewBag.BookingCounts = await LoadBookingCountsAsync();
            return View(nameof(Rooms), await LoadRoomsAsync());
        }

        if (existing == null)
        {
            model.Id = 0;
            _context.HotelRooms.Add(model);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.SaveChangesAsync();
            Audit(AuditActions.RoomCreated, AuditEntities.Room, model.Id,
                $"{model.Name} · {model.RoomType} · {DisplayText.Money(model.Price)}/คืน · {model.Capacity} คน" +
                (model.IsActive ? "" : " · ปิดรับจอง"));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = $"เพิ่มห้องพัก {model.Name} เรียบร้อย";
            return RedirectToAction(nameof(Rooms));
        }
        else
        {
            var changes = new List<string>();
            AuditText.Change(changes, "ชื่อ", existing.Name, model.Name);
            AuditText.Change(changes, "ประเภท", existing.RoomType, model.RoomType);
            AuditText.Change(changes, "ราคา/คืน", DisplayText.Money(existing.Price), DisplayText.Money(model.Price));
            AuditText.Change(changes, "รองรับ", $"{existing.Capacity} คน", $"{model.Capacity} คน");
            AuditText.Change(changes, "เตียง", existing.BedType, model.BedType);
            AuditText.Change(changes, "พื้นที่", existing.Area, model.Area);
            AuditText.Change(changes, "ป้ายกำกับ", existing.Tag, model.Tag);
            AuditText.Change(changes, "สปา", existing.HasSpa ? "มี" : "ไม่มี", model.HasSpa ? "มี" : "ไม่มี");
            AuditText.Change(changes, "การรับจอง", existing.IsActive ? "เปิด" : "ปิด", model.IsActive ? "เปิด" : "ปิด");
            if (existing.ImageUrl != model.ImageUrl)
            {
                changes.Add("เปลี่ยนรูปภาพ");
            }

            if (existing.Description != model.Description)
            {
                changes.Add("แก้รายละเอียด");
            }

            if (changes.Count > 0)
            {
                Audit(AuditActions.RoomUpdated, AuditEntities.Room, existing.Id, $"{model.Name} · {string.Join(" · ", changes)}");
            }

            var message = $"แก้ไขข้อมูลห้องพัก {model.Name} เรียบร้อย";
            if (existing.IsActive && !model.IsActive)
            {
                // Closing a room only stops new bookings; existing ones still have to be honoured or cancelled.
                var upcoming = await _context.HotelBookings
                    .Where(b => b.RoomId == existing.Id)
                    .Overlapping(DateTime.Today, DateTime.MaxValue.Date)
                    .CountAsync();
                if (upcoming > 0)
                {
                    message += $" — ปิดรับจองแล้ว แต่ยังมีการจองที่ยังไม่สิ้นสุด {upcoming} รายการ (ดูที่หน้าจัดการการจอง)";
                }
            }

            existing.Name = model.Name;
            existing.Price = model.Price;
            existing.Capacity = model.Capacity;
            existing.Area = model.Area;
            existing.ImageUrl = model.ImageUrl;
            existing.Tag = model.Tag;
            existing.Description = model.Description;
            existing.BedType = model.BedType;
            existing.RoomType = model.RoomType;
            existing.HasSpa = model.HasSpa;
            existing.IsActive = model.IsActive;
            TempData["SuccessMessage"] = message;
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        if (DenyUnless(UserRoles.Managers) is { } denied)
        {
            return denied;
        }

        var room = await _context.HotelRooms.FindAsync(id);
        if (room == null)
        {
            TempData["ErrorMessage"] = "ไม่พบห้องพักที่ต้องการลบ";
            return RedirectToAction(nameof(Rooms));
        }

        // Bookings (even old ones) point to their room: the history must keep it. Close the room instead.
        var bookingCount = await _context.HotelBookings.CountAsync(b => b.RoomId == id);
        if (bookingCount > 0)
        {
            TempData["ErrorMessage"] = $"ลบห้อง {room.Name} ไม่ได้ เพราะมีประวัติการจอง {bookingCount} รายการ — " +
                                       "ให้แก้ไขห้องแล้วเอาเครื่องหมาย \"เปิดให้จอง\" ออกแทน";
            return RedirectToAction(nameof(Rooms));
        }

        await _context.HousekeepingTasks.Where(t => t.RoomId == id).ExecuteDeleteAsync();
        _context.HotelRooms.Remove(room);
        Audit(AuditActions.RoomDeleted, AuditEntities.Room, id, $"{room.Name} · {room.RoomType} · {DisplayText.Money(room.Price)}/คืน");
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"ลบห้องพัก {room.Name} เรียบร้อย";
        return RedirectToAction(nameof(Rooms));
    }

    // Kept so old links do not hit a missing view (there is no Entry.cshtml).
    [HttpGet]
    public IActionResult Entry()
    {
        return RedirectToAction(nameof(Index));
    }
}
