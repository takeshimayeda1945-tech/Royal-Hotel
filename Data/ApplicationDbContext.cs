using Intouch_67173986_L003.Models;
using Microsoft.EntityFrameworkCore;
using MySql.EntityFrameworkCore.Extensions;

namespace Intouch_67173986_L003.Data;

/// <summary>
/// The web site's database. Column sizes and money precision are set here so the MySQL tables
/// get proper types (VARCHAR(n), DECIMAL(12,2)) instead of LONGTEXT / DECIMAL(65,30).
/// Relations are real foreign keys and the allowed values are CHECK constraints, so the database
/// itself refuses bad data. The same model still works with SQLite, which ignores the sizes.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<CyberUser> Users { get; set; }
    public DbSet<HotelRoom> HotelRooms { get; set; }
    public DbSet<HotelBooking> HotelBookings { get; set; }
    public DbSet<HousekeepingTask> HousekeepingTasks { get; set; }
    public DbSet<HousekeepingShift> HousekeepingShifts { get; set; }
    public DbSet<RolePortal> RolePortals { get; set; }
    public DbSet<RoomReview> RoomReviews { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    /// <summary>True when the site runs on MySQL (false for the SQLite fallback).</summary>
    public bool IsMySql => Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Days without a time of day (stay dates, birthdays, shift days) are DATE columns in MySQL,
        // so a time can never slip in. SQLite has no date type and keeps its text columns.
        var isMySql = IsMySql;
        void DateOnly<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
            System.Linq.Expressions.Expression<Func<TEntity, DateTime>> property) where TEntity : class
        {
            if (isMySql)
            {
                entity.Property(property).HasColumnType("date");
            }
        }

        // Users: sizes come from the [StringLength] attributes on CyberUser.
        modelBuilder.Entity<CyberUser>(user =>
        {
            user.ToTable(table => AddChecks(table, "Users"));
            user.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_Users_Email");
            DateOnly(user, u => u.Birthday);

            // The column holds a password hash; compare it byte-for-byte (MySQL's default collation
            // ignores upper/lower case). MySQL-only setting; SQLite ignores it.
            user.Property(u => u.Password).ForMySQLHasCollation("utf8mb4_bin");

            // Every role must exist in RolePortals (the port that role logs in on). A role that
            // users still have cannot be deleted from RolePortals.
            user.HasOne<RolePortal>().WithMany().HasForeignKey(u => u.Role)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_Users_RolePortals_Role");
            user.HasIndex(u => u.Role).HasDatabaseName("IX_Users_Role");
        });

        // One row per role: the port of that role's portal (Infrastructure/RolePortals.cs).
        modelBuilder.Entity<RolePortal>(portal =>
        {
            portal.ToTable(table => AddChecks(table, "RolePortals"));
            portal.HasIndex(p => p.Port).IsUnique().HasDatabaseName("IX_RolePortals_Port");
        });

        modelBuilder.Entity<HotelRoom>(room =>
        {
            room.ToTable(table => AddChecks(table, "HotelRooms"));
            room.Property(r => r.Name).HasMaxLength(100);
            room.Property(r => r.RoomType).HasMaxLength(20);
            room.Property(r => r.Price).HasPrecision(12, 2);
            room.Property(r => r.Area).HasMaxLength(100);
            room.Property(r => r.ImageUrl).HasMaxLength(500);
            room.Property(r => r.Tag).HasMaxLength(50);
            room.Property(r => r.Description).HasMaxLength(1000);
            room.Property(r => r.BedType).HasMaxLength(50);
        });

        modelBuilder.Entity<HotelBooking>(booking =>
        {
            booking.ToTable(table => AddChecks(table, "HotelBookings"));
            booking.Property(b => b.GuestName).HasMaxLength(100);
            booking.Property(b => b.ContactEmail).HasMaxLength(200);
            booking.Property(b => b.RoomName).HasMaxLength(100);
            booking.Property(b => b.BedType).HasMaxLength(50);
            // DEFAULT '' like CancelReason: older databases get the column with their rows kept.
            booking.Property(b => b.SpecialRequest).HasMaxLength(BookingOptions.MaxSpecialRequestLength).HasDefaultValue(string.Empty);
            booking.Property(b => b.PricePerNight).HasPrecision(12, 2);
            booking.Property(b => b.RoomTotal).HasPrecision(12, 2);
            booking.Property(b => b.ServiceTotal).HasPrecision(12, 2);
            booking.Property(b => b.Discount).HasPrecision(12, 2);
            booking.Property(b => b.DiscountDetails).HasMaxLength(1000);
            booking.Property(b => b.Tax).HasPrecision(12, 2);
            booking.Property(b => b.Total).HasPrecision(12, 2);
            booking.Property(b => b.PaymentMethod).HasMaxLength(50);
            booking.Property(b => b.PaymentStatus).HasMaxLength(20);
            booking.Property(b => b.PaymentReference).HasMaxLength(100);
            booking.Property(b => b.Status).HasMaxLength(20);
            // DEFAULT '' so older databases can get the column with their rows kept (DbSchema.NewColumns).
            booking.Property(b => b.CancelReason).HasMaxLength(20).HasDefaultValue(string.Empty);
            DateOnly(booking, b => b.CheckIn);
            DateOnly(booking, b => b.CheckOut);

            // A deleted member's bookings stay (with the guest name/e-mail saved on the booking).
            booking.HasOne<CyberUser>().WithMany().HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("FK_HotelBookings_Users_UserId");
            // A room with bookings cannot be deleted (close it instead), so history never loses its room.
            booking.HasOne<HotelRoom>().WithMany().HasForeignKey(b => b.RoomId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_HotelBookings_HotelRooms_RoomId");

            // Availability checks, "my bookings", the front desk and the finance views.
            booking.HasIndex(b => new { b.RoomId, b.CheckIn, b.CheckOut }).HasDatabaseName("IX_HotelBookings_RoomId_CheckIn_CheckOut");
            booking.HasIndex(b => b.UserId).HasDatabaseName("IX_HotelBookings_UserId");
            booking.HasIndex(b => b.Status).HasDatabaseName("IX_HotelBookings_Status");
            booking.HasIndex(b => b.PaymentStatus).HasDatabaseName("IX_HotelBookings_PaymentStatus");
        });

        modelBuilder.Entity<HousekeepingTask>(task =>
        {
            task.ToTable(table => AddChecks(table, "HousekeepingTasks"));
            task.Property(t => t.RoomName).HasMaxLength(100);
            task.Property(t => t.Note).HasMaxLength(200);
            task.Property(t => t.Status).HasMaxLength(20);
            task.Property(t => t.AssignedToName).HasMaxLength(100);

            task.HasOne<HotelRoom>().WithMany().HasForeignKey(t => t.RoomId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_HousekeepingTasks_HotelRooms_RoomId");
            task.HasOne<HotelBooking>().WithMany().HasForeignKey(t => t.BookingId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("FK_HousekeepingTasks_HotelBookings_BookingId");
            task.HasOne<CyberUser>().WithMany().HasForeignKey(t => t.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("FK_HousekeepingTasks_Users_AssignedToUserId");

            task.HasIndex(t => t.Status).HasDatabaseName("IX_HousekeepingTasks_Status");
            task.HasIndex(t => new { t.RoomId, t.Status }).HasDatabaseName("IX_HousekeepingTasks_RoomId_Status");
            task.HasIndex(t => t.BookingId).HasDatabaseName("IX_HousekeepingTasks_BookingId");
            task.HasIndex(t => t.AssignedToUserId).HasDatabaseName("IX_HousekeepingTasks_AssignedToUserId");
        });

        modelBuilder.Entity<HousekeepingShift>(shift =>
        {
            shift.ToTable(table => AddChecks(table, "HousekeepingShifts"));
            shift.Property(s => s.UserName).HasMaxLength(100);
            shift.Property(s => s.ShiftName).HasMaxLength(50);
            shift.Property(s => s.Note).HasMaxLength(200);
            DateOnly(shift, s => s.Date);

            // Deleting a housekeeper also deletes their duty shifts.
            shift.HasOne<CyberUser>().WithMany().HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_HousekeepingShifts_Users_UserId");

            // A housekeeper has each shift of a day at most once (also serves the "my shifts" lookups).
            shift.HasIndex(s => new { s.UserId, s.Date, s.ShiftName }).IsUnique()
                .HasDatabaseName("IX_HousekeepingShifts_UserId_Date_ShiftName");
        });

        // Guest reviews of finished stays (added after the first version: see DbSchema.NewTables).
        modelBuilder.Entity<RoomReview>(review =>
        {
            review.ToTable(table => AddChecks(table, "RoomReviews"));
            review.Property(r => r.ReviewerName).HasMaxLength(100);
            review.Property(r => r.Comment).HasMaxLength(ReviewRules.MaxCommentLength);

            // One review per stay; it goes with its booking, stays when the member's account is deleted.
            review.HasIndex(r => r.BookingId).IsUnique().HasDatabaseName("IX_RoomReviews_BookingId");
            review.HasOne<HotelBooking>().WithMany().HasForeignKey(r => r.BookingId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_RoomReviews_HotelBookings_BookingId");
            review.HasOne<HotelRoom>().WithMany().HasForeignKey(r => r.RoomId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_RoomReviews_HotelRooms_RoomId");
            review.HasOne<CyberUser>().WithMany().HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("FK_RoomReviews_Users_UserId");

            // The room page reads a room's visible reviews.
            review.HasIndex(r => new { r.RoomId, r.IsHidden }).HasDatabaseName("IX_RoomReviews_RoomId_IsHidden");
            review.HasIndex(r => r.UserId).HasDatabaseName("IX_RoomReviews_UserId");
        });

        // Audit log: append-only (added after the first version: see DbSchema.NewTables).
        modelBuilder.Entity<AuditLog>(log =>
        {
            log.ToTable(table => AddChecks(table, "AuditLogs"));
            log.Property(a => a.UserName).HasMaxLength(100);
            log.Property(a => a.UserRole).HasMaxLength(50);
            log.Property(a => a.Action).HasMaxLength(40);
            log.Property(a => a.EntityType).HasMaxLength(20);
            log.Property(a => a.Details).HasMaxLength(AuditLog.MaxDetailsLength);
            log.Property(a => a.IpAddress).HasMaxLength(45);

            // A deleted account keeps its lines (with the copied name).
            log.HasOne<CyberUser>().WithMany().HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("FK_AuditLogs_Users_UserId");

            // Newest first, per record ("history of booking #123"), per user, per action.
            log.HasIndex(a => a.CreatedAt).HasDatabaseName("IX_AuditLogs_CreatedAt");
            log.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("IX_AuditLogs_EntityType_EntityId");
            log.HasIndex(a => a.UserId).HasDatabaseName("IX_AuditLogs_UserId");
            log.HasIndex(a => a.Action).HasDatabaseName("IX_AuditLogs_Action");
        });

        base.OnModelCreating(modelBuilder);
    }

    private static void AddChecks(Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder table, string tableName)
    {
        foreach (var check in DbSchema.Checks.Where(c => c.Table == tableName))
        {
            table.HasCheckConstraint(check.Name, check.Sql);
        }
    }
}

/// <summary>
/// Constraint and index definitions shared by the EF model (new databases) and
/// <see cref="DbInitializer"/>, which adds whatever is missing to a database made by an older version.
/// </summary>
public static class DbSchema
{
    public record Check(string Table, string Name, string Sql);
    public record ForeignKey(string Table, string Name, string Column, string PrincipalTable, string OnDelete);
    public record Index(string Table, string Name, string Columns, bool Unique = false);
    public record DateColumn(string Table, string Column);
    /// <param name="After">MySQL puts the new column after this one, in the same place as in a new database.</param>
    public record Column(string Table, string Name, string MySqlType, string SqliteType, string After);

    private static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

    public static readonly Check[] Checks =
    {
        new("Users", "CK_Users_Role", $"Role IN ({InList(UserRoles.All)})"),
        new("RolePortals", "CK_RolePortals_Role", $"Role IN ({InList(UserRoles.All)})"),
        new("RolePortals", "CK_RolePortals_Port", "Port BETWEEN 1024 AND 65535"),
        new("HotelRooms", "CK_HotelRooms_Price", "Price > 0"),
        new("HotelRooms", "CK_HotelRooms_Capacity", $"Capacity BETWEEN 1 AND {BookingOptions.MaxGuests}"),
        new("HotelRooms", "CK_HotelRooms_RoomType", $"RoomType IN ({InList(RoomTypes.All)})"),
        new("HotelRooms", "CK_HotelRooms_BedType", $"BedType IN ({InList(BookingOptions.BedTypes)})"),
        new("HotelBookings", "CK_HotelBookings_Dates", "CheckOut > CheckIn"),
        new("HotelBookings", "CK_HotelBookings_Guests", "Guests >= 1"),
        new("HotelBookings", "CK_HotelBookings_Amounts", "Total >= 0 AND Discount >= 0 AND Tax >= 0"),
        new("HotelBookings", "CK_HotelBookings_Status", $"Status IN ({InList(BookingStatus.All)})"),
        new("HotelBookings", "CK_HotelBookings_PaymentStatus", $"PaymentStatus IN ({InList(PaymentStatus.All)})"),
        // Added in the second review: the rest of the booking's numbers and lists.
        new("HotelBookings", "CK_HotelBookings_Stay",
            $"Nights >= 1 AND Nights <= {BookingOptions.MaxNights} AND Guests <= {BookingOptions.MaxGuests} " +
            $"AND SpaSessions BETWEEN 0 AND {BookingOptions.MaxSpaSessions}"),
        new("HotelBookings", "CK_HotelBookings_Prices", "PricePerNight >= 0 AND RoomTotal >= 0 AND ServiceTotal >= 0"),
        new("HotelBookings", "CK_HotelBookings_BedType", $"BedType IN ({InList(BookingOptions.BedTypes)})"),
        // Empty = the guest has not chosen how to pay yet.
        new("HotelBookings", "CK_HotelBookings_PaymentMethod", $"PaymentMethod IN ('', {InList(BookingOptions.PaymentMethods)})"),
        // Empty = not cancelled (or cancelled before the reason was kept).
        new("HotelBookings", "CK_HotelBookings_CancelReason", $"CancelReason IN ('', {InList(CancelReasons.All)})"),
        new("HousekeepingTasks", "CK_HousekeepingTasks_Status", $"Status IN ({InList(HousekeepingTaskStatus.All)})"),
        new("HousekeepingShifts", "CK_HousekeepingShifts_ShiftName", $"ShiftName IN ({InList(ShiftNames.All)})"),
        new("RoomReviews", "CK_RoomReviews_Rating", $"Rating BETWEEN {ReviewRules.MinRating} AND {ReviewRules.MaxRating}"),
        new("AuditLogs", "CK_AuditLogs_Action", $"Action IN ({InList(AuditActions.All)})"),
        new("AuditLogs", "CK_AuditLogs_EntityType", $"EntityType IN ({InList(AuditEntities.All)})"),
    };

    public static readonly ForeignKey[] ForeignKeys =
    {
        new("HotelBookings", "FK_HotelBookings_Users_UserId", "UserId", "Users", "SET NULL"),
        new("HotelBookings", "FK_HotelBookings_HotelRooms_RoomId", "RoomId", "HotelRooms", "RESTRICT"),
        new("HousekeepingTasks", "FK_HousekeepingTasks_HotelRooms_RoomId", "RoomId", "HotelRooms", "CASCADE"),
        new("HousekeepingTasks", "FK_HousekeepingTasks_HotelBookings_BookingId", "BookingId", "HotelBookings", "SET NULL"),
        new("HousekeepingTasks", "FK_HousekeepingTasks_Users_AssignedToUserId", "AssignedToUserId", "Users", "SET NULL"),
        new("HousekeepingShifts", "FK_HousekeepingShifts_Users_UserId", "UserId", "Users", "CASCADE"),
    };

    /// <summary>Indexes added after the first MySQL version (older ones already exist).</summary>
    public static readonly Index[] NewIndexes =
    {
        new("HotelBookings", "IX_HotelBookings_PaymentStatus", "PaymentStatus"),
        new("HousekeepingTasks", "IX_HousekeepingTasks_RoomId_Status", "RoomId, Status"),
        new("HousekeepingTasks", "IX_HousekeepingTasks_BookingId", "BookingId"),
        new("HousekeepingTasks", "IX_HousekeepingTasks_AssignedToUserId", "AssignedToUserId"),
        new("Users", "IX_Users_Role", "Role"),
        new("HousekeepingShifts", "IX_HousekeepingShifts_UserId_Date_ShiftName", "UserId, Date, ShiftName", Unique: true),
    };

    /// <summary>Indexes that a newer one replaced (dropped once the replacement exists).</summary>
    public static readonly (Index Old, string ReplacedBy)[] RetiredIndexes =
    {
        (new("HousekeepingShifts", "IX_HousekeepingShifts_UserId_Date", "UserId, Date"), "IX_HousekeepingShifts_UserId_Date_ShiftName"),
    };

    /// <summary>
    /// Columns added after the first version: older databases get them (existing rows get the default).
    /// The site cannot run without them, so a failure stops the start instead of being skipped.
    /// </summary>
    public static readonly Column[] NewColumns =
    {
        new("HotelBookings", "CancelReason", "varchar(20) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''", After: "Status"),
        new("HotelBookings", "SpecialRequest", $"varchar({BookingOptions.MaxSpecialRequestLength}) NOT NULL DEFAULT ''", "TEXT NOT NULL DEFAULT ''", After: "SpaSessions"),
    };

    /// <summary>
    /// Tables the site no longer has. Promotions held the old promotion codes; promotions are now
    /// automatic rules (Models/PriceCalculator.cs) and the table was dropped after the second review.
    /// </summary>
    public static readonly string[] RetiredTables = { "Promotions" };

    /// <summary>
    /// Tables added after the first version. An older database gets each missing one, built by EF
    /// from the model exactly like a new database (columns, keys, indexes, foreign keys, CHECKs).
    /// </summary>
    public static readonly string[] NewTables = { "RoomReviews", "AuditLogs" };

    /// <summary>Day-only columns: DATE in MySQL (older databases stored them as DATETIME).</summary>
    public static readonly DateColumn[] DateColumns =
    {
        new("HotelBookings", "CheckIn"),
        new("HotelBookings", "CheckOut"),
        new("Users", "Birthday"),
        new("HousekeepingShifts", "Date"),
    };
}
