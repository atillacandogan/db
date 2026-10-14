import { AdminRouteGuard } from "@/components/auth/admin-route-guard";
import { ResourceAccessTeamDetail } from "@/components/admin/resource-access-team-detail";

export default async function TeamDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <AdminRouteGuard><ResourceAccessTeamDetail teamId={id} /></AdminRouteGuard>;
}
