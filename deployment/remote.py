"""Executed over SSH as root; only project systemd units are installed outside ROOT."""
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path("/opt/send-me-the-log")
IMAGE = SOURCE = APP_LABEL = RUN_LABEL = OWNER = ""
APP = "send-me-the-log"
TRAFFIC_SERVICE = APP + "-traffic.service"
TRAFFIC_TIMER = APP + "-traffic.timer"
TLS_SERVICE = APP + "-tls.service"
TLS_TIMER = APP + "-tls.timer"
UNIT_DIRECTORY = Path("/etc/systemd/system")
UNIT_MARKER = "# Managed by send-me-the-log deployment.\n"


def configure(repository):
    global IMAGE, SOURCE, APP_LABEL, RUN_LABEL, OWNER
    if not isinstance(repository, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9][A-Za-z0-9_.-]*", repository):
        raise ValueError("Invalid deployment repository.")
    OWNER = repository.split("/", 1)[0].lower()
    IMAGE = "ghcr.io/" + repository.lower()
    SOURCE = "https://github.com/" + repository
    APP_LABEL = f"io.github.{OWNER}.application"
    RUN_LABEL = f"io.github.{OWNER}.deployment-run"


def run(*args, **kwargs):
    return subprocess.run(args, check=True, text=True, **kwargs)


def output(*args):
    return run(*args, stdout=subprocess.PIPE).stdout.strip()


def validate_payload(payload):
    if not re.fullmatch(re.escape(IMAGE) + r"@sha256:[a-f0-9]{64}", payload["image"]):
        raise ValueError("Only this project's pinned GHCR image is accepted.")
    if not re.fullmatch(r"[1-9][0-9]*", str(payload["run"])):
        raise ValueError("Invalid deployment run number.")
    if not re.fullmatch(r"[a-f0-9]{40}", payload["revision"]):
        raise ValueError("Invalid source revision.")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", payload["actor"]):
        raise ValueError("Invalid registry actor.")


def plain(path, system_unit=False):
    # Check the path and every existing ancestor before any write or removal.
    for part in [path, *path.parents]:
        if part.is_symlink():
            raise RuntimeError(f"Refusing symlink in deployment path: {part}")
    if system_unit:
        allowed = {UNIT_DIRECTORY / (name + suffix) for name in (TRAFFIC_SERVICE, TRAFFIC_TIMER, TLS_SERVICE, TLS_TIMER)
                   for suffix in ("", ".new")}
        if path not in allowed:
            raise RuntimeError("Unexpected systemd unit path.")
    elif not path.is_relative_to(ROOT):
        raise RuntimeError("Path escapes the deployment directory.")


def atomic_write(path, content, system_unit=False):
    plain(path, system_unit)
    staging = path.with_suffix(path.suffix + ".new")
    plain(staging, system_unit)
    with staging.open("w", encoding="utf-8", newline="\n") as stream:
        os.chmod(staging, 0o644 if system_unit else 0o600)
        stream.write(content)
        stream.flush()
        os.fsync(stream.fileno())
    staging.replace(path)


def require_root_owner(path):
    if path.stat().st_uid != 0:
        raise RuntimeError("Traffic monitor directory must be owned by root.")


def install_traffic_monitor(payload):
    files = payload["traffic"]
    expected = {"traffic_monitor.py", "traffic-limits.json", TRAFFIC_SERVICE, TRAFFIC_TIMER}
    if set(files) != expected or any(not isinstance(value, str) for value in files.values()):
        raise ValueError("Missing or unexpected traffic monitor deployment files.")
    compile(files["traffic_monitor.py"], "traffic_monitor.py", "exec")
    limits = json.loads(files["traffic-limits.json"])
    if (set(limits) != {"DailyTrafficLimitGB", "MonthlyTrafficLimitGB"}
            or any(type(value) is not int or not 1 <= value <= 1_000_000 for value in limits.values())):
        raise ValueError("Invalid traffic limits.")
    directory = ROOT / "traffic-monitor"
    plain(directory)
    directory.mkdir(mode=0o700, exist_ok=True)
    require_root_owner(directory)
    os.chmod(directory, 0o700)
    public_status = directory / "status"
    plain(public_status)
    public_status.mkdir(mode=0o755, exist_ok=True)
    require_root_owner(public_status)
    os.chmod(public_status, 0o755)
    for name in (TRAFFIC_SERVICE, TRAFFIC_TIMER):
        path = UNIT_DIRECTORY / name
        plain(path, system_unit=True)
        if not files[name].startswith(UNIT_MARKER):
            raise ValueError("Unit ownership marker is missing.")
        if path.exists() and (not path.is_file() or not path.read_text(encoding="utf-8").startswith(UNIT_MARKER)):
            raise RuntimeError(f"Refusing to replace an unrelated systemd unit: {path}")
    # Preflight before stopping an existing monitor. No extra packages or credentials are installed.
    run("systemctl", "--version", stdout=subprocess.DEVNULL)
    run("ip", "-j", "route", "show", "default", stdout=subprocess.DEVNULL)
    run("/usr/bin/python3", "--version", stdout=subprocess.DEVNULL)
    # A first install has no units to stop; an update waits for the old sample to finish/stop.
    if (UNIT_DIRECTORY / TRAFFIC_TIMER).exists():
        run("systemctl", "stop", TRAFFIC_TIMER, TRAFFIC_SERVICE, timeout=60)
    for name in ("traffic_monitor.py", "traffic-limits.json"):
        atomic_write(directory / name, files[name])
    for name in (TRAFFIC_SERVICE, TRAFFIC_TIMER):
        atomic_write(UNIT_DIRECTORY / name, files[name], system_unit=True)
    # Never touch traffic-state.json: deployment and host restart both retain usage.
    run("systemctl", "daemon-reload")
    run("systemctl", "enable", "--now", TRAFFIC_TIMER)
    run("systemctl", "start", TRAFFIC_SERVICE, timeout=60)
    run("systemctl", "is-enabled", "--quiet", TRAFFIC_TIMER)
    run("systemctl", "is-active", "--quiet", TRAFFIC_TIMER)
    if output("systemctl", "show", TRAFFIC_SERVICE, "--property=Result", "--value") != "success":
        raise RuntimeError("Traffic protection's first check failed.")
    print("TRAFFIC MONITOR: installed, timer enabled/active, first check succeeded.", flush=True)


def inspect_container():
    # Listing first distinguishes absence from an unavailable Docker daemon.
    ids = output("docker", "ps", "-aq", "--filter", f"name=^/{APP}$")
    return json.loads(output("docker", "inspect", ids))[0] if ids else None


def install_tls(payload):
    files = payload["tls"]
    if (set(files) != {"tls_manager.py", "tls.json", TLS_SERVICE, TLS_TIMER}
            or any(not isinstance(value, str) for value in files.values())):
        raise ValueError("Missing or unexpected TLS deployment files.")
    compile(files["tls_manager.py"], "tls_manager.py", "exec")
    directory = ROOT / "tls"
    plain(directory)
    directory.mkdir(mode=0o700, exist_ok=True)
    require_root_owner(directory)
    os.chmod(directory, 0o700)
    for name in (TLS_SERVICE, TLS_TIMER):
        path = UNIT_DIRECTORY / name
        plain(path, system_unit=True)
        if not files[name].startswith(UNIT_MARKER):
            raise ValueError("TLS unit ownership marker is missing.")
        if path.exists() and (not path.is_file() or not path.read_text(encoding="utf-8").startswith(UNIT_MARKER)):
            raise RuntimeError(f"Refusing to replace an unrelated systemd unit: {path}")
    atomic_write(directory / "tls_manager.py", files["tls_manager.py"])
    spec = importlib.util.spec_from_file_location("tls_manager", directory / "tls_manager.py")
    manager = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(manager)
    manager.ROOT = ROOT
    manager.LABEL = APP_LABEL
    domain = manager.settings(files["tls.json"])
    manager.prepare()
    atomic_write(directory / "tls.json", files["tls.json"])
    if (UNIT_DIRECTORY / TLS_TIMER).exists():
        run("systemctl", "stop", TLS_TIMER, timeout=30)
    for name in (TLS_SERVICE, TLS_TIMER):
        atomic_write(UNIT_DIRECTORY / name, files[name], system_unit=True)
    run("systemctl", "daemon-reload")
    run("systemctl", "enable", "--now", TLS_TIMER)
    run("systemctl", "is-enabled", "--quiet", TLS_TIMER)
    run("systemctl", "is-active", "--quiet", TLS_TIMER)
    print("TLS: renewal timer enabled and active.", flush=True)
    return manager, domain


def check_container(container, run_number):
    if container is None:
        return
    labels = container["Config"].get("Labels") or {}
    if (labels.get(APP_LABEL) != APP
            or labels.get("com.docker.compose.project") != APP
            or labels.get("com.docker.compose.service") != "web"):
        raise RuntimeError("Existing container is not managed by this deployment. One-time migration is required.")
    if int(labels.get(RUN_LABEL, "0")) > int(run_number):
        raise RuntimeError("A newer workflow has already deployed; refusing stale deployment.")


def reset_request_counts():
    # Called only after the old writer has stopped. Uploads and host traffic state are untouched.
    for name in (".request-counts.json", ".request-counts.json.new"):
        path = ROOT / "app" / "data" / name
        plain(path)
        if path.exists():
            if not path.is_file():
                raise RuntimeError("Request counter path is not a regular file.")
            path.unlink()


def cleanup_images(current_id):
    # Never use system prune, volume prune, or force removal.
    digest_pattern = re.escape(IMAGE) + r"@sha256:[a-f0-9]{64}"
    # Containerd-backed Docker can also expose digest references in RepoTags.
    reference_pattern = re.escape(IMAGE) + r"(?::[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}|@sha256:[a-f0-9]{64})"
    candidates = output("docker", "image", "ls", "-aq", "--no-trunc",
                        "--filter", f"label={APP_LABEL}={APP}").splitlines()
    for image_id in sorted(set(candidates)):
        if image_id == current_id:
            continue
        info = json.loads(output("docker", "image", "inspect", image_id))[0]
        labels = info["Config"].get("Labels") or {}
        tags = info.get("RepoTags") or []
        digests = info.get("RepoDigests") or []
        if (labels.get(APP_LABEL) != APP or labels.get("org.opencontainers.image.source") != SOURCE
                or any(not re.fullmatch(reference_pattern, tag) for tag in tags)
                or any(not re.fullmatch(digest_pattern, digest) for digest in digests)):
            raise RuntimeError(f"Old image {image_id} has unexpected ownership/references; refusing to delete it.")
        if output("docker", "ps", "-aq", "--filter", f"ancestor={image_id}"):
            raise RuntimeError("Old project image is still referenced by a container; cleanup incomplete.")
        for reference in tags:
            run("docker", "image", "rm", reference)
        remaining = output("docker", "image", "ls", "-aq", "--no-trunc").splitlines()
        if image_id in remaining:
            run("docker", "image", "rm", image_id)
    remaining = set(output("docker", "image", "ls", "-aq", "--no-trunc",
                           "--filter", f"label={APP_LABEL}={APP}").splitlines())
    if remaining != {current_id}:
        raise RuntimeError("Server image cleanup verification failed.")


def main(payload):
    import fcntl

    configure(payload["repository"])
    validate_payload(payload)
    if os.geteuid() != 0:
        raise RuntimeError("Deployment requires passwordless sudo for this SSH account.")
    if os.uname().machine != "x86_64":
        raise RuntimeError("This deployment currently supports Linux x86_64 only.")
    os.umask(0o077)
    plain(ROOT)
    ROOT.mkdir(parents=True, exist_ok=True)
    lock_path = ROOT / ".deployment.lock"
    plain(lock_path)
    with lock_path.open("a") as lock:
        # A deployment waits for an in-progress renewal rather than interrupting it.
        fcntl.flock(lock, fcntl.LOCK_EX)
        run("docker", "info", stdout=subprocess.DEVNULL)
        run("docker", "compose", "version")
        check_container(inspect_container(), payload["run"])
        install_traffic_monitor(payload)
        tls, domain = install_tls(payload)
        # Reuse the test deployment's data path; no copying or deleting uploads.
        for directory in (ROOT / "app" / "data", ROOT / "app" / "tmp"):
            plain(directory)
            directory.mkdir(parents=True, exist_ok=True)
            if directory.stat().st_uid != 1000:
                if any(directory.iterdir()):
                    raise RuntimeError(f"Nonempty directory has unexpected ownership: {directory}")
                os.chown(directory, 1000, 1000)
            os.chmod(directory, 0o700)
        # The token is only on stdin and in a short-lived root-only Docker config.
        with tempfile.TemporaryDirectory(prefix=".registry-", dir=ROOT) as auth:
            docker = ["docker", "--config", auth]
            run(*docker, "login", "ghcr.io", "--username", payload["actor"],
                "--password-stdin", input=payload.pop("token") + "\n", stdout=subprocess.DEVNULL)
            run(*docker, "pull", payload["image"], timeout=600)
            image_info = json.loads(output("docker", "image", "inspect", payload["image"]))[0]
            labels = image_info["Config"].get("Labels") or {}
            if (labels.get(APP_LABEL) != APP
                    or labels.get("org.opencontainers.image.source") != SOURCE
                    or labels.get("org.opencontainers.image.revision") != payload["revision"]
                    or image_info.get("Architecture") != "amd64"
                    or not image_info["Config"].get("Healthcheck")):
                raise RuntimeError("Pulled image does not match the expected application/revision/architecture.")
            atomic_write(ROOT / "compose.yaml", payload["compose"])
            atomic_write(ROOT / "image.env", f"APP_IMAGE={payload['image']}\nDEPLOY_RUN={payload['run']}\nTLS_DOMAIN={domain}\nREPOSITORY_OWNER={OWNER}\n")
            compose = docker + ["compose", "--project-name", APP, "--project-directory", str(ROOT),
                                "--env-file", str(ROOT / "image.env"), "-f", str(ROOT / "compose.yaml")]
            run(*compose, "config", "--quiet")
            # Never clear counters while the old process can checkpoint them again.
            run(*compose, "stop", "--timeout", "150", "web", timeout=180)
            stopped = inspect_container()
            if stopped and stopped["State"]["Running"]:
                raise RuntimeError("Old application is still running; request counters were not reset.")
            reset_request_counts()
            # No automatic rollback; failed starts leave old images available for investigation.
            run(*compose, "up", "--detach", "--force-recreate", "--no-build", "--pull", "never",
                "--wait", "--wait-timeout", "120", "web", timeout=180)
            # A first deployment starts with HTTP validation only. Issuance activates HTTPS.
            # On subsequent deployments, valid persisted certificates are available immediately.
            tls.ensure_certificate(domain)
            run(*compose, "up", "--detach", "--no-build", "--pull", "never",
                "--wait", "--wait-timeout", "120", "web", timeout=180)
            current = inspect_container()
            if (not current or current["Image"] != image_info["Id"]
                    or not current["State"]["Running"]
                    or current["State"].get("Health", {}).get("Status") != "healthy"):
                raise RuntimeError("Running container failed final identity/health verification.")
            cleanup_images(current["Image"])
        print(f"DEPLOYED {payload['image']}", flush=True)
