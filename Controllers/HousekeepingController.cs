using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

/// <summary>แม่บ้าน: rooms to clean and duty shifts.</summary>
public class HousekeepingController : AppControllerBase
{
    public HousekeepingController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (DenyUnless(UserRoles.HousekeepingTeam) is { } denied)
        {
            return denied;
        }

        var today = DateTime.Today;
        var isHousekeeper = CurrentRole == UserRoles.Housekeeper;

        // Housekeepers see their own shifts; managers see everyone's shifts for the next 7 days.
        var shifts = _context.HousekeepingShifts.Where(s => s.Date >= today && s.Date < today.AddDays(7));
        if (isHousekeeper)
        {
            shifts = shifts.Where(s => s.UserId == CurrentUserId);
        }

        ViewBag.Shifts = (await shifts.ToListAsync())
            .OrderBy(s => s.Date)
            .ThenBy(s => ShiftNames.Order(s.ShiftName))
            .ThenBy(s => s.UserName)
            .ToList();
        ViewBag.IsHousekeeper = isHousekeeper;
        ViewBag.CurrentUserId = CurrentUserId;
        ViewBag.OpenTasks = await _context.HousekeepingTasks
            .Where(t => t.Status != HousekeepingTaskStatus.Done)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
        ViewBag.DoneToday = await _context.HousekeepingTasks
            .Where(t => t.Status == HousekeepingTaskStatus.Done && t.CompletedAt >= today)
            .OrderByDescending(t => t.CompletedAt)
            .ToListAsync();
        // Guests due to leave today (or late): their rooms will need cleaning soon.
        ViewBag.Departures = await _context.HotelBookings
            .DeparturesDue(today)
            .OrderBy(b => b.RoomName)
            .ToListAsync();
        ViewBag.Rooms = await _context.HotelRooms.OrderBy(r => r.Name).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id)
    {
        if (DenyUnless(UserRoles.HousekeepingTeam) is { } denied)
        {
            return denied;
        }

        // One UPDATE … WHERE Status = 'Pending': when two housekeepers press "รับงาน" at the same
        // moment, the database lets only the first one take the job.
        var userId = CurrentUserId;
        var userName = CurrentUserName;
        var claimed = await _context.HousekeepingTasks
            .Where(t => t.Id == id && t.Status == HousekeepingTaskStatus.Pending)
            .ExecuteUpdateAsync(t => t
                .SetProperty(x => x.Status, HousekeepingTaskStatus.InProgress)
                .SetProperty(x => x.AssignedToUserId, userId)
                .SetProperty(x => x.AssignedToName, userName));

        var task = await _context.HousekeepingTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (claimed == 0 || task == null)
        {
            TempData["ErrorMessage"] = task?.AssignedToName is { Length: > 0 } owner
                ? $"งานห้อง {task.RoomName} ถูกรับไปแล้วโดย {owner}"
                : "งานนี้ถูกรับไปแล้ว หรือทำเสร็จแล้ว";
        }
        else
        {
            Audit(AuditActions.CleaningStarted, AuditEntities.CleaningTask, task.Id, $"ห้อง {task.RoomName} · {task.Note}");
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"รับงานทำความสะอาดห้อง {task.RoomName} แล้ว";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        if (DenyUnless(UserRoles.HousekeepingTeam) is { } denied)
        {
            return denied;
        }

        var task = await _context.HousekeepingTasks.FindAsync(id);
        if (task == null || task.Status == HousekeepingTaskStatus.Done)
        {
            TempData["ErrorMessage"] = "ไม่พบงานนี้ หรือทำเสร็จไปแล้ว";
        }
        else if (CurrentRole == UserRoles.Housekeeper &&
                 task.AssignedToUserId != null && task.AssignedToUserId != CurrentUserId)
        {
            // A housekeeper cannot close a colleague's job; a manager can.
            TempData["ErrorMessage"] = $"งานห้อง {task.RoomName} เป็นของ {task.AssignedToName} — ให้ผู้รับงานกดเสร็จเอง";
        }
        else
        {
            task.Status = HousekeepingTaskStatus.Done;
            task.CompletedAt = DateTime.Now;
            var closedForOther = task.AssignedToUserId != null && task.AssignedToUserId != CurrentUserId;
            if (task.AssignedToUserId == null)
            {
                task.AssignedToUserId = CurrentUserId;
                task.AssignedToName = CurrentUserName;
            }

            Audit(AuditActions.CleaningDone, AuditEntities.CleaningTask, task.Id,
                $"ห้อง {task.RoomName}" + (closedForOther ? $" · ปิดงานแทน {task.AssignedToName}" : ""));
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"ทำความสะอาดห้อง {task.RoomName} เสร็จแล้ว — ห้องพร้อมให้บริการ";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Asks for a room to be cleaned outside of a check-out (e.g. a guest request at the front desk).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTask(int roomId, string? note, string? returnUrl)
    {
        if (DenyUnless(UserRoles.CleaningRequesters) is { } denied)
        {
            return denied;
        }

        var room = await _context.HotelRooms.FindAsync(roomId);
        note = NormalizeText(note);
        if (room == null)
        {
            TempData["ErrorMessage"] = "กรุณาเลือกห้องพัก";
        }
        else if (note.Length > 200)
        {
            TempData["ErrorMessage"] = "หมายเหตุต้องไม่เกิน 200 ตัวอักษร";
        }
        else if (await _context.HousekeepingTasks.AnyAsync(t => t.RoomId == roomId && t.Status != HousekeepingTaskStatus.Done))
        {
            TempData["ErrorMessage"] = $"ห้อง {room.Name} มีงานทำความสะอาดค้างอยู่แล้ว";
        }
        else
        {
            var task = new HousekeepingTask
            {
                RoomId = room.Id,
                RoomName = room.Name,
                Note = note.Length > 0 ? note : $"แจ้งโดย {CurrentUserName}",
                Status = HousekeepingTaskStatus.Pending,
                CreatedAt = DateTime.Now
            };
            _context.HousekeepingTasks.Add(task);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.SaveChangesAsync();
            Audit(AuditActions.CleaningRequested, AuditEntities.CleaningTask, task.Id, $"ห้อง {room.Name} · {task.Note}");
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = $"เพิ่มงานทำความสะอาดห้อง {room.Name} แล้ว";
        }

        return UserRoles.IsIn(CurrentRole, UserRoles.HousekeepingTeam)
            ? RedirectBack(returnUrl, nameof(Index))
            : RedirectBack(returnUrl, "Bookings", "Staff");
    }
}
