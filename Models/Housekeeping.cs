namespace Intouch_67173986_L003.Models;

/// <summary>A room that has to be cleaned. Created automatically when a guest checks out.</summary>
public class HousekeepingTask
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public int? BookingId { get; set; }
    public string Note { get; set; } = string.Empty;
    public string Status { get; set; } = HousekeepingTaskStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? AssignedToUserId { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>A housekeeper's duty shift (เวร), planned by an Admin.</summary>
public class HousekeepingShift
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string ShiftName { get; set; } = ShiftNames.Morning;
    public string Note { get; set; } = string.Empty;
}
