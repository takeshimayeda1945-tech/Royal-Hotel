using Intouch_67173986_L003.Data;
using Intouch_67173986_L003.Models;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Intouch_67173986_L003.Infrastructure;

/// <summary>
/// Runs before every action (registered globally in Program.cs). The session remembers who is
/// signed in, but the database is the truth: when a Super Admin changes someone's role or name,
/// or deletes the account, it takes effect on that person's very next click instead of after
/// they log out.
/// </summary>
public sealed class CurrentUserFilter : IAsyncActionFilter
{
    private readonly ApplicationDbContext _db;

    public CurrentUserFilter(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var session = http.Session;

        if (int.TryParse(session.GetString(SessionKeys.UserId), out var userId))
        {
            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Name, u.Role })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                // The account was deleted: sign the visitor out.
                session.Clear();
            }
            else
            {
                if (session.GetString(SessionKeys.UserRole) != user.Role)
                {
                    session.SetString(SessionKeys.UserRole, user.Role);
                }

                if (session.GetString(SessionKeys.UserName) != user.Name)
                {
                    session.SetString(SessionKeys.UserName, user.Name);
                }

                // Pages of a signed-in user show personal data: the browser must not keep a copy
                // (e.g. pressing Back after logging out on a shared computer).
                http.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                http.Response.Headers.Pragma = "no-cache";
            }
        }

        await next();
    }
}
