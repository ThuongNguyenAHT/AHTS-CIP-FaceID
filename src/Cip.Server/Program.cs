using System.Security.Claims;
using Cip.Server.Configuration;
using Cip.Server.Domain;
using Cip.Server.Hubs;
using Cip.Server.Infrastructure;
using Cip.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services
    .AddOptions<SourceDataOptions>()
    .Bind(builder.Configuration.GetSection(SourceDataOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<TabletOptions>()
    .Bind(builder.Configuration.GetSection(TabletOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<ConsentPolicyOptions>()
    .Bind(builder.Configuration.GetSection(ConsentPolicyOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<AdminOptions>()
    .Bind(builder.Configuration.GetSection(AdminOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<DatabaseConnectionFactory>();

var useMockDatabase = builder.Configuration.GetValue<bool>("UseMockDatabase", false);
if (useMockDatabase)
{
    builder.Services.AddSingleton<SourcePassengerRepository, MockSourcePassengerRepository>();
    builder.Services.AddSingleton<ConsentRepository, MockConsentRepository>();
}
else
{
    builder.Services.AddSingleton<SourcePassengerRepository>();
    builder.Services.AddSingleton<ConsentRepository>();
}

builder.Services.AddSingleton<AdminCredentialValidator>();
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "cip.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/admin/login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }

            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

var webPagesRoot = Path.Combine(app.Environment.ContentRootPath, "WebPages");

app.MapGet("/", () => Results.Redirect("/tablet"));
app.MapGet("/tablet", () => Results.File(
    Path.Combine(webPagesRoot, "tablet.html"),
    "text/html; charset=utf-8"));
app.MapGet("/admin/login", () => Results.File(
    Path.Combine(webPagesRoot, "admin-login.html"),
    "text/html; charset=utf-8"));
app.MapGet("/admin", () => Results.File(
        Path.Combine(webPagesRoot, "admin.html"),
        "text/html; charset=utf-8"))
    .RequireAuthorization();

app.MapGet("/api/tablet/config", (IOptions<TabletOptions> options) => Results.Ok(new
{
    fallbackPollingMilliseconds = options.Value.FallbackPollingMilliseconds
}));

app.MapGet("/api/tablet/current", async (
    ConsentRepository repository,
    CancellationToken cancellationToken) =>
{
    var current = await repository.GetCurrentAsync(cancellationToken);
    return current is null ? Results.NoContent() : Results.Ok(current);
});

app.MapPost("/api/tablet/requests/{id:int}/biometric", (
    int id,
    BiometricUploadRequest request,
    ConsentRepository repository) =>
{
    repository.SaveFaceImage(id, request.FaceImage);
    return Results.Ok(new { status = "Saved", id });
});

app.MapPost("/api/tablet/requests/{id:int}/decision", async (
    int id,
    DecisionRequest request,
    ConsentRepository repository,
    CancellationToken cancellationToken) =>
{
    var decision = request.Decision?.Trim();
    if (decision is not ("Accepted" or "Declined" or "NotMe"))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(request.Decision)] = ["Decision phải là Accepted, Declined hoặc NotMe."]
        });
    }

    if (decision == "Accepted" && !string.IsNullOrWhiteSpace(request.FaceImage))
    {
        repository.SaveFaceImage(id, request.FaceImage);
    }

    var outcome = decision == "NotMe"
        ? await repository.CancelPendingAsync(id, cancellationToken)
        : await repository.SaveDecisionAsync(id, decision == "Accepted", cancellationToken);
    if (!outcome.Found)
    {
        return Results.NotFound();
    }

    if (!outcome.Applied)
    {
        return Results.Conflict(new
        {
            message = "Phiên xác nhận không còn hiệu lực.",
            status = outcome.Status
        });
    }

    return Results.Ok(new
    {
        status = outcome.Status,
        confirmedAt = outcome.ConfirmedAt
    });
});

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    HttpContext httpContext,
    AdminCredentialValidator credentialValidator) =>
{
    if (!credentialValidator.IsConfigured)
    {
        return Results.Problem(
            "Chưa cấu hình Admin__Password trên server.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (string.IsNullOrWhiteSpace(request.Username)
        || string.IsNullOrEmpty(request.Password)
        || !credentialValidator.Validate(request.Username, request.Password))
    {
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, credentialValidator.Username),
        new Claim(ClaimTypes.Name, credentialValidator.Username),
        new Claim(ClaimTypes.Role, "Administrator")
    };
    var principal = new ClaimsPrincipal(
        new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = false });

    return Results.Ok(new { username = credentialValidator.Username, role = "Administrator" });
});

app.MapPost("/api/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/auth/me", (ClaimsPrincipal user) => Results.Ok(new
{
    username = user.Identity?.Name,
    role = user.FindFirstValue(ClaimTypes.Role)
})).RequireAuthorization();

app.MapGet("/api/admin/dashboard", async (
    ConsentRepository repository,
    CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetDashboardAsync(cancellationToken)))
    .RequireAuthorization();

app.MapGet("/api/admin/source-passengers", async (
    SourcePassengerRepository sourceRepository,
    ConsentRepository consentRepository,
    CancellationToken cancellationToken) =>
{
    var passengers = await sourceRepository.GetLatestAsync(cancellationToken);
    var states = await consentRepository.GetStatesAsync(
        passengers.Select(passenger => passenger.Id).ToArray(),
        cancellationToken);

    var result = passengers.Select(passenger =>
    {
        states.TryGetValue(passenger.Id, out var state);
        return new SourcePassengerWithConsent(
            passenger.Id,
            passenger.PassengerName,
            passenger.OperatingCarrierDesignator,
            passenger.FlightNumber,
            passenger.FlightCode,
            passenger.DateOfFlight,
            passenger.WorkstationCode,
            passenger.WarehouseName,
            passenger.UserName,
            passenger.FullName,
            state?.Status ?? "NotSent",
            state?.ConfirmedAt,
            state?.FaceImageUrl ?? consentRepository.GetFaceImage(passenger.Id));
    });

    return Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/admin/display", async (
    DisplayPassengerRequest request,
    SourcePassengerRepository sourceRepository,
    ConsentRepository consentRepository,
    IHubContext<TabletHub> hubContext,
    CancellationToken cancellationToken) =>
{
    var passenger = await sourceRepository.GetByIdAsync(request.SourcePassengerId, cancellationToken);
    if (passenger is null)
    {
        return Results.NotFound(new { message = "Không tìm thấy hành khách trong Database số 1." });
    }

    QueueForDisplayResult result;
    try
    {
        result = await consentRepository.QueueForDisplayAsync(passenger, cancellationToken);
    }
    catch (InvalidDataException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }

    if (result == QueueForDisplayResult.AlreadyCompleted)
    {
        return Results.Conflict(new { message = "Hành khách này đã xác nhận trước đó." });
    }

    if (result == QueueForDisplayResult.AnotherPassengerPending)
    {
        return Results.Conflict(new
        {
            message = "Tablet đang hiển thị một hành khách khác. Vui lòng hoàn tất lượt hiện tại trước."
        });
    }

    await hubContext.Clients.All.SendAsync(
        "CustomerAvailable",
        new { sourcePassengerId = passenger.Id },
        cancellationToken);

    return Results.Ok(new
    {
        status = result == QueueForDisplayResult.Queued ? "Queued" : "AlreadyPending",
        sourcePassengerId = passenger.Id
    });
}).RequireAuthorization();

app.MapPost("/api/admin/simulate-scan", (SourcePassengerRepository sourceRepository) =>
{
    if (sourceRepository is MockSourcePassengerRepository mockRepo)
    {
        var newPassenger = mockRepo.SimulateNewPassengerScan();
        return Results.Ok(new
        {
            message = "Đã quét thành công vé giả lập mới!",
            passenger = newPassenger
        });
    }

    return Results.BadRequest(new { message = "Chế độ Mock không được kích hoạt." });
}).RequireAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (
    SourcePassengerRepository sourceRepository,
    ConsentRepository consentRepository,
    CancellationToken cancellationToken) =>
{
    var vendorReady = await sourceRepository.CanConnectAsync(cancellationToken);
    var consentReady = await consentRepository.CanConnectAsync(cancellationToken);
    return vendorReady && consentReady
        ? Results.Ok(new { status = "Healthy", vendorDatabase = true, consentDatabase = true })
        : Results.Json(
            new { status = "Unhealthy", vendorDatabase = vendorReady, consentDatabase = consentReady },
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapHub<TabletHub>("/hubs/tablet");

app.Run();

public sealed record DecisionRequest(string? Decision, string? FaceImage = null);

public sealed record BiometricUploadRequest(string? FaceImage, string? PassengerName = null, string? FlightCode = null);

public sealed record LoginRequest(string? Username, string? Password);

public sealed record DisplayPassengerRequest(int SourcePassengerId);

public partial class Program;
