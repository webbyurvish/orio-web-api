using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using FluentValidation;
using FluentValidation.AspNetCore;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Options;
using PKeetDashboard.API.Security;
using PKeetDashboard.API.Services;
using PKeetDashboard.API.Validation;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddSecurityHardening(builder.Configuration, builder.Environment);

// Data Protection hardening:
// - Prevents "No XML encryptor configured" warnings
// - Ensures keys aren't persisted unencrypted (DPAPI on Windows)
// - Persists keys to a writable location on Azure App Service (HOME)
var home = Environment.GetEnvironmentVariable("HOME");
var keyRingPath = !string.IsNullOrWhiteSpace(home)
    ? Path.Combine(home, "data-protection-keys")
    : Path.Combine(builder.Environment.ContentRootPath, "data-protection-keys");

var dp = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

if (OperatingSystem.IsWindows())
{
    dp.ProtectKeysWithDpapi();
}

var maxBodyBytes = builder.Configuration.GetValue<long?>("Security:MaxRequestBodyBytes");
builder.WebHost.ConfigureKestrel(options =>
{
    // Defense-in-depth: hard caps against slow clients / memory abuse.
    if (maxBodyBytes is > 0)
        options.Limits.MaxRequestBodySize = maxBodyBytes;

    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(3);
        sql.CommandTimeout(30);
    });
});

var jwt = builder.Configuration.GetSection("JwtSettings");
var secretKey = (jwt["SecretKey"] ?? "").Trim();
// HS256 requires a symmetric key of at least 256 bits (32 UTF-8 bytes for typical ASCII secrets).
if (Encoding.UTF8.GetByteCount(secretKey) < 32)
    throw new InvalidOperationException(
        "JwtSettings:SecretKey must be at least 32 bytes in UTF-8 for HS256. " +
        "Set JwtSettings__SecretKey to a long random string (do not commit secrets).");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };

    // Reduce token abuse surface area.
    options.RequireHttpsMetadata = true;
    options.SaveToken = false;
    options.MapInboundClaims = false;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("is_admin", "true"));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // IMPORTANT: Do not clear KnownNetworks/Proxies. That enables spoofed X-Forwarded-* headers.
    // Configure trusted proxies via configuration in SecurityHardening registration.
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDashboard", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000",
                "http://127.0.0.1:5173",
                "http://127.0.0.1:3000",
                "https://smeedai.com",
                "https://www.smeedai.com"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<IAnalyticsRecorder, AnalyticsRecorder>();
builder.Services.AddScoped<AdminAnalyticsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddSingleton<DesktopAuthCodeStore>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("ComputerVision", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
})
.AddStandardResilienceHandler(options =>
{
    // Defensive defaults: handle transient failures without creating thundering herds.
    options.Retry.MaxRetryAttempts = 2;
    options.Retry.UseJitter = true;

    // NOTE: sampling duration must be >= 2x attempt timeout (validated at startup).
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.MinimumThroughput = 20;
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<ComputerVisionOcrService>();
builder.Services.AddScoped<ResumeTextExtractor>();
builder.Services.AddScoped<ResumeStructuredParsingService>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddFluentValidationAutoValidation(o =>
{
    // Ensure malformed payloads are rejected early and consistently.
    o.DisableDataAnnotationsValidation = true;
});
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Smeed AI User Dashboard API", Version = "v1" });
    c.OperationFilter<PKeetDashboard.API.Swagger.MultipartFormFileOperationFilter>();
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT: Bearer <token>",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseForwardedHeaders();

app.UseSecurityHardening(app.Configuration, app.Environment);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Smeed AI User Dashboard API v1");
        c.RoutePrefix = "swagger"; // Swagger UI at https://localhost:5050/swagger
    });
}


app.UseCors("AllowDashboard");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("PerUser");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(db);
    await AdminBootstrap.ApplyAsync(db, app.Configuration);
}

app.Run();
