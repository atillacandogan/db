#!/usr/bin/env bash

set -euo pipefail

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK bulunamadi. Once .NET 10 SDK'yi kurun." >&2
  exit 1
fi

if ! command -v openssl >/dev/null 2>&1; then
  echo "Secret uretmek icin openssl bulunamadi." >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project_path="$script_dir/../src/DbScanner.AppHost/DbScanner.AppHost.csproj"
existing_secrets="$(dotnet user-secrets list --project "$project_path")"
force=false

if [[ "${1:-}" == "--force" ]]; then
  force=true
elif [[ $# -gt 0 ]]; then
  echo "Kullanim: $0 [--force]" >&2
  exit 1
fi

new_demo_password() {
  printf '%sAa1!' "$(openssl rand -base64 24 | tr -d '\r\n')"
}

new_protection_key() {
  openssl rand -base64 32 | tr -d '\r\n'
}

has_secret() {
  grep -Fq "$1 = " <<<"$existing_secrets"
}

set_secret() {
  local name="$1"
  local value="$2"

  if [[ "$force" == false ]] && has_secret "$name"; then
    preserved=$((preserved + 1))
    return
  fi

  dotnet user-secrets --project "$project_path" set "$name" "$value" >/dev/null
  created=$((created + 1))
}

created=0
preserved=0

set_secret "Parameters:keycloak-admin-password" "$(new_demo_password)"
set_secret "Parameters:dbscanner-initial-admin-password" "$(new_demo_password)"
set_secret "Parameters:keycloak-admin-service-client-secret" "$(new_protection_key)"
set_secret "Parameters:demo-postgres-password" "$(new_demo_password)"
set_secret "Parameters:demo-mysql-password" "$(new_demo_password)"
set_secret "Parameters:demo-mysql-secure-password" "$(new_demo_password)"
set_secret "Parameters:demo-mssql-password" "$(new_demo_password)"
set_secret "Parameters:demo-mssql-app-password" "$(new_demo_password)"
set_secret "Parameters:demo-mssql-scanner-password" "$(new_demo_password)"
set_secret "Parameters:scanner-credential-protection-key" "$(new_protection_key)"
set_secret "Parameters:demo-oracle-password" "$(new_demo_password)"
set_secret "Parameters:demo-oracle-scanner-password" "$(new_demo_password)"

echo "Gelistirme secret kurulumu tamamlandi. Olusturulan: $created, korunan: $preserved."
echo "Degerler ekrana veya repository'ye yazilmadi."
