export const queryKeys = {
  status: ["status"],
  targets: ["targets"],
  scans: ["scans"],
  scan: (scanId: string) => ["scans", scanId] as const,
  findings: ["findings"],
  rules: ["rules"],
  discoveries: ["discoveries"],
  discovery: (discoveryId: string) => ["discoveries", discoveryId] as const,
} as const;
