using System.ComponentModel.DataAnnotations;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// Add / edit form of the Super Admin user list. Kept apart from <see cref="CyberUser"/> so the
/// form never touches the stored password hash: an empty password box means "keep the current one".
/// </summary>
public class UserFormModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "กรุณากรอกชื่อ")]
    [StringLength(100, ErrorMessage = "ชื่อต้องไม่เกิน {1} ตัวอักษร")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    [StringLength(200, ErrorMessage = "อีเมลต้องไม่เกิน {1} ตัวอักษร")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกเบอร์โทร")]
    [Phone(ErrorMessage = "รูปแบบเบอร์โทรไม่ถูกต้อง")]
    [StringLength(50, ErrorMessage = "เบอร์โทรต้องไม่เกิน {1} ตัวอักษร")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณาเลือกวันเกิด")]
    [Birthday]
    [DataType(DataType.Date)]
    public DateTime? Birthday { get; set; }

    /// <summary>Required for a new user. On the edit form an empty box keeps the current password.</summary>
    [StringLength(AccountRules.MaxPasswordLength, ErrorMessage = "รหัสผ่านต้องไม่เกิน {1} ตัวอักษร")]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public string Role { get; set; } = UserRoles.Customer;

    public static UserFormModel From(CyberUser user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        Birthday = user.Birthday,
        Role = user.Role
    };
}

/// <summary>"My account": the details a member may change themselves.</summary>
public class ProfileFormModel
{
    [Required(ErrorMessage = "กรุณากรอกชื่อ")]
    [StringLength(100, ErrorMessage = "ชื่อต้องไม่เกิน {1} ตัวอักษร")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกเบอร์โทร")]
    [Phone(ErrorMessage = "รูปแบบเบอร์โทรไม่ถูกต้อง")]
    [StringLength(50, ErrorMessage = "เบอร์โทรต้องไม่เกิน {1} ตัวอักษร")]
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ChangePasswordModel
{
    [Required(ErrorMessage = "กรุณากรอกรหัสผ่านปัจจุบัน")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกรหัสผ่านใหม่")]
    [StringLength(AccountRules.MaxPasswordLength, ErrorMessage = "รหัสผ่านต้องไม่เกิน {1} ตัวอักษร")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณายืนยันรหัสผ่านใหม่")]
    [Compare(nameof(NewPassword), ErrorMessage = "รหัสผ่านใหม่ทั้งสองช่องไม่ตรงกัน")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Everything shown on the "My account" page.</summary>
public class AccountPageModel
{
    public CyberUser User { get; set; } = new();
    public ProfileFormModel Profile { get; set; } = new();
    public ChangePasswordModel Password { get; set; } = new();
    public int BookingCount { get; set; }
    public int UpcomingCount { get; set; }
}
