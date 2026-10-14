import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api/client";
import { endpoints } from "@/lib/api/endpoints";
import type { DatabaseEngine, TargetConnectionMode, TargetDto } from "@/schemas/target.schema";

export type CreateTargetRequest = {
  name: string;
  host?: string;
  port?: number;
  engine: DatabaseEngine;
  environmentTag?: string;
  enabled?: boolean;
  connectionMode?: TargetConnectionMode;
  connectionString?: string;
  credentials?: DatabaseCredentialRequest;
};

export type DatabaseCredentialRequest = {
  database: string;
  username: string;
  password: string;
  encrypt: boolean;
  trustServerCertificate: boolean;
  connectionTimeoutSeconds: number;
};

export type UpdateTargetRequest = {
  name: string;
  host: string;
  port: number;
  engine: DatabaseEngine;
  environmentTag?: string;
  enabled: boolean;
  credentials?: DatabaseCredentialRequest;
  clearCredentials?: boolean;
};

export async function getTargets(): Promise<TargetDto[]> {
  const [targets, access] = await Promise.all([
    apiGet<TargetDto[]>(endpoints.targets),
    apiGet<{ targetId: string; permissions: number }[]>("/api/access/targets"),
  ]);
  return targets.map(target => ({ ...target, permissions: access.find(item => item.targetId === target.id)?.permissions ?? 0 }));
}

export function createTarget(request: CreateTargetRequest): Promise<TargetDto> {
  return apiPost<TargetDto, CreateTargetRequest>(endpoints.targets, request);
}

export function updateTarget(
  targetId: string,
  request: UpdateTargetRequest,
): Promise<TargetDto> {
  return apiPut<TargetDto, UpdateTargetRequest>(
    `${endpoints.targets}/${targetId}`,
    request,
  );
}

export function deleteTarget(targetId: string): Promise<void> {
  return apiDelete(`${endpoints.targets}/${targetId}`);
}
