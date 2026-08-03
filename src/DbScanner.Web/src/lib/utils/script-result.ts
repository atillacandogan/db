import type { ScanScriptResultDto } from "@/schemas/scan.schema";

export type ScriptExecutionStatus = "success" | "failed" | "skipped" | "unknown";

type ScriptResultStatusPresentation = {
  label:
    | "Success"
    | "Failed"
    | "Skipped"
    | "Not supported"
    | "Insufficient privileges"
    | "Connection failed"
    | "Authentication failed"
    | "Unknown";
  className: string;
  status: ScriptExecutionStatus;
};

export function getScriptResultStatus(
  result: Pick<ScanScriptResultDto, "exitCode" | "normalizedOutput" | "rawXml" | "resultStatus">,
): ScriptResultStatusPresentation {
  if (result.resultStatus) {
    return getExplicitStatus(result.resultStatus);
  }

  const exitCode = result.exitCode ?? null;

  if (hasSkippedStatus(result.normalizedOutput)) {
    return {
      label: "Skipped",
      className: "status-badge--skipped",
      status: "skipped",
    };
  }

  if (exitCode === null) {
    return {
      label: "Unknown",
      className: "status-badge--pending",
      status: "unknown",
    };
  }

  if (exitCode !== 0 || hasScriptExecutionError(result.normalizedOutput, result.rawXml)) {
    return {
      label: "Failed",
      className: "status-badge--failed",
      status: "failed",
    };
  }

  return {
    label: "Success",
    className: "status-badge--scanned",
    status: "success",
  };
}

function getExplicitStatus(
  status: NonNullable<ScanScriptResultDto["resultStatus"]>,
): ScriptResultStatusPresentation {
  switch (status) {
    case "passed":
      return { label: "Success", className: "status-badge--scanned", status: "success" };
    case "failed":
      return { label: "Failed", className: "status-badge--failed", status: "failed" };
    case "skipped":
      return { label: "Skipped", className: "status-badge--skipped", status: "skipped" };
    case "not_supported":
      return { label: "Not supported", className: "status-badge--skipped", status: "skipped" };
    case "insufficient_privileges":
      return {
        label: "Insufficient privileges",
        className: "status-badge--skipped",
        status: "skipped",
      };
    case "connection_failed":
      return { label: "Connection failed", className: "status-badge--failed", status: "failed" };
    case "authentication_failed":
      return { label: "Authentication failed", className: "status-badge--failed", status: "failed" };
  }
}

function hasSkippedStatus(output?: string | null) {
  return /(?:^|[;\s])status\s*=\s*skipped(?:;|\s|$)/i.test(output ?? "");
}

export function hasScriptExecutionError(...outputs: Array<string | null | undefined>) {
  return outputs.some((output) => {
    if (!output) {
      return false;
    }

    return /^\s*error(?:\s*:|\b)/i.test(output)
      || /script execution failed/i.test(output);
  });
}
