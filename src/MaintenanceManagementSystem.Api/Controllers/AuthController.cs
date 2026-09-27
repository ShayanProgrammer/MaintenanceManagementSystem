using System.Security.Claims;
using MaintenanceManagementSystem.Api.Auth;
using MaintenanceManagementSystem.Api.Contracts;
using MaintenanceManagementSystem.Api.Data;
using MaintenanceManagementSystem.Api.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MaintenanceManagementSystem.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokenService;

    public AuthController(AppDbContext db, JwtTokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Authenticates with email + password and returns a JWT access token.
    /// The organization id and role are taken from the stored user record —
    /// they are never accepted from the request. Unknown email and wrong
    /// password produce the identical 401 response (no user enumeration).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            return Unauthorized();
        }

        var passwordHasher = new PasswordHasher<User>();
        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }

        var (accessToken, expiresAtUtc) = _tokenService.CreateToken(user);

        return Ok(new LoginResponse(
            accessToken,
            expiresAtUtc,
            new UserDto(user.Id, user.OrganizationId, user.Email, user.Role.ToString())));
    }

    /// <summary>
    /// Returns the identity carried by the presented token. Values come from
    /// the validated claims via TenantContext — never from request data.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public IActionResult Me([FromServices] TenantContext tenant)
    {
        var email = User.FindFirstValue(AppClaimTypes.Email) ?? string.Empty;
        return Ok(new MeResponse(tenant.UserId, tenant.OrganizationId, email, tenant.Role.ToString()));
    }
}
