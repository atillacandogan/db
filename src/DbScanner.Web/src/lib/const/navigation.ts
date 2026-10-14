import { dbScannerRoles, type DbScannerRole } from "@/lib/auth/authorization";

export type NavigationItem = {
  href: string;
  label: string;
  requiredRole?: DbScannerRole;
};

export const navigationItems = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/targets", label: "Targets" },
  { href: "/scans", label: "Scans" },
  { href: "/rules", label: "Rules" },
  { href: "/findings", label: "Findings" },
  { href: "/admin/users", label: "Access Management", requiredRole: dbScannerRoles.admin },
  { href: "/admin/resource-access", label: "Teams and Target Permissions", requiredRole: dbScannerRoles.admin },
] as const satisfies readonly NavigationItem[];
