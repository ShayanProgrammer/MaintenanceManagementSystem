using System.ComponentModel.DataAnnotations;

namespace MaintenanceManagementSystem.Api.Contracts;

// Validation attributes sit on the primary-constructor parameters
// (required by .NET 10 record validation).
public record LoginRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(128)] string Password);

/// <summary>Basic user information safe to expose (never the password hash).</summary>
public record UserDto(int Id, int OrganizationId, string Email, string Role);

public record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

public record MeResponse(int UserId, int OrganizationId, string Email, string Role);
