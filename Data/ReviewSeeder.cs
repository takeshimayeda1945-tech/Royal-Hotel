using Intouch_67173986_L003.Models;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Data;

/// <summary>
/// Sample reviews for the room pages:
///
///   dotnet run -- --seed-reviews
///
/// Only stays of the sample members (e-mail @example.com, made by the other seeders) that were
/// checked out get a review — about one in three, as in real life. Real accounts and walk-in guests
/// are never touched. Each room gets its own typical score, so rooms differ. Two reviews are hidden
/// (an advert and a rude one) to show how an admin hides reviews. Runs once: if the sample members
/// already have reviews, nothing is added. To remove them all again:
///   DELETE r FROM RoomReviews r JOIN Users u ON u.Id = r.UserId WHERE u.Email LIKE '%@example.com';
/// </summary>
public static class ReviewSeeder
{
    private const double ShareReviewed = 0.35;
    private const double ShareWithoutComment = 0.2;

    private static readonly string[][] Comments =
    {
        // 1 star
        new[]
        {
            "ห้องมีกลิ่นอับ แอร์เสียงดังทั้งคืน แจ้งแล้วแต่ไม่มีใครมาดู",
            "รอเช็คอินนานเกือบชั่วโมง ห้องยังทำความสะอาดไม่เสร็จ",
            "ห้องไม่ตรงกับรูปเลย ผิดหวังมากครับ",
        },
        // 2 stars
        new[]
        {
            "ทำเลดี แต่ห้องค่อนข้างเก่า ห้องน้ำมีคราบ",
            "พนักงานบริการดี แต่ห้องเก็บเสียงไม่ดี ได้ยินเสียงห้องข้าง ๆ",
            "อาหารเช้าตัวเลือกน้อย มาสายหน่อยของหมดแล้ว",
            "Wi-Fi หลุดบ่อย ทำงานลำบาก",
        },
        // 3 stars
        new[]
        {
            "โดยรวมโอเค สมราคา ไม่มีอะไรน่าตื่นเต้น",
            "ห้องสะอาด แต่ที่จอดรถหายาก",
            "เตียงนอนสบาย แต่หมอนแข็งไปหน่อย",
            "เช็คอินเร็ว ห้องเล็กกว่าที่คิดนิดหนึ่ง",
            "Decent stay, a bit noisy at night.",
        },
        // 4 stars
        new[]
        {
            "ห้องสะอาด เตียงนุ่ม พนักงานยิ้มแย้ม จะกลับมาอีก",
            "วิวสวยมาก อาหารเช้าอร่อย เสียดายสระว่ายน้ำคนเยอะไปหน่อย",
            "เดินทางสะดวก ใกล้ร้านอาหาร ห้องกว้างดี",
            "พนักงานต้อนรับช่วยแนะนำที่เที่ยว ประทับใจครับ",
            "คุ้มค่ากับราคา ห้องน้ำสะอาด น้ำแรงดี",
            "Great location and friendly staff.",
            "สปาดีมาก ผ่อนคลายสุด ๆ แต่ต้องจองล่วงหน้า",
        },
        // 5 stars
        new[]
        {
            "ประทับใจทุกอย่าง ห้องสวย สะอาด บริการเยี่ยม แนะนำเลยค่ะ",
            "มาฉลองวันเกิด ได้ส่วนลดด้วย พนักงานใส่ใจมาก",
            "ห้องกว้าง วิวดี อาหารเช้าหลากหลาย ครอบครัวชอบมาก",
            "เตียงหลับสบายที่สุดตั้งแต่เคยพักโรงแรมมา",
            "บริการดีตั้งแต่เช็คอินจนเช็คเอาท์ จะกลับมาพักอีกแน่นอน",
            "สปาและอาหารเช้าดีเกินคาด คุ้มทุกบาท",
            "Perfect stay! Spotless room and amazing breakfast.",
            "พนักงานช่วยเหลือดีมาก ขอห้องชั้นสูงก็จัดให้",
        },
    };

    /// <summary>Hidden examples: what an admin would take down.</summary>
    private static readonly (int Rating, string Comment)[] HiddenExamples =
    {
        (1, "รับจองโรงแรมราคาถูกกว่านี้ ทักแชทมาได้เลย โปรโมชั่นพิเศษทุกวัน!!!"),
        (1, "แย่ที่สุด!!! พนักงานไม่ได้เรื่องสักคน ห่วยทั้งโรงแรม"),
    };

    public static string Seed(ApplicationDbContext db, ILogger logger)
    {
        var members = db.Users.AsNoTracking()
            .Where(u => u.Role == UserRoles.Customer && u.Email.EndsWith("@example.com"))
            .ToDictionary(u => u.Id, u => u.Name);
        if (members.Count == 0)
        {
            return "ไม่มีสมาชิกตัวอย่าง (@example.com) — รัน --seed-demo หรือ --seed-more ก่อน";
        }

        var memberIds = members.Keys.ToList();
        if (db.RoomReviews.Any(r => r.UserId != null && memberIds.Contains(r.UserId.Value)))
        {
            return "สมาชิกตัวอย่างมีรีวิวอยู่แล้ว — ไม่ได้เพิ่มรีวิว";
        }

        var stays = db.HotelBookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.CheckedOut && b.UserId != null && memberIds.Contains(b.UserId.Value))
            .Where(b => !db.RoomReviews.Any(r => r.BookingId == b.Id))
            .OrderBy(b => b.Id)
            .ToList();

        var random = new Random(20261125); // the same reviews on every database
        var now = DateTime.Now;
        var reviews = new List<RoomReview>();
        foreach (var stay in stays)
        {
            if (random.NextDouble() > ShareReviewed)
            {
                continue;
            }

            var rating = RatingFor(stay.RoomId, random);
            var pool = Comments[rating - 1];
            var checkedOut = stay.CheckedOutAt ?? stay.CheckOut.Date.AddHours(11);
            var written = checkedOut.AddHours(2 + random.Next(0, 70));
            reviews.Add(new RoomReview
            {
                BookingId = stay.Id,
                RoomId = stay.RoomId,
                UserId = stay.UserId,
                ReviewerName = ReviewRules.ShortName(members[stay.UserId!.Value]),
                Rating = rating,
                Comment = random.NextDouble() < ShareWithoutComment ? string.Empty : pool[random.Next(pool.Length)],
                CreatedAt = written < now ? written : now.AddMinutes(-random.Next(5, 600))
            });
        }

        // Turn the last two short reviews into the hidden examples.
        foreach (var (example, review) in HiddenExamples.Zip(reviews.AsEnumerable().Reverse().Where(r => r.Rating <= 2)))
        {
            review.Rating = example.Rating;
            review.Comment = example.Comment;
            review.IsHidden = true;
        }

        db.RoomReviews.AddRange(reviews);
        db.SaveChanges();
        var hidden = reviews.Count(r => r.IsHidden);
        logger.LogInformation("Sample reviews added: {Count} ({Hidden} hidden).", reviews.Count, hidden);
        return $"เพิ่มรีวิวตัวอย่าง {reviews.Count} รายการ จากการเข้าพักที่เช็คเอาท์แล้ว {stays.Count} ครั้ง (ซ่อนไว้ {hidden} รายการ)";
    }

    /// <summary>
    /// Stars for a stay: every room has its own typical score (3.3–4.8, from its id), and each
    /// guest's opinion varies around it.
    /// </summary>
    private static int RatingFor(int roomId, Random random)
    {
        var typical = 3.3 + (roomId * 37 % 16) / 10.0;
        // Normal distribution (Box–Muller), spread 0.8 stars.
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        return Math.Clamp((int)Math.Round(typical + 0.8 * normal), ReviewRules.MinRating, ReviewRules.MaxRating);
    }
}
