"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { createScan, getScan, type CreateScanRequest } from "@/lib/api/scans";
import { queryKeys } from "@/lib/query/query-keys";

export function useStartScan() {
  return useMutation({
    mutationFn: (request: CreateScanRequest) => createScan(request),
  });
}

export function useScanStatus(scanId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.scan(scanId ?? ""),
    queryFn: () => getScan(scanId ?? ""),
    enabled: Boolean(scanId),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === "pending" || status === "scanning" ? 2500 : false;
    },
  });
}
