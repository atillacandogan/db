import { getAccessToken } from "@/lib/auth/keycloak";

const defaultBaseUrl = "http://localhost:5166";
const requestTimeoutMilliseconds = 15_000;

function createRequestSignal(signal?: AbortSignal | null): AbortSignal {
  const timeoutSignal = AbortSignal.timeout(requestTimeoutMilliseconds);
  return signal ? AbortSignal.any([signal, timeoutSignal]) : timeoutSignal;
}

async function createHeaders(init?: RequestInit, includeContentType = false): Promise<Headers> {
  const headers = new Headers(init?.headers);
  headers.set("Accept", "application/json");
  headers.set("Authorization", `Bearer ${await getAccessToken()}`);

  if (includeContentType) {
    headers.set("Content-Type", "application/json");
  }

  return headers;
}

async function parseResponse<TResponse>(response: Response): Promise<TResponse> {
  if (!response.ok) {
    let message = `API request failed with ${response.status}`;

    try {
      const payload = (await response.json()) as { error?: string };
      if (payload?.error) {
        message = payload.error;
      }
    } catch {
      // Ignore body parse failures for non-json error responses.
    }

    throw new Error(message);
  }

  if (response.status === 204) {
    return undefined as TResponse;
  }

  return (await response.json()) as TResponse;
}

export async function apiGet<TResponse>(path: string, init?: RequestInit): Promise<TResponse> {
  const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? defaultBaseUrl;
  const response = await fetch(new URL(path, baseUrl), {
    ...init,
    headers: await createHeaders(init),
    signal: createRequestSignal(init?.signal),
  });

  return parseResponse<TResponse>(response);
}

export async function apiPost<TResponse, TRequest = unknown>(
  path: string,
  body: TRequest,
  init?: RequestInit,
): Promise<TResponse> {
  const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? defaultBaseUrl;
  const response = await fetch(new URL(path, baseUrl), {
    ...init,
    method: "POST",
    headers: await createHeaders(init, true),
    body: JSON.stringify(body),
    signal: createRequestSignal(init?.signal),
  });

  return parseResponse<TResponse>(response);
}

export async function apiDelete(path: string, init?: RequestInit): Promise<void> {
  const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? defaultBaseUrl;
  const response = await fetch(new URL(path, baseUrl), {
    ...init,
    method: "DELETE",
    headers: await createHeaders(init),
    signal: createRequestSignal(init?.signal),
  });

  await parseResponse<void>(response);
}

export async function apiPut<TResponse, TRequest = unknown>(
  path: string,
  body: TRequest,
  init?: RequestInit,
): Promise<TResponse> {
  const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? defaultBaseUrl;
  const response = await fetch(new URL(path, baseUrl), {
    ...init,
    method: "PUT",
    headers: await createHeaders(init, true),
    body: JSON.stringify(body),
    signal: createRequestSignal(init?.signal),
  });

  return parseResponse<TResponse>(response);
}
