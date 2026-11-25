using Microsoft.AspNetCore.Mvc.Rendering;

namespace Intouch_67173986_L003.Infrastructure;

/// <summary>Small helpers for the layouts and menus.</summary>
public static class ViewHelpers
{
    /// <summary>True when the current page is one of the given actions of the given controller.</summary>
    public static bool IsActive(this ViewContext context, string controller, params string[] actions)
    {
        var currentController = context.RouteData.Values["controller"]?.ToString();
        var currentAction = context.RouteData.Values["action"]?.ToString();

        return string.Equals(currentController, controller, StringComparison.OrdinalIgnoreCase) &&
               (actions.Length == 0 || actions.Any(action => string.Equals(currentAction, action, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>"active" for the menu item of the current page, otherwise null (no class attribute value).</summary>
    public static string? ActiveClass(this ViewContext context, string controller, params string[] actions)
    {
        return context.IsActive(controller, actions) ? "active" : null;
    }

    /// <summary>aria-current="page" for the menu item of the current page.</summary>
    public static string? AriaCurrent(this ViewContext context, string controller, params string[] actions)
    {
        return context.IsActive(controller, actions) ? "page" : null;
    }

    /// <summary>The first letter of a name for the round avatar ("?" when the name is empty).</summary>
    public static string Initial(string? name)
    {
        // A text element keeps emoji and combined characters whole.
        return string.IsNullOrWhiteSpace(name)
            ? "?"
            : System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
    }
}

/// <summary>Names of the rate-limit policies (see Program.cs).</summary>
public static class RateLimits
{
    /// <summary>Log-in and sign-up attempts per minute from one IP address (guessing passwords).</summary>
    public const string Auth = "auth";
}
