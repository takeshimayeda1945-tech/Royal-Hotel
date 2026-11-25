-- =====================================================================
-- Royal Hotel (royalhotel.com) - MySQL database + application account
-- Run ONCE as root (PowerShell, in the project folder):
--   Get-Content -Raw Database\01_create_database.sql | & "C:\Program Files\MySQL\MySQL Server 26.7\bin\mysql.exe" -u root -p
-- Then start the web site: it creates the tables itself (or run 02_schema.sql),
-- and 03_royalhotel_dump.sql restores the tables together with the data.
-- =====================================================================

-- utf8mb4 stores Thai text (and emoji) correctly.
CREATE DATABASE IF NOT EXISTS royalhotel
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_0900_ai_ci;

-- The web site connects with this account, never with root.
-- The password must match ConnectionStrings:MySql. RoyalHotel_Dev_2026 is the development default that
-- appsettings.json uses (the account only accepts connections from this computer). For your own password,
-- change it here and put the connection string in appsettings.Local.json (not in git), as README.md shows.
CREATE USER IF NOT EXISTS 'royalhotel_app'@'localhost' IDENTIFIED BY 'RoyalHotel_Dev_2026';
GRANT ALL PRIVILEGES ON royalhotel.* TO 'royalhotel_app'@'localhost';
FLUSH PRIVILEGES;

SELECT 'royalhotel database and royalhotel_app user are ready' AS result;
