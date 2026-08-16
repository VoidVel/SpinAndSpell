using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SpinSpell.Server.Auth;

public static class PlayerClaims
{
    public const string RoomId = "room";
    public const string RoomCode = "code";
    public const string PlayerId = "player";
}

/// <summary>
/// No signup wall (§7 of the source of truth). A join hands back a capability
/// token that says "you are player X in room Y" and nothing else — it grants no
/// authority anywhere else in the system, so there is no account to protect and
/// no session store to shard later.
/// </summary>
public sealed class PlayerTokenService
{
    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _validation;

    public PlayerTokenService(IConfiguration config)
    {
        var secret = config["Auth:Secret"];

        // A generated key is fine for a single instance: tokens outlive nothing
        // but the process, and a restart evicts the rooms they refer to anyway.
        var key = new SymmetricSecurityKey(string.IsNullOrWhiteSpace(secret)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(secret));

        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _validation = new TokenValidationParameters
        {
            IssuerSigningKey = key,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    public TokenValidationParameters ValidationParameters => _validation;

    public string Issue(Guid roomId, string code, Guid playerId)
    {
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(PlayerClaims.RoomId, roomId.ToString()),
                new Claim(PlayerClaims.RoomCode, code),
                new Claim(PlayerClaims.PlayerId, playerId.ToString())
            ],
            // Long enough for a party that runs late, short enough to be useless later.
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class PrincipalExtensions
{
    public static Guid PlayerId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(PlayerClaims.PlayerId)?.Value, out var id)
            ? id
            : throw new HubUnauthorizedException("This connection has no player identity.");

    public static string RoomCode(this ClaimsPrincipal user) =>
        user.FindFirst(PlayerClaims.RoomCode)?.Value
        ?? throw new HubUnauthorizedException("This connection has no room.");
}

public sealed class HubUnauthorizedException(string message) : Exception(message);
