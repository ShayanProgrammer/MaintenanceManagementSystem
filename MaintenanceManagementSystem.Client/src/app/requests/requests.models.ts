// Mirrors the backend API contracts exactly (camelCase JSON):
// SiteDto, MaintenanceRequestDto, CreateMaintenanceRequestRequest.
// Server-assigned fields (organization/raiser/status/timestamps) are
// deliberately absent from the create payload — the backend owns them.

export interface Site {
  id: number;
  organizationId: number;
  name: string;
  description: string | null;
}

export interface MaintenanceRequest {
  id: number;
  siteId: number;
  siteName: string;
  title: string;
  description: string | null;
  estimatedCost: number;
  actualCost: number | null;
  /// <summary>Server-assigned lifecycle state string, e.g. "Approved" or
  /// "PendingApproval". The frontend never computes this.</summary>
  status: string;
  raisedByUserId: number;
  raisedByEmail: string;
  createdAtUtc: string;
  approvedAtUtc: string | null;
  approvedByUserId: number | null;
  completedAtUtc: string | null;
}

export interface CreateMaintenanceRequest {
  siteId: number;
  title: string;
  description: string;
  estimatedCost: number;
}
