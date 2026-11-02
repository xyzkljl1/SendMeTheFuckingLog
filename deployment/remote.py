"""Executed in memory over SSH, as root. All deployment files stay under ROOT."""
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path("/opt/send-me-the-log")
IMAGE = "ghcr.io/xyzkljl1/sendmethefuckinglog"
SOURCE = "https://github.com/xyzkljl1/SendMeTheFuckingLog"
APP_LABEL = "io.github.xyzkljl1.application"
APP = "send-me-the-log"


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


def plain(path):
    # Check the path and every existing ancestor before any write or removal.
    for part in [path, *path.parents]:
        if part.is_symlink():
            raise RuntimeError(f"Refusing symlink in deployment path: {part}")
    if not path.is_relative_to(ROOT):
        raise RuntimeError("Path escapes the deployment directory.")


def atomic_write(path, content):
    plain(path)
    staging = path.with_suffix(path.suffix + ".new")
    plain(staging)
    with staging.open("w", encoding="utf-8", newline="\n") as stream:
        os.chmod(staging, 0o600)
        stream.write(content)
        stream.flush()
        os.fsync(stream.fileno())
    staging.replace(path)


def inspect_container():
    # Listing first distinguishes absence from an unavailable Docker daemon.
    ids = output("docker", "ps", "-aq", "--filter", f"name=^/{APP}$")
    return json.loads(output("docker", "inspect", ids))[0] if ids else None


def check_container(container, run_number):
    if container is None:
        return
    labels = container["Config"].get("Labels") or {}
    if (labels.get(APP_LABEL) != APP
            or labels.get("com.docker.compose.project") != APP
            or labels.get("com.docker.compose.service") != "web"):
        raise RuntimeError("Existing container is not managed by this deployment. One-time migration is required.")
    if int(labels.get("io.github.xyzkljl1.deployment-run", "0")) > int(run_number):
        raise RuntimeError("A newer workflow has already deployed; refusing stale deployment.")


def cleanup_images(current_id):
    # Never use system prune, volume prune, or force removal.
    candidates = output("docker", "image", "ls", "-aq", "--no-trunc",
                        "--filter", f"label={APP_LABEL}={APP}").splitlines()
    for image_id in sorted(set(candidates)):
        if image_id == current_id:
            continue
        info = json.loads(output("docker", "image", "inspect", image_id))[0]
        labels = info["Config"].get("Labels") or {}
        tags = info.get("RepoTags") or []
        digests = info.get("RepoDigests") or []
        if (labels.get("org.opencontainers.image.source") != SOURCE
                or any(not tag.startswith(IMAGE + ":") for tag in tags)
                or any(not digest.startswith(IMAGE + "@") for digest in digests)):
            raise RuntimeError("Old image has unexpected ownership/references; refusing to delete it.")
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
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        run("docker", "info", stdout=subprocess.DEVNULL)
        run("docker", "compose", "version")
        check_container(inspect_container(), payload["run"])
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
            atomic_write(ROOT / "image.env", f"APP_IMAGE={payload['image']}\nDEPLOY_RUN={payload['run']}\n")
            compose = docker + ["compose", "--project-name", APP, "--project-directory", str(ROOT),
                                "--env-file", str(ROOT / "image.env"), "-f", str(ROOT / "compose.yaml")]
            run(*compose, "config", "--quiet")
            # No down step and no automatic rollback. Failed starts are reported;
            # old images remain until a subsequent successful deployment.
            run(*compose, "up", "--detach", "--no-build", "--pull", "never",
                "--wait", "--wait-timeout", "120", "web", timeout=180)
            current = inspect_container()
            if (not current or current["Image"] != image_info["Id"]
                    or not current["State"]["Running"]
                    or current["State"].get("Health", {}).get("Status") != "healthy"):
                raise RuntimeError("Running container failed final identity/health verification.")
            cleanup_images(current["Image"])
        print(f"DEPLOYED {payload['image']}", flush=True)
