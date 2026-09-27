// TypeScript shapes for the authentication API contracts (camelCase JSON as
// produced by the ASP.NET Core backend).

/// <summary>Basic identity exposed by both login and /me.</summary>
export interface CurrentUser {
  id: number;
  organizationId: number;
  email: string;
  role: string;
}

/// <summary>POST /api/auth/login response.</summary>
export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: CurrentUser;
}

/// <summary>GET /api/auth/me response (note: userId, not id).</summary>
export interface MeResponse {
  userId: number;
  organizationId: number;
  email: string;
  role: string;
}
