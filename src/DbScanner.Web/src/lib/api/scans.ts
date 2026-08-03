import { apiDelete, apiGet, apiPost } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type { ScanDto, ScanListItemDto } from "@/schemas/scan.schema";

export type CreateScanRequest = {
  targetId: string;
  selectedNseScriptIds?: string[];
};

export function createScan(request: CreateScanRequest): Promise<ScanDto> {
  return apiPost<ScanDto>(endpoints.scans, request);
}

export function getScan(scanId: string): Promise<ScanDto> {
  return apiGet<ScanDto>(`${endpoints.scans}/${scanId}`);
}

export function getScans(): Promise<ScanListItemDto[]> {
  return apiGet<ScanListItemDto[]>(endpoints.scans);
}

export function cancelScan(scanId: string): Promise<ScanDto> {
  return apiPost<ScanDto, Record<string, never>>(`${endpoints.scans}/${scanId}/cancel`, {});
}

export function deleteScan(scanId: string): Promise<void> {
  return apiDelete(`${endpoints.scans}/${scanId}`);
}
