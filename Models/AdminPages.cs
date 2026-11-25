namespace Intouch_67173986_L003.Models;

/// <summary>Page numbers of a long list (newest first).</summary>
public record Paging(int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int First => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int Last => Math.Min(Page * PageSize, Total);

    /// <summary>A page number that exists (1 when the list is empty or the number is out of range).</summary>
    public static int Clamp(int page, int pageSize, int total) =>
        Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
}

/// <summary>What the pager partial needs: the paging, the action and the filter to keep.</summary>
public record PagerModel(Paging Paging, string Action, IReadOnlyDictionary<string, string> RouteValues);

/// <summary>Filters of the audit log page (all optional).</summary>
public class AuditLogFilter
{
    /// <summary>Key of one of <see cref="AuditActions.Groups"/>.</summary>
    public string? Group { get; set; }
    /// <summary>One of <see cref="AuditEntities.All"/> with an id: the history of one record.</summary>
    public string? Entity { get; set; }
    public int? Id { get; set; }
    /// <summary>Lines written by this user.</summary>
    public int? UserId { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Q { get; set; }

    public bool IsEmpty => Group == null && Entity == null && UserId == null && From == null && To == null && string.IsNullOrEmpty(Q);

    /// <summary>Query-string values that keep the filter on the page links.</summary>
    public Dictionary<string, string> RouteValues()
    {
        var values = new Dictionary<string, string>();
        void Add(string key, object? value)
        {
            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(text))
            {
                values[key] = text;
            }
        }

        Add("group", Group);
        Add("entity", Entity);
        Add("id", Id);
        Add("userId", UserId);
        Add("from", From);
        Add("to", To);
        Add("q", Q);
        return values;
    }
}

public class AuditLogPageModel
{
    public List<AuditLog> Lines { get; set; } = new();
    public AuditLogFilter Filter { get; set; } = new();
    public Paging Paging { get; set; } = new(1, 50, 0);

    /// <summary>People who have lines in the log (for the "who" list).</summary>
    public List<CyberUser> People { get; set; } = new();
    public int TodayCount { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>One review in the admin list, with its room.</summary>
public record ReviewRow(RoomReview Review, string RoomName);

public class ReviewAdminPageModel
{
    public List<ReviewRow> Rows { get; set; } = new();
    public Paging Paging { get; set; } = new(1, 50, 0);

    /// <summary>"visible", "hidden" or null for all.</summary>
    public string? Show { get; set; }
    public int? Stars { get; set; }

    public int VisibleCount { get; set; }
    public int HiddenCount { get; set; }
    public RatingSummary? Overall { get; set; }
}
