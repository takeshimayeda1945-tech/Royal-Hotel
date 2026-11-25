using Intouch_67173986_L003.Models;

namespace Intouch_67173986_L003.Infrastructure;

/// <summary>
/// Every role has its own port (its own "portal"): customers use the public site on one port,
/// the front desk, housekeeping, finance, admin and super admin each use another one.
/// <list type="bullet">
/// <item>The ports are fixed here in the code: no setting, command-line option or database edit
/// changes them. The RolePortals table holds a copy for reference (and as the target of the
/// Users.Role foreign key); Data/DbInitializer.cs puts these values back on every start.</item>
/// <item>Anyone may log in on any port: the site then moves them to the port of their role,
/// already signed in (<see cref="LoginHandoff"/>).</item>
/// <item>Each port keeps its own session cookie, so several roles can be signed in at the same
/// time in different browser tabs.</item>
/// </list>
/// </summary>
public sealed class RolePortals
{
    /// <summary>The fixed port of every role, in the order of <see cref="UserRoles.All"/>.</summary>
    public static readonly RolePortals Fixed = new(new[]
    {
        (UserRoles.Customer, 5243),
        (UserRoles.Staff, 5244),
        (UserRoles.Housekeeper, 5245),
        (UserRoles.Finance, 5246),
        (UserRoles.Admin, 5247),
        (UserRoles.SuperAdmin, 5248),
    });

    private readonly Dictionary<string, int> _portByRole;
    private readonly Dictionary<int, string> _roleByPort;

    private RolePortals(IReadOnlyList<(string Role, int Port)> portals)
    {
        All = portals;
        _portByRole = portals.ToDictionary(p => p.Role, p => p.Port);
        _roleByPort = portals.ToDictionary(p => p.Port, p => p.Role);
    }

    /// <summary>Every role with its port, in the order of <see cref="UserRoles.All"/>.</summary>
    public IReadOnlyList<(string Role, int Port)> All { get; }

    /// <summary>The role whose portal listens on <paramref name="port"/>, or null for any other port.</summary>
    public string? RoleForPort(int port) => _roleByPort.GetValueOrDefault(port);

    /// <summary>The role of the portal this request came in on (by the port the server received it on).</summary>
    public string? RoleOf(HttpContext context) => RoleForPort(context.Connection.LocalPort);

    public int PortFor(string role) => _portByRole[role];

    public bool HasPortal(string? role) => role != null && _portByRole.ContainsKey(role);

    /// <summary>True for every portal except the customers' public site.</summary>
    public static bool IsBackOffice(string? portalRole) => portalRole != null && portalRole != UserRoles.Customer;

    /// <summary>The first page a user of <paramref name="role"/> sees after logging in.</summary>
    public static (string Controller, string Action) HomePageOf(string? role) => role switch
    {
        UserRoles.SuperAdmin or UserRoles.Admin => ("Admin", "Dashboard"),
        UserRoles.Staff => ("Staff", "Bookings"),
        UserRoles.Housekeeper => ("Housekeeping", "Index"),
        UserRoles.Finance => ("Finance", "Index"),
        _ => ("Home", "Index")
    };

    /// <summary>
    /// Absolute link to <paramref name="path"/> on the portal of <paramref name="role"/>, using the
    /// host name the visitor typed (localhost, 127.0.0.1 …).
    /// </summary>
    public string UrlFor(string role, HttpRequest request, string path = "/")
    {
        return $"{request.Scheme}://{request.Host.Host}:{PortFor(role)}{path}";
    }

    /// <summary>The addresses Kestrel listens on: one http address per role.</summary>
    public string[] ListenUrls(string host) => All.Select(p => $"http://{host}:{p.Port}").ToArray();
}
