using System.Text.Json.Serialization;
using CountryPackage.Api.Auth;
using CountryPackage.Api.Endpoints;
using CountryPackage.Api.Infrastructure;
using CountryPackage.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = (builder.Configuration.GetValue<long?>("Storage:MaximumUploadBytes") ?? 10 * 1024 * 1024) + 1024 * 1024);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IModelGateway, ConfigurableModelGateway>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FictionalUserDirectory>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IdempotentExecutor>();
builder.Services.AddScoped<DocumentValidator>();
builder.Services.AddScoped<CountryPackageService>();
builder.Services.AddScoped<EvidenceCopilotService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<ProblemDetailsMiddleware>();
app.UseMiddleware<DevelopmentIdentityMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.ContentSecurityPolicy = context.Request.Path.StartsWithSegments("/swagger")
        ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:"
        : "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'";
    await next();
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/openapi.yaml", () => Results.File(Path.Combine(AppContext.BaseDirectory, "openapi.yaml"), "application/yaml"))
    .ExcludeFromDescription();
if (app.Environment.IsDevelopment()) app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/openapi.yaml", "Country Package Approval API");
});

var localWebRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../CountryPackage.Web/dist"));
var webRoot = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html"))
    ? app.Environment.WebRootPath!
    : localWebRoot;
if (File.Exists(Path.Combine(webRoot, "index.html")))
{
    var files = new PhysicalFileProvider(webRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}

app.MapCountryPackageEndpoints();
if (File.Exists(Path.Combine(webRoot, "index.html")))
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
    });

await app.RunAsync();

public partial class Program;
