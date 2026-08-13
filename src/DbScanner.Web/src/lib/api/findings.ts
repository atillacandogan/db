import { apiGet } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type { FindingListItemDto } from "@/schemas/finding.schema";

export function getFindings(): Promise<FindingListItemDto[]> {
  return apiGet<FindingListItemDto[]>(endpoints.findings);
}

export function getFindingsForExport(): Promise<FindingListItemDto[]> {
  return apiGet<FindingListItemDto[]>(`${endpoints.findings}/export`);
}
