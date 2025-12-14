using DomainServices.Data;
using DomainServices.Data.Repository;
using DomainServices.Data.UserIdentity;
using DomainServices.Services;
using DomainServices.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NLog.Web;
using OpenIddict.Validation.AspNetCore;
using static DomainServices.Data.Repository.DomainDBContext;

var builder = WebApplication.CreateBuilder(args);

string EncryptionKey = builder.Configuration["EncryptionKey"];

// Add controllers + swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Connection string
var connectionString = builder.Configuration.GetConnectionString("ApplicationDBContextConnection")
    ?? throw new InvalidOperationException("Connection string not found.");

connectionString = Encyption.Decrypt(connectionString, EncryptionKey);

// ---------------------------
// Register DbContexts
// ---------------------------

// Identity + OpenIddict tables exist ONLY in ApplicationDbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.UseOpenIddict(); // Required for OpenIddict stores
});

// DomainServices data context — NO OpenIddict here
builder.Services.AddDbContext<DomainDBContext>(options =>
{
    options.UseSqlServer(connectionString);
});

// ----------------------------------------
// Register Identity for authentication
// ----------------------------------------
builder.Services.AddDefaultIdentity<Users>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

// ----------------------------------------
// Register Repositories & Services
// ----------------------------------------
builder.Services.AddScoped<ILocalResourceService, LocalResourceService>();
builder.Services.AddScoped<LocalResourcesRepository>();
builder.Services.AddScoped<CoreData>();
builder.Services.AddScoped<CommonServices>();

// ❗ If DomainRepo is a separate class, register properly
builder.Services.AddScoped<DomainRepo>();

builder.Services.AddScoped<CoreServices>();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

// ----------------------------------------
// NLog
// ----------------------------------------
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
builder.Host.ConfigureLogging(logging =>
{
    logging.ClearProviders();
    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
}).UseNLog();

// ----------------------------------------
// OpenIddict Validation 
// ----------------------------------------
var authServerUrl = builder.Configuration.GetValue<string>("AuthServer:BaseUrl");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme =
        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});

builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer(authServerUrl);

        options.AddAudiences("Eservice_Hub");

        options.AddEncryptionKey(new SymmetricSecurityKey(
            Convert.FromBase64String("DRjd/GnduI3Efzen9V9BvbNUfc/VKgXltV7Kbk9sMkY=")));

        options.UseSystemNetHttp();

        options.UseAspNetCore();
    });

builder.Services.AddAuthorization();

// ----------------------------------------
var app = builder.Build();

// Swagger for dev only
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// No need for static files in API unless needed
app.MapControllers();

app.Run();
