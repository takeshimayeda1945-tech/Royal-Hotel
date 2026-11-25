using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Intouch_67173986_L003.Infrastructure;

/// <summary>
/// Moves a signed-in user to the port of their role. Every port has its own session cookie, so
/// the port where the password was checked cannot sign the user in on another port directly.
/// Instead it issues a ticket and redirects to /Home/Handoff?ticket=… on the role's port, which
/// redeems the ticket and starts the session there.
/// A ticket
/// <list type="bullet">
/// <item>is a random 256-bit value that works once and expires after <see cref="Lifetime"/>;</item>
/// <item>only works on the port of the role it was issued for;</item>
/// <item>only works in the browser that typed the password: it is tied to a random browser id kept
/// in a cookie. Browsers send a cookie to every port of the same host, so the role's port can
/// check it. A ticket link sent to someone else therefore cannot sign them in.</item>
/// </list>
/// Tickets live in memory (the site is one process).
/// </summary>
public sealed class LoginHandoff
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private const string BrowserCookie = ".RoyalHotel.Browser";

    private sealed record Ticket(int UserId, string Role, string BrowserId, DateTime ExpiresUtc);

    private readonly ConcurrentDictionary<string, Ticket> _tickets = new();
    private readonly RolePortals _portals;

    public LoginHandoff(RolePortals portals)
    {
        _portals = portals;
    }

    /// <summary>The address on the role's port that signs <paramref name="userId"/> in there.</summary>
    public string HandoffUrl(int userId, string role, HttpContext http)
    {
        RemoveExpired();
        var ticket = NewRandomValue();
        _tickets[ticket] = new Ticket(userId, role, GetOrCreateBrowserId(http), DateTime.UtcNow + Lifetime);
        return _portals.UrlFor(role, http.Request, $"/Home/Handoff?ticket={ticket}");
    }

    /// <summary>
    /// Uses up the ticket. True (with the user id) only when it exists, has not expired, was issued
    /// for <paramref name="portalRole"/> and comes from the browser it was issued to.
    /// </summary>
    public bool TryRedeem(string? ticket, string portalRole, HttpContext http, out int userId)
    {
        userId = 0;
        if (string.IsNullOrEmpty(ticket) || !_tickets.TryRemove(ticket, out var entry))
        {
            return false;
        }

        var browserId = http.Request.Cookies[BrowserCookie];
        if (entry.ExpiresUtc < DateTime.UtcNow || entry.Role != portalRole ||
            browserId == null || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(browserId), System.Text.Encoding.ASCII.GetBytes(entry.BrowserId)))
        {
            return false;
        }

        userId = entry.UserId;
        return true;
    }

    private static string GetOrCreateBrowserId(HttpContext http)
    {
        var browserId = http.Request.Cookies[BrowserCookie];
        if (string.IsNullOrEmpty(browserId) || browserId.Length != 43)
        {
            browserId = NewRandomValue();
            http.Response.Cookies.Append(BrowserCookie, browserId, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        }

        return browserId;
    }

    private static string NewRandomValue() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, entry) in _tickets)
        {
            if (entry.ExpiresUtc < now)
            {
                _tickets.TryRemove(key, out _);
            }
        }
    }
}
