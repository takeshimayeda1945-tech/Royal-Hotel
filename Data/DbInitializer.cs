using System.Data;
using Intouch_67173986_L003.Infrastructure;
using Intouch_67173986_L003.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Intouch_67173986_L003.Data;

/// <summary>
/// Runs once when the site starts: creates the database (or brings an older one up to date),
/// adds the demo accounts and rooms, and converts any plain-text password into a hash.
/// Every step is safe to run again.
/// </summary>
public static class DbInitializer
{
    public static void Initialize(ApplicationDbContext db, ILogger logger)
    {
        // A new database gets every table, key, index and constraint from the EF model.
        db.Database.EnsureCreated();

        // EnsureCreated() does nothing on an existing database, so older ones are upgraded here.
        // RolePortals comes first: Users.Role is a foreign key to it, so it needs its rows
        // before that key is added and before any user is created.
        if (db.IsMySql)
        {
            CreateMySqlRolePortalsTable(db);
        }
        else
        {
            UpgradeSqliteSchema(db, logger);
        }

        // Before the MySQL upgrade below, which adds missing CHECK rules: the new tables already have theirs.
        CreateMissingTables(db, logger);

        SyncRolePortals(db, logger);

        if (db.IsMySql)
        {
            UpgradeMySqlSchema(db, logger);
        }

        SeedAccountsAndRooms(db);
        FixFutureDemoBirthdays(db);
        HashPlainTextPasswords(db, logger);
        SeedDemoShifts(db);
    }

    // ------------------------------------------------------------------ demo data

    /// <summary>
    /// One row per role with the port of its portal. The ports are fixed in the code
    /// (Infrastructure/RolePortals.cs) and the site never reads them from this table, so a row
    /// edited by hand changes nothing and is put back here on the next start.
    /// </summary>
    private static void SyncRolePortals(ApplicationDbContext db, ILogger logger)
    {
        var rows = db.RolePortals.ToDictionary(p => p.Role);
        var changed = RolePortals.Fixed.All
            .Where(p => !rows.TryGetValue(p.Role, out var row) || row.Port != p.Port || row.ThaiName != UserRoles.ThaiName(p.Role))
            .ToList();
        if (changed.Count == 0)
        {
            return;
        }

        using var transaction = db.Database.BeginTransaction();

        // Ports are UNIQUE: rows that move first step aside to a free port, so two rows can swap.
        var parked = false;
        var spare = 65535;
        foreach (var (role, _) in changed)
        {
            if (rows.TryGetValue(role, out var row))
            {
                while (rows.Values.Any(r => r.Port == spare))
                {
                    spare--;
                }

                row.Port = spare--;
                parked = true;
            }
        }

        if (parked)
        {
            db.SaveChanges();
        }

        foreach (var (role, port) in changed)
        {
            if (!rows.TryGetValue(role, out var row))
            {
                row = new RolePortal { Role = role };
                db.RolePortals.Add(row);
            }

            row.Port = port;
            row.ThaiName = UserRoles.ThaiName(role);
        }

        db.SaveChanges();
        transaction.Commit();

        if (rows.Count > 0)
        {
            logger.LogWarning("RolePortals did not match the fixed ports; restored: {Roles}", string.Join(", ", changed.Select(c => c.Role)));
        }
    }

    private static void SeedAccountsAndRooms(ApplicationDbContext db)
    {
        // The default Super Admin always keeps its role (so there is always a way in), but a name or
        // password changed from the User List page is not overwritten.
        var superAdmin = DemoAccounts.All.First(a => a.Role == UserRoles.SuperAdmin);
        var adminUser = db.Users.FirstOrDefault(u => u.Email == superAdmin.Email);
        if (adminUser != null && adminUser.Role != UserRoles.SuperAdmin)
        {
            adminUser.Role = UserRoles.SuperAdmin;
        }

        // One demo account per role, created only when its e-mail is missing.
        var existingEmails = db.Users.Select(u => u.Email.ToLower()).ToHashSet();
        foreach (var account in DemoAccounts.All.Where(a => !existingEmails.Contains(a.Email)))
        {
            db.Users.Add(new CyberUser
            {
                Name = account.Name,
                Email = account.Email,
                PhoneNumber = account.PhoneNumber,
                Birthday = account.Birthday,
                Password = PasswordSecurity.Hash(account.Password),
                Role = account.Role
            });
        }

        if (!db.HotelRooms.Any())
        {
            db.HotelRooms.AddRange(
                new HotelRoom { Name = "Royal Deluxe", RoomType = RoomTypes.Deluxe, Price = 3200, Capacity = 2, Area = "Downtown", ImageUrl = "https://images.unsplash.com/photo-1505693416388-ac5ce068fe85?auto=format&fit=crop&w=900&q=80", Tag = "Popular", Description = "ห้องดีลักซ์สำหรับคู่รัก พร้อมวิวเมือง", BedType = BookingOptions.DoubleBed, HasSpa = true },
                new HotelRoom { Name = "Royal Horizon", RoomType = RoomTypes.Standard, Price = 2800, Capacity = 2, Area = "Skyline", ImageUrl = "https://images.unsplash.com/photo-1494526585095-c41746248156?auto=format&fit=crop&w=900&q=80", Tag = "Value", Description = "ห้องพักมุมสูง ประหยัดและสะดวกสบาย", BedType = BookingOptions.SingleBed, HasSpa = false },
                new HotelRoom { Name = "Royal Suite", RoomType = RoomTypes.Suite, Price = 4200, Capacity = 4, Area = "Sea Harbor", ImageUrl = "https://images.unsplash.com/photo-1564501049412-61c2a3083791?auto=format&fit=crop&w=900&q=80", Tag = "VIP", Description = "สวีทพรีเมียมสำหรับครอบครัวและแขกกลุ่ม", BedType = BookingOptions.DoubleBed, HasSpa = true }
            );
        }

        db.SaveChanges();
    }

    /// <summary>
    /// The first demo accounts had birthdays in 2087 and 2091. A birthday cannot be in the future,
    /// so those are moved back 100 years (same day and month, so the birthday promotion is unchanged).
    /// </summary>
    private static void FixFutureDemoBirthdays(ApplicationDbContext db)
    {
        var demoEmails = DemoAccounts.All.Select(a => a.Email).ToList();
        var today = DateTime.Today;
        var users = db.Users.Where(u => demoEmails.Contains(u.Email) && u.Birthday > today).ToList();
        foreach (var user in users)
        {
            while (user.Birthday > today)
            {
                user.Birthday = user.Birthday.AddYears(-100);
            }
        }

        db.SaveChanges();
    }

    /// <summary>Rows saved before passwords were hashed still hold the password itself.</summary>
    private static void HashPlainTextPasswords(ApplicationDbContext db, ILogger logger)
    {
        var plainText = db.Users.ToList().Where(u => !PasswordSecurity.IsHash(u.Password)).ToList();
        foreach (var user in plainText)
        {
            user.Password = PasswordSecurity.Hash(user.Password);
        }

        if (plainText.Count > 0)
        {
            db.SaveChanges();
            logger.LogInformation("Stored {Count} plain-text password(s) as hashes.", plainText.Count);
        }
    }

    /// <summary>A week of demo shifts for the housekeeper, so the duty page is not empty on a new database.</summary>
    private static void SeedDemoShifts(ApplicationDbContext db)
    {
        var maidEmail = DemoAccounts.All.First(a => a.Role == UserRoles.Housekeeper).Email;
        if (db.HousekeepingShifts.Any() || db.Users.FirstOrDefault(u => u.Email == maidEmail) is not { } maid)
        {
            return;
        }

        for (var day = 0; day < 7; day++)
        {
            db.HousekeepingShifts.Add(new HousekeepingShift
            {
                UserId = maid.Id,
                UserName = maid.Name,
                Date = DateTime.Today.AddDays(day),
                ShiftName = day % 3 == 2 ? ShiftNames.Afternoon : ShiftNames.Morning
            });
        }

        db.SaveChanges();
    }

    // ------------------------------------------------------------------ new tables (both providers)

    /// <summary>
    /// Tables added after the first version (<see cref="DbSchema.NewTables"/>): a database made by an
    /// older version gets each missing one. EF writes the SQL from the model, the same way
    /// EnsureCreated does for a new database, so the table has exactly the same columns, keys,
    /// indexes, foreign keys and CHECK rules (MySQL and SQLite alike). Existing tables are not touched.
    /// Not skipped on error: the pages that use these tables cannot work without them.
    /// </summary>
    private static void CreateMissingTables(ApplicationDbContext db, ILogger logger)
    {
        var missing = DbSchema.NewTables
            .Where(table => db.IsMySql
                ? !MySqlTableExists(db, table)
                : QueryCount(db, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;", ("@table", table)) == 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (missing.Count == 0)
        {
            return;
        }

        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel())
            .Where(operation => operation switch
            {
                CreateTableOperation table => missing.Contains(table.Name),
                CreateIndexOperation index => missing.Contains(index.Table),
                _ => false
            })
            .ToList();

        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
        db.GetService<IMigrationCommandExecutor>().ExecuteNonQuery(commands, db.GetService<IRelationalConnection>());
        logger.LogInformation("Database upgrade: table(s) {Tables} created.", string.Join(", ", missing));
    }

    // ------------------------------------------------------------------ MySQL upgrade

    /// <summary>
    /// Databases made before the RolePortals table existed get it here (same definition as the EF
    /// model, see Database/02_schema.sql). Not skipped on error: the site cannot start without it.
    /// </summary>
    private static void CreateMySqlRolePortalsTable(ApplicationDbContext db)
    {
        var roleCheck = DbSchema.Checks.First(c => c.Name == "CK_RolePortals_Role");
        var portCheck = DbSchema.Checks.First(c => c.Name == "CK_RolePortals_Port");
        // Every part comes from constants (DbSchema / this file), never from user input.
#pragma warning disable EF1002
        db.Database.ExecuteSqlRaw($@"
            CREATE TABLE IF NOT EXISTS `RolePortals` (
                `Role` varchar(50) NOT NULL,
                `Port` int NOT NULL,
                `ThaiName` varchar(50) NOT NULL,
                PRIMARY KEY (`Role`),
                UNIQUE KEY `IX_RolePortals_Port` (`Port`),
                CONSTRAINT `{portCheck.Name}` CHECK ({portCheck.Sql}),
                CONSTRAINT `{roleCheck.Name}` CHECK ({roleCheck.Sql})
            );");
#pragma warning restore EF1002
    }

    /// <summary>
    /// Adds the columns, indexes, foreign keys and CHECK constraints that a database created by an
    /// older version of the site does not have yet, and drops tables the site no longer uses. A step
    /// that fails (e.g. old rows break a rule) is logged and skipped; the site keeps working, the rule
    /// is simply checked by the code only. Only a missing column stops the start.
    /// </summary>
    private static void UpgradeMySqlSchema(ApplicationDbContext db, ILogger logger)
    {
        // New columns first: the site reads them on every page. Not skipped on error (see NewColumns).
        foreach (var column in DbSchema.NewColumns)
        {
            if (!MySqlColumnExists(db, column.Table, column.Name))
            {
#pragma warning disable EF1002 // every part comes from DbSchema (constants), never from user input
                var affected = db.Database.ExecuteSqlRaw(
                    $"ALTER TABLE `{column.Table}` ADD COLUMN `{column.Name}` {column.MySqlType} AFTER `{column.After}`;");
#pragma warning restore EF1002
                logger.LogInformation("Database upgrade: column {Table}.{Column} added ({Rows} row(s)).", column.Table, column.Name, affected);
            }
        }

        foreach (var table in DbSchema.RetiredTables)
        {
            if (MySqlTableExists(db, table))
            {
                TryExecute(db, logger, $"DROP TABLE `{table}`;", $"drop unused table {table}");
            }
        }

        foreach (var index in DbSchema.NewIndexes)
        {
            if (!MySqlIndexExists(db, index.Table, index.Name))
            {
                // A UNIQUE index cannot be added while old rows break it: logged and skipped then.
                var columns = string.Join(", ", index.Columns.Split(',').Select(c => $"`{c.Trim()}`"));
                var unique = index.Unique ? "UNIQUE " : string.Empty;
                TryExecute(db, logger, $"CREATE {unique}INDEX `{index.Name}` ON `{index.Table}` ({columns});", $"index {index.Name}");
            }
        }

        // An index that a newer one replaced goes once the newer one exists (never before: the
        // foreign key on its first column needs one of the two).
        foreach (var (old, replacedBy) in DbSchema.RetiredIndexes)
        {
            if (MySqlIndexExists(db, old.Table, old.Name) && MySqlIndexExists(db, old.Table, replacedBy))
            {
                TryExecute(db, logger, $"DROP INDEX `{old.Name}` ON `{old.Table}`;", $"drop index {old.Name} (replaced by {replacedBy})");
            }
        }

        ConvertMySqlDateColumns(db, logger);

        foreach (var fk in DbSchema.ForeignKeys)
        {
            if (MySqlConstraintExists(db, fk.Table, fk.Name))
            {
                continue;
            }

            // Rows that still point to something deleted earlier would block the new key.
            var orphan = $"`{fk.Column}` IS NOT NULL AND NOT EXISTS " +
                         $"(SELECT 1 FROM `{fk.PrincipalTable}` p WHERE p.`Id` = `{fk.Table}`.`{fk.Column}`)";
            if (fk.OnDelete == "SET NULL")
            {
                TryExecute(db, logger, $"UPDATE `{fk.Table}` SET `{fk.Column}` = NULL WHERE {orphan};", $"cleanup for {fk.Name}");
            }
            else if (fk.OnDelete == "CASCADE")
            {
                TryExecute(db, logger, $"DELETE FROM `{fk.Table}` WHERE {orphan};", $"cleanup for {fk.Name}");
            }
            else if (QueryCount(db, $"SELECT COUNT(*) FROM `{fk.Table}` WHERE {orphan};") > 0)
            {
                logger.LogWarning("Database upgrade: {Key} skipped, some {Table} rows point to a {Principal} row that no longer exists.",
                    fk.Name, fk.Table, fk.PrincipalTable);
                continue;
            }

            TryExecute(db, logger,
                $"ALTER TABLE `{fk.Table}` ADD CONSTRAINT `{fk.Name}` FOREIGN KEY (`{fk.Column}`) " +
                $"REFERENCES `{fk.PrincipalTable}` (`Id`) ON DELETE {fk.OnDelete};",
                $"foreign key {fk.Name}");
        }

        // The site treats an unknown role as a customer; store it that way before the rule is added.
        var roleCheck = DbSchema.Checks.First(c => c.Name == "CK_Users_Role");
        if (!MySqlConstraintExists(db, roleCheck.Table, roleCheck.Name))
        {
            TryExecute(db, logger, $"UPDATE `Users` SET `Role` = '{UserRoles.Customer}' WHERE NOT ({roleCheck.Sql});", "role cleanup");
        }

        foreach (var check in DbSchema.Checks)
        {
            if (!MySqlConstraintExists(db, check.Table, check.Name))
            {
                TryExecute(db, logger, $"ALTER TABLE `{check.Table}` ADD CONSTRAINT `{check.Name}` CHECK ({check.Sql});", $"check {check.Name}");
            }
        }

        // Users.Role -> RolePortals.Role (after the role cleanup above, so every role has its row).
        if (!MySqlConstraintExists(db, "Users", "FK_Users_RolePortals_Role"))
        {
            TryExecute(db, logger,
                "ALTER TABLE `Users` ADD CONSTRAINT `FK_Users_RolePortals_Role` FOREIGN KEY (`Role`) " +
                "REFERENCES `RolePortals` (`Role`) ON DELETE RESTRICT;",
                "foreign key FK_Users_RolePortals_Role");
        }
    }

    /// <summary>
    /// Stay dates, birthdays and shift days were DATETIME columns in older databases; they become
    /// DATE. A column is only converted when none of its rows has a time of day, because the
    /// conversion would silently drop it.
    /// </summary>
    private static void ConvertMySqlDateColumns(ApplicationDbContext db, ILogger logger)
    {
        foreach (var column in DbSchema.DateColumns)
        {
            var raw = QueryScalar(db,
                "SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
                "AND LOWER(TABLE_NAME) = LOWER(@table) AND LOWER(COLUMN_NAME) = LOWER(@column);",
                ("@table", column.Table), ("@column", column.Column));
            var type = raw is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : raw as string;
            if (!string.Equals(type, "datetime", StringComparison.OrdinalIgnoreCase))
            {
                continue; // already DATE (or the column is missing)
            }

            if (QueryCount(db, $"SELECT COUNT(*) FROM `{column.Table}` WHERE `{column.Column}` <> DATE(`{column.Column}`);") > 0)
            {
                logger.LogWarning("Database upgrade: {Table}.{Column} stays DATETIME, some rows have a time of day.",
                    column.Table, column.Column);
                continue;
            }

            TryExecute(db, logger, $"ALTER TABLE `{column.Table}` MODIFY `{column.Column}` date NOT NULL;",
                $"{column.Table}.{column.Column} as DATE");
        }
    }

    private static bool MySqlIndexExists(ApplicationDbContext db, string table, string index)
    {
        // lower_case_table_names differs between Windows and Linux, so compare without case.
        return QueryCount(db,
            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND LOWER(TABLE_NAME) = LOWER(@table) AND LOWER(INDEX_NAME) = LOWER(@name);",
            ("@table", table), ("@name", index)) > 0;
    }

    private static bool MySqlTableExists(ApplicationDbContext db, string table)
    {
        return QueryCount(db,
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = LOWER(@table);",
            ("@table", table)) > 0;
    }

    private static bool MySqlColumnExists(ApplicationDbContext db, string table, string column)
    {
        return QueryCount(db,
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() " +
            "AND LOWER(TABLE_NAME) = LOWER(@table) AND LOWER(COLUMN_NAME) = LOWER(@column);",
            ("@table", table), ("@column", column)) > 0;
    }

    private static bool MySqlConstraintExists(ApplicationDbContext db, string table, string constraint)
    {
        return QueryCount(db,
            "SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() " +
            "AND LOWER(TABLE_NAME) = LOWER(@table) AND LOWER(CONSTRAINT_NAME) = LOWER(@name);",
            ("@table", table), ("@name", constraint)) > 0;
    }

    private static void TryExecute(ApplicationDbContext db, ILogger logger, string sql, string what)
    {
        try
        {
            // Table and column names come from DbSchema (constants), never from user input.
            var affected = db.Database.ExecuteSqlRaw(sql);
            logger.LogInformation("Database upgrade: {What} done ({Rows} row(s)).", what, affected);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database upgrade: {What} failed and was skipped. The site keeps working without it.", what);
        }
    }

    // ------------------------------------------------------------------ SQLite (fallback database)

    /// <summary>
    /// The old cyber_users.db was created before some tables/columns existed. New tables and
    /// columns are added by hand; existing rows are kept and new columns get safe defaults.
    /// </summary>
    private static void UpgradeSqliteSchema(ApplicationDbContext db, ILogger logger)
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""HotelRooms"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_HotelRooms"" PRIMARY KEY AUTOINCREMENT,
                ""Name"" TEXT NOT NULL,
                ""Price"" TEXT NOT NULL,
                ""Capacity"" INTEGER NOT NULL DEFAULT 2,
                ""Area"" TEXT NOT NULL,
                ""ImageUrl"" TEXT NOT NULL,
                ""Tag"" TEXT NOT NULL,
                ""Description"" TEXT NOT NULL,
                ""BedType"" TEXT NOT NULL DEFAULT 'เตียงคู่',
                ""HasSpa"" INTEGER NOT NULL DEFAULT 0,
                ""IsActive"" INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS ""HotelBookings"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_HotelBookings"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" INTEGER NULL,
                ""GuestName"" TEXT NOT NULL,
                ""ContactEmail"" TEXT NOT NULL,
                ""RoomId"" INTEGER NOT NULL,
                ""RoomName"" TEXT NOT NULL,
                ""CheckIn"" TEXT NOT NULL,
                ""CheckOut"" TEXT NOT NULL,
                ""Guests"" INTEGER NOT NULL,
                ""Nights"" INTEGER NOT NULL,
                ""Total"" TEXT NOT NULL,
                ""PaymentMethod"" TEXT NOT NULL DEFAULT 'เงินสด',
                ""BedType"" TEXT NOT NULL DEFAULT 'เตียงคู่',
                ""IncludeSpa"" INTEGER NOT NULL DEFAULT 0,
                ""Status"" TEXT NOT NULL DEFAULT 'Confirmed',
                ""CreatedAt"" TEXT NOT NULL
            );
        ");

        if (!SqliteTableHasColumn(db, "Users", "Role"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN Role TEXT NOT NULL DEFAULT 'Customer';");
        }

        db.Database.ExecuteSqlRaw("UPDATE Users SET Role = 'Customer' WHERE Role IS NULL OR TRIM(Role) = '';");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""HousekeepingTasks"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_HousekeepingTasks"" PRIMARY KEY AUTOINCREMENT,
                ""RoomId"" INTEGER NOT NULL,
                ""RoomName"" TEXT NOT NULL,
                ""BookingId"" INTEGER NULL,
                ""Note"" TEXT NOT NULL DEFAULT '',
                ""Status"" TEXT NOT NULL DEFAULT 'Pending',
                ""CreatedAt"" TEXT NOT NULL,
                ""AssignedToUserId"" INTEGER NULL,
                ""AssignedToName"" TEXT NOT NULL DEFAULT '',
                ""CompletedAt"" TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS ""RolePortals"" (
                ""Role"" TEXT NOT NULL CONSTRAINT ""PK_RolePortals"" PRIMARY KEY,
                ""Port"" INTEGER NOT NULL,
                ""ThaiName"" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RolePortals_Port"" ON ""RolePortals"" (""Port"");

            CREATE TABLE IF NOT EXISTS ""HousekeepingShifts"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_HousekeepingShifts"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" INTEGER NOT NULL,
                ""UserName"" TEXT NOT NULL,
                ""Date"" TEXT NOT NULL,
                ""ShiftName"" TEXT NOT NULL,
                ""Note"" TEXT NOT NULL DEFAULT ''
            );
        ");

        if (!SqliteTableHasColumn(db, "HotelRooms", "RoomType"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE HotelRooms ADD COLUMN RoomType TEXT NOT NULL DEFAULT 'Standard';");
            // Existing rooms: guess the type from the name once (editable afterwards on the Rooms page).
            db.Database.ExecuteSqlRaw("UPDATE HotelRooms SET RoomType = 'Deluxe' WHERE lower(Name) LIKE '%deluxe%';");
            db.Database.ExecuteSqlRaw("UPDATE HotelRooms SET RoomType = 'Suite' WHERE lower(Name) LIKE '%suite%';");
        }

        AddSqliteColumnIfMissing(db, "HotelBookings", "IncludeBreakfast", "INTEGER NOT NULL DEFAULT 0");
        AddSqliteColumnIfMissing(db, "HotelBookings", "ExtraBed", "INTEGER NOT NULL DEFAULT 0");
        AddSqliteColumnIfMissing(db, "HotelBookings", "PricePerNight", "TEXT NOT NULL DEFAULT '0'");
        AddSqliteColumnIfMissing(db, "HotelBookings", "RoomTotal", "TEXT NOT NULL DEFAULT '0'");
        AddSqliteColumnIfMissing(db, "HotelBookings", "ServiceTotal", "TEXT NOT NULL DEFAULT '0'");
        AddSqliteColumnIfMissing(db, "HotelBookings", "Discount", "TEXT NOT NULL DEFAULT '0'");
        AddSqliteColumnIfMissing(db, "HotelBookings", "DiscountDetails", "TEXT NOT NULL DEFAULT ''");
        AddSqliteColumnIfMissing(db, "HotelBookings", "Tax", "TEXT NOT NULL DEFAULT '0'");
        AddSqliteColumnIfMissing(db, "HotelBookings", "PaymentReference", "TEXT NOT NULL DEFAULT ''");
        AddSqliteColumnIfMissing(db, "HotelBookings", "PaidAt", "TEXT NULL");
        AddSqliteColumnIfMissing(db, "HotelBookings", "CheckedInAt", "TEXT NULL");
        AddSqliteColumnIfMissing(db, "HotelBookings", "CheckedOutAt", "TEXT NULL");
        foreach (var column in DbSchema.NewColumns)
        {
            AddSqliteColumnIfMissing(db, column.Table, column.Name, column.SqliteType);
        }

        foreach (var table in DbSchema.RetiredTables)
        {
            if (QueryCount(db, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;", ("@table", table)) > 0)
            {
#pragma warning disable EF1002 // the name comes from DbSchema (a constant)
                db.Database.ExecuteSqlRaw($"DROP TABLE \"{table}\";");
#pragma warning restore EF1002
                logger.LogInformation("Database upgrade: unused table {Table} dropped.", table);
            }
        }

        if (!SqliteTableHasColumn(db, "HotelBookings", "SpaSessions"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE HotelBookings ADD COLUMN SpaSessions INTEGER NOT NULL DEFAULT 0;");
            db.Database.ExecuteSqlRaw("UPDATE HotelBookings SET SpaSessions = 1 WHERE IncludeSpa = 1;");
        }

        if (!SqliteTableHasColumn(db, "HotelBookings", "PaymentStatus"))
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE HotelBookings ADD COLUMN PaymentStatus TEXT NOT NULL DEFAULT 'Unpaid';");
            // Bookings made before the payment step existed were paid while booking.
            db.Database.ExecuteSqlRaw("UPDATE HotelBookings SET PaymentStatus = 'Paid', PaidAt = CreatedAt WHERE Status <> 'Cancelled';");
        }

        RemoveDuplicateSqliteEmails(db);

        db.Database.ExecuteSqlRaw(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Users_Email"" ON ""Users"" (""Email"");
            CREATE INDEX IF NOT EXISTS ""IX_HotelBookings_PaymentStatus"" ON ""HotelBookings"" (""PaymentStatus"");
            CREATE INDEX IF NOT EXISTS ""IX_HousekeepingTasks_RoomId_Status"" ON ""HousekeepingTasks"" (""RoomId"", ""Status"");
        ");

        // One row per housekeeper, day and shift (skipped while old rows still break the rule).
        try
        {
            db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_HousekeepingShifts_UserId_Date_ShiftName""
                ON ""HousekeepingShifts"" (""UserId"", ""Date"", ""ShiftName"");");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            logger.LogWarning(ex, "Database upgrade: unique shift index skipped (the same shift is in the table twice).");
        }
    }

    /// <summary>Very old SQLite files allowed the same e-mail twice; keep the oldest account.</summary>
    private static void RemoveDuplicateSqliteEmails(ApplicationDbContext db)
    {
        var duplicateEmails = db.Users
            .GroupBy(u => u.Email)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var email in duplicateEmails)
        {
            var duplicates = db.Users
                .Where(u => u.Email == email)
                .OrderBy(u => u.Id)
                .Skip(1)
                .ToList();

            db.Users.RemoveRange(duplicates);
        }

        db.SaveChanges();
    }

    private static void AddSqliteColumnIfMissing(ApplicationDbContext db, string table, string column, string definition)
    {
        if (!SqliteTableHasColumn(db, table, column))
        {
            // Table, column and definition are constants from this file, never user input.
#pragma warning disable EF1002
            db.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};");
#pragma warning restore EF1002
        }
    }

    private static bool SqliteTableHasColumn(ApplicationDbContext db, string tableName, string columnName)
    {
        return QueryCount(db,
            "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column;",
            ("@table", tableName), ("@column", columnName)) > 0;
    }

    // ------------------------------------------------------------------ helpers

    private static long QueryCount(ApplicationDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        return Convert.ToInt64(QueryScalar(db, sql, parameters));
    }

    private static object? QueryScalar(ApplicationDbContext db, string sql, params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            var result = command.ExecuteScalar();
            return result is DBNull ? null : result;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }
}
