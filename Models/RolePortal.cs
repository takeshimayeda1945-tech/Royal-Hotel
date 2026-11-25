using System.ComponentModel.DataAnnotations;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// One row of the RolePortals table: the port on which a role uses the site. Users.Role is a
/// foreign key to <see cref="Role"/>. The ports themselves are fixed in the code
/// (Infrastructure/RolePortals.cs); this table is a read-only copy that the site restores on
/// every start, so editing it changes nothing.
/// </summary>
public class RolePortal
{
    /// <summary>Role name, the same text as Users.Role (e.g. "Staff", "Super Admin").</summary>
    [Key]
    [StringLength(50)]
    public string Role { get; set; } = string.Empty;

    /// <summary>The http port of this role's portal; unique, so no two roles share a port.</summary>
    public int Port { get; set; }

    /// <summary>Thai name of the role, shown on the log-in page.</summary>
    [StringLength(50)]
    public string ThaiName { get; set; } = string.Empty;
}
