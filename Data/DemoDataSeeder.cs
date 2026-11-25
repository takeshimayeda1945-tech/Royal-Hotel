using Intouch_67173986_L003.Models;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Data;

/// <summary>
/// Fills the database with realistic sample data so every page has something to show:
/// more rooms, members, about six months of booking history (dashboard charts), guests in the
/// hotel today, arrivals, departures, transfers to check, refunds, cleaning jobs and shifts.
///
///   dotnet run -- --seed-demo
///
/// Dates are relative to the day it runs. Prices come from <see cref="PriceCalculator"/>, so every
/// booking is exactly what the site would have charged. Runs once: if the sample members already
/// exist, nothing is added. Existing rows are never changed or deleted.
/// </summary>
public static class DemoDataSeeder
{
    public const string MemberPassword = "demo1234";
    private const string MarkerEmail = "somchai.j@example.com";

    private record RoomSeed(string Name, string Type, decimal Price, int Capacity, string Area, string Image, string Tag,
        string Description, string Bed, bool Spa, bool Active = true);

    private static string Photo(string id) => $"https://images.unsplash.com/{id}?auto=format&fit=crop&w=900&q=80";

    private static readonly RoomSeed[] Rooms =
    {
        new("Royal Garden Standard", RoomTypes.Standard, 1900, 2, "Garden Wing", Photo("photo-1566665797739-1674de7a421a"), "Value", "ห้องมาตรฐานติดสวน เงียบสงบ เหมาะกับการพักผ่อนระยะสั้น", BookingOptions.DoubleBed, false),
        new("Royal Budget Single", RoomTypes.Standard, 1500, 1, "Garden Wing", Photo("photo-1595576508898-0ad5c879a061"), "Budget", "ห้องเดี่ยวราคาประหยัดสำหรับนักเดินทางคนเดียว", BookingOptions.SingleBed, false),
        new("Royal Twin City", RoomTypes.Standard, 2400, 2, "Downtown", Photo("photo-1590490360182-c33d57733427"), "City", "ห้องเตียงเดี่ยวสองเตียงใจกลางเมือง ใกล้รถไฟฟ้า", BookingOptions.SingleBed, false),
        new("Royal Deluxe Sea View", RoomTypes.Deluxe, 3800, 2, "Sea Harbor", Photo("photo-1611892440504-42a792e24d32"), "Sea View", "ดีลักซ์วิวทะเล ระเบียงส่วนตัว รับลมทะเลยามเย็น", BookingOptions.DoubleBed, true),
        new("Royal Deluxe Family", RoomTypes.Deluxe, 4500, 4, "Downtown", Photo("photo-1582719478250-c89cae4dc85b"), "Family", "ดีลักซ์สำหรับครอบครัว พื้นที่กว้าง รองรับ 4 คน", BookingOptions.DoubleBed, true),
        new("Royal Executive Suite", RoomTypes.Suite, 6500, 4, "Skyline", Photo("photo-1631049307264-da0ec9d70304"), "Executive", "สวีทชั้นบน มีห้องนั่งเล่นแยกและโต๊ะทำงาน", BookingOptions.DoubleBed, true),
        new("Royal Presidential Suite", RoomTypes.Suite, 12000, 6, "Skyline", Photo("photo-1618773928121-c32242e63f39"), "Signature", "สวีทใหญ่ที่สุดของโรงแรม วิวเมือง 180 องศา พร้อมบริการบัตเลอร์", BookingOptions.DoubleBed, true),
        new("Royal Heritage (ปิดปรับปรุง)", RoomTypes.Deluxe, 3500, 2, "Old Town", Photo("photo-1578683010236-d716f9a3f461"), "Renovating", "ห้องสไตล์โคโลเนียล ปิดปรับปรุงชั่วคราว", BookingOptions.DoubleBed, false, Active: false),
    };

    private record MemberSeed(string Name, string Email, string Phone, DateTime Birthday);

    private static readonly MemberSeed[] Members =
    {
        new("สมชาย ใจดี", MarkerEmail, "081-234-1001", new DateTime(1985, 11, 3)),
        new("สุดารัตน์ ศรีสุข", "sudarat.s@example.com", "089-555-1002", new DateTime(1992, 12, 19)),
        new("วิทยา พงษ์ไพร", "witthaya.p@example.com", "086-777-1003", new DateTime(1979, 3, 8)),
        new("กมลชนก แก้วมณี", "kamonchanok.k@example.com", "084-321-1004", new DateTime(1998, 7, 22)),
        new("ณัฐพล บุญมา", "nattapon.b@example.com", "092-468-1005", new DateTime(1990, 1, 30)),
        new("ปิยะนุช ทองดี", "piyanuch.t@example.com", "095-135-1006", new DateTime(2001, 5, 14)),
        new("อนุชา รุ่งเรือง", "anucha.r@example.com", "081-999-1007", new DateTime(1975, 9, 9)),
        new("พิมพ์ชนก วงศ์สวัสดิ์", "pimchanok.w@example.com", "087-246-1008", new DateTime(1996, 2, 27)),
        new("ธนกฤต ชัยมงคล", "thanakrit.c@example.com", "083-864-1009", new DateTime(1988, 10, 5)),
        new("John Carter", "john.carter@example.com", "+1-415-555-0110", new DateTime(1983, 6, 11)),
        new("Emily Chen", "emily.chen@example.com", "+65-8123-0111", new DateTime(1994, 11, 28)),
        new("Kenji Sato", "kenji.sato@example.com", "+81-90-1234-0112", new DateTime(1987, 4, 2)),
    };

    private static readonly string[] WalkInGuests =
    {
        "ประเสริฐ มีสุข", "Anna Müller", "วรวุฒิ แสงทอง", "Liam O'Brien", "นภาพร ใจงาม", "Carlos Rivera", "อรอุมา สายบุญ"
    };

    public static string Seed(ApplicationDbContext db, ILogger logger)
    {
        if (db.Users.Any(u => u.Email == MarkerEmail))
        {
            return "Sample data is already in the database — nothing was added.";
        }

        var random = new Random(2026); // same data every time it is generated
        var now = DateTime.Now;
        var today = DateTime.Today;
        using var transaction = db.Database.BeginTransaction();

        // ----- rooms, members, second housekeeper
        foreach (var r in Rooms.Where(r => !db.HotelRooms.Any(x => x.Name == r.Name)))
        {
            db.HotelRooms.Add(new HotelRoom
            {
                Name = r.Name, RoomType = r.Type, Price = r.Price, Capacity = r.Capacity, Area = r.Area, ImageUrl = r.Image,
                Tag = r.Tag, Description = r.Description, BedType = r.Bed, HasSpa = r.Spa, IsActive = r.Active
            });
        }

        var memberHash = PasswordSecurity.Hash(MemberPassword);
        foreach (var m in Members.Where(m => !db.Users.Any(u => u.Email == m.Email)))
        {
            db.Users.Add(new CyberUser { Name = m.Name, Email = m.Email, PhoneNumber = m.Phone, Birthday = m.Birthday, Password = memberHash, Role = UserRoles.Customer });
        }

        if (!db.Users.Any(u => u.Email == "maid2@royalhotel.com"))
        {
            db.Users.Add(new CyberUser
            {
                Name = "Housekeeper Somsri", Email = "maid2@royalhotel.com", PhoneNumber = "02-000-0001",
                Birthday = new DateTime(1993, 4, 18), Password = PasswordSecurity.Hash("maid123"), Role = UserRoles.Housekeeper
            });
        }

        db.SaveChanges();

        var rooms = db.HotelRooms.Where(r => r.IsActive).OrderBy(r => r.Id).ToList();
        var allRooms = db.HotelRooms.ToList();
        // Only the sample members get bookings (real accounts already in the database are left alone).
        var memberEmails = Members.Select(m => m.Email).ToList();
        var members = db.Users.Where(u => memberEmails.Contains(u.Email)).AsEnumerable()
            .OrderBy(u => memberEmails.IndexOf(u.Email)).ToList();
        var maids = db.Users.Where(u => u.Role == UserRoles.Housekeeper).OrderBy(u => u.Id).ToList();

        // Nights already taken (existing bookings are respected, never overlapped).
        var taken = db.HotelBookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .Select(b => new { b.RoomId, b.CheckIn, b.CheckOut })
            .AsEnumerable()
            .Select(b => (b.RoomId, b.CheckIn, b.CheckOut))
            .ToList();
        bool IsFree(int roomId, DateTime from, DateTime to) =>
            !taken.Any(x => x.RoomId == roomId && x.CheckIn < to && x.CheckOut > from);

        var bookings = new List<HotelBooking>();
        var tasks = new List<(HousekeepingTask Task, HotelBooking? Booking)>();

        HotelBooking Book(HotelRoom room, DateTime checkIn, DateTime checkOut, CyberUser? member, string? walkInName, DateTime createdAt)
        {
            var guests = random.Next(1, Math.Max(room.Capacity, 1) + 1);
            var request = new PriceRequest(checkIn, checkOut, guests,
                random.NextDouble() < 0.4, random.NextDouble() < 0.15, room.HasSpa && random.NextDouble() < 0.3 ? random.Next(1, 3) : 0);
            var quote = PriceCalculator.Calculate(room, request, member, createdAt.Date);
            var booking = BookingRules.NewBooking(room, request, quote, room.BedType,
                member?.Name ?? walkInName ?? "Walk-in guest", member?.Email ?? string.Empty, member?.Id);
            booking.CreatedAt = createdAt;
            taken.Add((room.Id, checkIn, checkOut));
            bookings.Add(booking);
            return booking;
        }

        // A fixed situation for today's demo, only when the room is free on those nights.
        HotelBooking? Scenario(string roomName, DateTime checkIn, DateTime checkOut, CyberUser? member, string? walkInName,
            DateTime createdAt, Action<HotelBooking> setup)
        {
            var room = allRooms.FirstOrDefault(r => r.Name == roomName && r.IsActive);
            if (room == null || !IsFree(room.Id, checkIn, checkOut))
            {
                return null;
            }

            var booking = Book(room, checkIn, checkOut, member, walkInName, createdAt);
            setup(booking);
            return booking;
        }

        CyberUser AnyMember() => members[random.Next(members.Count)];
        string AnyWalkIn() => WalkInGuests[random.Next(WalkInGuests.Length)];
        string AnyOnlineMethod() => random.Next(3) switch { 0 => BookingOptions.CreditCard, 1 => BookingOptions.DebitCard, _ => BookingOptions.QrCode };

        // Booked some days before the stay, never in the future.
        DateTime BookedBefore(DateTime checkIn, int maxDays)
        {
            var created = checkIn.AddDays(-random.Next(1, maxDays + 1)).AddHours(random.Next(8, 22)).AddMinutes(random.Next(60));
            return created < now
                ? created
                : now.AddDays(-random.Next(0, 10)).AddHours(-random.Next(1, 10)).AddMinutes(-random.Next(60));
        }

        void Pay(HotelBooking b, string method, DateTime paidAt)
        {
            b.PaymentMethod = method;
            b.PaymentStatus = PaymentStatus.Paid;
            b.PaidAt = paidAt;
            b.PaymentReference = method switch
            {
                BookingOptions.DebitCard or BookingOptions.CreditCard => $"**** {random.Next(1000, 10000)}",
                BookingOptions.QrCode => $"QR{paidAt:yyyyMMdd}{random.Next(10000, 99999)}",
                _ => string.Empty
            };
        }

        void CheckIn(HotelBooking b)
        {
            b.Status = BookingStatus.CheckedIn;
            b.CheckedInAt = b.CheckIn.AddHours(14).AddMinutes(random.Next(0, 180));
        }

        void CheckOut(HotelBooking b, DateTime at)
        {
            CheckIn(b);
            b.Status = BookingStatus.CheckedOut;
            b.CheckedOutAt = at;
        }

        void PendingTransfer(HotelBooking b, string reference)
        {
            b.PaymentMethod = BookingOptions.QrCode;
            b.PaymentStatus = PaymentStatus.Pending;
            b.PaymentReference = reference;
        }

        // ----- today's front-desk situations (fixed, so the demo always shows them)
        var t = today;
        Scenario("Royal Suite", t.AddDays(-2), t.AddDays(1), members[0], null, BookedBefore(t.AddDays(-2), 20),
            b => { Pay(b, BookingOptions.CreditCard, b.CreatedAt.AddMinutes(4)); CheckIn(b); });                  // in house
        Scenario("Royal Horizon", t.AddDays(-3), t, null, AnyWalkIn(), t.AddDays(-3).AddHours(13),
            b => { Pay(b, BookingOptions.Cash, b.CreatedAt); CheckIn(b); });                                         // leaves today
        var executive = Scenario("Royal Executive Suite", t.AddDays(-1), t.AddDays(3), members[9], null, BookedBefore(t.AddDays(-1), 30),
            b => { Pay(b, BookingOptions.QrCode, b.CreatedAt.AddHours(2)); CheckIn(b); });                         // in house
        var gardenOut = Scenario("Royal Garden Standard", t.AddDays(-2), t, members[3], null, BookedBefore(t.AddDays(-2), 10),
            b => { Pay(b, BookingOptions.DebitCard, b.CreatedAt.AddMinutes(3)); CheckOut(b, now.Date.AddHours(10).AddMinutes(20)); });
        Scenario("Royal Garden Standard", t, t.AddDays(1), null, AnyWalkIn(), now.AddHours(-3),
            b => b.PaymentMethod = BookingOptions.Cash);                                                             // arrives today, pays at the desk
        var seaViewOut = Scenario("Royal Deluxe Sea View", t.AddDays(-3), t, members[7], null, BookedBefore(t.AddDays(-3), 25),
            b => { Pay(b, BookingOptions.CreditCard, b.CreatedAt.AddMinutes(2)); CheckOut(b, now.Date.AddHours(9).AddMinutes(5)); });
        Scenario("Royal Deluxe Sea View", t, t.AddDays(2), members[1], null, BookedBefore(t, 12),
            b => Pay(b, BookingOptions.CreditCard, b.CreatedAt.AddMinutes(5)));                                     // arrives today, paid
        Scenario("Royal Budget Single", t.AddDays(-1), t.AddDays(2), members[5], null, BookedBefore(t.AddDays(-1), 8),
            b => Pay(b, BookingOptions.QrCode, b.CreatedAt.AddHours(1)));                                           // late arrival
        Scenario("Royal Twin City", t.AddDays(-4), t.AddDays(-2), members[6], null, BookedBefore(t.AddDays(-4), 15),
            b => b.PaymentMethod = BookingOptions.Cash);                                                             // no-show (was to pay at the desk)
        Scenario("Royal Deluxe Family", t.AddDays(5), t.AddDays(8), members[2], null, now.AddHours(-5),
            b => PendingTransfer(b, $"QR{t:yyyyMMdd}48213"));                                                        // transfer to check
        Scenario("Royal Presidential Suite", t.AddDays(10), t.AddDays(12), members[10], null, now.AddDays(-6),
            b => { Pay(b, BookingOptions.CreditCard, b.CreatedAt.AddMinutes(6)); BookingRules.Cancel(b, CancelReasons.Guest); }); // refund to pay back
        Scenario("Royal Garden Standard", t.AddDays(15), t.AddDays(17), members[11], null, now.AddDays(-2),
            b => { PendingTransfer(b, "QR-UNVERIFIED-77"); BookingRules.Cancel(b, CancelReasons.Guest); }); // refund of an unchecked transfer
        Scenario("Royal Suite", t.AddDays(20), t.AddDays(22), members[8], null, now.AddHours(-1),
            b => BookingRules.Cancel(b, CancelReasons.PaymentTimeout));                                              // left at the payment step: cancelled after 30 minutes

        // ----- six months of history and the coming two months, room by room
        var start = today.AddMonths(-6);
        var end = today.AddDays(60);
        foreach (var room in rooms)
        {
            var day = start.AddDays(random.Next(0, 6));
            while (day < end)
            {
                var checkIn = day;
                var checkOut = day.AddDays(random.Next(1, 5));
                day = checkOut.AddDays(random.Next(2, 9));
                if (!IsFree(room.Id, checkIn, checkOut) || (checkIn <= today && checkOut >= today))
                {
                    continue; // today's situations are the fixed ones above
                }

                var walkIn = checkIn <= today && random.NextDouble() < 0.15;
                var member = walkIn ? null : AnyMember();
                var created = walkIn ? checkIn.AddHours(random.Next(11, 20)) : BookedBefore(checkIn, random.NextDouble() < 0.3 ? 45 : 14);
                var b = Book(room, checkIn, checkOut, member, walkIn ? AnyWalkIn() : null, created);

                if (checkOut < today)
                {
                    if (random.NextDouble() < 0.10)
                    {
                        // Cancelled in advance: money paid online was refunded.
                        if (random.NextDouble() < 0.5)
                        {
                            Pay(b, AnyOnlineMethod(), b.CreatedAt.AddMinutes(10));
                        }

                        BookingRules.Cancel(b, CancelReasons.Guest);
                        if (b.PaymentStatus == PaymentStatus.RefundPending)
                        {
                            b.PaymentStatus = PaymentStatus.Refunded;
                        }

                        continue;
                    }

                    if (walkIn)
                    {
                        Pay(b, BookingOptions.Cash, b.CreatedAt);
                    }
                    else if (random.NextDouble() < 0.25)
                    {
                        Pay(b, BookingOptions.Cash, checkIn.AddHours(14));
                    }
                    else
                    {
                        var method = AnyOnlineMethod();
                        Pay(b, method, b.CreatedAt.AddMinutes(method == BookingOptions.QrCode ? 90 : 5));
                    }

                    CheckOut(b, checkOut.AddHours(10).AddMinutes(random.Next(0, 90)));
                    if (checkOut >= today.AddDays(-14))
                    {
                        var maid = maids[random.Next(maids.Count)];
                        tasks.Add((new HousekeepingTask
                        {
                            RoomId = room.Id, RoomName = room.Name, Status = HousekeepingTaskStatus.Done,
                            Note = $"แขกเช็คเอาท์ ({b.Guests} คน{(b.ExtraBed ? ", มีเตียงเสริม" : "")})",
                            CreatedAt = b.CheckedOutAt!.Value, CompletedAt = b.CheckedOutAt.Value.AddHours(2),
                            AssignedToUserId = maid.Id, AssignedToName = maid.Name
                        }, b));
                    }
                }
                else
                {
                    var roll = random.NextDouble();
                    if (roll < 0.40)
                    {
                        var method = AnyOnlineMethod();
                        Pay(b, method, b.CreatedAt.AddMinutes(method == BookingOptions.QrCode ? 90 : 5));
                    }
                    else if (roll < 0.55)
                    {
                        PendingTransfer(b, $"QR{b.CreatedAt:yyyyMMdd}{random.Next(10000, 99999)}");
                    }
                    else if (roll < 0.85)
                    {
                        b.PaymentMethod = BookingOptions.Cash;
                    }
                    else if (b.IsPaymentChoiceOverdue(now))
                    {
                        // Left at the payment step: cancelled automatically (BookingOptions.PaymentChoiceMinutes).
                        BookingRules.Cancel(b, CancelReasons.PaymentTimeout);
                    }
                    // otherwise: booked a few minutes ago, the guest is still choosing how to pay
                }
            }
        }

        db.HotelBookings.AddRange(bookings);
        db.SaveChanges();

        // ----- cleaning jobs from this morning's check-outs, one guest request, shifts for two weeks
        if (gardenOut != null)
        {
            tasks.Add((new HousekeepingTask
            {
                RoomId = gardenOut.RoomId, RoomName = gardenOut.RoomName, Status = HousekeepingTaskStatus.Pending,
                Note = $"แขกเช็คเอาท์ ({gardenOut.Guests} คน) — มีแขกใหม่เข้าวันนี้", CreatedAt = gardenOut.CheckedOutAt!.Value
            }, gardenOut));
        }

        if (seaViewOut != null)
        {
            tasks.Add((new HousekeepingTask
            {
                RoomId = seaViewOut.RoomId, RoomName = seaViewOut.RoomName, Status = HousekeepingTaskStatus.InProgress,
                Note = $"แขกเช็คเอาท์ ({seaViewOut.Guests} คน)", CreatedAt = seaViewOut.CheckedOutAt!.Value,
                AssignedToUserId = maids[0].Id, AssignedToName = maids[0].Name
            }, seaViewOut));
        }

        if (executive != null)
        {
            tasks.Add((new HousekeepingTask
            {
                RoomId = executive.RoomId, RoomName = executive.RoomName, Status = HousekeepingTaskStatus.Pending,
                Note = "แขกขอเปลี่ยนผ้าปูและผ้าเช็ดตัว", CreatedAt = now.AddHours(-1)
            }, null));
        }

        foreach (var (task, booking) in tasks)
        {
            task.BookingId = booking?.Id;
        }

        db.HousekeepingTasks.AddRange(tasks.Select(x => x.Task));

        var shiftCount = 0;
        for (var d = 0; d < 14; d++)
        {
            var date = today.AddDays(d);
            for (var m = 0; m < maids.Count; m++)
            {
                // Mostly mornings for the first housekeeper, afternoons for the second; one day off a week each.
                var shiftName = m % 2 == 0
                    ? (d % 3 == 2 ? ShiftNames.Afternoon : ShiftNames.Morning)
                    : (d % 4 == 3 ? ShiftNames.Night : ShiftNames.Afternoon);
                var maidId = maids[m].Id;
                if (d % 7 == (m % 2 == 0 ? 6 : 3) || db.HousekeepingShifts.Any(s => s.UserId == maidId && s.Date == date))
                {
                    continue;
                }

                db.HousekeepingShifts.Add(new HousekeepingShift
                {
                    UserId = maidId, UserName = maids[m].Name, Date = date, ShiftName = shiftName,
                    Note = m % 2 == 0 ? "ชั้น 1–3" : "ชั้น 4–6"
                });
                shiftCount++;
            }
        }

        db.SaveChanges();
        transaction.Commit();

        var summary = $"Added {Rooms.Length} rooms, {Members.Length + 1} users, {bookings.Count} bookings, " +
                      $"{tasks.Count} cleaning jobs and {shiftCount} shifts.";
        logger.LogInformation("Sample data: {Summary}", summary);
        return summary;
    }
}
