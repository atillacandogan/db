#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 || $# -gt 4 ]]; then
  echo "Usage: $0 <environment> <api-base-url> <admin-token> [output-jsonl]" >&2
  exit 64
fi

environment_name="$1"
api_base_url="${2%/}"
admin_token="$3"
output_path="${4:-docs/architecture/scan-compatibility-observations.jsonl}"

status_json="$(curl --fail --silent --show-error "${api_base_url}/api/status")"
readiness_json="$(curl --fail --silent --show-error \
  --header "Authorization: Bearer ${admin_token}" \
  "${api_base_url}/api/scan-checks/compatibility-readiness")"

jq --compact-output --null-input \
  --arg observedAtUtc "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg environment "$environment_name" \
  --argjson deployment "$status_json" \
  --argjson readiness "$readiness_json" \
  '{observedAtUtc:$observedAtUtc, environment:$environment, deployment:$deployment, readiness:$readiness}' \
  >> "$output_path"

echo "Compatibility observation appended to ${output_path}."
