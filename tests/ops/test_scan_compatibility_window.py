import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from dataclasses import replace
from datetime import datetime, timedelta, timezone
from pathlib import Path


SCRIPT_PATH = Path(__file__).parents[2] / "scripts" / "manage-scan-compatibility-window.py"
SPEC = importlib.util.spec_from_file_location("compatibility_window", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class FakePrometheusClient:
    def __init__(self, *, version="stage-13", readiness=1, fallback=0, conflict=0, missing_points=0):
        self.version = version
        self.readiness = readiness
        self.fallback = fallback
        self.conflict = conflict
        self.missing_points = missing_points

    def query(self, expression, at):
        if MODULE.FALLBACK_METRIC in expression:
            return scalar(self.fallback)
        if MODULE.CONFLICT_METRIC in expression:
            return scalar(self.conflict)
        return [
            readiness_series("DbScanner.Api", "api-1", self.version, self.readiness),
            readiness_series("DbScanner.ScannerWorker", "worker-1", self.version, self.readiness),
        ]

    def query_range(self, expression, start, end, step_seconds):
        point_count = int((end - start).total_seconds() // step_seconds) + 1
        point_count = max(0, point_count - self.missing_points)
        timestamps = [start.timestamp() + index * step_seconds for index in range(point_count)]
        return [
            range_series("DbScanner.Api", self.version, timestamps, self.readiness),
            range_series("DbScanner.ScannerWorker", self.version, timestamps, self.readiness),
        ]


def scalar(value):
    return [{"metric": {}, "value": [0, str(value)]}]


def readiness_series(service, instance, version, readiness):
    return {
        "metric": {
            "service_name": service,
            "service_version": version,
            "service_instance_id": instance,
        },
        "value": [0, str(readiness)],
    }


def range_series(service, version, timestamps, readiness):
    return {
        "metric": {"service_name": service, "service_version": version},
        "values": [[timestamp, str(readiness)] for timestamp in timestamps],
    }


class CompatibilityWindowTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.manifest_path = Path(self.directory.name) / "manifest.json"
        self.signing_key = b"stage-13-test-signing-key-material" * 2
        self.manifest_path.write_text(json.dumps({
            "schemaVersion": 3,
            "environment": "staging",
            "requiredDeploymentVersion": "stage-13",
            "windowDays": 7,
            "sampleStepMinutes": 1440,
            "preflightMinutes": 2880,
            "evidenceSigningKeyId": "test-stage-13",
            "expectedServices": [
                {"name": "DbScanner.Api", "minimumInstances": 1},
                {"name": "DbScanner.ScannerWorker", "minimumInstances": 1},
            ],
        }))
        self.manifest = MODULE.load_manifest(self.manifest_path)
        self.now = datetime(2026, 10, 11, tzinfo=timezone.utc)

    def tearDown(self):
        self.directory.cleanup()

    def state(self, start=None):
        return MODULE.create_state(
            self.manifest,
            start or self.now - timedelta(days=7),
            {"inventory": "test"},
            self.signing_key,
        )

    def ready_evidence_files(self):
        state_path = Path(self.directory.name) / "state.json"
        artifact_path = Path(self.directory.name) / "result.json"
        key_path = Path(self.directory.name) / "signing.key"
        state = self.state()
        _, result = MODULE.evaluate_window(
            FakePrometheusClient(),
            self.manifest,
            state,
            self.now,
            self.signing_key,
        )
        MODULE.write_json(state_path, state)
        MODULE.write_json(
            artifact_path,
            MODULE.attest_result(result, self.manifest, state, self.signing_key),
        )
        key_path.write_bytes(self.signing_key)
        key_path.chmod(0o600)
        return state_path, artifact_path, key_path

    def test_preflight_accepts_expected_instances(self):
        passed, reasons, inventory = MODULE.evaluate_current_inventory(
            FakePrometheusClient(), self.manifest, self.now
        )

        self.assertTrue(passed)
        self.assertEqual([], reasons)
        self.assertEqual("api-1", inventory["DbScanner.Api"][0]["instance"])

    def test_schema_one_manifest_remains_parseable_without_burn_in(self):
        data = json.loads(self.manifest_path.read_text())
        data["schemaVersion"] = 1
        data.pop("preflightMinutes")
        data.pop("evidenceSigningKeyId")
        self.manifest_path.write_text(json.dumps(data))

        manifest = MODULE.load_manifest(self.manifest_path)

        self.assertEqual(1, manifest.schema_version)
        self.assertEqual(0, manifest.preflight_minutes)

    def test_schema_two_rejects_burn_in_shorter_than_sample_step(self):
        data = json.loads(self.manifest_path.read_text())
        data["schemaVersion"] = 2
        data.pop("evidenceSigningKeyId")
        data["preflightMinutes"] = 60
        self.manifest_path.write_text(json.dumps(data))

        with self.assertRaises(MODULE.GateError):
            MODULE.load_manifest(self.manifest_path)

    def test_schema_three_requires_signing_key_id(self):
        data = json.loads(self.manifest_path.read_text())
        data.pop("evidenceSigningKeyId")
        self.manifest_path.write_text(json.dumps(data))

        with self.assertRaises(MODULE.GateError):
            MODULE.load_manifest(self.manifest_path)

    def test_preflight_rejects_old_version(self):
        passed, reasons, _ = MODULE.evaluate_current_inventory(
            FakePrometheusClient(version="stage-9"), self.manifest, self.now
        )

        self.assertFalse(passed)
        self.assertTrue(any("required stage-13" in reason for reason in reasons))

    def test_preflight_requires_continuous_warmup(self):
        passed, reasons, evidence = MODULE.evaluate_preflight(
            FakePrometheusClient(missing_points=1),
            self.manifest,
            self.now,
        )

        self.assertFalse(passed)
        self.assertTrue(any("continuous ready samples" in reason for reason in reasons))
        self.assertEqual(2880, evidence["preflight"]["durationMinutes"])

    def test_preflight_rejects_fallback_growth(self):
        passed, reasons, _ = MODULE.evaluate_preflight(
            FakePrometheusClient(fallback=1),
            self.manifest,
            self.now,
        )

        self.assertFalse(passed)
        self.assertTrue(any("during preflight" in reason for reason in reasons))

    def test_evaluate_requires_state(self):
        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(), self.manifest, None, self.now, self.signing_key
        )

        self.assertEqual(4, exit_code)
        self.assertEqual("not_started", result["status"])

    def test_complete_clean_window_is_ready(self):
        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(), self.manifest, self.state(), self.now, self.signing_key
        )

        self.assertEqual(0, exit_code)
        self.assertEqual("ready", result["status"])

    def test_fallback_invalidates_window(self):
        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(fallback=1), self.manifest, self.state(), self.now, self.signing_key
        )

        self.assertEqual(3, exit_code)
        self.assertEqual("blocked", result["status"])
        self.assertTrue(any("fallback" in reason.lower() for reason in result["reasons"]))

    def test_missing_continuity_sample_invalidates_window(self):
        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(missing_points=1), self.manifest, self.state(), self.now, self.signing_key
        )

        self.assertEqual(3, exit_code)
        self.assertTrue(any("continuous ready samples" in reason for reason in result["reasons"]))

    def test_manifest_change_invalidates_window_without_querying(self):
        state = self.state()
        changed_manifest = replace(self.manifest, digest="different")

        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(), changed_manifest, state, self.now, self.signing_key
        )

        self.assertEqual(3, exit_code)
        self.assertTrue(any("manifest changed" in reason.lower() for reason in result["reasons"]))

    def test_tampered_state_is_blocked_without_querying(self):
        state = self.state()
        state["startedAtUtc"] = MODULE.format_time(self.now - timedelta(days=30))

        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(),
            self.manifest,
            state,
            self.now,
            self.signing_key,
        )

        self.assertEqual(3, exit_code)
        self.assertTrue(any("integrity digest" in reason for reason in result["reasons"]))

    def test_recomputed_checksum_cannot_bypass_hmac_signature(self):
        state = self.state()
        state["startedAtUtc"] = MODULE.format_time(self.now - timedelta(days=30))
        digest_source = {
            key: value
            for key, value in state.items()
            if key not in {
                "stateDigest",
                "signatureAlgorithm",
                "signingKeyId",
                "stateSignature",
            }
        }
        state["stateDigest"] = MODULE.canonical_digest(digest_source)

        errors = MODULE.validate_state(
            state,
            self.signing_key,
            self.manifest.signing_key_id,
        )

        self.assertTrue(any("HMAC signature is invalid" in error for error in errors))

    def test_wrong_signing_key_is_rejected(self):
        errors = MODULE.validate_state(
            self.state(),
            b"different-stage-13-signing-key" * 2,
            self.manifest.signing_key_id,
        )

        self.assertTrue(any("HMAC signature is invalid" in error for error in errors))

    def test_future_state_is_blocked(self):
        state = self.state(start=self.now + timedelta(minutes=1))

        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(),
            self.manifest,
            state,
            self.now,
            self.signing_key,
        )

        self.assertEqual(3, exit_code)
        self.assertTrue(any("starts after" in reason for reason in result["reasons"]))

    def test_state_is_written_atomically_with_integrity_digest(self):
        state_path = Path(self.directory.name) / "state.json"
        state = self.state()

        MODULE.write_json(state_path, state)

        self.assertEqual(
            [],
            MODULE.validate_state(
                json.loads(state_path.read_text()),
                self.signing_key,
                self.manifest.signing_key_id,
            ),
        )
        self.assertEqual([], list(state_path.parent.glob(f".{state_path.name}.*.tmp")))

    def test_evaluation_artifact_is_signed_and_verifiable(self):
        state = self.state()
        exit_code, result = MODULE.evaluate_window(
            FakePrometheusClient(),
            self.manifest,
            state,
            self.now,
            self.signing_key,
        )

        artifact = MODULE.attest_result(
            result,
            self.manifest,
            state,
            self.signing_key,
        )

        self.assertEqual(0, exit_code)
        self.assertEqual(
            [],
            MODULE.validate_attested_result(
                artifact,
                self.manifest,
                state,
                self.signing_key,
            ),
        )

    def test_modified_evaluation_artifact_is_rejected(self):
        state = self.state()
        _, result = MODULE.evaluate_window(
            FakePrometheusClient(),
            self.manifest,
            state,
            self.now,
            self.signing_key,
        )
        artifact = MODULE.attest_result(
            result,
            self.manifest,
            state,
            self.signing_key,
        )
        artifact["status"] = "blocked"

        errors = MODULE.validate_attested_result(
            artifact,
            self.manifest,
            state,
            self.signing_key,
        )

        self.assertTrue(any("HMAC signature is invalid" in error for error in errors))

    def test_signing_key_file_requires_at_least_32_bytes(self):
        key_path = Path(self.directory.name) / "short.key"
        key_path.write_bytes(b"too-short")
        key_path.chmod(0o600)

        with self.assertRaises(MODULE.GateError):
            MODULE.load_signing_key(key_path)

    @unittest.skipUnless(os.name == "posix", "POSIX permission check")
    def test_signing_key_file_rejects_group_or_world_access(self):
        key_path = Path(self.directory.name) / "open.key"
        key_path.write_bytes(self.signing_key)
        key_path.chmod(0o644)

        with self.assertRaises(MODULE.GateError):
            MODULE.load_signing_key(key_path)

    def test_verify_command_checks_state_and_artifact_offline(self):
        state_path = Path(self.directory.name) / "state.json"
        artifact_path = Path(self.directory.name) / "result.json"
        key_path = Path(self.directory.name) / "signing.key"
        state = self.state()
        _, result = MODULE.evaluate_window(
            FakePrometheusClient(),
            self.manifest,
            state,
            self.now,
            self.signing_key,
        )
        MODULE.write_json(state_path, state)
        MODULE.write_json(
            artifact_path,
            MODULE.attest_result(result, self.manifest, state, self.signing_key),
        )
        key_path.write_bytes(self.signing_key)
        key_path.chmod(0o600)

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT_PATH),
                "verify",
                "--manifest",
                str(self.manifest_path),
                "--state",
                str(state_path),
                "--artifact",
                str(artifact_path),
                "--signing-key-file",
                str(key_path),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(0, completed.returncode, completed.stdout)
        self.assertEqual("verified", json.loads(completed.stdout)["status"])

    def test_verify_command_preserves_not_started_exit_without_key(self):
        missing_state = Path(self.directory.name) / "missing-state.json"

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT_PATH),
                "verify",
                "--manifest",
                str(self.manifest_path),
                "--state",
                str(missing_state),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(4, completed.returncode, completed.stdout)
        self.assertEqual("not_started", json.loads(completed.stdout)["status"])

    def test_verify_command_rejects_authentic_blocked_artifact_for_release(self):
        state_path = Path(self.directory.name) / "state.json"
        artifact_path = Path(self.directory.name) / "blocked-result.json"
        key_path = Path(self.directory.name) / "signing.key"
        state = self.state()
        blocked = MODULE.result_document(
            "blocked",
            self.now,
            ["test blocker"],
        )
        MODULE.write_json(state_path, state)
        MODULE.write_json(
            artifact_path,
            MODULE.attest_result(blocked, self.manifest, state, self.signing_key),
        )
        key_path.write_bytes(self.signing_key)
        key_path.chmod(0o600)

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT_PATH),
                "verify",
                "--manifest",
                str(self.manifest_path),
                "--state",
                str(state_path),
                "--artifact",
                str(artifact_path),
                "--signing-key-file",
                str(key_path),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

        result = json.loads(completed.stdout)
        self.assertEqual(3, completed.returncode, completed.stdout)
        self.assertTrue(any("required 'ready'" in reason for reason in result["reasons"]))

    def test_verify_command_requires_evaluation_artifact(self):
        state_path = Path(self.directory.name) / "state.json"
        key_path = Path(self.directory.name) / "signing.key"
        MODULE.write_json(state_path, self.state())
        key_path.write_bytes(self.signing_key)
        key_path.chmod(0o600)

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT_PATH),
                "verify",
                "--manifest",
                str(self.manifest_path),
                "--state",
                str(state_path),
                "--signing-key-file",
                str(key_path),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

        result = json.loads(completed.stdout)
        self.assertEqual(3, completed.returncode, completed.stdout)
        self.assertTrue(any("requires --artifact" in reason for reason in result["reasons"]))

    def test_sealed_bundle_is_self_contained_and_verifiable(self):
        state_path, artifact_path, _ = self.ready_evidence_files()
        bundle_path = Path(self.directory.name) / "release-evidence"

        try:
            index = MODULE.seal_evidence_bundle(
                bundle_path,
                self.manifest_path,
                state_path,
                artifact_path,
                self.signing_key,
                self.now,
            )
            reasons, evidence = MODULE.verify_evidence_bundle(
                bundle_path,
                self.signing_key,
            )

            self.assertEqual([], reasons)
            self.assertEqual("stage-13", evidence["deploymentVersion"])
            self.assertEqual(self.manifest.digest, index["manifestDigest"])
            self.assertEqual(
                sorted(MODULE.BUNDLE_FILES),
                sorted(path.name for path in bundle_path.iterdir()),
            )
        finally:
            if bundle_path.exists() and os.name == "posix":
                bundle_path.chmod(0o700)
                for path in bundle_path.iterdir():
                    path.chmod(0o600)

    def test_seal_refuses_to_overwrite_existing_bundle(self):
        state_path, artifact_path, _ = self.ready_evidence_files()
        bundle_path = Path(self.directory.name) / "release-evidence"

        try:
            MODULE.seal_evidence_bundle(
                bundle_path,
                self.manifest_path,
                state_path,
                artifact_path,
                self.signing_key,
                self.now,
            )

            with self.assertRaises(MODULE.GateError):
                MODULE.seal_evidence_bundle(
                    bundle_path,
                    self.manifest_path,
                    state_path,
                    artifact_path,
                    self.signing_key,
                    self.now,
                )
        finally:
            if bundle_path.exists() and os.name == "posix":
                bundle_path.chmod(0o700)
                for path in bundle_path.iterdir():
                    path.chmod(0o600)

    def test_tampered_sealed_bundle_is_rejected(self):
        state_path, artifact_path, _ = self.ready_evidence_files()
        bundle_path = Path(self.directory.name) / "release-evidence"

        try:
            MODULE.seal_evidence_bundle(
                bundle_path,
                self.manifest_path,
                state_path,
                artifact_path,
                self.signing_key,
                self.now,
            )
            bundled_artifact = bundle_path / MODULE.BUNDLE_ARTIFACT
            if os.name == "posix":
                bundle_path.chmod(0o700)
                bundled_artifact.chmod(0o600)
            artifact = json.loads(bundled_artifact.read_text())
            artifact["status"] = "blocked"
            bundled_artifact.write_text(json.dumps(artifact))

            reasons, _ = MODULE.verify_evidence_bundle(
                bundle_path,
                self.signing_key,
            )

            self.assertTrue(any("HMAC signature is invalid" in reason for reason in reasons))
            self.assertTrue(any("file inventory or digest" in reason for reason in reasons))
        finally:
            if bundle_path.exists() and os.name == "posix":
                bundle_path.chmod(0o700)
                for path in bundle_path.iterdir():
                    path.chmod(0o600)

    def test_bundle_with_extra_file_is_rejected(self):
        state_path, artifact_path, _ = self.ready_evidence_files()
        bundle_path = Path(self.directory.name) / "release-evidence"

        try:
            MODULE.seal_evidence_bundle(
                bundle_path,
                self.manifest_path,
                state_path,
                artifact_path,
                self.signing_key,
                self.now,
            )
            if os.name == "posix":
                bundle_path.chmod(0o700)
            (bundle_path / "unexpected.txt").write_text("not part of the bundle")

            reasons, _ = MODULE.verify_evidence_bundle(
                bundle_path,
                self.signing_key,
            )

            self.assertEqual(
                ["Evidence bundle must contain exactly the four expected files."],
                reasons,
            )
        finally:
            if bundle_path.exists() and os.name == "posix":
                bundle_path.chmod(0o700)
                for path in bundle_path.iterdir():
                    path.chmod(0o600)

    def test_bundle_with_wrong_key_is_rejected(self):
        state_path, artifact_path, _ = self.ready_evidence_files()
        bundle_path = Path(self.directory.name) / "release-evidence"

        try:
            MODULE.seal_evidence_bundle(
                bundle_path,
                self.manifest_path,
                state_path,
                artifact_path,
                self.signing_key,
                self.now,
            )

            reasons, _ = MODULE.verify_evidence_bundle(
                bundle_path,
                b"wrong-stage-13-bundle-key-material" * 2,
            )

            self.assertTrue(any("HMAC signature is invalid" in reason for reason in reasons))
        finally:
            if bundle_path.exists() and os.name == "posix":
                bundle_path.chmod(0o700)
                for path in bundle_path.iterdir():
                    path.chmod(0o600)

    def test_seal_rejects_authentic_non_ready_artifact(self):
        state_path = Path(self.directory.name) / "state.json"
        artifact_path = Path(self.directory.name) / "result.json"
        state = self.state()
        blocked = MODULE.result_document("blocked", self.now, ["test blocker"])
        MODULE.write_json(state_path, state)
        MODULE.write_json(
            artifact_path,
            MODULE.attest_result(blocked, self.manifest, state, self.signing_key),
        )

        with self.assertRaises(MODULE.GateError):
            MODULE.seal_evidence_bundle(
                Path(self.directory.name) / "release-evidence",
                self.manifest_path,
                state_path,
                artifact_path,
                self.signing_key,
                self.now,
            )

    def test_verify_bundle_command_operates_offline(self):
        state_path, artifact_path, key_path = self.ready_evidence_files()
        bundle_path = Path(self.directory.name) / "release-evidence"

        try:
            MODULE.seal_evidence_bundle(
                bundle_path,
                self.manifest_path,
                state_path,
                artifact_path,
                self.signing_key,
                self.now,
            )
            completed = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT_PATH),
                    "verify-bundle",
                    "--bundle",
                    str(bundle_path),
                    "--signing-key-file",
                    str(key_path),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, completed.returncode, completed.stdout)
            self.assertEqual("bundle_verified", json.loads(completed.stdout)["status"])
        finally:
            if bundle_path.exists() and os.name == "posix":
                bundle_path.chmod(0o700)
                for path in bundle_path.iterdir():
                    path.chmod(0o600)


if __name__ == "__main__":
    unittest.main()
