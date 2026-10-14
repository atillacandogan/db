export const dbScannerRoles = {
  viewer: "dbscanner-viewer",
  operator: "dbscanner-operator",
  admin: "dbscanner-admin",
} as const;

export type DbScannerRole = (typeof dbScannerRoles)[keyof typeof dbScannerRoles];

export type DbScannerPermission =
  | "view"
  | "operateTargets"
  | "operateScans"
  | "manageCatalog"
  | "deleteResources"
  | "manageUsers";

const permissionRoles: Record<DbScannerPermission, readonly DbScannerRole[]> = {
  view: [dbScannerRoles.viewer, dbScannerRoles.operator, dbScannerRoles.admin],
  operateTargets: [dbScannerRoles.operator, dbScannerRoles.admin],
  operateScans: [dbScannerRoles.operator, dbScannerRoles.admin],
  manageCatalog: [dbScannerRoles.admin],
  deleteResources: [dbScannerRoles.admin],
  manageUsers: [dbScannerRoles.admin],
};

export function hasAnyRole(roles: readonly string[], expectedRoles: readonly string[]) {
  return expectedRoles.some((role) => roles.includes(role));
}

export function hasPermission(roles: readonly string[], permission: DbScannerPermission) {
  return hasAnyRole(roles, permissionRoles[permission]);
}
