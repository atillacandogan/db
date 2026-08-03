import type { ScanStatus } from "@/schemas/scan.schema";
import { getScanStatusLabel } from "@/lib/utils/status";

type ScanStatusBadgeProps = {
  status: ScanStatus;
};

export function ScanStatusBadge({ status }: ScanStatusBadgeProps) {
  return <span className={`status-badge status-badge--${status}`}>{getScanStatusLabel(status)}</span>;
}
