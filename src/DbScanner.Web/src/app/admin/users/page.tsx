import { AdminRouteGuard } from "@/components/auth/admin-route-guard";
import { TenantUserManagement } from "@/components/admin/tenant-user-management";

export default function AdminUsersPage() {
  return (
    <AdminRouteGuard>
      <TenantUserManagement />
    </AdminRouteGuard>
  );
}
