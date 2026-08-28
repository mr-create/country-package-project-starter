using CountryPackage.Api.Domain;

namespace CountryPackage.Api.Auth;

public sealed record FictionalUser(
    string UserId,
    string DisplayName,
    UserRole Role,
    IReadOnlySet<string> CountryScopes,
    Clearance? Clearance);

public interface ICurrentUser
{
    FictionalUser? User { get; set; }
}

public sealed class CurrentUser : ICurrentUser
{
    public FictionalUser? User { get; set; }
}

public sealed class FictionalUserDirectory
{
    private static readonly IReadOnlyList<FictionalUser> Users =
    [
        new("editor-bgd", "Amina Rahman — Bangladesh Editor", UserRole.CountryEditor, Set("BGD"), null),
        new("reviewer-bgd-country", "Karim Hossain — Country Reviewer", UserRole.CountryReviewer, Set("BGD"), Clearance.Country),
        new("reviewer-bgd-regional", "Maya Sen — Regional Reviewer", UserRole.CountryReviewer, Set("BGD", "IND", "NPL"), Clearance.Regional),
        new("editor-ken", "Wanjiku Njoroge — Kenya Editor", UserRole.CountryEditor, Set("KEN"), null),
        new("reviewer-ken-country", "David Otieno — Country Reviewer", UserRole.CountryReviewer, Set("KEN"), Clearance.Country),
        new("reviewer-ken-regional", "Asha Kamau — Regional Reviewer", UserRole.CountryReviewer, Set("KEN", "UGA", "TZA"), Clearance.Regional)
    ];

    public IReadOnlyList<FictionalUser> All => Users;
    public FictionalUser? Find(string? userId) => Users.FirstOrDefault(x => x.UserId == userId);

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}

public sealed class DevelopmentIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, FictionalUserDirectory directory, ICurrentUser currentUser, IWebHostEnvironment environment)
    {
        var userId = context.Request.Headers["X-User-Id"].FirstOrDefault();
        if ((environment.IsDevelopment() || environment.IsEnvironment("Testing")) && !string.IsNullOrWhiteSpace(userId))
        {
            currentUser.User = directory.Find(userId);
        }

        await next(context);
    }
}
