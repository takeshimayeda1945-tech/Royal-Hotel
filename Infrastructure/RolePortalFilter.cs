using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Intouch_67173986_L003.Infrastructure;

/// <summary>
/// Keeps every role on its own port (see <see cref="RolePortals"/>). Runs before every action,
/// right after <see cref="CurrentUserFilter"/> has refreshed the role from the database.
/// <list type="bullet">
/// <item>A signed-in user whose role does not belong to this port (a Super Admin changed their
/// role) is signed out here and moved to the port of the new role, signed in there.</item>
/// <item>On a back-office port, a visitor who is not signed in only sees the log-in page.</item>
/// <item>On a back-office port, the customer pages (rooms, booking, my bookings …) are not shown:
/// employees work on their own pages, and the public site lives on the customer port.</item>
/// </list>
/// Which pages a role may open is still decided by each controller (DenyUnless).
/// </summary>
public sealed class RolePortalFilter : IActionFilter
{
    /// <summary>Pages a visitor who is not signed in may open on a back-office port.</summary>
    private static readonly HashSet<string> OpenOnBackOffice = new(StringComparer.OrdinalIgnoreCase)
    {
        "Home.Login", "Home.Handoff", "Home.Logout", "Home.Error", "Home.HttpError"
    };

    /// <summary>Pages of the customers' site (they only make sense on the customer port).</summary>
    private static readonly HashSet<string> CustomerPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Home.Index", "Home.Rooms", "Home.RoomDetails", "Home.Promotions", "Home.Register",
        "Home.Checkout", "Home.Quote", "Home.Payment", "Home.BookingSuccess", "Home.BookingHistory", "Home.CancelBooking",
        "Home.Receipt", "Home.Review"
    };

    private readonly RolePortals _portals;
    private readonly LoginHandoff _handoff;

    public RolePortalFilter(RolePortals portals, LoginHandoff handoff)
    {
        _portals = portals;
        _handoff = handoff;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        var portalRole = _portals.RoleOf(http);
        if (portalRole == null)
        {
            return; // Program.cs already refuses requests on any other port.
        }

        var session = http.Session;
        var isSignedIn = !string.IsNullOrEmpty(session.GetString(SessionKeys.UserId));
        var userRole = session.GetString(SessionKeys.UserRole);

        if (isSignedIn && userRole != portalRole)
        {
            var userIdText = session.GetString(SessionKeys.UserId);
            session.Clear();
            context.Result = int.TryParse(userIdText, out var userId) && _portals.HasPortal(userRole)
                ? new RedirectResult(_handoff.HandoffUrl(userId, userRole!, http))
                : new RedirectToActionResult("Login", "Home", null);
            return;
        }

        if (!RolePortals.IsBackOffice(portalRole))
        {
            return;
        }

        var controllerName = context.RouteData.Values["controller"] as string;
        var actionName = context.RouteData.Values["action"] as string;
        var page = $"{controllerName}.{actionName}";
        var isGet = HttpMethods.IsGet(http.Request.Method);
        var requestUrl = http.Request.PathBase + http.Request.Path + http.Request.QueryString;

        if (!isSignedIn)
        {
            if (OpenOnBackOffice.Contains(page))
            {
                return;
            }

            // Come back to the requested back-office page after logging in (not to the public home page).
            var returnUrl = isGet && !string.Equals(controllerName, "Home", StringComparison.OrdinalIgnoreCase) ? requestUrl : null;
            context.Result = new RedirectToActionResult("Login", "Home", new { returnUrl });
            return;
        }

        if (CustomerPages.Contains(page))
        {
            // The home page of a back-office port is the role's first page (the logo and the
            // "back to home" buttons lead there). Any other customer page opens on the customer port.
            if (isGet && !string.Equals(page, "Home.Index", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new RedirectResult(_portals.UrlFor(UserRoles.Customer, http.Request, requestUrl));
            }
            else
            {
                var (controller, action) = RolePortals.HomePageOf(userRole);
                context.Result = new RedirectToActionResult(action, controller, null);
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
