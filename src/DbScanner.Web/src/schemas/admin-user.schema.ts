import type { DbScannerRole } from "@/lib/auth/authorization";

export type AdminUserDto = {
  id: string;
  username: string;
  firstName?: string | null;
  lastName?: string | null;
  role?: DbScannerRole | null;
  enabled: boolean;
  mfaConfigured?: boolean | null;
  mfaRequired: boolean;
  lastLoginAt?: string | null;
};

export type AdminUserPageDto = {
  items: AdminUserDto[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type AdminUserCreationDto = {
  user: AdminUserDto;
};
