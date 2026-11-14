using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Configure Jwt__Key com pelo menos 32 bytes aleatórios.");
if (Encoding.UTF8.GetByteCount(secret) < 32)
    throw new InvalidOperationException("Jwt__Key precisa de pelo menos 32 bytes.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
const string issuer = "security-api";
const string audience = "security-api-clients";
var database = new SqliteConnectionStringBuilder {
    DataSource = builder.Configuration["Database:Path"] ?? "security.db",
    Mode = SqliteOpenMode.ReadWriteCreate
}.ToString();

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4096);
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => {
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateLifetime = true, RequireExpirationTime = true,
        ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(10)
    };
});
builder.Services.AddAuthorization();
if (builder.Configuration["Proxy:KnownIp"] is { Length: > 0 } proxyIp) {
    var trustedProxy = System.Net.IPAddress.Parse(proxyIp);
    builder.Services.Configure<ForwardedHeadersOptions>(options => {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(trustedProxy);
        options.ForwardLimit = 1;
    });
}
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions {
            PermitLimit = 8, QueueLimit = 0
        }));
});

using (var connection = new SqliteConnection(database)) {
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS users (
            id TEXT PRIMARY KEY,
            email TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL
        );
        """;
    command.ExecuteNonQuery();
}
// Também verifica a disponibilidade da biblioteca nativa na inicialização.
var dummyHash = NativePasswords.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.Use(async (context, next) => {
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
var auth = app.MapGroup("/auth").RequireRateLimiting("auth");
auth.MapPost("/register", async (Credentials input) => {
    if (!Valid(input)) return Results.BadRequest(new { error = "Informe e-mail válido e senha com 12 a 128 bytes UTF-8, sem caractere nulo." });
    var id = Guid.NewGuid().ToString();
    var email = input.Email!.Trim().ToLowerInvariant();
    var hash = NativePasswords.Hash(input.Password!);
    await using var connection = new SqliteConnection(database);
    await connection.OpenAsync();
    using var command = connection.CreateCommand();
    command.CommandText = "INSERT INTO users (id,email,password_hash) VALUES ($id,$email,$hash)";
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$email", email);
    command.Parameters.AddWithValue("$hash", hash);
    try { await command.ExecuteNonQueryAsync(); }
    catch (SqliteException exception) when (exception.SqliteErrorCode == 19) {
        return Results.Conflict(new { error = "Cadastro indisponível para este e-mail." });
    }
    return Results.Created("/me", new { id, email });
});
auth.MapPost("/login", async (Credentials input) => {
    if (!Valid(input)) return Results.Unauthorized();
    await using var connection = new SqliteConnection(database);
    await connection.OpenAsync();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT id,email,password_hash FROM users WHERE email = $email";
    command.Parameters.AddWithValue("$email", input.Email!.Trim().ToLowerInvariant());
    using var reader = await command.ExecuteReaderAsync();
    var found = await reader.ReadAsync();
    var hash = found ? reader.GetString(2) : dummyHash;
    if (!NativePasswords.Verify(input.Password!, hash) || !found) return Results.Unauthorized();
    var now = DateTime.UtcNow;
    var token = new JwtSecurityToken(issuer, audience, [
        new Claim(JwtRegisteredClaimNames.Sub, reader.GetString(0)),
        new Claim(JwtRegisteredClaimNames.Email, reader.GetString(1)),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    ], now, now.AddMinutes(15), new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token), token_type = "Bearer", expires_in = 900 });
});
app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new {
    id = user.FindFirstValue(JwtRegisteredClaimNames.Sub),
    email = user.FindFirstValue(JwtRegisteredClaimNames.Email)
})).RequireAuthorization();
app.Run();

static bool Valid(Credentials input) =>
    input.Email is { Length: > 0 and <= 254 } email &&
    email == email.Trim() && MailAddress.TryCreate(email, out var address) && address.Address == email &&
    input.Password is { } password && !password.Contains('\0') &&
    Encoding.UTF8.GetByteCount(password) is >= 12 and <= 128;

record Credentials(string? Email, string? Password);
