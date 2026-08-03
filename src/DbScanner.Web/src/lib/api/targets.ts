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

export function getTargets(): Promise<TargetDto[]> {
  return apiGet<TargetDto[]>(endpoints.targets);
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
