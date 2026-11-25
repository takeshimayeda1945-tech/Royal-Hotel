using Intouch_67173986_L003.Models;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Data;

/// <summary>
/// A large amount of extra sample data, on top of <see cref="DemoDataSeeder"/>:
/// 12 more rooms, ~75 more members, a few more staff, and bookings that fill the free nights of
/// every open room from 12 months ago to 3 months ahead (a busy hotel: guests in the hotel
/// today, arrivals, departures, history for the yearly charts).
///
///   dotnet run -- --seed-more
///
/// Rooms and users are added only when missing (by name / e-mail). Bookings only go into nights
/// that are still free, so running it again adds fewer and fewer bookings and never overlaps.
/// Prices come from <see cref="PriceCalculator"/>. Existing rows are never changed or deleted.
/// </summary>
public static class BulkDataSeeder
{
    public const string MemberPassword = DemoDataSeeder.MemberPassword;

    private static string Photo(string id) => $"https://images.unsplash.com/{id}?auto=format&fit=crop&w=900&q=80";

    private static readonly HotelRoom[] Rooms =
    {
        new() { Name = "Royal Orchid Standard", RoomType = RoomTypes.Standard, Price = 2100, Capacity = 2, Area = "Garden Wing", ImageUrl = Photo("photo-1564078516393-cf04bd966897"), Tag = "Value", Description = "ห้องมาตรฐานโทนกล้วยไม้ มองเห็นสวนกลางโรงแรม", BedType = BookingOptions.DoubleBed },
        new() { Name = "Royal Lotus Twin", RoomType = RoomTypes.Standard, Price = 2300, Capacity = 2, Area = "Downtown", ImageUrl = Photo("photo-1560185007-cde436f6a4d0"), Tag = "Twin", Description = "สองเตียงเดี่ยว เหมาะกับเพื่อนร่วมทริปหรือเดินทางธุรกิจ", BedType = BookingOptions.SingleBed },
        new() { Name = "Royal Jasmine Single", RoomType = RoomTypes.Standard, Price = 1600, Capacity = 1, Area = "Garden Wing", ImageUrl = Photo("photo-1560448204-e02f11c3d0e2"), Tag = "Solo", Description = "ห้องเดี่ยวกะทัดรัด ครบทุกสิ่งที่จำเป็น", BedType = BookingOptions.SingleBed },
        new() { Name = "Royal Business Studio", RoomType = RoomTypes.Standard, Price = 2600, Capacity = 2, Area = "Downtown", ImageUrl = Photo("photo-1505691938895-1758d7feb511"), Tag = "Business", Description = "สตูดิโอพร้อมโต๊ะทำงานกว้างและอินเทอร์เน็ตความเร็วสูง", BedType = BookingOptions.SingleBed },
        new() { Name = "Royal Riverside Deluxe", RoomType = RoomTypes.Deluxe, Price = 3600, Capacity = 2, Area = "Riverside", ImageUrl = Photo("photo-1512918728675-ed5a9ecdebfd"), Tag = "River View", Description = "ดีลักซ์ริมแม่น้ำ ชมพระอาทิตย์ตกจากระเบียง", BedType = BookingOptions.DoubleBed, HasSpa = true },
        new() { Name = "Royal Panorama Deluxe", RoomType = RoomTypes.Deluxe, Price = 4100, Capacity = 3, Area = "Skyline", ImageUrl = Photo("photo-1571896349842-33c89424de2d"), Tag = "Panorama", Description = "ดีลักซ์ชั้นสูง หน้าต่างกระจกรอบด้าน", BedType = BookingOptions.DoubleBed, HasSpa = true },
        new() { Name = "Royal Coral Deluxe", RoomType = RoomTypes.Deluxe, Price = 3900, Capacity = 2, Area = "Sea Harbor", ImageUrl = Photo("photo-1542314831-068cd1dbfeeb"), Tag = "Sea Breeze", Description = "ดีลักซ์โทนทะเล ใกล้ชายหาดส่วนตัว", BedType = BookingOptions.DoubleBed, HasSpa = true },
        new() { Name = "Royal Pool Access", RoomType = RoomTypes.Deluxe, Price = 4800, Capacity = 2, Area = "Garden Wing", ImageUrl = Photo("photo-1551882547-ff40c63fe5fa"), Tag = "Pool Access", Description = "ลงสระว่ายน้ำได้จากหน้าห้อง", BedType = BookingOptions.DoubleBed, HasSpa = true },
        new() { Name = "Royal Family Connecting", RoomType = RoomTypes.Deluxe, Price = 5200, Capacity = 5, Area = "Downtown", ImageUrl = Photo("photo-1520250497591-112f2f40a3f4"), Tag = "Family", Description = "สองห้องเชื่อมกัน สำหรับครอบครัวใหญ่", BedType = BookingOptions.DoubleBed, HasSpa = true },
        new() { Name = "Royal Honeymoon Suite", RoomType = RoomTypes.Suite, Price = 7800, Capacity = 2, Area = "Sea Harbor", ImageUrl = Photo("photo-1445019980597-93fa8acb246c"), Tag = "Honeymoon", Description = "สวีทสำหรับคู่รัก อ่างอาบน้ำวิวทะเล", BedType = BookingOptions.DoubleBed, HasSpa = true },
        new() { Name = "Royal Garden Villa", RoomType = RoomTypes.Suite, Price = 9500, Capacity = 4, Area = "Garden Wing", ImageUrl = Photo("photo-1455587734955-081b22074882"), Tag = "Villa", Description = "วิลล่าส่วนตัวกลางสวน พร้อมสระว่ายน้ำขนาดเล็ก", BedType = BookingOptions.DoubleBed, HasSpa = true },
        new() { Name = "Royal Penthouse Suite", RoomType = RoomTypes.Suite, Price = 15000, Capacity = 6, Area = "Skyline", ImageUrl = Photo("photo-1561501900-3701fa6a0864"), Tag = "Penthouse", Description = "เพนต์เฮาส์ชั้นบนสุด ดาดฟ้าส่วนตัว", BedType = BookingOptions.DoubleBed, HasSpa = true },
    };

    private static readonly (string Thai, string Latin)[] FirstNames =
    {
        ("กิตติ", "kitti"), ("ชนิดา", "chanida"), ("ธีรวัฒน์", "theerawat"), ("นันทนา", "nantana"), ("ปกรณ์", "pakorn"),
        ("พรทิพย์", "pornthip"), ("ภาณุ", "phanu"), ("มยุรี", "mayuree"), ("รัฐพล", "rattaphon"), ("ลลิตา", "lalita"),
        ("วีระ", "weera"), ("ศิริพร", "siriporn"), ("สุรเชษฐ์", "surachet"), ("อรวรรณ", "orawan"), ("เอกชัย", "ekkachai"),
        ("จิราพร", "jiraporn"), ("ชยพล", "chayaphon"), ("ดวงใจ", "duangjai"), ("ทศพล", "thotsaphon"), ("เบญจมาศ", "benjamas"),
    };

    private static readonly (string Thai, string Latin)[] LastNames =
    {
        ("สุขสวัสดิ์", "suksawat"), ("ศรีวงศ์", "sriwong"), ("ทองคำ", "thongkham"), ("บุญเรือง", "boonruang"), ("จันทร์เพ็ญ", "janpen"),
        ("รัตนพงศ์", "rattanapong"), ("แสงอรุณ", "saengarun"), ("พูลผล", "poonphon"), ("วัฒนกุล", "wattanakul"), ("มณีรัตน์", "maneerat"),
        ("เจริญชัย", "charoenchai"), ("ประเสริฐศรี", "prasertsri"), ("ศักดิ์ดี", "sakdee"), ("อินทร์แก้ว", "inkaew"), ("ชัยวัฒน์", "chaiwat"),
    };

    private static readonly string[] InternationalNames =
    {
        "Oliver Smith", "Sophia Müller", "Lucas Martin", "Mia Rossi", "Noah Johnson", "Hana Kim", "Arjun Patel", "Chloe Dubois",
        "Mateo García", "Yuki Tanaka", "Ethan Brown", "Ava Wilson", "Lin Wei", "Isabella Costa", "Jack Taylor",
    };

    private static readonly (string Name, string Email, string Password, string Role)[] Staff =
    {
        ("Front Desk Night", "staff2@royalhotel.com", "staff123", UserRoles.Staff),
        ("Front Desk Weekend", "staff3@royalhotel.com", "staff123", UserRoles.Staff),
        ("Housekeeper Wanee", "maid3@royalhotel.com", "maid123", UserRoles.Housekeeper),
        ("Finance Assistant", "finance2@royalhotel.com", "finance123", UserRoles.Finance),
    };

    private static readonly string[] WalkInGuests =
    {
        "สมศักดิ์ เดินทาง", "Peter Novak", "อัญชลี พักผ่อน", "Marco Bianchi", "ไพโรจน์ แวะพัก", "Sara Lindqvist", "บุญชัย ผ่านมา", "Tom Becker"
    };

    public static string Seed(ApplicationDbContext db, ILogger logger)
    {
        var random = new Random(4021 + db.HotelBookings.Count()); // a different mix on each run
        var now = DateTime.Now;
        var today = DateTime.Today;
        using var transaction = db.Database.BeginTransaction();

        // ----- rooms, members, staff (only what is missing)
        var roomNames = db.HotelRooms.Select(r => r.Name).ToHashSet();
        var newRooms = Rooms.Where(r => !roomNames.Contains(r.Name)).ToList();
        db.HotelRooms.AddRange(newRooms);

        var emails = db.Users.Select(u => u.Email.ToLower()).ToHashSet();
        var memberHash = PasswordSecurity.Hash(MemberPassword);
        var newUsers = new List<CyberUser>();
        void AddUser(string name, string email, string phone, DateTime birthday, string password, string role)
        {
            if (emails.Add(email))
            {
                newUsers.Add(new CyberUser { Name = name, Email = email, PhoneNumber = phone, Birthday = birthday, Password = password, Role = role });
            }
        }

        var nameRandom = new Random(77); // the same people every time
        for (var i = 0; i < 60; i++)
        {
            var first = FirstNames[(i * 7) % FirstNames.Length];
            var last = LastNames[(i * 11 + i / FirstNames.Length) % LastNames.Length];
            AddUser($"{first.Thai} {last.Thai}", $"{first.Latin}.{last.Latin}@example.com",
                $"08{nameRandom.Next(1, 10)}-{nameRandom.Next(100, 1000)}-{nameRandom.Next(1000, 10000)}",
                new DateTime(nameRandom.Next(1960, 2005), nameRandom.Next(1, 13), nameRandom.Next(1, 29)), memberHash, UserRoles.Customer);
        }

        foreach (var name in InternationalNames)
        {
            AddUser(name, name.ToLowerInvariant().Replace(' ', '.').Replace("ü", "u").Replace("í", "i") + "@example.com",
                $"+{nameRandom.Next(1, 90)}-{nameRandom.Next(100, 1000)}-{nameRandom.Next(1000, 10000)}",
                new DateTime(nameRandom.Next(1960, 2005), nameRandom.Next(1, 13), nameRandom.Next(1, 29)), memberHash, UserRoles.Customer);
        }

        foreach (var s in Staff)
        {
            AddUser(s.Name, s.Email, "02-000-0100", new DateTime(nameRandom.Next(1975, 2001), nameRandom.Next(1, 13), nameRandom.Next(1, 29)),
                PasswordSecurity.Hash(s.Password), s.Role);
        }

        db.Users.AddRange(newUsers);
        db.SaveChanges();

        // Only the sample members book (never the real accounts already in the database).
        var members = db.Users.Where(u => u.Role == UserRoles.Customer && u.Email.EndsWith("@example.com")).ToList();
        if (members.Count == 0)
        {
            return "No sample members found.";
        }

        var maids = db.Users.Where(u => u.Role == UserRoles.Housekeeper).OrderBy(u => u.Id).ToList();
        var rooms = db.HotelRooms.Where(r => r.IsActive).OrderBy(r => r.Id).ToList();
        var taken = db.HotelBookings
            .Where(b => b.Status != BookingStatus.Cancelled)
            .Select(b => new { b.RoomId, b.CheckIn, b.CheckOut })
            .AsEnumerable()
            .Select(b => (b.RoomId, b.CheckIn, b.CheckOut))
            .ToList();
        // Rooms that already have a guest checked in (one at a time per room).
        var occupied = db.HotelBookings.Where(b => b.Status == BookingStatus.CheckedIn).Select(b => b.RoomId).ToHashSet();
        bool IsFree(int roomId, DateTime from, DateTime to) => !taken.Any(x => x.RoomId == roomId && x.CheckIn < to && x.CheckOut > from);

        string OnlineMethod() => random.Next(3) switch { 0 => BookingOptions.CreditCard, 1 => BookingOptions.DebitCard, _ => BookingOptions.QrCode };

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

        void PayOnline(HotelBooking b)
        {
            var method = OnlineMethod();
            Pay(b, method, b.CreatedAt.AddMinutes(method == BookingOptions.QrCode ? random.Next(30, 240) : random.Next(2, 10)));
        }

        DateTime BookedBefore(DateTime checkIn)
        {
            var created = checkIn.AddDays(-(random.NextDouble() < 0.3 ? random.Next(20, 60) : random.Next(1, 15)))
                .AddHours(random.Next(7, 23)).AddMinutes(random.Next(60));
            return created < now ? created : now.AddDays(-random.Next(0, 14)).AddHours(-random.Next(1, 12)).AddMinutes(-random.Next(60));
        }

        var bookings = new List<HotelBooking>();
        var doneTasks = new List<(HousekeepingTask Task, HotelBooking Booking)>();
        var start = today.AddMonths(-12);
        var end = today.AddDays(90);

        foreach (var room in rooms)
        {
            var day = start.AddDays(random.Next(0, 4));
            while (day < end)
            {
                var nights = random.NextDouble() switch { < 0.35 => 1, < 0.65 => 2, < 0.85 => 3, < 0.95 => 4, _ => random.Next(5, 8) };
                var checkIn = day;
                var checkOut = day.AddDays(nights);
                day = checkOut.AddDays(random.Next(0, 5));
                if (!IsFree(room.Id, checkIn, checkOut))
                {
                    continue;
                }

                var staysNow = checkIn < today && checkOut >= today;
                if (staysNow && !occupied.Add(room.Id))
                {
                    continue; // somebody is already checked in to this room
                }

                var walkIn = checkIn <= today && random.NextDouble() < 0.12;
                var member = walkIn ? null : members[random.Next(members.Count)];
                var created = walkIn ? checkIn.AddHours(random.Next(10, 21)).AddMinutes(random.Next(60)) : BookedBefore(checkIn);
                if (created > now)
                {
                    created = now.AddMinutes(-random.Next(5, 120));
                }

                var guests = random.Next(1, Math.Max(room.Capacity, 1) + 1);
                var request = new PriceRequest(checkIn, checkOut, guests, random.NextDouble() < 0.45, random.NextDouble() < 0.12,
                    room.HasSpa && random.NextDouble() < 0.3 ? random.Next(1, 4) : 0);
                var quote = PriceCalculator.Calculate(room, request, member, created.Date);
                var b = BookingRules.NewBooking(room, request, quote, room.BedType,
                    member?.Name ?? WalkInGuests[random.Next(WalkInGuests.Length)], member?.Email ?? string.Empty, member?.Id);
                b.CreatedAt = created;
                taken.Add((room.Id, checkIn, checkOut));
                bookings.Add(b);

                if (checkOut < today)
                {
                    var roll = random.NextDouble();
                    if (roll < 0.08)
                    {
                        if (random.NextDouble() < 0.5)
                        {
                            PayOnline(b);
                        }

                        BookingRules.Cancel(b, CancelReasons.Guest);
                        if (b.PaymentStatus == PaymentStatus.RefundPending)
                        {
                            b.PaymentStatus = PaymentStatus.Refunded;
                        }

                        continue;
                    }

                    if (roll < 0.10 && checkOut >= today.AddDays(-10))
                    {
                        // No-show, still to be closed by the front desk: was going to pay at the desk.
                        b.PaymentMethod = BookingOptions.Cash;
                        continue;
                    }

                    if (walkIn) Pay(b, BookingOptions.Cash, created);
                    else if (random.NextDouble() < 0.25) Pay(b, BookingOptions.Cash, checkIn.AddHours(14).AddMinutes(random.Next(120)));
                    else PayOnline(b);

                    b.Status = BookingStatus.CheckedOut;
                    b.CheckedInAt = checkIn.AddHours(14).AddMinutes(random.Next(0, 240));
                    b.CheckedOutAt = checkOut.AddHours(9).AddMinutes(random.Next(0, 180));
                    if (checkOut >= today.AddDays(-21) && maids.Count > 0)
                    {
                        var maid = maids[random.Next(maids.Count)];
                        doneTasks.Add((new HousekeepingTask
                        {
                            RoomId = room.Id, RoomName = room.Name, Status = HousekeepingTaskStatus.Done,
                            Note = $"แขกเช็คเอาท์ ({b.Guests} คน{(b.ExtraBed ? ", มีเตียงเสริม" : "")})",
                            CreatedAt = b.CheckedOutAt.Value, CompletedAt = b.CheckedOutAt.Value.AddMinutes(random.Next(60, 200)),
                            AssignedToUserId = maid.Id, AssignedToName = maid.Name
                        }, b));
                    }
                }
                else if (staysNow)
                {
                    // In the hotel today (leaving today when the check-out day is today).
                    if (walkIn) Pay(b, BookingOptions.Cash, created); else PayOnline(b);
                    b.Status = BookingStatus.CheckedIn;
                    b.CheckedInAt = checkIn.AddHours(14).AddMinutes(random.Next(0, 240));
                }
                else
                {
                    var roll = random.NextDouble();
                    if (roll < 0.04)
                    {
                        // Cancelled recently: a paid one still waits for its refund only if very recent.
                        PayOnline(b);
                        BookingRules.Cancel(b, CancelReasons.Guest);
                        if (b.CreatedAt < now.AddDays(-5))
                        {
                            b.PaymentStatus = PaymentStatus.Refunded;
                        }
                    }
                    else if (roll < 0.50) PayOnline(b);
                    else if (roll < 0.60)
                    {
                        b.PaymentMethod = BookingOptions.QrCode;
                        b.PaymentStatus = PaymentStatus.Pending;
                        b.PaymentReference = $"QR{created:yyyyMMdd}{random.Next(10000, 99999)}";
                    }
                    else if (roll < 0.90) b.PaymentMethod = BookingOptions.Cash;
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

        foreach (var (task, booking) in doneTasks)
        {
            task.BookingId = booking.Id;
        }

        db.HousekeepingTasks.AddRange(doneTasks.Select(x => x.Task));

        // ----- a month of shifts for every housekeeper (days already planned are skipped)
        var shiftCount = 0;
        var planned = db.HousekeepingShifts.Where(s => s.Date >= today).Select(s => new { s.UserId, s.Date }).AsEnumerable()
            .Select(s => (s.UserId, s.Date)).ToHashSet();
        for (var d = 0; d < 30; d++)
        {
            var date = today.AddDays(d);
            for (var m = 0; m < maids.Count; m++)
            {
                if ((d + m * 2) % 7 == 6 || planned.Contains((maids[m].Id, date)))
                {
                    continue; // one day off a week
                }

                db.HousekeepingShifts.Add(new HousekeepingShift
                {
                    UserId = maids[m].Id, UserName = maids[m].Name, Date = date, ShiftName = ShiftNames.All[(m + d / 7) % 3],
                    Note = $"ชั้น {m * 3 + 1}–{m * 3 + 3}"
                });
                shiftCount++;
            }
        }

        db.SaveChanges();
        transaction.Commit();

        var summary = $"Added {newRooms.Count} rooms, {newUsers.Count} users, {bookings.Count} bookings, " +
                      $"{doneTasks.Count} cleaning jobs and {shiftCount} shifts.";
        logger.LogInformation("Bulk sample data: {Summary}", summary);
        return summary;
    }
}
