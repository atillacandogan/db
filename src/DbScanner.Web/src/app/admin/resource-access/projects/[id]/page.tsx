import { AdminRouteGuard } from "@/components/auth/admin-route-guard";
import { ResourceAccessProjectDetail } from "@/components/admin/resource-access-project-detail";

export default async function ProjectDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <AdminRouteGuard><ResourceAccessProjectDetail projectId={id} /></AdminRouteGuard>;
}
