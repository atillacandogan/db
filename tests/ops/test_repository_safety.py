"""Read-only publication guards and isolated setup-script tests; no live secrets."""
import json
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class RepositorySafetyTests(unittest.TestCase):
    def test_private_artifacts_and_credentials_are_ignored_but_source_is_not(self):
        private = [".tmp-staj/artifact.md", "deliverables/private.docx",
                   "tmp/pdfs/stamp_signature.png", "output/pdf/private.pdf",
                   "ops/observability/compatibility-rollout.local.json",
                   ".env.production", "secrets.json", "local.key", "database.dump"]
        result = subprocess.run(["git", "check-ignore", "--no-index", "--stdin"],
                                input="\n".join(private) + "\n", text=True,
                                capture_output=True, cwd=ROOT, check=True)
        self.assertEqual(set(private), set(result.stdout.splitlines()))
        source = ["src/DbScanner.Api/Program.cs", "tests/ops/test_repository_safety.py",
                  "docker/keycloak/import/dbscanner-realm.json",
                  "ops/observability/compatibility-rollout.example.json"]
        result = subprocess.run(["git", "check-ignore", "--no-index", "--stdin"],
                                input="\n".join(source) + "\n", text=True,
                                capture_output=True, cwd=ROOT)
        self.assertEqual(1, result.returncode)
        self.assertEqual("", result.stdout)

    def test_realm_passwords_and_client_secrets_are_environment_placeholders(self):
        realm = json.loads((ROOT / "docker/keycloak/import/dbscanner-realm.json").read_text())
        for client in realm["clients"]:
            if "secret" in client:
                self.assertRegex(client["secret"], r"^\$\{[A-Z_]+\}$")
        admin = next(user for user in realm["users"] if user["username"] == "dbscanner-admin")
        credential = admin["credentials"][0]
        self.assertEqual("${DBSCANNER_INITIAL_ADMIN_PASSWORD}", credential["value"])
        self.assertTrue(credential["temporary"])
        host = (ROOT / "src/DbScanner.AppHost/AppHost.cs").read_text()
        self.assertIn('.WithEnvironment("DBSCANNER_INITIAL_ADMIN_PASSWORD", dbscannerInitialAdminPassword)', host)
        self.assertNotRegex(host, r'AddParameter\(\s*"[^"]+"\s*,\s*"[^"]+"\s*,\s*secret:\s*true')
        api = json.loads((ROOT / "src/DbScanner.Api/appsettings.json").read_text())
        self.assertNotIn("ConnectionStrings", api)

    def test_shell_and_powershell_provision_the_same_secret_names(self):
        shell = (ROOT / "scripts/setup-dev-secrets.sh").read_text()
        powershell = (ROOT / "scripts/setup-dev-secrets.ps1").read_text()
        names = set(re.findall(r'set_secret "(Parameters:[^"]+)"', shell))
        self.assertEqual(names, set(re.findall(r'"(Parameters:[^"]+)"\s*=', powershell)))
        self.assertIn("Parameters:dbscanner-initial-admin-password", names)
        self.assertIn("Parameters:demo-postgres-password", names)
        self.assertIn("Parameters:demo-mysql-password", names)

    def test_local_observability_does_not_publish_anonymous_ui_to_the_lan(self):
        compose = (ROOT / "ops/observability/docker-compose.yml").read_text()
        self.assertIn('"127.0.0.1:9090:9090"', compose)
        self.assertIn('"127.0.0.1:3001:3000"', compose)
        self.assertIn('${DBSCANNER_OTLP_BIND_ADDRESS:-127.0.0.1}:4317:4317', compose)
        self.assertIn('${DBSCANNER_OTLP_BIND_ADDRESS:-127.0.0.1}:4318:4318', compose)

    @unittest.skipUnless(shutil.which("bash") and shutil.which("openssl"), "Bash/OpenSSL not available")
    def test_setup_preserves_existing_values_and_never_prints_generated_secrets(self):
        # Mock only dotnet; generation still uses the real cryptographic OpenSSL RNG.
        with tempfile.TemporaryDirectory(prefix="dbscanner-setup-test-") as temporary:
            directory = Path(temporary)
            state = directory / "state.json"
            state.write_text(json.dumps({"Parameters:keycloak-admin-password": "fixture-existing"}))
            fake = directory / "dotnet"
            fake.write_text('''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
state = Path(os.environ["DBSCANNER_SETUP_TEST_STATE"])
values = json.loads(state.read_text())
args = sys.argv[1:]
if "list" in args:
    for name, value in values.items(): print(name + " = " + value)
elif "set" in args:
    index = args.index("set")
    values[args[index + 1]] = args[index + 2]
    state.write_text(json.dumps(values))
else: sys.exit(1)
''')
            fake.chmod(0o700)
            environment = dict(os.environ, PATH=str(directory) + os.pathsep + os.environ["PATH"],
                               DBSCANNER_SETUP_TEST_STATE=str(state))
            first = subprocess.run(["bash", str(ROOT / "scripts/setup-dev-secrets.sh")],
                                   env=environment, text=True, capture_output=True, check=True)
            values = json.loads(state.read_text())
            self.assertEqual("fixture-existing", values["Parameters:keycloak-admin-password"])
            self.assertEqual(12, len(values))
            second = subprocess.run(["bash", str(ROOT / "scripts/setup-dev-secrets.sh")],
                                    env=environment, text=True, capture_output=True, check=True)
            self.assertEqual(values, json.loads(state.read_text()))
            for value in values.values():
                self.assertNotIn(value, first.stdout + first.stderr + second.stdout + second.stderr)
            self.assertNotEqual(values["Parameters:demo-postgres-password"], values["Parameters:demo-mysql-password"])


if __name__ == "__main__":
    unittest.main()
