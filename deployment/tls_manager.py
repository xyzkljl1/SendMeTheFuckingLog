"""Host-side ACME orchestration; the Certbot container exists only during a check."""
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import ssl
import subprocess
import sys
import time

ROOT = Path("/opt/send-me-the-log")
APP = "send-me-the-log"
LABEL = "io.sendmethelog.application"
LEGACY_LABEL = ""
CERTBOT = "certbot/certbot@sha256:bc93cb710b413e6b456d622f9b0133b3e109c0a78c2ed9976af43d209b0a1805"
CERTBOT_NAME = APP + "-certbot"


def run(*args, **kwargs):
    return subprocess.run(args, check=True, text=True, **kwargs)


def plain(path):
    if not path.is_relative_to(ROOT):
        raise RuntimeError("TLS path escapes the application directory.")
    for item in (path, *path.parents):
        if item.is_symlink():
            raise RuntimeError(f"Refusing a symlink in TLS paths: {item}")


def atomic(path, data, shared=False):
    plain(path)
    staging = path.with_name(path.name + ".new")
    plain(staging)
    with staging.open("wb") as stream:
        os.chmod(staging, 0o640 if shared else 0o600)
        os.chown(staging, 0, 1000 if shared else 0)
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    staging.replace(path)
    sync_directory(path.parent)


def sync_directory(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def settings(text):
    value = json.loads(text)
    domain = value.get("Domain", "")
    if (set(value) != {"Domain"} or not isinstance(domain, str) or len(domain) > 253
            or not re.fullmatch(r"(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}", domain)):
        raise ValueError("TLS settings require one DNS Domain without a scheme or port.")
    return domain


def prepare():
    for name in ("", "certbot", "work", "logs", "acme", "acme/.well-known",
                 "acme/.well-known/acme-challenge", "served"):
        directory = ROOT / "tls" / name
        plain(directory)
        directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        if directory.stat().st_uid != 0:
            raise RuntimeError("TLS directories must be owned by root.")
        shared = name == "served" or name.startswith("acme")
        os.chown(directory, 0, 1000 if shared else 0)
        os.chmod(directory, 0o755 if name.startswith("acme") else 0o750 if shared else 0o700)
    run("openssl", "version", stdout=subprocess.DEVNULL)
    # Download before stopping the current website; renewal uses this cached, pinned image.
    run("docker", "pull", "--platform", "linux/amd64", CERTBOT, timeout=600)


def remove_stale_certbot():
    ids = run("docker", "ps", "-aq", "--filter", "name=^/" + CERTBOT_NAME + "$",
              stdout=subprocess.PIPE).stdout.strip()
    if not ids:
        return
    info = json.loads(run("docker", "inspect", ids, stdout=subprocess.PIPE).stdout)[0]
    labels = info["Config"].get("Labels") or {}
    if labels.get(LABEL) != APP and labels.get(LEGACY_LABEL) != APP:
        raise RuntimeError("Refusing to remove an unrelated Certbot container.")
    run("docker", "rm", "--force", ids, stdout=subprocess.DEVNULL)


def issue_or_renew(domain):
    remove_stale_certbot()
    args = ["docker", "run", "--rm", "--pull", "never", "--name", CERTBOT_NAME,
            "--label", LABEL + "=" + APP, "--platform", "linux/amd64", "--read-only",
            "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
            "--memory", "256m", "--memory-swap", "256m", "--pids-limit", "64",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=16m"]
    for source, target in (("certbot", "/etc/letsencrypt"), ("work", "/var/lib/letsencrypt"),
                           ("logs", "/var/log/letsencrypt"), ("acme", "/webroot")):
        args += ["--mount", f"type=bind,src={ROOT / 'tls' / source},dst={target}"]
    renewal = ROOT / "tls" / "certbot" / "renewal" / (domain + ".conf")
    plain(renewal)
    args += [CERTBOT]
    if renewal.exists():
        args += ["renew", "--cert-name", domain, "--webroot", "--webroot-path", "/webroot"]
    else:
        args += ["certonly", "--webroot", "--webroot-path", "/webroot", "--cert-name", domain,
                 "--domains", domain, "--agree-tos", "--register-unsafely-without-email"]
    args += ["--non-interactive"]
    try:
        run(*args, timeout=240)
    finally:
        # A Docker client timeout must not leave a detached issuance process running.
        remove_stale_certbot()


def certificate_source(domain, name):
    archive = ROOT / "tls" / "certbot" / "archive"
    plain(archive)
    candidate = ROOT / "tls" / "certbot" / "live" / domain / name
    plain(candidate.parent)
    resolved = candidate.resolve(strict=True)  # Certbot itself maintains the live symlinks.
    if not resolved.is_relative_to(archive):
        raise RuntimeError("Certbot certificate escaped its archive directory.")
    plain(resolved)
    if not resolved.is_file() or resolved.stat().st_size > 128 * 1024:
        raise RuntimeError("Invalid certificate file.")
    return resolved


def validated_certificate(domain):
    fullchain = certificate_source(domain, "fullchain.pem")
    key = certificate_source(domain, "privkey.pem")
    ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER).load_cert_chain(fullchain, key)
    run("openssl", "x509", "-in", str(fullchain), "-noout", "-checkend", "86400", stdout=subprocess.DEVNULL)
    # Some OpenSSL versions return exit status zero even for a nonmatching -checkhost.
    match = run("openssl", "x509", "-in", str(fullchain), "-noout", "-checkhost", domain,
                stdout=subprocess.PIPE).stdout.strip()
    if match != f"Hostname {domain} does match certificate":
        raise RuntimeError("The issued certificate does not match the configured domain.")
    public, private = fullchain.read_bytes(), key.read_bytes()
    generation = hashlib.sha256(public + private).hexdigest()
    leaf = public.split(b"-----END CERTIFICATE-----", 1)[0] + b"-----END CERTIFICATE-----\n"
    fingerprint = hashlib.sha256(ssl.PEM_cert_to_DER_cert(leaf.decode("ascii"))).hexdigest()
    return public, private, generation, fingerprint


def compose(*args, **kwargs):
    return run("docker", "compose", "--project-name", APP, "--project-directory", str(ROOT),
               "--env-file", str(ROOT / "image.env"), "-f", str(ROOT / "compose.yaml"), *args, **kwargs)


def verify_https(domain, fingerprint, attempts=30):
    context = ssl.create_default_context()
    for attempt in range(attempts):
        try:
            with socket.create_connection(("127.0.0.1", 443), timeout=3) as connection:
                with context.wrap_socket(connection, server_hostname=domain) as secured:
                    if hashlib.sha256(secured.getpeercert(binary_form=True)).hexdigest() != fingerprint:
                        raise RuntimeError("Website is still serving a different TLS certificate.")
                    secured.sendall(f"GET / HTTP/1.1\r\nHost: {domain}\r\nConnection: close\r\n\r\n".encode("ascii"))
                    with secured.makefile("rb") as response:
                        status = response.readline(4096).split()
                    if len(status) < 2 or status[1] not in (b"200", b"429"):
                        raise RuntimeError("HTTPS application check failed.")
                    return
        except (OSError, RuntimeError):
            if attempt == attempts - 1:
                raise
            time.sleep(2)


def restart():
    # The normal graceful stop saves request counters and lets in-flight uploads finish.
    compose("restart", "--timeout", "150", "web", timeout=210)


def activate(domain):
    public, private, generation, fingerprint = validated_certificate(domain)
    served = ROOT / "tls" / "served"
    manifest = served / "active.json"
    plain(manifest)
    old = manifest.read_bytes() if manifest.exists() else None
    new = json.dumps({"version": 1, "generation": generation}).encode("ascii")
    if old == new:
        try:
            verify_https(domain, fingerprint, attempts=1)
        except (OSError, RuntimeError):
            # Recover a host crash between publishing the manifest and restarting Kestrel.
            restart()
            verify_https(domain, fingerprint)
            print("TLS: interrupted activation recovered and HTTPS verified.", flush=True)
            return
        print("TLS: existing certificate verified; no restart needed.", flush=True)
        return
    directory = served / generation
    plain(directory)
    directory.mkdir(mode=0o750, exist_ok=True)
    os.chown(directory, 0, 1000)
    os.chmod(directory, 0o750)
    atomic(directory / "fullchain.pem", public, shared=True)
    atomic(directory / "privkey.pem", private, shared=True)
    atomic(manifest, new, shared=True)
    try:
        restart()
        verify_https(domain, fingerprint)
    except Exception:
        # Restore the last certificate (or HTTP validation-only bootstrap) on activation failure.
        if old is None:
            manifest.unlink()
            sync_directory(served)
        else:
            atomic(manifest, old, shared=True)
        restart()
        raise
    print("TLS: new certificate activated and HTTPS verified.", flush=True)


def ensure_certificate(domain):
    issue_or_renew(domain)
    activate(domain)


def main():
    import fcntl

    if os.geteuid() != 0:
        raise RuntimeError("Certificate renewal requires root.")
    os.umask(0o077)
    path = ROOT / ".deployment.lock"
    plain(path)
    with path.open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print("TLS: deployment is in progress; it will check the certificate.")
            return
        config = ROOT / "tls" / "tls.json"
        plain(config)
        ensure_certificate(settings(config.read_text(encoding="utf-8")))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"TLS failed: {error}", file=sys.stderr)
        sys.exit(1)
