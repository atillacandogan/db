import type { ScanStatus } from "@/schemas/scan.schema";

export function getScanStatusLabel(status: ScanStatus): string {
  const labels: Record<ScanStatus, string> = {
    pending: "Pending",
    scanning: "Scanning",
    scanned: "Scanned",
    failed: "Failed",
    timeout: "Timeout",
    cancelled: "Cancelled",
  };

  return labels[status];
}
