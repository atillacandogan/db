import { AdminRouteGuard } from "@/components/auth/admin-route-guard";
import { ResourceAccessClient } from "@/components/admin/resource-access-client";

export default function ResourceAccessPage() {
  return <AdminRouteGuard><ResourceAccessClient /></AdminRouteGuard>;
}
