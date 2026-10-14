import { apiGet, apiPatch, apiPost, apiPut } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type { DbScannerRole } from "@/lib/auth/authorization";
import type {
  AdminUserDto,
  AdminUserCreationDto,
  AdminUserPageDto,
} from "@/schemas/admin-user.schema";

export type AdminUserListRequest = {
  search?: string;
  role?: DbScannerRole;
  enabled?: boolean;
  page: number;
  pageSize: number;
};

export type CreateAdminUserRequest = {
  username: string;
  firstName?: string;
  lastName?: string;
  role: DbScannerRole;
  requireMfa: boolean;
  temporaryPassword: string;
};

export type UpdateAdminUserRequest = {
  firstName?: string;
  lastName?: string;
};

export function getAdminUsers(request: AdminUserListRequest): Promise<AdminUserPageDto> {
  const query = new URLSearchParams({
    page: String(request.page),
    pageSize: String(request.pageSize),
  });
  if (request.search) query.set("search", request.search);
  if (request.role) query.set("role", request.role);
  if (request.enabled !== undefined) query.set("enabled", String(request.enabled));
  return apiGet<AdminUserPageDto>(`${endpoints.adminUsers}?${query}`);
}

export function createAdminUser(request: CreateAdminUserRequest): Promise<AdminUserCreationDto> {
  return apiPost<AdminUserCreationDto, CreateAdminUserRequest>(endpoints.adminUsers, request);
}

export function updateAdminUser(
  userId: string,
  request: UpdateAdminUserRequest,
): Promise<AdminUserDto> {
  return apiPut<AdminUserDto, UpdateAdminUserRequest>(`${endpoints.adminUsers}/${userId}`, request);
}

export function updateAdminUserRole(userId: string, role: DbScannerRole): Promise<AdminUserDto> {
  return apiPut<AdminUserDto, { role: DbScannerRole }>(
    `${endpoints.adminUsers}/${userId}/roles`,
    { role },
  );
}

export function updateAdminUserStatus(userId: string, enabled: boolean): Promise<AdminUserDto> {
  return apiPatch<AdminUserDto, { enabled: boolean }>(
    `${endpoints.adminUsers}/${userId}/status`,
    { enabled },
  );
}

export function updateAdminUserMfa(userId: string, required: boolean): Promise<AdminUserDto> {
  return apiPut<AdminUserDto, { required: boolean }>(
    `${endpoints.adminUsers}/${userId}/mfa`,
    { required },
  );
}

export function logoutAdminUserSessions(userId: string): Promise<void> {
  return apiPost<void, Record<string, never>>(`${endpoints.adminUsers}/${userId}/logout`, {});
}
