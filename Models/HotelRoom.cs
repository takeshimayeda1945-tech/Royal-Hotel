using System.ComponentModel.DataAnnotations.Schema;

namespace Intouch_67173986_L003.Models;

public class HotelRoom
{
    /// <summary>Shown when a room has no picture.</summary>
    public const string DefaultImageUrl = "https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?auto=format&fit=crop&w=900&q=80";

    /// <summary>Local picture used when an image cannot be loaded (broken URL, or no internet during a demo).</summary>
    public const string PlaceholderImageUrl = "/img/room-placeholder.svg";

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Standard / Deluxe / Suite. Deluxe rooms get the Friday–Saturday discount.</summary>
    public string RoomType { get; set; } = RoomTypes.Standard;
    public decimal Price { get; set; }
    public int Capacity { get; set; } = 2;
    public string Area { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BedType { get; set; } = BookingOptions.DoubleBed;
    public bool HasSpa { get; set; }
    public bool IsActive { get; set; } = true;

    [NotMapped]
    public string DisplayImageUrl => string.IsNullOrWhiteSpace(ImageUrl) ? DefaultImageUrl : ImageUrl;

    [NotMapped]
    public bool IsDeluxe => RoomType == RoomTypes.Deluxe;
}
