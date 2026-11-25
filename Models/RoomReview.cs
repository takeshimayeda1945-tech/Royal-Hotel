using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// A guest's rating (1–5 stars) and comment for one finished stay. Only the member who made the
/// booking can write it, and only after checking out; one review per stay (it can be edited).
/// </summary>
public class RoomReview
{
    public int Id { get; set; }

    /// <summary>The stay being reviewed (unique: one review per booking).</summary>
    public int BookingId { get; set; }

    /// <summary>Copied from the booking so a room's reviews are found without joining the bookings.</summary>
    public int RoomId { get; set; }

    /// <summary>The member who wrote it (null once that account is deleted; the review stays).</summary>
    public int? UserId { get; set; }

    /// <summary>Name shown with the review: first name and the initial of the last name (privacy).</summary>
    public string ReviewerName { get; set; } = string.Empty;

    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Hidden by an admin (rude, advertising …): not shown and not counted in the room's rating.</summary>
    public bool IsHidden { get; set; }
}

public static class ReviewRules
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 1000;

    /// <summary>A stay can be reviewed once the guest has checked out.</summary>
    public static bool CanReview(HotelBooking booking) => booking.Status == BookingStatus.CheckedOut && booking.UserId != null;

    /// <summary>"สมชาย ใจดี" → "สมชาย ใ.", "Guest Runner" → "Guest R.", one word stays as it is.</summary>
    public static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "ผู้เข้าพัก",
            1 => parts[0],
            _ => $"{parts[0]} {StringInfo.GetNextTextElement(parts[^1])}."
        };
    }

    public static string RatingLabel(int rating) => rating switch
    {
        5 => "ยอดเยี่ยม",
        4 => "ดีมาก",
        3 => "ดี",
        2 => "พอใช้",
        1 => "ควรปรับปรุง",
        _ => string.Empty
    };
}

/// <summary>Average stars and number of visible reviews of a room.</summary>
public record RatingSummary(double Average, int Count)
{
    public string AverageText => Average.ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>Reviews block of the room details page.</summary>
public class RoomReviewsViewModel
{
    public RatingSummary? Summary { get; set; }

    /// <summary>How many reviews gave 1..5 stars (index 0 = 1 star).</summary>
    public int[] Distribution { get; set; } = new int[ReviewRules.MaxRating];

    public List<RoomReview> Latest { get; set; } = new();
}

/// <summary>Review form (Home/Review).</summary>
public class ReviewFormModel
{
    public int BookingId { get; set; }

    [Range(ReviewRules.MinRating, ReviewRules.MaxRating, ErrorMessage = "กรุณาให้คะแนน 1–5 ดาว")]
    public int Rating { get; set; }

    /// <summary>Optional. Cleaned and length-checked by the controller.</summary>
    public string? Comment { get; set; }
}

/// <summary>The review page: the stay, the form and (when editing) the saved review.</summary>
public class ReviewPageModel
{
    public required HotelBooking Booking { get; init; }
    public required ReviewFormModel Form { get; init; }
    public RoomReview? Existing { get; init; }
}
