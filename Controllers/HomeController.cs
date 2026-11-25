using System.Diagnostics;
using System.Globalization;
using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Infrastructure;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Controllers;

public class HomeController : AppControllerBase
{
    private const string RoomNotFoundMessage = "ไม่พบห้องพักที่เลือก หรือห้องพักนี้ปิดให้บริการแล้ว";
    private const string BookingNotFoundMessage = "ไม่พบข้อมูลการจองนี้";
    private const string RoomTakenMessage = "ห้องนี้มีผู้จองแล้วในช่วงวันที่เลือก กรุณาเลือกวันอื่น";

    /// <summary>How many upcoming booked ranges the room page lists (the nearest ones).</summary>
    private const int BookedRangesShown = 12;

    /// <summary>Newest reviews on the room page, and the most shown by "all reviews".</summary>
    public const int ReviewsShown = 6;
    public const int MaxReviewsShown = 200;

    private readonly RolePortals _portals;
    private readonly LoginHandoff _handoff;

    public HomeController(ApplicationDbContext context, RolePortals portals, LoginHandoff handoff) : base(context)
    {
        _portals = portals;
        _handoff = handoff;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Loads the signed-in user; clears the session if the account no longer exists.</summary>
    private async Task<CyberUser?> GetCurrentUserAsync()
    {
        if (CurrentUserId is not int userId)
        {
            return null;
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            HttpContext.Session.Clear();
        }

        return user;
    }

    private IActionResult RedirectToLogin(string? returnUrl)
    {
        return RedirectToAction(nameof(Login), new { returnUrl });
    }

    private Task<HotelRoom?> FindActiveRoomAsync(int id)
    {
        return _context.HotelRooms.FirstOrDefaultAsync(r => r.Id == id && r.IsActive);
    }

    private Task<HotelBooking?> FindOwnBookingAsync(int id, int userId)
    {
        return _context.HotelBookings.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
    }

    // ------------------------------------------------------------------ home & rooms

    /// <summary>หน้า Home: featured rooms, services and promotions.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? destination, string? checkIn, string? checkOut, int? guests)
    {
        // Old search links pointed here; the room list now has its own page.
        if (destination != null || checkIn != null || checkOut != null || guests != null)
        {
            return RedirectToAction(nameof(Rooms), new { destination, checkIn, checkOut, guests });
        }

        ViewData["IsHomePage"] = true;
        var rooms = await _context.HotelRooms.Where(r => r.IsActive).OrderBy(r => r.Id).Take(3).ToListAsync();
        return View(rooms);
    }

    /// <summary>
    /// หน้ารวมห้องพัก: every open room, filtered by the search bar (dates, guests, area), then by the
    /// filter bar (room type, price per night) and sorted.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Rooms(string? destination, string? checkIn, string? checkOut, int? guests,
        string? type, int? minPrice, int? maxPrice, string? sort)
    {
        var isFiltered = destination != null || checkIn != null || checkOut != null || guests != null;
        var search = RoomSearchModel.Create(destination, checkIn, checkOut, guests, isFiltered);

        var query = _context.HotelRooms.Where(r => r.IsActive);
        if (isFiltered)
        {
            query = query.Where(r => r.Capacity >= search.Guests);

            if (search.Destination != null)
            {
                var keyword = search.Destination.ToLower();
                query = query.Where(r =>
                    r.Area.ToLower().Contains(keyword) ||
                    r.Name.ToLower().Contains(keyword) ||
                    r.RoomType.ToLower().Contains(keyword));
            }

            var bookedRoomIds = _context.HotelBookings
                .Overlapping(search.CheckInDate, search.CheckOutDate)
                .Select(b => b.RoomId);
            query = query.Where(r => !bookedRoomIds.Contains(r.Id));
        }

        var filter = RoomFilterModel.Create(type, minPrice, maxPrice, sort);
        ViewData["RoomSearch"] = search;
        ViewData["RoomFilter"] = filter; // the search bar keeps it when the dates change

        var found = await query.OrderBy(r => r.Id).ToListAsync();
        var inPriceRange = found.Where(filter.MatchesPrice).ToList();
        var ratings = await LoadRatingsAsync();
        return View(new RoomListViewModel
        {
            Rooms = filter.Apply(found, ratings),
            Ratings = ratings,
            Search = search,
            Filter = filter,
            SearchResultCount = found.Count,
            TypeCounts = RoomTypes.All.ToDictionary(t => t, t => inPriceRange.Count(r => r.RoomType == t)),
            AnyTypeCount = inPriceRange.Count,
            LowestPrice = found.Count > 0 ? found.Min(r => r.Price) : null,
            HighestPrice = found.Count > 0 ? found.Max(r => r.Price) : null
        });
    }

    /// <summary>Average stars and number of visible reviews per room (rooms without reviews are missing).</summary>
    private async Task<Dictionary<int, RatingSummary>> LoadRatingsAsync()
    {
        var rows = await _context.RoomReviews
            .Where(r => !r.IsHidden)
            .GroupBy(r => r.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Rating) })
            .ToListAsync();
        return rows.ToDictionary(r => r.RoomId, r => new RatingSummary((double)r.Sum / r.Count, r.Count));
    }

    [HttpGet]
    public async Task<IActionResult> RoomDetails(int id, string? checkIn, string? checkOut, int? guests, bool allReviews = false)
    {
        var room = await FindActiveRoomAsync(id);
        if (room == null)
        {
            TempData["ErrorMessage"] = RoomNotFoundMessage;
            return RedirectToAction(nameof(Rooms));
        }

        var search = RoomSearchModel.Create(checkIn: checkIn, checkOut: checkOut, guests: guests);
        ViewData["RoomSearch"] = search;

        // Nights that are already taken (dates only, no guest details), so the guest can pick free dates.
        var today = DateTime.Today;
        ViewBag.BookedRanges = await _context.HotelBookings
            .Where(b => b.RoomId == id)
            .Overlapping(today, DateTime.MaxValue.Date)
            .OrderBy(b => b.CheckIn)
            .Select(b => new DateRange(b.CheckIn, b.CheckOut))
            .Take(BookedRangesShown)
            .ToListAsync();
        ViewBag.BookedRangesShown = BookedRangesShown;
        ViewBag.SelectedDatesTaken = await IsRoomBookedAsync(id, search.CheckInDate, search.CheckOutDate);

        // Reviews: summary of every visible review, then the newest ones (all of them on request).
        var visible = _context.RoomReviews.AsNoTracking().Where(r => r.RoomId == id && !r.IsHidden);
        var stars = await visible.Select(r => r.Rating).ToListAsync();
        ViewBag.Reviews = new RoomReviewsViewModel
        {
            Summary = stars.Count > 0 ? new RatingSummary(stars.Average(), stars.Count) : null,
            Distribution = Enumerable.Range(ReviewRules.MinRating, ReviewRules.MaxRating).Select(s => stars.Count(r => r == s)).ToArray(),
            Latest = await visible
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                .Take(allReviews ? MaxReviewsShown : ReviewsShown)
                .ToListAsync()
        };
        return View(room);
    }

    [HttpGet]
    public IActionResult Promotions()
    {
        return View();
    }

    // ------------------------------------------------------------------ booking (step 1)

    [HttpGet]
    public async Task<IActionResult> Checkout(int roomId, string? checkIn, string? checkOut, int? guests)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return RedirectToLogin(CurrentRequestUrl);
        }

        var room = await FindActiveRoomAsync(roomId);
        if (room == null)
        {
            TempData["ErrorMessage"] = RoomNotFoundMessage;
            return RedirectToAction(nameof(Rooms));
        }

        var stay = RoomSearchModel.Create(checkIn: checkIn, checkOut: checkOut, guests: guests);
        var model = new BookingCheckoutModel
        {
            RoomId = room.Id,
            CheckIn = stay.CheckIn,
            CheckOut = stay.CheckOut,
            Guests = Math.Min(stay.Guests, Math.Max(room.Capacity, 1)),
            BedType = BookingOptions.BedTypes.Contains(room.BedType) ? room.BedType : BookingOptions.DoubleBed,
            GuestName = user.Name,
            ContactEmail = user.Email
        };

        if (await IsRoomBookedAsync(room.Id, stay.CheckInDate, stay.CheckOutDate))
        {
            ViewBag.AvailabilityWarning = "ห้องนี้มีผู้จองแล้วในช่วงวันที่เลือก กรุณาเปลี่ยนวันเช็คอิน/เช็คเอาท์";
        }

        PrepareCheckoutView(model, room, user);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(BookingCheckoutModel model)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return RedirectToLogin(Url.Action(nameof(Checkout), new
            {
                roomId = model.RoomId,
                checkIn = model.CheckIn,
                checkOut = model.CheckOut,
                guests = model.Guests
            }));
        }

        // Room, price and totals always come from the database, never from the posted form.
        var room = await FindActiveRoomAsync(model.RoomId);
        if (room == null)
        {
            TempData["ErrorMessage"] = RoomNotFoundMessage;
            return RedirectToAction(nameof(Rooms));
        }

        model.GuestName = NormalizeText(model.GuestName);
        model.ContactEmail = NormalizeText(model.ContactEmail);
        model.SpecialRequest = BookingRules.CleanSpecialRequest(model.SpecialRequest);
        if (!room.HasSpa)
        {
            model.SpaSessions = 0;
        }

        if (model.SpecialRequest.Length > BookingOptions.MaxSpecialRequestLength)
        {
            ModelState.AddModelError(nameof(model.SpecialRequest), BookingRules.SpecialRequestTooLongMessage);
        }

        var hasValidDates = ValidateStayDates(model.CheckIn, model.CheckOut, out var checkIn, out var checkOut);

        if (model.Guests > room.Capacity)
        {
            ModelState.AddModelError(nameof(model.Guests), $"ห้องนี้รองรับผู้เข้าพักได้สูงสุด {room.Capacity} คน");
        }

        if (!BookingOptions.BedTypes.Contains(model.BedType))
        {
            AddErrorIfNone(nameof(model.BedType), "กรุณาเลือกประเภทเตียงจากรายการ");
        }

        if (hasValidDates && await IsRoomBookedAsync(room.Id, checkIn, checkOut))
        {
            ModelState.AddModelError(string.Empty, RoomTakenMessage);
        }

        if (!ModelState.IsValid)
        {
            PrepareCheckoutView(model, room, user);
            return View(model);
        }

        var request = ToPriceRequest(model, checkIn, checkOut);
        var quote = PriceCalculator.Calculate(room, request, user, DateTime.Today);

        // Check again inside a transaction that locks the room, then save (see LockRoomAsync).
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockRoomAsync(room.Id);
        if (await IsRoomBookedAsync(room.Id, checkIn, checkOut))
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty, RoomTakenMessage);
            PrepareCheckoutView(model, room, user);
            return View(model);
        }

        var booking = BookingRules.NewBooking(room, request, quote, model.BedType, model.GuestName, model.ContactEmail, user.Id,
            model.SpecialRequest);
        _context.HotelBookings.Add(booking);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        // Post/Redirect/Get: refreshing can no longer create a duplicate booking.
        return RedirectToAction(nameof(Payment), new { id = booking.Id });
    }

    /// <summary>Live price summary for the booking form (returns the same partial the page renders).</summary>
    [HttpGet]
    public async Task<IActionResult> Quote(int roomId, string? checkIn, string? checkOut, int guests, bool breakfast, bool extraBed, int spaSessions)
    {
        var user = await GetCurrentUserAsync();
        var room = await FindActiveRoomAsync(roomId);
        if (user == null || room == null)
        {
            return NotFound();
        }

        if (!DateInput.TryParse(checkIn, out var from) || !DateInput.TryParse(checkOut, out var to) ||
            to <= from || (to - from).Days > BookingOptions.MaxNights)
        {
            from = to = DateTime.Today;
        }

        var request = new PriceRequest(from, to, Math.Clamp(guests, 1, Math.Max(room.Capacity, 1)), breakfast, extraBed, spaSessions);
        return PartialView("_PriceSummary", PriceCalculator.Calculate(room, request, user, DateTime.Today));
    }

    private static PriceRequest ToPriceRequest(BookingCheckoutModel model, DateTime checkIn, DateTime checkOut)
    {
        return new PriceRequest(checkIn, checkOut, model.Guests, model.Breakfast, model.ExtraBed, model.SpaSessions);
    }

    /// <summary>Fills the display-only parts of the booking page (room and price summary).</summary>
    private static void PrepareCheckoutView(BookingCheckoutModel model, HotelRoom room, CyberUser user)
    {
        model.Room = room;
        var hasDates = DateInput.TryParse(model.CheckIn, out var checkIn) &
                       DateInput.TryParse(model.CheckOut, out var checkOut);
        if (!hasDates || checkOut <= checkIn || (checkOut - checkIn).Days > BookingOptions.MaxNights)
        {
            checkIn = checkOut = DateTime.Today;
        }

        var guests = Math.Clamp(model.Guests, 1, Math.Max(room.Capacity, 1));
        model.Quote = PriceCalculator.Calculate(room,
            new PriceRequest(checkIn, checkOut, guests, model.Breakfast, model.ExtraBed, model.SpaSessions),
            user, DateTime.Today);
    }

    // ------------------------------------------------------------------ payment (step 2)

    /// <summary>Why the payment page cannot take a payment for this booking (shown after a redirect).</summary>
    private static string NotPayableMessage(HotelBooking booking)
    {
        return booking.CancelReason == CancelReasons.PaymentTimeout || booking.IsPaymentChoiceOverdue(DateTime.Now)
            ? $"หมดเวลาเลือกวิธีชำระเงินของการจอง #{booking.Id} (ต้องเลือกภายใน {BookingOptions.PaymentChoiceMinutes} นาทีหลังจอง) " +
              "ระบบจึงยกเลิกการจองและคืนห้องให้ผู้อื่นแล้ว — กรุณาจองใหม่อีกครั้ง"
            : $"การจอง #{booking.Id} ไม่อยู่ในสถานะที่ชำระเงินได้";
    }

    [HttpGet]
    public async Task<IActionResult> Payment(int id)
    {
        if (CurrentUserId is not int userId)
        {
            return RedirectToLogin(CurrentRequestUrl);
        }

        await CancelOverduePaymentChoicesAsync();
        var booking = await FindOwnBookingAsync(id, userId);
        if (booking == null)
        {
            TempData["ErrorMessage"] = BookingNotFoundMessage;
            return RedirectToAction(nameof(BookingHistory));
        }

        if (!booking.CanPayOnline)
        {
            return RedirectToAction(nameof(BookingSuccess), new { id });
        }

        var method = BookingOptions.PaymentMethods.Contains(booking.PaymentMethod) ? booking.PaymentMethod : BookingOptions.QrCode;
        return View(new PaymentModel { BookingId = booking.Id, PaymentMethod = method, Booking = booking });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Payment(PaymentModel model)
    {
        if (CurrentUserId is not int userId)
        {
            return RedirectToLogin(Url.Action(nameof(Payment), new { id = model.BookingId }));
        }

        await CancelOverduePaymentChoicesAsync();
        var booking = await FindOwnBookingAsync(model.BookingId, userId);
        if (booking == null)
        {
            TempData["ErrorMessage"] = BookingNotFoundMessage;
            return RedirectToAction(nameof(BookingHistory));
        }

        if (!booking.CanPayOnline)
        {
            TempData["ErrorMessage"] = NotPayableMessage(booking);
            return RedirectToAction(nameof(BookingSuccess), new { id = booking.Id });
        }

        model.Booking = booking;
        ModelState.Clear();

        if (!BookingOptions.PaymentMethods.Contains(model.PaymentMethod))
        {
            ModelState.AddModelError(nameof(model.PaymentMethod), "กรุณาเลือกวิธีชำระเงินจากรายการ");
            return View(model);
        }

        // Check the form first; the booking itself is only changed below, after locking it.
        string paymentStatus;
        var reference = string.Empty;
        DateTime? paidAt = null;
        string message;
        if (model.PaymentMethod == BookingOptions.Cash)
        {
            paymentStatus = PaymentStatus.Unpaid;
            message = $"บันทึกการจอง #{booking.Id} แล้ว — กรุณาชำระเงินสดที่เคาน์เตอร์ในวันเช็คอิน";
        }
        else if (model.PaymentMethod == BookingOptions.QrCode)
        {
            reference = NormalizeText(model.TransferReference);
            if (reference.Length is 0 or > 50)
            {
                ModelState.AddModelError(nameof(model.TransferReference), "กรุณากรอกเลขที่อ้างอิงการโอน (ไม่เกิน 50 ตัวอักษร)");
                return View(model);
            }

            paymentStatus = PaymentStatus.Pending;
            message = $"ได้รับแจ้งการโอนเงินสำหรับการจอง #{booking.Id} แล้ว — รอเจ้าหน้าที่ตรวจสอบ";
        }
        else
        {
            if (!ValidateCardDetails(model, out var last4))
            {
                return View(model);
            }

            // Demo card payment: approved immediately. Only the last 4 digits are kept.
            paymentStatus = PaymentStatus.Paid;
            reference = $"**** {last4}";
            paidAt = DateTime.Now;
            message = $"ชำระเงินการจอง #{booking.Id} ด้วย{model.PaymentMethod}สำเร็จ";
        }

        // While the guest filled in the form, the booking may have been cancelled (in another tab, or
        // because the time to choose a payment method ran out): lock it, read it again, then save.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await LockBookingAsync(booking.Id);
        await _context.Entry(booking).ReloadAsync();
        if (!booking.CanPayOnline)
        {
            await transaction.RollbackAsync();
            await CancelOverduePaymentChoicesAsync();
            TempData["ErrorMessage"] = NotPayableMessage(booking);
            return RedirectToAction(nameof(BookingSuccess), new { id = booking.Id });
        }

        booking.PaymentMethod = model.PaymentMethod;
        booking.PaymentStatus = paymentStatus;
        booking.PaymentReference = reference;
        if (paidAt != null)
        {
            booking.PaidAt = paidAt;
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(BookingSuccess), new { id = booking.Id });
    }

    private bool ValidateCardDetails(PaymentModel model, out string last4)
    {
        last4 = string.Empty;
        var name = NormalizeText(model.CardName);
        var number = NormalizeText(model.CardNumber);
        var expiry = NormalizeText(model.CardExpiry);
        var cvv = NormalizeText(model.CardCvv);

        if (name.Length is 0 or > 100)
        {
            ModelState.AddModelError(nameof(model.CardName), "กรุณากรอกชื่อบนบัตร");
        }

        var digits = new string(number.Where(char.IsAsciiDigit).ToArray());
        var hasOnlyAllowedCharacters = number.All(c => char.IsAsciiDigit(c) || c == ' ' || c == '-');
        if (!hasOnlyAllowedCharacters || digits.Length is < 12 or > 19)
        {
            ModelState.AddModelError(nameof(model.CardNumber), "หมายเลขบัตรต้องเป็นตัวเลข 12–19 หลัก");
        }
        else if (!PassesLuhnCheck(digits))
        {
            ModelState.AddModelError(nameof(model.CardNumber), "หมายเลขบัตรไม่ถูกต้อง กรุณาตรวจสอบตัวเลขอีกครั้ง");
        }
        else
        {
            last4 = digits[^4..];
        }

        if (!TryParseCardExpiry(expiry, out var lastValidDay))
        {
            ModelState.AddModelError(nameof(model.CardExpiry), "วันหมดอายุต้องอยู่ในรูปแบบ MM/YY");
        }
        else if (lastValidDay < DateTime.Today)
        {
            ModelState.AddModelError(nameof(model.CardExpiry), "บัตรนี้หมดอายุแล้ว");
        }

        if (cvv.Length is < 3 or > 4 || !cvv.All(char.IsAsciiDigit))
        {
            ModelState.AddModelError(nameof(model.CardCvv), "CVV ต้องเป็นตัวเลข 3–4 หลัก");
        }

        // Never echo card data back into the form.
        model.CardNumber = null;
        model.CardCvv = null;
        return ModelState.IsValid;
    }

    /// <summary>The check digit every real card number has (Luhn / mod 10), so typing mistakes are caught.</summary>
    private static bool PassesLuhnCheck(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }

    private static bool TryParseCardExpiry(string? value, out DateTime lastValidDay)
    {
        lastValidDay = default;

        var parts = NormalizeText(value).Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        var monthText = parts[0].Trim();
        var yearText = parts[1].Trim();
        if (monthText.Length is < 1 or > 2 || yearText.Length != 2 ||
            !int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            month is < 1 or > 12)
        {
            return false;
        }

        year += 2000;
        lastValidDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return true;
    }

    // ------------------------------------------------------------------ my bookings

    [HttpGet]
    public async Task<IActionResult> BookingSuccess(int id)
    {
        if (CurrentUserId is not int userId)
        {
            return RedirectToLogin(CurrentRequestUrl);
        }

        await CancelOverduePaymentChoicesAsync();
        var booking = await FindOwnBookingAsync(id, userId);
        if (booking == null)
        {
            TempData["ErrorMessage"] = BookingNotFoundMessage;
            return RedirectToAction(nameof(BookingHistory));
        }

        return View(booking);
    }

    /// <summary>ใบเสร็จรับเงิน of the guest's own paid booking (printable).</summary>
    [HttpGet]
    public async Task<IActionResult> Receipt(int id)
    {
        if (CurrentUserId is not int userId)
        {
            return RedirectToLogin(CurrentRequestUrl);
        }

        var booking = await FindOwnBookingAsync(id, userId);
        if (booking == null)
        {
            TempData["ErrorMessage"] = BookingNotFoundMessage;
            return RedirectToAction(nameof(BookingHistory));
        }

        if (!BookingReceipt.IsAvailable(booking))
        {
            TempData["ErrorMessage"] = $"ใบเสร็จของการจอง #{booking.Id} ออกได้เมื่อชำระเงินเรียบร้อยแล้ว";
            return RedirectToAction(nameof(BookingSuccess), new { id });
        }

        return View("Receipt", new ReceiptViewModel
        {
            Booking = booking,
            BackUrl = Url.Action(nameof(BookingSuccess), new { id }) ?? "/",
            BackLabel = "กลับไปหน้าการจอง"
        });
    }

    [HttpGet]
    public async Task<IActionResult> BookingHistory()
    {
        // Only the signed-in user's own bookings.
        if (CurrentUserId is not int userId)
        {
            return RedirectToLogin(CurrentRequestUrl);
        }

        await CancelOverduePaymentChoicesAsync();
        var bookings = await _context.HotelBookings
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .ToListAsync();

        // Stars already given per stay (stays without a review show a "review" button once checked out).
        ViewBag.ReviewStars = await _context.RoomReviews
            .Where(r => r.UserId == userId)
            .ToDictionaryAsync(r => r.BookingId, r => r.Rating);
        return View(bookings);
    }

    // ------------------------------------------------------------------ reviews

    /// <summary>Finds the guest's own stay that can be reviewed, or sets the message why not.</summary>
    private async Task<HotelBooking?> FindReviewableBookingAsync(int id, int userId)
    {
        var booking = await FindOwnBookingAsync(id, userId);
        if (booking == null)
        {
            TempData["ErrorMessage"] = BookingNotFoundMessage;
        }
        else if (!ReviewRules.CanReview(booking))
        {
            TempData["ErrorMessage"] = $"รีวิวการจอง #{booking.Id} ได้หลังเช็คเอาท์แล้วเท่านั้น";
        }

        return booking != null && ReviewRules.CanReview(booking) ? booking : null;
    }

    /// <summary>Review form of one finished stay (new review, or edit the one already written).</summary>
    [HttpGet]
    public async Task<IActionResult> Review(int id)
    {
        if (CurrentUserId is not int userId)
        {
            return RedirectToLogin(CurrentRequestUrl);
        }

        var booking = await FindReviewableBookingAsync(id, userId);
        if (booking == null)
        {
            return RedirectToAction(nameof(BookingHistory));
        }

        var existing = await _context.RoomReviews.AsNoTracking().FirstOrDefaultAsync(r => r.BookingId == id);
        return View(new ReviewPageModel
        {
            Booking = booking,
            Existing = existing,
            Form = new ReviewFormModel { BookingId = id, Rating = existing?.Rating ?? 0, Comment = existing?.Comment }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(ReviewFormModel form)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return RedirectToLogin(Url.Action(nameof(Review), new { id = form.BookingId }));
        }

        var booking = await FindReviewableBookingAsync(form.BookingId, user.Id);
        if (booking == null)
        {
            return RedirectToAction(nameof(BookingHistory));
        }

        form.Comment = TextInput.CleanMultiline(form.Comment);
        if (form.Comment.Length > ReviewRules.MaxCommentLength)
        {
            ModelState.AddModelError(nameof(form.Comment), $"ความคิดเห็นต้องไม่เกิน {ReviewRules.MaxCommentLength} ตัวอักษร");
        }

        var review = await _context.RoomReviews.FirstOrDefaultAsync(r => r.BookingId == booking.Id);
        if (!ModelState.IsValid)
        {
            return View(new ReviewPageModel { Booking = booking, Existing = review, Form = form });
        }

        var isNew = review == null;
        if (review == null)
        {
            review = new RoomReview
            {
                BookingId = booking.Id,
                RoomId = booking.RoomId,
                UserId = user.Id,
                ReviewerName = ReviewRules.ShortName(user.Name),
                CreatedAt = DateTime.Now
            };
            _context.RoomReviews.Add(review);
        }
        else
        {
            review.UpdatedAt = DateTime.Now;
        }

        review.Rating = form.Rating;
        review.Comment = form.Comment;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException) when (isNew)
        {
            // The form was sent twice at once: the unique index kept the first review. Anything else is a real error.
            if (!await _context.RoomReviews.AsNoTracking().AnyAsync(r => r.BookingId == booking.Id))
            {
                throw;
            }
        }

        TempData["SuccessMessage"] = isNew
            ? $"ขอบคุณสำหรับรีวิวห้อง {booking.RoomName} ({form.Rating} ดาว)"
            : $"แก้ไขรีวิวห้อง {booking.RoomName} แล้ว";
        return RedirectToAction(nameof(BookingHistory));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelBooking(int id)
    {
        if (CurrentUserId is not int userId)
        {
            return RedirectToLogin(Url.Action(nameof(BookingHistory)));
        }

        // Users can only cancel their own bookings.
        var booking = await FindOwnBookingAsync(id, userId);
        if (booking == null)
        {
            TempData["ErrorMessage"] = BookingNotFoundMessage;
        }
        else if (booking.Status == BookingStatus.Cancelled)
        {
            TempData["ErrorMessage"] = $"การจอง #{booking.Id} ถูกยกเลิกไปแล้ว";
        }
        else if (!booking.CanCancel)
        {
            TempData["ErrorMessage"] = $"ไม่สามารถยกเลิกการจอง #{booking.Id} ได้ เพราะเลยวันเช็คอินหรือเข้าพักแล้ว";
        }
        else
        {
            var needsRefund = BookingRules.Cancel(booking, CancelReasons.Guest);
            Audit(AuditActions.GuestCancel, AuditEntities.Booking, booking.Id,
                AuditText.Booking(booking) + (needsRefund ? " · ส่งเรื่องคืนเงินให้ฝ่ายการเงิน" : ""));
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = needsRefund
                ? $"ยกเลิกการจอง #{booking.Id} เรียบร้อยแล้ว — ฝ่ายการเงินจะคืนเงินให้ภายหลัง"
                : $"ยกเลิกการจอง #{booking.Id} เรียบร้อยแล้ว";
        }

        return RedirectToAction(nameof(BookingHistory));
    }

    // ------------------------------------------------------------------ pages

    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>Unexpected error (production only; Development shows the detailed error page).</summary>
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    /// <summary>Friendly page for 404 / 403 / 429 … (Program.cs: UseStatusCodePagesWithReExecute).</summary>
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult HttpError(int code)
    {
        if (code is < 400 or > 599)
        {
            code = StatusCodes.Status404NotFound;
        }

        Response.StatusCode = code;
        return View(code);
    }

    // ------------------------------------------------------------------ account

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);
        if (IsSignedIn)
        {
            return returnUrl != null ? LocalRedirect(returnUrl) : RedirectToRoleHome(CurrentRole);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.Email = NormalizeEmail(model.Email);
        var password = NormalizeText(model.Password);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email);
        var check = PasswordCheck.Failed;
        if (user == null)
        {
            PasswordSecurity.SimulateCheck(password);
        }
        else
        {
            check = PasswordSecurity.Verify(user.Password, password);
        }

        if (user == null || check == PasswordCheck.Failed)
        {
            ModelState.AddModelError(string.Empty, "อีเมลหรือรหัสผ่านไม่ถูกต้อง");
            return View(model);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.Password = PasswordSecurity.Hash(password);
            await _context.SaveChangesAsync();
        }

        HttpContext.Session.Clear();

        // Every role works on its own port: an account of another role is moved there, signed in
        // (Infrastructure/LoginHandoff.cs). The return URL belongs to this port, so it is dropped.
        var portalRole = _portals.RoleOf(HttpContext);
        if (portalRole != null && user.Role != portalRole && _portals.HasPortal(user.Role))
        {
            return Redirect(_handoff.HandoffUrl(user.Id, user.Role, HttpContext));
        }

        SetSessionUser(user);
        TempData["SuccessMessage"] = $"ยินดีต้อนรับ {user.Name}";

        return returnUrl != null ? LocalRedirect(returnUrl) : RedirectToRoleHome(user.Role);
    }

    /// <summary>
    /// Second half of a log-in on another role's port: redeems the one-time ticket made by
    /// <see cref="LoginHandoff"/> and signs the user in on this port.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Handoff(string? ticket)
    {
        var portalRole = _portals.RoleOf(HttpContext);
        var user = portalRole != null && _handoff.TryRedeem(ticket, portalRole, HttpContext, out var userId)
            ? await _context.Users.FindAsync(userId)
            : null;

        // Expired, used, meant for another port or another browser, or the role changed in the
        // seconds between the two steps. Whoever is signed in on this port stays signed in, so a
        // bad link cannot be used to log someone out.
        if (user == null || user.Role != portalRole)
        {
            TempData["ErrorMessage"] = "ลิงก์เข้าสู่ระบบนี้ใช้ไม่ได้ (หมดอายุ ถูกใช้ไปแล้ว หรือไม่ได้มาจากเบราว์เซอร์นี้) กรุณาเข้าสู่ระบบอีกครั้ง";
            return IsSignedIn ? RedirectToRoleHome(CurrentRole) : RedirectToAction(nameof(Login));
        }

        HttpContext.Session.Clear();
        SetSessionUser(user);
        TempData["SuccessMessage"] = $"ยินดีต้อนรับ {user.Name} (พอร์ต {HttpContext.Connection.LocalPort} · {UserRoles.ThaiName(user.Role)})";

        // Redirect once more so the ticket does not stay in the address bar or the history.
        return RedirectToRoleHome(user.Role);
    }

    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);
        if (IsSignedIn)
        {
            return returnUrl != null ? LocalRedirect(returnUrl) : RedirectToRoleHome(CurrentRole);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);
        ViewData["ReturnUrl"] = returnUrl;

        model.Name = NormalizeText(model.Name);
        model.Email = NormalizeEmail(model.Email);
        model.PhoneNumber = NormalizeText(model.PhoneNumber);
        model.Password = NormalizeText(model.Password);
        CheckNewPasswordLength(model.Password, nameof(model.Password));

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Emails are stored in lower case; compare case-insensitively for older mixed-case rows.
        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == model.Email))
        {
            ModelState.AddModelError(nameof(RegisterViewModel.Email), "อีเมลนี้ลงทะเบียนไว้แล้ว");
            return View(model);
        }

        var user = new CyberUser
        {
            Name = model.Name,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            Birthday = model.Birthday!.Value.Date,
            Password = PasswordSecurity.Hash(model.Password),
            Role = UserRoles.Customer
        };

        _context.Users.Add(user);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Someone registered the same e-mail a moment ago: the unique index refused the copy.
            if (!await EmailTakenAsync(model.Email))
            {
                throw;
            }

            ModelState.AddModelError(nameof(RegisterViewModel.Email), "อีเมลนี้ลงทะเบียนไว้แล้ว");
            return View(model);
        }

        TempData["SuccessMessage"] = "สมัครสมาชิกเรียบร้อย กรุณาเข้าสู่ระบบ";
        return RedirectToAction(nameof(Login), new { returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        TempData.Clear();
        TempData["SuccessMessage"] = "ออกจากระบบเรียบร้อยแล้ว";
        return RedirectToAction(nameof(Login));
    }

    // ------------------------------------------------------------------ legacy URLs
    // User management moved to SuperAdminController; keep the old /Home/... links working.

    [HttpGet]
    public IActionResult AddUser()
    {
        return RedirectToAction(nameof(AddUser), "SuperAdmin");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddUser(CyberUser model)
    {
        return RedirectToAction(nameof(AddUser), "SuperAdmin");
    }

    [HttpGet]
    public IActionResult UserList(int? editId)
    {
        return RedirectToAction(nameof(UserList), "SuperAdmin", new { editId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateUser(CyberUser model)
    {
        return RedirectToAction(nameof(UserList), "SuperAdmin");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteUser(int id)
    {
        return RedirectToAction(nameof(UserList), "SuperAdmin");
    }
}
