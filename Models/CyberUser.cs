using System.ComponentModel.DataAnnotations;

namespace Intouch_67173986_L003.Models;

public class CyberUser
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
    [DataType(DataType.Date)]
    public DateTime Birthday { get; set; }

    /// <summary>
    /// The salted PBKDF2 hash of the password (see <see cref="PasswordSecurity"/>), never the password itself.
    /// </summary>
    [Required(ErrorMessage = "กรุณากรอกรหัสผ่าน")]
    [DataType(DataType.Password)]
    [StringLength(200, ErrorMessage = "รหัสผ่านต้องไม่เกิน {1} ตัวอักษร")]
    public string Password { get; set; } = string.Empty;

    [StringLength(50)]
    public string Role { get; set; } = UserRoles.Customer;
}
