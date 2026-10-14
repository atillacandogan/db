#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "Usage: $0 <evidence-bundle-directory> <signing-key-file> [verification-output.json]" >&2
  exit 64
fi

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
bundle_path="$1"
signing_key_path="$2"

arguments=(
  verify-bundle
  --bundle "$bundle_path"
  --signing-key-file "$signing_key_path"
)

if [[ $# -eq 3 ]]; then
  arguments+=(--output "$3")
fi

exec python3 "$script_directory/manage-scan-compatibility-window.py" "${arguments[@]}"
