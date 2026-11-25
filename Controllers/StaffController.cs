using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Infrastructure;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

/// <summary>Front desk (Staff): manage bookings, check payments, check guests in and out.</summary>
public class StaffController : AppControllerBase
{
    public const string ViewToday = "today";
    public const string ViewInHouse = "inhouse";
    public const string ViewPayments = "payments";
    public const string ViewUpcoming = "upcoming";
    public const string ViewNoShow = "noshow";
    public const string ViewAll = "all";

    private const int MaxRows = 300;

    public StaffController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Bookings(string? view, string? q)
    {
        if (DenyUnless(UserRoles.BookingManagers) is { } denied)
        {
            return denied;
        }

        view = view is ViewInHouse or ViewPayments or ViewUpcoming or ViewNoShow or ViewAll ? view : ViewToday;
        var today = DateTime.Today;
        await CancelOverduePaymentChoicesAsync();
        var bookings = _context.HotelBookings.AsQueryable();

        var query = view switch
        {
            // Guests to check in (including late arrivals) and guests whose check-out day has come.
            ViewToday => bookings.Where(b =>
                (b.Status == BookingStatus.Confirmed && b.CheckIn <= today && b.CheckOut > today) ||
                (b.Status == BookingStatus.CheckedIn && b.CheckOut <= today)),
            ViewInHouse => bookings.Where(b => b.Status == BookingStatus.CheckedIn),
            ViewPayments => bookings.Where(b =>
                b.PaymentStatus == PaymentStatus.Pending ||
                (b.PaymentStatus == PaymentStatus.Unpaid && b.Status == BookingStatus.Confirmed && b.CheckOut > today)),
            ViewUpcoming => bookings.Where(b => b.Status == BookingStatus.Confirmed && b.CheckIn >= today),
            ViewNoShow => bookings.NoShows(today),
            _ => bookings
        };

        q = NormalizeText(q);
        if (q.Length > 0)
        {
            var keyword = q.ToLower();
            var id = int.TryParse(q.TrimStart('#'), out var parsedId) ? parsedId : -1;
            query = query.Where(b =>
                b.Id == id ||
                b.GuestName.ToLower().Contains(keyword) ||
                b.ContactEmail.ToLower().Contains(keyword) ||
                b.RoomName.ToLower().Contains(keyword));
        }

        // History views show the newest stays first; the working views show the nearest first.
        query = view is ViewAll or ViewNoShow
            ? query.OrderByDescending(b => b.CheckIn).ThenByDescending(b => b.Id)
            : query.OrderBy(b => b.CheckIn).ThenBy(b => b.Id);

        ViewBag.View = view;
        ViewBag.Query = q;
        ViewBag.MaxRows = MaxRows;
        ViewBag.ArrivalsToday = await bookings.ArrivalsDue(today).CountAsync();
        ViewBag.DeparturesToday = await bookings.DeparturesDue(today).CountAsync();
        ViewBag.InHouse = await bookings.CountAsync(b => b.Status == BookingStatus.CheckedIn);
        ViewBag.PendingPayments = await bookings.CountAsync(b => b.PaymentStatus == PaymentStatus.Pending);
        ViewBag.NoShows = await bookings.NoShows(today).CountAsync();

        // Rooms that housekeeping has not finished yet (shown as a warning before checking a guest in).
        ViewBag.RoomsBeingCleaned = (await _context.HousekeepingTasks
            .Where(t => t.Status != HousekeepingTaskStatus.Done)
            .Select(t => t.RoomId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        ViewBag.Rooms = await _context.HotelRooms.OrderBy(r => r.Name).ToListAsync();

        return View(await query.Take(MaxRows).ToListAsync());
    }

    /// <summary>
    /// ใบเสร็จรับเงิน of any paid booking, for the front desk and finance (e.g. a walk-in guest
    /// without an account, who cannot open it on the customer site).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Receipt(int id, string? returnUrl)
    {
        if (DenyUnless(UserRoles.PaymentCheckers) is { } denied)
        {
            return denied;
        }

        var (homeController, homeAction) = RolePortals.HomePageOf(CurrentRole);
        var backUrl = GetSafeReturnUrl(returnUrl) ?? Url.Action(homeAction, homeController) ?? "/";

        var booking = await _context.HotelBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking == null || !BookingReceipt.IsAvailable(booking))
        {
            TempData["ErrorMessage"] = booking == null
                ? $"ไม่พบการจอง #{id}"
                : $"การจอง #{id} ยังไม่ได้ชำระเงิน (หรือคืนเงินแล้ว) จึงยังออกใบเสร็จไม่ได้";
            return LocalRedirect(backUrl);
        }

        return View("Receipt", new ReceiptViewModel
        {
            Booking = booking,
            BackUrl = backUrl,
            BackLabel = "กลับ",
            ForStaff = true
        });
    }

    // ------------------------------------------------------------------ walk-in booking

    /// <summary>Booking form for a guest at the front desk (walk-in or phone), with or without an account.</summary>
    [HttpGet]
    public async Task<IActionResult> WalkIn(int? roomId)
    {
        if (DenyUnless(UserRoles.BookingManagers) is { } denied)
        {
            return denied;
        }

        var today = DateTime.Today;
        var model = new WalkInBookingModel
        {
            RoomId = roomId ?? 0,
            CheckIn = DateInput.ToValue(today),
            CheckOut = DateInput.ToValue(today.AddDays(1))
        };

        if (roomId is int id && await _context.HotelRooms.FirstOrDefaultAsync(r => r.Id == id && r.IsActive) is { } room &&
            BookingOptions.BedTypes.Contains(room.BedType))
        {
            model.BedType = room.BedType;
        }

        await PrepareWalkInViewAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WalkIn(WalkInBookingModel model)
    {
        if (DenyUnless(UserRoles.BookingManagers) is { } denied)
        {
            return denied;
        }

        model.GuestName = NormalizeText(model.GuestName);
        model.ContactEmail = NormalizeEmail(model.ContactEmail);
        model.PaymentReference = NormalizeText(model.PaymentReference);
        model.SpecialRequest = BookingRules.CleanSpecialRequest(model.SpecialRequest);
        if (model.SpecialRequest.Length > BookingOptions.MaxSpecialRequestLength)
        {
            ModelState.AddModelError(nameof(model.SpecialRequest), BookingRules.SpecialRequestTooLongMessage);
        }

        var today = DateTime.Today;

        // Room, prices and totals always come from the database, never from the form.
        var room = model.RoomId > 0
            ? await _context.HotelRooms.FirstOrDefaultAsync(r => r.Id == model.RoomId && r.IsActive)
            : null;
        if (room == null)
        {
            AddErrorIfNone(nameof(model.RoomId), "กรุณาเลือกห้องพักที่เปิดให้บริการ");
        }
        else
        {
            if (!room.HasSpa)
            {
                model.SpaSessions = 0;
            }

            if (model.Guests > room.Capacity)
            {
                AddErrorIfNone(nameof(model.Guests), $"ห้อง {room.Name} รองรับผู้เข้าพักได้สูงสุด {room.Capacity} คน");
            }
        }

        var datesValid = ValidateStayDates(model.CheckIn, model.CheckOut, out var checkIn, out var checkOut);

        if (!BookingOptions.BedTypes.Contains(model.BedType))
        {
            AddErrorIfNone(nameof(model.BedType), "กรุณาเลือกประเภทเตียงจากรายการ");
        }

        if (!BookingOptions.PaymentMethods.Contains(model.PaymentMethod))
        {
            AddErrorIfNone(nameof(model.PaymentMethod), "กรุณาเลือกวิธีชำระเงินจากรายการ");
        }
        else if (BookingOptions.IsCardPayment(model.PaymentMethod) && model.PaymentReference.Count(char.IsAsciiDigit) >= 12)
        {
            // Card numbers are never stored: only the approval code or the last 4 digits.
            AddErrorIfNone(nameof(model.PaymentReference), "ห้ามกรอกหมายเลขบัตรเต็ม — ใส่เฉพาะเลขอนุมัติหรือเลข 4 หลักท้าย");
        }

        if (model.CheckInNow)
        {
            if (!model.PaidNow)
            {
                AddErrorIfNone(nameof(model.CheckInNow), "เช็คอินทันทีได้เมื่อรับชำระเงินแล้วเท่านั้น");
            }
            else if (datesValid && checkIn != today)
            {
                AddErrorIfNone(nameof(model.CheckInNow), "เช็คอินทันทีได้เฉพาะการเข้าพักที่เริ่มวันนี้");
            }
        }

        if (room != null && datesValid && await IsRoomBookedAsync(room.Id, checkIn, checkOut))
        {
            ModelState.AddModelError(string.Empty, $"ห้อง {room.Name} มีผู้จองแล้วในช่วงวันที่เลือก กรุณาเลือกห้องหรือวันอื่น");
        }

        if (!ModelState.IsValid || room == null)
        {
            await PrepareWalkInViewAsync(model);
            return View(model);
        }

        // A member's e-mail links the booking to that account (and gives the member promotions).
        var member = await FindMemberAsync(model.ContactEmail);
        var request = new PriceRequest(checkIn, checkOut, model.Guests, model.Breakfast, model.ExtraBed, model.SpaSessions);
        var quote = PriceCalculator.Calculate(room, request, member, today);

        // Same protection as the online checkout: lock the room, check again, then save.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockRoomAsync(room.Id);
        string? conflict = null;
        if (await IsRoomBookedAsync(room.Id, checkIn, checkOut))
        {
            conflict = $"ห้อง {room.Name} เพิ่งมีผู้จองในช่วงวันที่เลือก กรุณาเลือกห้องหรือวันอื่น";
        }
        else if (model.CheckInNow && await _context.HotelBookings.AnyAsync(b => b.RoomId == room.Id && b.Status == BookingStatus.CheckedIn))
        {
            conflict = $"ห้อง {room.Name} ยังมีแขกเข้าพักอยู่ — เช็คเอาท์แขกคนก่อนก่อน";
        }

        if (conflict != null)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty, conflict);
            await PrepareWalkInViewAsync(model);
            return View(model);
        }

        var booking = BookingRules.NewBooking(room, request, quote, model.BedType, model.GuestName, model.ContactEmail, member?.Id,
            model.SpecialRequest);
        booking.PaymentMethod = model.PaymentMethod;
        booking.PaymentReference = model.PaymentReference;
        if (model.PaidNow)
        {
            booking.PaymentStatus = PaymentStatus.Paid;
            booking.PaidAt = DateTime.Now;
        }

        if (model.CheckInNow)
        {
            booking.Status = BookingStatus.CheckedIn;
            booking.CheckedInAt = DateTime.Now;
        }

        _context.HotelBookings.Add(booking);
        await _context.SaveChangesAsync();
        // The booking number exists only after the first save; the log line goes in the same transaction.
        Audit(AuditActions.WalkInBooking, AuditEntities.Booking, booking.Id,
            $"{AuditText.Booking(booking)} · {PaymentStatus.ThaiName(booking.PaymentStatus)} ({booking.PaymentMethod})" +
            (booking.Status == BookingStatus.CheckedIn ? " · เช็คอินทันที" : "") +
            (member != null ? $" · สมาชิก {member.Email}" : " · ไม่มีบัญชีสมาชิก"));
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var roomNotReady = booking.Status == BookingStatus.CheckedIn && await IsRoomBeingCleanedAsync(room.Id);
        TempData["SuccessMessage"] =
            $"จองห้อง {room.Name} ให้ {booking.GuestName} แล้ว — การจอง #{booking.Id} ยอด {DisplayText.Money(booking.Total)} " +
            $"({PaymentStatus.ThaiName(booking.PaymentStatus)}, {booking.PaymentMethod})" +
            (booking.Status == BookingStatus.CheckedIn ? " · เช็คอินเรียบร้อย" : "") +
            (roomNotReady ? " · ⚠ ห้องยังมีงานทำความสะอาดค้าง — แจ้งแม่บ้านให้เร่งทำ" : "") +
            (member != null ? $" · ผูกกับบัญชีสมาชิก {member.Name}" : "");
        return RedirectToAction(nameof(Bookings), new { view = ViewAll, q = booking.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    /// <summary>Live price and availability box of the walk-in form.</summary>
    [HttpGet]
    public async Task<IActionResult> WalkInQuote(int roomId, string? checkIn, string? checkOut, int guests,
        bool breakfast, bool extraBed, int spaSessions, string? email)
    {
        if (DenyUnless(UserRoles.BookingManagers) is { } denied)
        {
            return denied;
        }

        return PartialView("_WalkInQuote",
            await BuildWalkInQuoteAsync(roomId, checkIn, checkOut, guests, breakfast, extraBed, spaSessions, email));
    }

    private async Task<CyberUser?> FindMemberAsync(string? email)
    {
        email = NormalizeEmail(email);
        return email.Length == 0
            ? null
            : await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == email);
    }

    private async Task<WalkInQuoteModel> BuildWalkInQuoteAsync(int roomId, string? checkInText, string? checkOutText,
        int guests, bool breakfast, bool extraBed, int spaSessions, string? email)
    {
        var member = await FindMemberAsync(email);
        var result = new WalkInQuoteModel
        {
            Room = await _context.HotelRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId && r.IsActive),
            MemberName = member?.Name
        };
        if (result.Room == null)
        {
            return result;
        }

        var today = DateTime.Today;
        var hasDates = DateInput.TryParse(checkInText, out var from) & DateInput.TryParse(checkOutText, out var to);
        result.DatesValid = hasDates && from >= today && to > from && (to - from).Days <= BookingOptions.MaxNights;
        if (result.DatesValid)
        {
            result.RoomTaken = await IsRoomBookedAsync(result.Room.Id, from, to);
            // Only matters for a guest who moves in today (a later stay finds the room cleaned).
            result.RoomBeingCleaned = from == today && await IsRoomBeingCleanedAsync(result.Room.Id);
        }
        else
        {
            from = to = today;
        }

        var request = new PriceRequest(from, to, Math.Clamp(guests, 1, Math.Max(result.Room.Capacity, 1)), breakfast, extraBed, spaSessions);
        result.Quote = PriceCalculator.Calculate(result.Room, request, member, today);
        return result;
    }

    /// <summary>True while the room has a cleaning job that housekeeping has not finished.</summary>
    private Task<bool> IsRoomBeingCleanedAsync(int roomId)
    {
        return _context.HousekeepingTasks.AnyAsync(t => t.RoomId == roomId && t.Status != HousekeepingTaskStatus.Done);
    }

    /// <summary>
    /// Rooms (marked when taken on the chosen dates, or still waiting for housekeeping) and the price
    /// box for the walk-in page.
    /// </summary>
    private async Task PrepareWalkInViewAsync(WalkInBookingModel model)
    {
        var takenRoomIds = new HashSet<int>();
        if (DateInput.TryParse(model.CheckIn, out var from) && DateInput.TryParse(model.CheckOut, out var to) && to > from)
        {
            takenRoomIds = (await _context.HotelBookings.Overlapping(from, to).Select(b => b.RoomId).Distinct().ToListAsync()).ToHashSet();
        }

        ViewBag.Rooms = await _context.HotelRooms.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync();
        ViewBag.TakenRoomIds = takenRoomIds;
        // Same warning as the check-in button of the bookings page: checking a guest in "now" into one
        // of these rooms asks first (the page script), and the price box says so.
        ViewBag.RoomsBeingCleaned = (await _context.HousekeepingTasks
            .Where(t => t.Status != HousekeepingTaskStatus.Done)
            .Select(t => t.RoomId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        ViewBag.Quote = await BuildWalkInQuoteAsync(model.RoomId, model.CheckIn, model.CheckOut, model.Guests,
            model.Breakfast, model.ExtraBed, model.SpaSessions, model.ContactEmail);
    }

    // ------------------------------------------------------------------ payment checks (Staff + Finance)

    /// <summary>Marks a booking as paid: a checked QR transfer, or cash received at the desk.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment(int id, string? returnUrl)
    {
        if (DenyUnless(UserRoles.PaymentCheckers) is { } denied)
        {
            return denied;
        }

        // Locked while it is checked and saved: a booking that is being cancelled at this moment
        // (e.g. its time to choose a payment method ran out) cannot be marked as paid as well.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockBookingAsync(id);
        var booking = await _context.HotelBookings.FindAsync(id);
        if (booking == null)
        {
            TempData["ErrorMessage"] = "ไม่พบการจองนี้";
        }
        else if (!(booking.CanCheckTransfer ||
                   (booking.PaymentStatus == PaymentStatus.Unpaid && booking.Status != BookingStatus.Cancelled)))
        {
            // Only a transfer to check (also of a closed no-show, whose money is kept) or cash for a
            // booking that is going ahead can be marked as paid.
            TempData["ErrorMessage"] = $"การจอง #{id} ไม่อยู่ในสถานะที่ยืนยันการชำระเงินได้";
        }
        else
        {
            if (booking.PaymentMethod.Length == 0)
            {
                booking.PaymentMethod = BookingOptions.Cash; // paid at the front desk
            }

            var wasTransfer = booking.PaymentStatus == PaymentStatus.Pending;
            booking.PaymentStatus = PaymentStatus.Paid;
            booking.PaidAt = DateTime.Now;
            Audit(wasTransfer ? AuditActions.PaymentConfirmed : AuditActions.CashReceived, AuditEntities.Booking, booking.Id,
                $"{AuditText.Booking(booking)} · {booking.PaymentMethod}" +
                (booking.PaymentReference.Length > 0 ? $" อ้างอิง {booking.PaymentReference}" : ""));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = $"ยืนยันการชำระเงินการจอง #{id} ({booking.PaymentMethod}) แล้ว";
        }

        return RedirectBack(returnUrl, nameof(Bookings));
    }

    /// <summary>The transfer could not be found: the guest has to pay again.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectPayment(int id, string? returnUrl)
    {
        if (DenyUnless(UserRoles.PaymentCheckers) is { } denied)
        {
            return denied;
        }

        // Locked like ConfirmPayment: a confirm and a reject of the same transfer cannot both win.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockBookingAsync(id);
        var booking = await _context.HotelBookings.FindAsync(id);
        if (booking == null || !booking.CanCheckTransfer)
        {
            TempData["ErrorMessage"] = $"การจอง #{id} ไม่มีการแจ้งโอนที่รอตรวจสอบ";
        }
        else
        {
            Audit(AuditActions.PaymentRejected, AuditEntities.Booking, booking.Id,
                $"{AuditText.Booking(booking)} · แจ้งโอนอ้างอิง {booking.PaymentReference} — ไม่พบยอดโอน");
            booking.PaymentStatus = PaymentStatus.Unpaid;
            booking.PaymentReference = string.Empty;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = $"ปฏิเสธการแจ้งโอนของการจอง #{id} แล้ว — ลูกค้าต้องชำระใหม่";
        }

        return RedirectBack(returnUrl, nameof(Bookings));
    }

    // ------------------------------------------------------------------ stay

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(int id, string? returnUrl)
    {
        if (DenyUnless(UserRoles.BookingManagers) is { } denied)
        {
            return denied;
        }

        // Locked while it is checked and saved: the guest may be cancelling it online at this moment.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockBookingAsync(id);
        var booking = await _context.HotelBookings.FindAsync(id);
        if (booking == null)
        {
            TempData["ErrorMessage"] = "ไม่พบการจองนี้";
        }
        else if (booking.Status != BookingStatus.Confirmed)
        {
            TempData["ErrorMessage"] = $"การจอง #{id} อยู่ในสถานะ {BookingStatus.ThaiName(booking.Status)} เช็คอินไม่ได้";
        }
        else if (booking.PaymentStatus != PaymentStatus.Paid)
        {
            TempData["ErrorMessage"] = $"การจอง #{id} ยังไม่ได้ชำระเงิน — กด \"รับชำระ\" ก่อนเช็คอิน";
        }
        else if (!booking.CanCheckIn)
        {
            TempData["ErrorMessage"] = booking.CheckOut.Date <= DateTime.Today
                ? $"การจอง #{id} เลยวันเช็คเอาท์ ({DisplayText.Date(booking.CheckOut)}) แล้ว เช็คอินไม่ได้"
                : $"เช็คอินการจอง #{id} ได้ตั้งแต่วันที่ {DisplayText.Date(booking.CheckIn)} เท่านั้น";
        }
        else if (await _context.HotelBookings.AnyAsync(b =>
                     b.RoomId == booking.RoomId && b.Id != booking.Id && b.Status == BookingStatus.CheckedIn))
        {
            // The previous guest of this room has not been checked out yet.
            TempData["ErrorMessage"] = $"ห้อง {booking.RoomName} ยังมีแขกเข้าพักอยู่ — เช็คเอาท์แขกคนก่อนก่อน";
        }
        else
        {
            booking.Status = BookingStatus.CheckedIn;
            booking.CheckedInAt = DateTime.Now;
            Audit(AuditActions.CheckIn, AuditEntities.Booking, booking.Id, AuditText.Booking(booking));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = $"เช็คอิน {booking.GuestName} เข้าห้อง {booking.RoomName} แล้ว";
        }

        return RedirectBack(returnUrl, nameof(Bookings));
    }

    /// <summary>Checks the guest out and sends the room to housekeeping.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckOut(int id, string? returnUrl)
    {
        if (DenyUnless(UserRoles.BookingManagers) is { } denied)
        {
            return denied;
        }

        var booking = await _context.HotelBookings.FindAsync(id);
        if (booking == null || !booking.CanCheckOut)
        {
            TempData["ErrorMessage"] = $"การจอง #{id} ยังไม่ได้เช็คอิน จึงเช็คเอาท์ไม่ได้";
            return RedirectBack(returnUrl, nameof(Bookings));
        }

        booking.Status = BookingStatus.CheckedOut;
        booking.CheckedOutAt = DateTime.Now;

        // One open cleaning job per room: if there is already one (e.g. a guest request), add to it.
        var note = $"แขกเช็คเอาท์ (การจอง #{booking.Id}, {booking.Guests} คน{(booking.ExtraBed ? ", มีเตียงเสริม" : "")})";
        var openTask = await _context.HousekeepingTasks.FirstOrDefaultAsync(t =>
            t.RoomId == booking.RoomId && t.Status != HousekeepingTaskStatus.Done);
        if (openTask == null)
        {
            _context.HousekeepingTasks.Add(new HousekeepingTask
            {
                RoomId = booking.RoomId,
                RoomName = booking.RoomName,
                BookingId = booking.Id,
                Note = note,
                Status = HousekeepingTaskStatus.Pending,
                CreatedAt = DateTime.Now
            });
        }
        else
        {
            openTask.BookingId ??= booking.Id;
            var combined = $"{openTask.Note} · {note}";
            openTask.Note = combined.Length <= 200 ? combined : combined[..200];
        }

        Audit(AuditActions.CheckOut, AuditEntities.Booking, booking.Id, AuditText.Booking(booking) + " · ส่งห้องให้แม่บ้าน");
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"เช็คเอาท์ {booking.GuestName} แล้ว — ส่งห้อง {booking.RoomName} ให้แม่บ้านทำความสะอาด";
        return RedirectBack(returnUrl, nameof(Bookings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? returnUrl)
    {
        if (DenyUnless(UserRoles.BookingManagers) is { } denied)
        {
            return denied;
        }

        // Locked while it is checked and saved: a payment saved at the same moment (cash at the desk,
        // the guest's card) either comes first and is then refunded below, or waits for the cancel.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockBookingAsync(id);
        var booking = await _context.HotelBookings.FindAsync(id);
        if (booking == null || booking.Status != BookingStatus.Confirmed)
        {
            TempData["ErrorMessage"] = $"ยกเลิกการจอง #{id} ไม่ได้ (ยกเลิกได้เฉพาะการจองที่ยังไม่เช็คอิน)";
        }
        else if (booking.IsNoShow)
        {
            // The stay is over and the guest never came: the front desk closes the no-show.
            // No refund (the guest could cancel until the check-in day); see BookingRules.Cancel.
            BookingRules.Cancel(booking, CancelReasons.NoShow);
            Audit(AuditActions.NoShowClosed, AuditEntities.Booking, booking.Id,
                $"{AuditText.Booking(booking)} · ไม่คืนเงิน ({PaymentStatus.ThaiName(booking.PaymentStatus)})");
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = booking.PaymentStatus switch
            {
                PaymentStatus.Paid => $"ปิดรายการไม่มาเข้าพัก #{id} แล้ว — ไม่คืนเงิน (ยอด {DisplayText.Money(booking.Total)} เป็นรายได้ของโรงแรม)",
                PaymentStatus.Pending => $"ปิดรายการไม่มาเข้าพัก #{id} แล้ว — ไม่คืนเงิน · ยังมียอดโอนรอตรวจสอบ ให้ยืนยันหรือปฏิเสธยอดโอนตามปกติ",
                _ => $"ปิดรายการไม่มาเข้าพัก #{id} แล้ว"
            };
        }
        else
        {
            var needsRefund = BookingRules.Cancel(booking, CancelReasons.Staff);
            Audit(AuditActions.StaffCancel, AuditEntities.Booking, booking.Id,
                AuditText.Booking(booking) + (needsRefund ? " · ส่งเรื่องคืนเงินให้ฝ่ายการเงิน" : ""));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["SuccessMessage"] = needsRefund
                ? $"ยกเลิกการจอง #{id} แล้ว — ส่งเรื่องคืนเงินให้ฝ่ายการเงิน"
                : $"ยกเลิกการจอง #{id} แล้ว";
        }

        return RedirectBack(returnUrl, nameof(Bookings));
    }
}
