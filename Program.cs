using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PKeetDashboard.API.Data;
using PKeetDashboard.API.Options;
using PKeetDashboard.API.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
    throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(3);
        sql.CommandTimeout(30);
    });
});

var jwt = builder.Configuration.GetSection("JwtSettings");
var secretKey = (jwt["SecretKey"] ?? "YourSuperSecretKeyThatShouldBeAtLeast32CharactersLong!").Trim();
// HS256 requires a symmetric key of at least 256 bits (32 UTF-8 bytes for typical ASCII secrets).
if (Encoding.UTF8.GetByteCount(secretKey) < 32)
    throw new InvalidOperationException(
        "JwtSettings:SecretKey must be at least 32 bytes in UTF-8 for HS256. " +
        "Set JwtSettings__SecretKey (and match JWT_SECRET if you use it) to a longer random string, e.g. " +
        "`openssl rand -base64 48`.");

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
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("is_admin", "true"));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Nginx on the same Docker network is the reverse proxy
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
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
});
builder.Services.AddSingleton<ComputerVisionOcrService>();
builder.Services.AddScoped<ResumeTextExtractor>();
builder.Services.AddScoped<ResumeStructuredParsingService>();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

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

// Enable Swagger so you can see and try the APIs
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Smeed AI User Dashboard API v1");
    c.RoutePrefix = "swagger"; // Swagger UI at https://localhost:5050/swagger
});


app.UseCors("AllowDashboard");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(db);
    await AdminBootstrap.ApplyAsync(db, app.Configuration);
}

app.Run();
