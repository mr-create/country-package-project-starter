namespace CountryPackage.Api.Services;

public sealed class ApiException(int statusCode, string code, string title, string? detail = null) : Exception(detail ?? title)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
}
