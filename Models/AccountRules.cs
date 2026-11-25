using System.ComponentModel.DataAnnotations;

namespace Intouch_67173986_L003.Models;

/// <summary>Rules shared by every form that creates or edits an account.</summary>
public static class AccountRules
{
    public const int MinPasswordLength = 6;
    public const int MaxPasswordLength = 100;

    public static readonly DateTime MinBirthday = new(1900, 1, 1);
}

/// <summary>A birthday must be a real date from 1900 up to today (never in the future).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BirthdayAttribute : ValidationAttribute
{
    public BirthdayAttribute()
        : base("กรุณากรอกวันเกิดที่ถูกต้อง (ตั้งแต่ปี 1900 และไม่เป็นวันในอนาคต)")
    {
    }

    public override bool IsValid(object? value) => value switch
    {
        null => true, // a missing date is reported by [Required]
        DateTime date => date.Date >= AccountRules.MinBirthday && date.Date <= DateTime.Today,
        _ => false
    };
}

/// <summary>
/// The demo account of every role. Created on start-up when the e-mail is missing, and listed on
/// the login page while the site runs in Development (so the course demo can switch roles quickly).
/// </summary>
public static class DemoAccounts
{
    public record Account(string Name, string Email, string Password, string Role, DateTime Birthday, string PhoneNumber);

    public static readonly Account[] All =
    {
        new("Admin Zero", "admin@nightcity.net", "admin123", UserRoles.SuperAdmin, new DateTime(1987, 5, 14), "081-111-1111"),
        new("Hotel Manager", "manager@royalhotel.com", "manager123", UserRoles.Admin, new DateTime(1985, 1, 15), "02-000-0000"),
        new("Front Desk Staff", "staff@royalhotel.com", "staff123", UserRoles.Staff, new DateTime(1995, 3, 10), "02-000-0000"),
        new("Housekeeper Mali", "maid@royalhotel.com", "maid123", UserRoles.Housekeeper, new DateTime(1990, 6, 2), "02-000-0000"),
        new("Finance Officer", "finance@royalhotel.com", "finance123", UserRoles.Finance, new DateTime(1988, 11, 25), "02-000-0000"),
        new("Guest Runner", "guest@nightcity.net", "guest123", UserRoles.Customer, new DateTime(1991, 8, 23), "082-222-2222"),
    };
}
