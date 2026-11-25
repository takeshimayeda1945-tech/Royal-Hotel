namespace Intouch_67173986_L003.Models;

/// <summary>
/// Stay dates, guests and destination chosen in the search bar. Invalid input
/// (past dates, check-out before check-in, too many nights) is replaced with safe defaults.
/// </summary>
public class RoomSearchModel
{
    public const int DefaultGuests = 2;
    private const int DefaultLeadDays = 2;
    private const int DefaultNights = 3;

    public string? Destination { get; init; }
    public DateTime CheckInDate { get; init; }
    public DateTime CheckOutDate { get; init; }
    public int Guests { get; init; } = DefaultGuests;

    /// <summary>True when the visitor actually submitted the search form.</summary>
    public bool IsFiltered { get; init; }

    /// <summary>True when the submitted dates were invalid and had to be replaced.</summary>
    public bool DatesAdjusted { get; init; }

    public string CheckIn => DateInput.ToValue(CheckInDate);
    public string CheckOut => DateInput.ToValue(CheckOutDate);
    public int Nights => (CheckOutDate - CheckInDate).Days;

    /// <summary>Query-string values that repeat this search on another link (none when nothing was searched).</summary>
    public Dictionary<string, string> RouteValues()
    {
        var values = new Dictionary<string, string>();
        if (!IsFiltered)
        {
            return values;
        }

        if (Destination != null) values["destination"] = Destination;
        values["checkIn"] = CheckIn;
        values["checkOut"] = CheckOut;
        values["guests"] = Guests.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return values;
    }

    public static RoomSearchModel Create(
        string? destination = null,
        string? checkIn = null,
        string? checkOut = null,
        int? guests = null,
        bool isFiltered = false)
    {
        var today = DateTime.Today;
        var adjusted = false;

        DateTime from;
        if (DateInput.TryParse(checkIn, out var parsedFrom) && parsedFrom >= today)
        {
            from = parsedFrom;
        }
        else
        {
            from = today.AddDays(DefaultLeadDays);
            adjusted |= !string.IsNullOrWhiteSpace(checkIn);
        }

        DateTime to;
        if (DateInput.TryParse(checkOut, out var parsedTo) && parsedTo > from && (parsedTo - from).Days <= BookingOptions.MaxNights)
        {
            to = parsedTo;
        }
        else
        {
            to = from.AddDays(DefaultNights);
            adjusted |= !string.IsNullOrWhiteSpace(checkOut);
        }

        return new RoomSearchModel
        {
            Destination = string.IsNullOrWhiteSpace(destination) ? null : destination.Trim(),
            CheckInDate = from,
            CheckOutDate = to,
            Guests = Math.Clamp(guests ?? DefaultGuests, 1, BookingOptions.MaxGuests),
            IsFiltered = isFiltered,
            DatesAdjusted = adjusted
        };
    }
}

/// <summary>A stay from one date (check-in) to another (check-out).</summary>
public record DateRange(DateTime From, DateTime To);

/// <summary>
/// Narrows and orders the room list (Home/Rooms): room type, price per night and sort order.
/// Values the page does not offer are ignored, so an edited link never breaks the page.
/// </summary>
public class RoomFilterModel
{
    public const string SortRecommended = "";
    public const string SortPriceLow = "price-asc";
    public const string SortPriceHigh = "price-desc";
    public const string SortCapacity = "capacity";
    public const string SortRating = "rating";

    public static readonly (string Value, string Label)[] SortOptions =
    {
        (SortRecommended, "แนะนำ"),
        (SortPriceLow, "ราคา: ต่ำ → สูง"),
        (SortPriceHigh, "ราคา: สูง → ต่ำ"),
        (SortCapacity, "รองรับผู้เข้าพักมากที่สุด"),
        (SortRating, "คะแนนรีวิวสูงสุด"),
    };

    /// <summary>One of <see cref="RoomTypes.All"/>, or null for every type.</summary>
    public string? Type { get; init; }
    public int? MinPrice { get; init; }
    public int? MaxPrice { get; init; }
    public string Sort { get; init; } = SortRecommended;

    /// <summary>True when the list is narrowed (type or price), not just sorted.</summary>
    public bool HasFilter => Type != null || MinPrice != null || MaxPrice != null;

    public static RoomFilterModel Create(string? type = null, int? minPrice = null, int? maxPrice = null, string? sort = null)
    {
        var min = minPrice > 0 ? minPrice : null;
        var max = maxPrice > 0 ? maxPrice : null;
        if (min > max)
        {
            (min, max) = (max, min);
        }

        return new RoomFilterModel
        {
            Type = RoomTypes.All.FirstOrDefault(t => string.Equals(t, type?.Trim(), StringComparison.OrdinalIgnoreCase)),
            MinPrice = min,
            MaxPrice = max,
            Sort = SortOptions.Any(o => o.Value == sort) ? sort! : SortRecommended
        };
    }

    public bool MatchesPrice(HotelRoom room) =>
        (MinPrice == null || room.Price >= MinPrice) && (MaxPrice == null || room.Price <= MaxPrice);

    public bool Matches(HotelRoom room) => (Type == null || room.RoomType == Type) && MatchesPrice(room);

    /// <summary>
    /// Filters and sorts rooms already loaded from the database. Done in memory on purpose: the
    /// list is short, and SQLite (the fallback database) cannot compare or sort decimal prices.
    /// </summary>
    /// <param name="ratings">Review average per room id; rooms without reviews come last when sorting by rating.</param>
    public List<HotelRoom> Apply(IEnumerable<HotelRoom> rooms, IReadOnlyDictionary<int, RatingSummary>? ratings = null)
    {
        var matching = rooms.Where(Matches);
        double Average(HotelRoom r) => ratings != null && ratings.TryGetValue(r.Id, out var s) ? s.Average : 0;
        int Count(HotelRoom r) => ratings != null && ratings.TryGetValue(r.Id, out var s) ? s.Count : 0;
        return (Sort switch
        {
            SortPriceLow => matching.OrderBy(r => r.Price).ThenBy(r => r.Id),
            SortPriceHigh => matching.OrderByDescending(r => r.Price).ThenBy(r => r.Id),
            SortCapacity => matching.OrderByDescending(r => r.Capacity).ThenBy(r => r.Price).ThenBy(r => r.Id),
            SortRating => matching.OrderByDescending(Average).ThenByDescending(Count).ThenBy(r => r.Id),
            _ => matching.OrderBy(r => r.Id)
        }).ToList();
    }

    /// <summary>Query-string values that keep this filter on another link (empty parts left out).</summary>
    public Dictionary<string, string> RouteValues()
    {
        var values = new Dictionary<string, string>();
        if (Type != null) values["type"] = Type;
        if (MinPrice != null) values["minPrice"] = MinPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (MaxPrice != null) values["maxPrice"] = MaxPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Sort != SortRecommended) values["sort"] = Sort;
        return values;
    }
}

public class RoomListViewModel
{
    public List<HotelRoom> Rooms { get; set; } = new();
    public RoomSearchModel Search { get; set; } = RoomSearchModel.Create();
    public RoomFilterModel Filter { get; set; } = RoomFilterModel.Create();

    /// <summary>Rooms that match the search (dates, guests, area) before the type and price filter.</summary>
    public int SearchResultCount { get; set; }

    /// <summary>For the type list: how many rooms of each type fit the search and the price range.</summary>
    public Dictionary<string, int> TypeCounts { get; set; } = new();
    public int AnyTypeCount { get; set; }

    /// <summary>Cheapest and dearest room of the search result (hints in the price boxes).</summary>
    public decimal? LowestPrice { get; set; }
    public decimal? HighestPrice { get; set; }

    /// <summary>Visible reviews per room id (rooms without reviews are missing).</summary>
    public Dictionary<int, RatingSummary> Ratings { get; set; } = new();
}
