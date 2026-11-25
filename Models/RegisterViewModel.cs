using System.ComponentModel.DataAnnotations;

namespace Intouch_67173986_L003.Models;

public class RegisterViewModel
{
    public const int MinPasswordLength = AccountRules.MinPasswordLength;

    [Required(ErrorMessage = "กรุณากรอกชื่อ")]
    [StringLength(100, ErrorMessage = "ชื่อต้องไม่เกิน {1} ตัวอักษร")]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกอีเมล")]
    [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
    [StringLength(200, ErrorMessage = "อีเมลต้องไม่เกิน {1} ตัวอักษร")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "กรุณากรอกเบอร์โทร")]
    [Phone(ErrorMessage = "รูปแบบเบอร์โทรไม่ถูกต้อง")]
    [StringLength(50, ErrorMessage = "เบอร์โทรต้องไม่เกิน {1} ตัวอักษร")]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    // Nullable so the empty form shows a blank date picker instead of 01/01/0001.
    // The birthday month gives the birthday promotion, so members cannot change it later.
    [Required(ErrorMessage = "กรุณาเลือกวันเกิด")]
    [Birthday]
    [DataType(DataType.Date)]
    [Display(Name = "Birthday")]
    public DateTime? Birthday { get; set; }

    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    [StringLength(AccountRules.MaxPasswordLength, ErrorMessage = "รหัสผ่านต้องไม่เกิน {1} ตัวอักษร")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;
}
