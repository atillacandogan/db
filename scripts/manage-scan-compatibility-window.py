#!/usr/bin/env python3
"""Manage and archive the scan-check compatibility observation window."""

from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
import re
import shutil
import sys
import tempfile
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any


READINESS_METRIC = "dbscanner_scan_check_compatibility_ready"
FALLBACK_METRIC = "dbscanner_scan_check_compatibility_fallbacks_total"
CONFLICT_METRIC = "dbscanner_scan_check_compatibility_conflicts_total"
SAFE_LABEL = re.compile(r"^[A-Za-z0-9_.-]+$")
BUNDLE_MANIFEST = "rollout-manifest.json"
BUNDLE_STATE = "observation-state.json"
BUNDLE_ARTIFACT = "evaluation-result.json"
BUNDLE_INDEX = "evidence-index.json"
BUNDLE_FILES = (BUNDLE_MANIFEST, BUNDLE_STATE, BUNDLE_ARTIFACT, BUNDLE_INDEX)


class GateError(RuntimeError):
    pass


@dataclass(frozen=True)
class ExpectedService:
    name: str
    minimum_instances: int


@dataclass(frozen=True)
class RolloutManifest:
    schema_version: int
    environment: str
    required_version: str
    window_days: int
    sample_step_minutes: int
    preflight_minutes: int
    signing_key_id: str | None
    expected_services: tuple[ExpectedService, ...]
    digest: str


class PrometheusClient:
    def __init__(self, base_url: str, timeout_seconds: int = 15) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = timeout_seconds

    def query(self, expression: str, at: datetime) -> list[dict[str, Any]]:
        return self._request(
            "/api/v1/query",
            {"query": expression, "time": format_time(at)},
        )

    def query_range(
        self,
        expression: str,
        start: datetime,
        end: datetime,
        step_seconds: int,
    ) -> list[dict[str, Any]]:
        return self._request(
            "/api/v1/query_range",
            {
                "query": expression,
                "start": format_time(start),
                "end": format_time(end),
                "step": str(step_seconds),
            },
        )

    def _request(self, path: str, parameters: dict[str, str]) -> list[dict[str, Any]]:
        url = f"{self.base_url}{path}?{urllib.parse.urlencode(parameters)}"
        try:
            with urllib.request.urlopen(url, timeout=self.timeout_seconds) as response:
                payload = json.load(response)
        except Exception as exception:
            raise GateError(f"Prometheus request failed: {exception}") from exception

        if payload.get("status") != "success":
            raise GateError(f"Prometheus rejected the query: {payload}")
        return payload.get("data", {}).get("result", [])


def load_manifest(path: Path) -> RolloutManifest:
    raw_bytes = path.read_bytes()
    data = json.loads(raw_bytes)
    schema_version = data.get("schemaVersion")
    if schema_version not in (1, 2, 3):
        raise GateError("Unsupported rollout manifest schemaVersion.")

    environment = require_safe_label(data.get("environment"), "environment")
    required_version = require_safe_label(
        data.get("requiredDeploymentVersion"),
        "requiredDeploymentVersion",
    )
    window_days = require_positive_int(data.get("windowDays"), "windowDays")
    sample_step_minutes = require_positive_int(
        data.get("sampleStepMinutes"),
        "sampleStepMinutes",
    )
    preflight_minutes = (
        require_positive_int(data.get("preflightMinutes"), "preflightMinutes")
        if schema_version >= 2
        else 0
    )
    if preflight_minutes and preflight_minutes < sample_step_minutes:
        raise GateError("preflightMinutes must be at least sampleStepMinutes.")
    if preflight_minutes % sample_step_minutes != 0:
        raise GateError("preflightMinutes must be divisible by sampleStepMinutes.")
    signing_key_id = (
        require_safe_label(data.get("evidenceSigningKeyId"), "evidenceSigningKeyId")
        if schema_version == 3
        else None
    )
    services = tuple(
        ExpectedService(
            require_safe_label(item.get("name"), "expectedServices.name"),
            require_positive_int(
                item.get("minimumInstances"),
                "expectedServices.minimumInstances",
            ),
        )
        for item in data.get("expectedServices", [])
    )
    if not services:
        raise GateError("expectedServices must contain at least one service.")
    if len({service.name for service in services}) != len(services):
        raise GateError("expectedServices contains duplicate service names.")

    return RolloutManifest(
        schema_version,
        environment,
        required_version,
        window_days,
        sample_step_minutes,
        preflight_minutes,
        signing_key_id,
        services,
        hashlib.sha256(raw_bytes).hexdigest(),
    )


def require_safe_label(value: Any, field: str) -> str:
    if not isinstance(value, str) or not SAFE_LABEL.fullmatch(value):
        raise GateError(f"{field} must use only letters, numbers, dot, underscore or dash.")
    return value


def require_positive_int(value: Any, field: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
        raise GateError(f"{field} must be a positive integer.")
    return value


def selector(manifest: RolloutManifest) -> str:
    return f'deployment_environment_name="{manifest.environment}"'


def evaluate_current_inventory(
    client: PrometheusClient,
    manifest: RolloutManifest,
    at: datetime,
) -> tuple[bool, list[str], dict[str, Any]]:
    expression = (
        "min by (service_name, service_version, service_instance_id) "
        f"({READINESS_METRIC}{{{selector(manifest)}}})"
    )
    result = client.query(expression, at)
    reasons: list[str] = []
    inventory: dict[str, list[dict[str, Any]]] = {
        expected.name: [] for expected in manifest.expected_services
    }

    for series in result:
        metric = series.get("metric", {})
        service_name = metric.get("service_name")
        if service_name not in inventory:
            continue
        value = float(series.get("value", [0, "nan"])[1])
        inventory[service_name].append(
            {
                "version": metric.get("service_version", "unknown"),
                "instance": metric.get("service_instance_id", "unknown"),
                "readiness": value,
            }
        )

    for expected in manifest.expected_services:
        instances = inventory[expected.name]
        if len(instances) < expected.minimum_instances:
            reasons.append(
                f"{expected.name} has {len(instances)} instance(s); "
                f"expected at least {expected.minimum_instances}."
            )
        for instance in instances:
            if instance["version"] != manifest.required_version:
                reasons.append(
                    f"{expected.name}/{instance['instance']} reports version "
                    f"{instance['version']}; required {manifest.required_version}."
                )
            if instance["readiness"] != 1:
                reasons.append(
                    f"{expected.name}/{instance['instance']} readiness is "
                    f"{instance['readiness']}; required 1."
                )

    return not reasons, reasons, inventory


def evaluate_preflight(
    client: PrometheusClient,
    manifest: RolloutManifest,
    at: datetime,
) -> tuple[bool, list[str], dict[str, Any]]:
    current_ok, reasons, inventory = evaluate_current_inventory(client, manifest, at)
    evidence: dict[str, Any] = {"inventory": inventory}
    if manifest.preflight_minutes == 0:
        return current_ok, reasons, evidence

    started_at = at - timedelta(minutes=manifest.preflight_minutes)
    continuity = evaluate_continuity(client, manifest, started_at, at)
    reasons.extend(continuity["reasons"])
    fallback_increase = query_counter_increase(
        client,
        FALLBACK_METRIC,
        manifest,
        started_at,
        at,
    )
    conflict_increase = query_counter_increase(
        client,
        CONFLICT_METRIC,
        manifest,
        started_at,
        at,
    )
    if fallback_increase > 0:
        reasons.append(
            f"Legacy fallback counter increased by {fallback_increase} during preflight."
        )
    if conflict_increase > 0:
        reasons.append(
            f"Contract conflict counter increased by {conflict_increase} during preflight."
        )
    evidence["preflight"] = {
        "startedAtUtc": format_time(started_at),
        "durationMinutes": manifest.preflight_minutes,
        "continuity": continuity["evidence"],
        "fallbackIncrease": fallback_increase,
        "conflictIncrease": conflict_increase,
    }
    return not reasons and current_ok, reasons, evidence


def evaluate_window(
    client: PrometheusClient,
    manifest: RolloutManifest,
    state: dict[str, Any] | None,
    at: datetime,
    signing_key: bytes | None = None,
) -> tuple[int, dict[str, Any]]:
    if state is None:
        return 4, result_document("not_started", at, ["Observation state does not exist."])
    state_errors = validate_state(state, signing_key, manifest.signing_key_id)
    if state_errors:
        return 3, result_document("blocked", at, state_errors)
    if state.get("manifestDigest") != manifest.digest:
        return 3, result_document(
            "blocked",
            at,
            ["Rollout manifest changed after the observation window started."],
        )

    try:
        started_at = parse_time(state.get("startedAtUtc"))
    except (GateError, ValueError) as exception:
        return 3, result_document("blocked", at, [str(exception)])
    if started_at > at:
        return 3, result_document(
            "blocked",
            at,
            ["Observation state starts after the evaluation time."],
        )
    required_end = started_at + timedelta(days=manifest.window_days)
    current_ok, reasons, inventory = evaluate_current_inventory(client, manifest, at)
    continuity = evaluate_continuity(client, manifest, started_at, at)
    reasons.extend(continuity["reasons"])
    fallback_increase = query_counter_increase(
        client, FALLBACK_METRIC, manifest, started_at, at
    )
    conflict_increase = query_counter_increase(
        client, CONFLICT_METRIC, manifest, started_at, at
    )
    if fallback_increase > 0:
        reasons.append(f"Legacy fallback counter increased by {fallback_increase}.")
    if conflict_increase > 0:
        reasons.append(f"Contract conflict counter increased by {conflict_increase}.")

    evidence = {
        "windowStartedAtUtc": format_time(started_at),
        "requiredWindowEndUtc": format_time(required_end),
        "inventory": inventory,
        "continuity": continuity["evidence"],
        "fallbackIncrease": fallback_increase,
        "conflictIncrease": conflict_increase,
    }
    if reasons or not current_ok:
        return 3, result_document("blocked", at, reasons, evidence)
    if at < required_end:
        remaining = required_end - at
        return 2, result_document(
            "observing",
            at,
            [f"Observation window has {remaining} remaining."],
            evidence,
        )
    return 0, result_document("ready", at, [], evidence)


def evaluate_continuity(
    client: PrometheusClient,
    manifest: RolloutManifest,
    start: datetime,
    end: datetime,
) -> dict[str, Any]:
    step_seconds = manifest.sample_step_minutes * 60
    expected_points = max(1, int((end - start).total_seconds() // step_seconds) + 1)
    expression = (
        "sum by (service_name, service_version) "
        f"({READINESS_METRIC}{{{selector(manifest)}}} == bool 1)"
    )
    result = client.query_range(expression, start, end, step_seconds)
    by_service: dict[str, list[dict[str, Any]]] = {}
    for series in result:
        by_service.setdefault(series.get("metric", {}).get("service_name", ""), []).append(series)

    reasons: list[str] = []
    evidence: dict[str, Any] = {}
    for expected in manifest.expected_services:
        matching = [
            series
            for series in by_service.get(expected.name, [])
            if series.get("metric", {}).get("service_version") == manifest.required_version
        ]
        values_by_timestamp: dict[float, float] = {}
        for series in matching:
            for timestamp, value in series.get("values", []):
                values_by_timestamp[float(timestamp)] = values_by_timestamp.get(
                    float(timestamp), 0
                ) + float(value)
        ready_points = sum(
            1 for value in values_by_timestamp.values()
            if value >= expected.minimum_instances
        )
        evidence[expected.name] = {
            "requiredPoints": expected_points,
            "readyPoints": ready_points,
            "minimumInstances": expected.minimum_instances,
        }
        if ready_points < expected_points:
            reasons.append(
                f"{expected.name} has {ready_points}/{expected_points} continuous ready samples."
            )
    return {"reasons": reasons, "evidence": evidence}


def query_counter_increase(
    client: PrometheusClient,
    metric: str,
    manifest: RolloutManifest,
    start: datetime,
    end: datetime,
) -> float:
    seconds = max(1, int((end - start).total_seconds()))
    expression = f'sum(increase({metric}{{{selector(manifest)}}}[{seconds}s]))'
    result = client.query(expression, end)
    if not result:
        return 0.0
    return float(result[0].get("value", [0, "0"])[1])


def result_document(
    status: str,
    evaluated_at: datetime,
    reasons: list[str],
    evidence: dict[str, Any] | None = None,
) -> dict[str, Any]:
    return {
        "status": status,
        "evaluatedAtUtc": format_time(evaluated_at),
        "reasons": reasons,
        "evidence": evidence or {},
    }


def parse_time(value: Any) -> datetime:
    if not isinstance(value, str):
        raise GateError("Observation state has no valid startedAtUtc.")
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)


def format_time(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def load_state(path: Path) -> dict[str, Any] | None:
    return json.loads(path.read_text()) if path.exists() else None


def canonical_bytes(value: dict[str, Any]) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")


def canonical_digest(value: dict[str, Any]) -> str:
    return hashlib.sha256(canonical_bytes(value)).hexdigest()


def canonical_signature(value: dict[str, Any], signing_key: bytes) -> str:
    return hmac.new(signing_key, canonical_bytes(value), hashlib.sha256).hexdigest()


def load_signing_key(path: Path | None) -> bytes:
    if path is None:
        raise GateError("A signed rollout manifest requires --signing-key-file.")
    if os.name == "posix" and path.stat().st_mode & 0o077:
        raise GateError("Signing key file permissions must be owner-only (chmod 600).")
    signing_key = path.read_bytes()
    if len(signing_key) < 32:
        raise GateError("Signing key must contain at least 32 bytes.")
    return signing_key


def create_state(
    manifest: RolloutManifest,
    started_at: datetime,
    preflight_evidence: dict[str, Any],
    signing_key: bytes | None = None,
) -> dict[str, Any]:
    state = {
        "schemaVersion": 3 if manifest.signing_key_id else 2,
        "environment": manifest.environment,
        "manifestDigest": manifest.digest,
        "startedAtUtc": format_time(started_at),
        "preflightEvidence": preflight_evidence,
    }
    state["stateDigest"] = canonical_digest(state)
    if manifest.signing_key_id:
        if signing_key is None:
            raise GateError("Signed state creation requires a signing key.")
        state["signatureAlgorithm"] = "HMAC-SHA256"
        state["signingKeyId"] = manifest.signing_key_id
        state["stateSignature"] = canonical_signature(state, signing_key)
    return state


def validate_state(
    state: dict[str, Any],
    signing_key: bytes | None = None,
    expected_key_id: str | None = None,
) -> list[str]:
    schema_version = state.get("schemaVersion")
    if schema_version not in (2, 3):
        return ["Observation state must use schemaVersion 2 or 3."]
    supplied_digest = state.get("stateDigest")
    if not isinstance(supplied_digest, str):
        return ["Observation state has no integrity digest."]
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
    expected_digest = canonical_digest(digest_source)
    if not hmac.compare_digest(supplied_digest, expected_digest):
        return ["Observation state integrity digest does not match its contents."]

    if expected_key_id and schema_version != 3:
        return ["A signed rollout requires a schemaVersion 3 observation state."]
    if schema_version == 2:
        return []
    if state.get("signatureAlgorithm") != "HMAC-SHA256":
        return ["Observation state signature algorithm is not HMAC-SHA256."]
    if state.get("signingKeyId") != expected_key_id:
        return ["Observation state signing key id does not match the manifest."]
    if signing_key is None:
        return ["Observation state signature cannot be verified without a signing key."]
    supplied_signature = state.get("stateSignature")
    if not isinstance(supplied_signature, str):
        return ["Observation state has no HMAC signature."]
    signature_source = {
        key: value for key, value in state.items() if key != "stateSignature"
    }
    expected_signature = canonical_signature(signature_source, signing_key)
    if not hmac.compare_digest(supplied_signature, expected_signature):
        return ["Observation state HMAC signature is invalid."]
    return []


def attest_result(
    document: dict[str, Any],
    manifest: RolloutManifest,
    state: dict[str, Any],
    signing_key: bytes,
) -> dict[str, Any]:
    attested = json.loads(json.dumps(document))
    attested["attestation"] = {
        "schemaVersion": 1,
        "algorithm": "HMAC-SHA256",
        "keyId": manifest.signing_key_id,
        "manifestDigest": manifest.digest,
        "stateDigest": state.get("stateDigest"),
    }
    attested["attestation"]["signature"] = canonical_signature(attested, signing_key)
    return attested


def validate_attested_result(
    document: dict[str, Any],
    manifest: RolloutManifest,
    state: dict[str, Any],
    signing_key: bytes,
) -> list[str]:
    attestation = document.get("attestation")
    if not isinstance(attestation, dict):
        return ["Evaluation artifact has no attestation."]
    if attestation.get("schemaVersion") != 1:
        return ["Evaluation artifact uses an unsupported attestation schema."]
    if attestation.get("algorithm") != "HMAC-SHA256":
        return ["Evaluation artifact signature algorithm is not HMAC-SHA256."]
    if attestation.get("keyId") != manifest.signing_key_id:
        return ["Evaluation artifact signing key id does not match the manifest."]
    if attestation.get("manifestDigest") != manifest.digest:
        return ["Evaluation artifact manifest digest does not match the manifest."]
    if attestation.get("stateDigest") != state.get("stateDigest"):
        return ["Evaluation artifact is not bound to the supplied state."]
    supplied_signature = attestation.get("signature")
    if not isinstance(supplied_signature, str):
        return ["Evaluation artifact has no HMAC signature."]
    unsigned = json.loads(json.dumps(document))
    del unsigned["attestation"]["signature"]
    expected_signature = canonical_signature(unsigned, signing_key)
    if not hmac.compare_digest(supplied_signature, expected_signature):
        return ["Evaluation artifact HMAC signature is invalid."]
    return []


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def validate_release_evidence(
    manifest: RolloutManifest,
    state: dict[str, Any],
    artifact: dict[str, Any],
    signing_key: bytes,
) -> list[str]:
    reasons = validate_state(state, signing_key, manifest.signing_key_id)
    if state.get("manifestDigest") != manifest.digest:
        reasons.append("Observation state manifest digest does not match.")
    reasons.extend(
        validate_attested_result(artifact, manifest, state, signing_key)
    )
    if artifact.get("status") != "ready":
        reasons.append(
            f"Evaluation artifact status is {artifact.get('status')!r}; required 'ready'."
        )
    return reasons


def create_evidence_index(
    manifest: RolloutManifest,
    state: dict[str, Any],
    artifact: dict[str, Any],
    file_bytes: dict[str, bytes],
    signing_key: bytes,
    created_at: datetime,
) -> dict[str, Any]:
    index: dict[str, Any] = {
        "schemaVersion": 1,
        "createdAtUtc": format_time(created_at),
        "environment": manifest.environment,
        "deploymentVersion": manifest.required_version,
        "manifestDigest": manifest.digest,
        "stateDigest": state.get("stateDigest"),
        "evaluationDigest": canonical_digest(artifact),
        "files": {
            name: {"sha256": sha256_bytes(contents), "size": len(contents)}
            for name, contents in sorted(file_bytes.items())
        },
        "attestation": {
            "schemaVersion": 1,
            "algorithm": "HMAC-SHA256",
            "keyId": manifest.signing_key_id,
        },
    }
    index["attestation"]["signature"] = canonical_signature(index, signing_key)
    return index


def validate_evidence_index(
    index: dict[str, Any],
    manifest: RolloutManifest,
    state: dict[str, Any],
    artifact: dict[str, Any],
    file_bytes: dict[str, bytes],
    signing_key: bytes,
) -> list[str]:
    reasons: list[str] = []
    if index.get("schemaVersion") != 1:
        reasons.append("Evidence index uses an unsupported schema version.")
    if index.get("environment") != manifest.environment:
        reasons.append("Evidence index environment does not match the manifest.")
    if index.get("deploymentVersion") != manifest.required_version:
        reasons.append("Evidence index deployment version does not match the manifest.")
    if index.get("manifestDigest") != manifest.digest:
        reasons.append("Evidence index manifest digest does not match.")
    if index.get("stateDigest") != state.get("stateDigest"):
        reasons.append("Evidence index state digest does not match.")
    if index.get("evaluationDigest") != canonical_digest(artifact):
        reasons.append("Evidence index evaluation digest does not match.")

    expected_files = {
        name: {"sha256": sha256_bytes(contents), "size": len(contents)}
        for name, contents in sorted(file_bytes.items())
    }
    if index.get("files") != expected_files:
        reasons.append("Evidence bundle file inventory or digest does not match.")

    attestation = index.get("attestation")
    if not isinstance(attestation, dict):
        reasons.append("Evidence index has no attestation.")
        return reasons
    if attestation.get("schemaVersion") != 1:
        reasons.append("Evidence index uses an unsupported attestation schema.")
    if attestation.get("algorithm") != "HMAC-SHA256":
        reasons.append("Evidence index signature algorithm is not HMAC-SHA256.")
    if attestation.get("keyId") != manifest.signing_key_id:
        reasons.append("Evidence index signing key id does not match the manifest.")
    signature = attestation.get("signature")
    if not isinstance(signature, str):
        reasons.append("Evidence index has no HMAC signature.")
        return reasons
    unsigned = json.loads(json.dumps(index))
    del unsigned["attestation"]["signature"]
    if not hmac.compare_digest(signature, canonical_signature(unsigned, signing_key)):
        reasons.append("Evidence index HMAC signature is invalid.")
    return reasons


def write_bytes_fsync(path: Path, value: bytes) -> None:
    with path.open("xb") as stream:
        stream.write(value)
        stream.flush()
        os.fsync(stream.fileno())


def seal_evidence_bundle(
    bundle_path: Path,
    manifest_path: Path,
    state_path: Path,
    artifact_path: Path,
    signing_key: bytes,
    created_at: datetime,
) -> dict[str, Any]:
    if bundle_path.exists():
        raise GateError("Evidence bundle already exists; sealed bundles are never overwritten.")
    if bundle_path.is_symlink():
        raise GateError("Evidence bundle path must not be a symbolic link.")

    manifest_bytes = manifest_path.read_bytes()
    state_bytes = state_path.read_bytes()
    artifact_bytes = artifact_path.read_bytes()
    manifest = load_manifest(manifest_path)
    state = json.loads(state_bytes)
    artifact = json.loads(artifact_bytes)
    reasons = validate_release_evidence(manifest, state, artifact, signing_key)
    if reasons:
        raise GateError("Evidence cannot be sealed: " + " ".join(reasons))

    source_files = {
        BUNDLE_MANIFEST: manifest_bytes,
        BUNDLE_STATE: state_bytes,
        BUNDLE_ARTIFACT: artifact_bytes,
    }
    index = create_evidence_index(
        manifest,
        state,
        artifact,
        source_files,
        signing_key,
        created_at,
    )
    bundle_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = Path(
        tempfile.mkdtemp(dir=bundle_path.parent, prefix=f".{bundle_path.name}.")
    )
    try:
        for name, contents in source_files.items():
            write_bytes_fsync(temporary_path / name, contents)
        write_bytes_fsync(
            temporary_path / BUNDLE_INDEX,
            json.dumps(index, indent=2).encode("utf-8") + b"\n",
        )
        if os.name == "posix":
            for name in BUNDLE_FILES:
                (temporary_path / name).chmod(0o440)
            temporary_path.chmod(0o550)
        os.replace(temporary_path, bundle_path)
        if hasattr(os, "O_DIRECTORY"):
            parent_descriptor = os.open(bundle_path.parent, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(parent_descriptor)
            finally:
                os.close(parent_descriptor)
    finally:
        if temporary_path.exists():
            if os.name == "posix":
                temporary_path.chmod(0o700)
            shutil.rmtree(temporary_path)
    return index


def verify_evidence_bundle(
    bundle_path: Path,
    signing_key: bytes,
) -> tuple[list[str], dict[str, Any]]:
    if bundle_path.is_symlink() or not bundle_path.is_dir():
        raise GateError("Evidence bundle must be an existing non-symlink directory.")
    actual_names = sorted(path.name for path in bundle_path.iterdir())
    if actual_names != sorted(BUNDLE_FILES):
        return ["Evidence bundle must contain exactly the four expected files."], {}
    paths = {name: bundle_path / name for name in BUNDLE_FILES}
    if any(path.is_symlink() or not path.is_file() for path in paths.values()):
        return ["Evidence bundle files must be regular files, not symbolic links."], {}

    manifest = load_manifest(paths[BUNDLE_MANIFEST])
    state = json.loads(paths[BUNDLE_STATE].read_text())
    artifact = json.loads(paths[BUNDLE_ARTIFACT].read_text())
    index = json.loads(paths[BUNDLE_INDEX].read_text())
    source_files = {
        BUNDLE_MANIFEST: paths[BUNDLE_MANIFEST].read_bytes(),
        BUNDLE_STATE: paths[BUNDLE_STATE].read_bytes(),
        BUNDLE_ARTIFACT: paths[BUNDLE_ARTIFACT].read_bytes(),
    }
    reasons = validate_release_evidence(manifest, state, artifact, signing_key)
    reasons.extend(
        validate_evidence_index(
            index,
            manifest,
            state,
            artifact,
            source_files,
            signing_key,
        )
    )
    evidence = {
        "bundlePath": str(bundle_path),
        "environment": manifest.environment,
        "deploymentVersion": manifest.required_version,
        "signingKeyId": manifest.signing_key_id,
        "manifestDigest": manifest.digest,
        "stateDigest": state.get("stateDigest"),
        "evaluationDigest": canonical_digest(artifact),
    }
    return reasons, evidence


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=path.parent,
        prefix=f".{path.name}.",
        suffix=".tmp",
        text=True,
    )
    temporary_path = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(value, stream, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
    finally:
        temporary_path.unlink(missing_ok=True)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "command",
        choices=(
            "preflight",
            "start",
            "evaluate",
            "verify",
            "seal",
            "verify-bundle",
        ),
    )
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--state", type=Path)
    parser.add_argument("--prometheus-url")
    parser.add_argument("--signing-key-file", type=Path)
    parser.add_argument("--artifact", type=Path)
    parser.add_argument("--bundle", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--at", help="UTC evaluation time for reproducible operations/tests")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        at = parse_time(args.at) if args.at else datetime.now(timezone.utc)
        if args.command == "verify-bundle":
            if args.bundle is None:
                raise GateError("verify-bundle requires --bundle.")
            signing_key = load_signing_key(args.signing_key_file)
            reasons, evidence = verify_evidence_bundle(args.bundle, signing_key)
            exit_code = 0 if not reasons else 3
            document = result_document(
                "bundle_verified" if not reasons else "blocked",
                at,
                reasons,
                evidence,
            )
            if args.output:
                write_json(args.output, document)
            print(json.dumps(document, indent=2))
            return exit_code

        if args.manifest is None or args.state is None:
            raise GateError(f"{args.command} requires --manifest and --state.")
        manifest = load_manifest(args.manifest)
        existing_state = (
            load_state(args.state)
            if args.command in ("evaluate", "verify")
            else None
        )
        signing_key = (
            load_signing_key(args.signing_key_file)
            if manifest.signing_key_id
            and (args.command in ("start", "seal") or existing_state is not None)
            else None
        )

        if args.command == "seal":
            if args.artifact is None or args.bundle is None:
                raise GateError("seal requires --artifact and --bundle.")
            if signing_key is None:
                raise GateError("seal requires a signed manifest and signing key.")
            index = seal_evidence_bundle(
                args.bundle,
                args.manifest,
                args.state,
                args.artifact,
                signing_key,
                at,
            )
            exit_code = 0
            document = result_document(
                "sealed",
                at,
                [],
                {
                    "bundlePath": str(args.bundle),
                    "environment": index["environment"],
                    "deploymentVersion": index["deploymentVersion"],
                    "manifestDigest": index["manifestDigest"],
                    "stateDigest": index["stateDigest"],
                    "evaluationDigest": index["evaluationDigest"],
                    "signingKeyId": index["attestation"]["keyId"],
                },
            )
        elif args.command == "verify":
            state = existing_state
            if state is None:
                exit_code = 4
                document = result_document(
                    "not_started",
                    at,
                    ["Observation state does not exist."],
                )
            else:
                artifact_status = None
                if not args.artifact:
                    reasons = ["Signed rollout verification requires --artifact."]
                else:
                    artifact = json.loads(args.artifact.read_text())
                    reasons = validate_release_evidence(
                        manifest,
                        state,
                        artifact,
                        signing_key or b"",
                    )
                    artifact_status = artifact.get("status")
                exit_code = 0 if not reasons else 3
                document = result_document(
                    "verified" if not reasons else "blocked",
                    at,
                    reasons,
                    {
                        "stateDigest": state.get("stateDigest"),
                        "signingKeyId": state.get("signingKeyId"),
                        "artifactStatus": artifact_status,
                    },
                )
        else:
            if not args.prometheus_url:
                raise GateError(f"{args.command} requires --prometheus-url.")
            client = PrometheusClient(args.prometheus_url)

            if args.command == "preflight":
                passed, reasons, evidence = evaluate_preflight(client, manifest, at)
                exit_code = 0 if passed else 3
                document = result_document(
                    "ready_to_start" if passed else "blocked",
                    at,
                    reasons,
                    evidence,
                )
            elif args.command == "start":
                if args.state.exists():
                    raise GateError(
                        "Observation state already exists; preserve it or explicitly archive it before restarting."
                    )
                passed, reasons, evidence = evaluate_preflight(client, manifest, at)
                if not passed:
                    exit_code = 3
                    document = result_document("blocked", at, reasons, evidence)
                else:
                    state = create_state(manifest, at, evidence, signing_key)
                    write_json(args.state, state)
                    exit_code = 0
                    document = result_document(
                        "started",
                        at,
                        [],
                        {
                            "statePath": str(args.state),
                            "stateDigest": state["stateDigest"],
                            "signingKeyId": state.get("signingKeyId"),
                            **evidence,
                        },
                    )
            else:
                state = existing_state
                exit_code, document = evaluate_window(
                    client,
                    manifest,
                    state,
                    at,
                    signing_key,
                )
                if manifest.signing_key_id and state is not None and signing_key is not None:
                    document = attest_result(document, manifest, state, signing_key)

        if args.output:
            write_json(args.output, document)
        print(json.dumps(document, indent=2))
        return exit_code
    except (GateError, OSError, ValueError, json.JSONDecodeError) as exception:
        print(json.dumps({"status": "error", "message": str(exception)}, indent=2))
        return 5


if __name__ == "__main__":
    sys.exit(main())
