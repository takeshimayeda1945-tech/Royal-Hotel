-- =====================================================================
-- Royal Hotel (royalhotel.com) - MySQL tables, keys and rules
-- Generated from Data/ApplicationDbContext.cs (EF Core model), so it matches the code exactly:
--   dotnet run -- --print-schema
--
-- You normally do NOT need this file: the web site creates every table on its first start
-- (EnsureCreated) and upgrades a database made by an older version by itself.
-- Use it only to create the tables by hand in an EMPTY royalhotel database. Some CHECK rules contain
-- Thai text, so Windows PowerShell must read and send the file as UTF-8 (otherwise Thai arrives as "?"):
--   $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
--   Get-Content -Raw -Encoding UTF8 Database\02_schema.sql | & "C:\Program Files\MySQL\MySQL Server 26.7\bin\mysql.exe" -u royalhotel_app -p royalhotel
--
-- Tables
--   Users              members and staff (Role = Customer / Staff / Housekeeper / Finance / Admin / Super Admin);
--                      Password holds a salted PBKDF2 hash, never the password itself
--   HotelRooms         rooms and prices (RoomType Deluxe gets the Friday-Saturday discount)
--   HotelBookings      bookings with the saved price breakdown, payment status and check-in/out times;
--                      CancelReason = why it was cancelled: Guest, Staff, NoShow, or PaymentTimeout (booked online
--                      but no payment method chosen within 30 minutes: the site cancels it to free the room)
--                      SpecialRequest = the guest's note for the hotel (max 500 characters, empty when none)
--   HousekeepingTasks  rooms to clean (created when a guest checks out, or asked by the front desk)
--   HousekeepingShifts housekeeper duty shifts
--   RoomReviews        guest reviews (1-5 stars + comment), one per checked-out stay; IsHidden = taken down by an admin
--   AuditLogs          audit log: who did what to which record and when (append-only; name/role copied on each line)
--   RolePortals        one row per role: the http port of that role's portal (Customer 5243 ... Super Admin 5248);
--                      the ports are fixed in Infrastructure/RolePortals.cs; this table is a reference copy that
--                      the site restores on every start (editing it changes nothing)
--
-- Relations (FOREIGN KEY ... ON DELETE)
--   Users.Role                       -> RolePortals.Role  RESTRICT  (a role that users still have cannot be removed)
--   HotelBookings.UserId             -> Users.Id          SET NULL  (a deleted member's bookings stay)
--   HotelBookings.RoomId             -> HotelRooms.Id     RESTRICT  (a room with bookings cannot be deleted)
--   HousekeepingTasks.RoomId         -> HotelRooms.Id     CASCADE
--   HousekeepingTasks.BookingId      -> HotelBookings.Id  SET NULL
--   HousekeepingTasks.AssignedToUserId -> Users.Id        SET NULL
--   HousekeepingShifts.UserId        -> Users.Id          CASCADE
--   RoomReviews.BookingId            -> HotelBookings.Id  CASCADE   (one review per stay)
--   RoomReviews.RoomId               -> HotelRooms.Id     RESTRICT
--   RoomReviews.UserId               -> Users.Id          SET NULL  (a deleted member's reviews stay)
--   AuditLogs.UserId                 -> Users.Id          SET NULL  (log lines stay, with the copied name)
-- CHECK constraints keep every list value valid (roles, booking/payment statuses, payment methods, cancel reasons,
-- room and bed types, housekeeping statuses, shift names, audit actions and record types) and every number (room price, capacity, nights 1-30, guests 1-10,
-- spa 0-10, amounts never negative, ports 1024-65535).
-- UNIQUE: Users.Email, RolePortals.Port, HousekeepingShifts (UserId, Date, ShiftName) and RoomReviews.BookingId.
-- Review stars must be 1-5.
-- Stay dates, birthdays and shift days are DATE columns (a day, never a time of day).
-- =====================================================================

SET NAMES utf8mb4;
USE royalhotel;

CREATE TABLE `HotelRooms` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) NOT NULL,
    `RoomType` varchar(20) NOT NULL,
    `Price` decimal(12,2) NOT NULL,
    `Capacity` int NOT NULL,
    `Area` varchar(100) NOT NULL,
    `ImageUrl` varchar(500) NOT NULL,
    `Tag` varchar(50) NOT NULL,
    `Description` varchar(1000) NOT NULL,
    `BedType` varchar(50) NOT NULL,
    `HasSpa` tinyint(1) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_HotelRooms_BedType` CHECK (BedType IN ('เตียงเดี่ยว', 'เตียงคู่')),
    CONSTRAINT `CK_HotelRooms_Capacity` CHECK (Capacity BETWEEN 1 AND 10),
    CONSTRAINT `CK_HotelRooms_Price` CHECK (Price > 0),
    CONSTRAINT `CK_HotelRooms_RoomType` CHECK (RoomType IN ('Standard', 'Deluxe', 'Suite'))
);


CREATE TABLE `RolePortals` (
    `Role` varchar(50) NOT NULL,
    `Port` int NOT NULL,
    `ThaiName` varchar(50) NOT NULL,
    PRIMARY KEY (`Role`),
    CONSTRAINT `CK_RolePortals_Port` CHECK (Port BETWEEN 1024 AND 65535),
    CONSTRAINT `CK_RolePortals_Role` CHECK (Role IN ('Customer', 'Staff', 'Housekeeper', 'Finance', 'Admin', 'Super Admin'))
);


CREATE TABLE `Users` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) NOT NULL,
    `Email` varchar(200) NOT NULL,
    `PhoneNumber` varchar(50) NOT NULL,
    `Birthday` date NOT NULL,
    `Password` varchar(200) COLLATE utf8mb4_bin NOT NULL,
    `Role` varchar(50) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_Users_Role` CHECK (Role IN ('Customer', 'Staff', 'Housekeeper', 'Finance', 'Admin', 'Super Admin')),
    CONSTRAINT `FK_Users_RolePortals_Role` FOREIGN KEY (`Role`) REFERENCES `RolePortals` (`Role`) ON DELETE RESTRICT
);


CREATE TABLE `AuditLogs` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CreatedAt` datetime(6) NOT NULL,
    `UserId` int NULL,
    `UserName` varchar(100) NOT NULL,
    `UserRole` varchar(50) NOT NULL,
    `Action` varchar(40) NOT NULL,
    `EntityType` varchar(20) NOT NULL,
    `EntityId` int NULL,
    `Details` varchar(1000) NOT NULL,
    `IpAddress` varchar(45) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_AuditLogs_Action` CHECK (Action IN ('WalkInBooking', 'CheckIn', 'CheckOut', 'StaffCancel', 'NoShowClosed', 'GuestCancel', 'AutoCancel', 'PaymentConfirmed', 'CashReceived', 'PaymentRejected', 'RefundPaid', 'RefundClosed', 'CsvExported', 'CleaningRequested', 'CleaningStarted', 'CleaningDone', 'ShiftAdded', 'ShiftDeleted', 'RoomCreated', 'RoomUpdated', 'RoomDeleted', 'UserCreated', 'UserUpdated', 'UserRoleChanged', 'UserPasswordReset', 'UserDeleted', 'PasswordChanged', 'ReviewHidden', 'ReviewShown')),
    CONSTRAINT `CK_AuditLogs_EntityType` CHECK (EntityType IN ('Booking', 'Room', 'User', 'Shift', 'CleaningTask', 'Review', 'Report')),
    CONSTRAINT `FK_AuditLogs_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE SET NULL
);


CREATE TABLE `HotelBookings` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NULL,
    `GuestName` varchar(100) NOT NULL,
    `ContactEmail` varchar(200) NOT NULL,
    `RoomId` int NOT NULL,
    `RoomName` varchar(100) NOT NULL,
    `CheckIn` date NOT NULL,
    `CheckOut` date NOT NULL,
    `Guests` int NOT NULL,
    `Nights` int NOT NULL,
    `BedType` varchar(50) NOT NULL,
    `IncludeBreakfast` tinyint(1) NOT NULL,
    `ExtraBed` tinyint(1) NOT NULL,
    `IncludeSpa` tinyint(1) NOT NULL,
    `SpaSessions` int NOT NULL,
    `SpecialRequest` varchar(500) NOT NULL DEFAULT '',
    `PricePerNight` decimal(12,2) NOT NULL,
    `RoomTotal` decimal(12,2) NOT NULL,
    `ServiceTotal` decimal(12,2) NOT NULL,
    `Discount` decimal(12,2) NOT NULL,
    `DiscountDetails` varchar(1000) NOT NULL,
    `Tax` decimal(12,2) NOT NULL,
    `Total` decimal(12,2) NOT NULL,
    `PaymentMethod` varchar(50) NOT NULL,
    `PaymentStatus` varchar(20) NOT NULL,
    `PaymentReference` varchar(100) NOT NULL,
    `PaidAt` datetime(6) NULL,
    `Status` varchar(20) NOT NULL,
    `CancelReason` varchar(20) NOT NULL DEFAULT '',
    `CreatedAt` datetime(6) NOT NULL,
    `CheckedInAt` datetime(6) NULL,
    `CheckedOutAt` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_HotelBookings_Amounts` CHECK (Total >= 0 AND Discount >= 0 AND Tax >= 0),
    CONSTRAINT `CK_HotelBookings_BedType` CHECK (BedType IN ('เตียงเดี่ยว', 'เตียงคู่')),
    CONSTRAINT `CK_HotelBookings_CancelReason` CHECK (CancelReason IN ('', 'Guest', 'Staff', 'NoShow', 'PaymentTimeout')),
    CONSTRAINT `CK_HotelBookings_Dates` CHECK (CheckOut > CheckIn),
    CONSTRAINT `CK_HotelBookings_Guests` CHECK (Guests >= 1),
    CONSTRAINT `CK_HotelBookings_PaymentMethod` CHECK (PaymentMethod IN ('', 'เงินสด', 'QR Code', 'บัตรเดบิต', 'บัตรเครดิต')),
    CONSTRAINT `CK_HotelBookings_PaymentStatus` CHECK (PaymentStatus IN ('Unpaid', 'Pending', 'Paid', 'RefundPending', 'Refunded')),
    CONSTRAINT `CK_HotelBookings_Prices` CHECK (PricePerNight >= 0 AND RoomTotal >= 0 AND ServiceTotal >= 0),
    CONSTRAINT `CK_HotelBookings_Status` CHECK (Status IN ('Confirmed', 'CheckedIn', 'CheckedOut', 'Cancelled')),
    CONSTRAINT `CK_HotelBookings_Stay` CHECK (Nights >= 1 AND Nights <= 30 AND Guests <= 10 AND SpaSessions BETWEEN 0 AND 10),
    CONSTRAINT `FK_HotelBookings_HotelRooms_RoomId` FOREIGN KEY (`RoomId`) REFERENCES `HotelRooms` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_HotelBookings_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE SET NULL
);


CREATE TABLE `HousekeepingShifts` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `UserName` varchar(100) NOT NULL,
    `Date` date NOT NULL,
    `ShiftName` varchar(50) NOT NULL,
    `Note` varchar(200) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_HousekeepingShifts_ShiftName` CHECK (ShiftName IN ('เช้า 07:00–15:00', 'บ่าย 15:00–23:00', 'ดึก 23:00–07:00')),
    CONSTRAINT `FK_HousekeepingShifts_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
);


CREATE TABLE `HousekeepingTasks` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RoomId` int NOT NULL,
    `RoomName` varchar(100) NOT NULL,
    `BookingId` int NULL,
    `Note` varchar(200) NOT NULL,
    `Status` varchar(20) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `AssignedToUserId` int NULL,
    `AssignedToName` varchar(100) NOT NULL,
    `CompletedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_HousekeepingTasks_Status` CHECK (Status IN ('Pending', 'InProgress', 'Done')),
    CONSTRAINT `FK_HousekeepingTasks_HotelBookings_BookingId` FOREIGN KEY (`BookingId`) REFERENCES `HotelBookings` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `FK_HousekeepingTasks_HotelRooms_RoomId` FOREIGN KEY (`RoomId`) REFERENCES `HotelRooms` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_HousekeepingTasks_Users_AssignedToUserId` FOREIGN KEY (`AssignedToUserId`) REFERENCES `Users` (`Id`) ON DELETE SET NULL
);


CREATE TABLE `RoomReviews` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BookingId` int NOT NULL,
    `RoomId` int NOT NULL,
    `UserId` int NULL,
    `ReviewerName` varchar(100) NOT NULL,
    `Rating` int NOT NULL,
    `Comment` varchar(1000) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NULL,
    `IsHidden` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `CK_RoomReviews_Rating` CHECK (Rating BETWEEN 1 AND 5),
    CONSTRAINT `FK_RoomReviews_HotelBookings_BookingId` FOREIGN KEY (`BookingId`) REFERENCES `HotelBookings` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_RoomReviews_HotelRooms_RoomId` FOREIGN KEY (`RoomId`) REFERENCES `HotelRooms` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_RoomReviews_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE SET NULL
);


CREATE INDEX `IX_AuditLogs_Action` ON `AuditLogs` (`Action`);


CREATE INDEX `IX_AuditLogs_CreatedAt` ON `AuditLogs` (`CreatedAt`);


CREATE INDEX `IX_AuditLogs_EntityType_EntityId` ON `AuditLogs` (`EntityType`, `EntityId`);


CREATE INDEX `IX_AuditLogs_UserId` ON `AuditLogs` (`UserId`);


CREATE INDEX `IX_HotelBookings_PaymentStatus` ON `HotelBookings` (`PaymentStatus`);


CREATE INDEX `IX_HotelBookings_RoomId_CheckIn_CheckOut` ON `HotelBookings` (`RoomId`, `CheckIn`, `CheckOut`);


CREATE INDEX `IX_HotelBookings_Status` ON `HotelBookings` (`Status`);


CREATE INDEX `IX_HotelBookings_UserId` ON `HotelBookings` (`UserId`);


CREATE UNIQUE INDEX `IX_HousekeepingShifts_UserId_Date_ShiftName` ON `HousekeepingShifts` (`UserId`, `Date`, `ShiftName`);


CREATE INDEX `IX_HousekeepingTasks_AssignedToUserId` ON `HousekeepingTasks` (`AssignedToUserId`);


CREATE INDEX `IX_HousekeepingTasks_BookingId` ON `HousekeepingTasks` (`BookingId`);


CREATE INDEX `IX_HousekeepingTasks_RoomId_Status` ON `HousekeepingTasks` (`RoomId`, `Status`);


CREATE INDEX `IX_HousekeepingTasks_Status` ON `HousekeepingTasks` (`Status`);


CREATE UNIQUE INDEX `IX_RolePortals_Port` ON `RolePortals` (`Port`);


CREATE UNIQUE INDEX `IX_RoomReviews_BookingId` ON `RoomReviews` (`BookingId`);


CREATE INDEX `IX_RoomReviews_RoomId_IsHidden` ON `RoomReviews` (`RoomId`, `IsHidden`);


CREATE INDEX `IX_RoomReviews_UserId` ON `RoomReviews` (`UserId`);


CREATE UNIQUE INDEX `IX_Users_Email` ON `Users` (`Email`);


CREATE INDEX `IX_Users_Role` ON `Users` (`Role`);



