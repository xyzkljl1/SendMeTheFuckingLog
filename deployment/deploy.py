"""GitHub runner client: send config, an ephemeral token and the remote script over SSH."""
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import tempfile


def main():
    required = ["DEPLOY_HOST", "DEPLOY_USER", "DEPLOY_PORT", "DEPLOY_SSH_KEY",
                "GH_TOKEN", "GITHUB_ACTOR", "GITHUB_SHA",
                "GITHUB_RUN_NUMBER", "APP_IMAGE"]
    missing = [name for name in required if not os.environ.get(name, "").strip()]
    if missing:
        raise ValueError("Missing configuration: " + ", ".join(missing))
    host, user, port = (os.environ[name] for name in required[:3])
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]*", host):
        raise ValueError("Invalid server hostname or IPv4 address.")
    if not re.fullmatch(r"[a-z_][a-z0-9_-]*", user):
        raise ValueError("Invalid SSH username.")
    if not port.isdecimal() or not 1 <= int(port) <= 65535:
        raise ValueError("Invalid SSH port.")
    root = Path(__file__).resolve().parent.parent
    payload = {
        "script": (root / "deployment" / "remote.py").read_text(encoding="utf-8"),
        "compose": (root / "compose.yaml").read_text(encoding="utf-8"),
        "image": os.environ["APP_IMAGE"], "actor": os.environ["GITHUB_ACTOR"],
        "revision": os.environ["GITHUB_SHA"], "run": os.environ["GITHUB_RUN_NUMBER"],
        "token": os.environ["GH_TOKEN"],
    }
    bootstrap = "import json,sys; p=json.load(sys.stdin); exec(compile(p.pop('script'),'<deployment>','exec')); main(p)"
    # Temporary credentials are inside the checkout and removed even on failure.
    with tempfile.TemporaryDirectory(prefix=".ssh-", dir=root) as directory:
        key = Path(directory) / "key"
        key.write_text(os.environ["DEPLOY_SSH_KEY"].replace("\r", "").strip() + "\n", encoding="utf-8")
        key.chmod(0o600)
        args = ["ssh", "-F", "/dev/null", "-T", "-i", str(key), "-p", port,
                "-o", "BatchMode=yes", "-o", "IdentitiesOnly=yes", "-o", "StrictHostKeyChecking=no",
                "-o", "UserKnownHostsFile=/dev/null", "-o", "GlobalKnownHostsFile=/dev/null",
                "-o", "UpdateHostKeys=no", "-o", "ConnectTimeout=15",
                "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=4",
                f"{user}@{host}", "sudo -n python3 -c " + shlex.quote(bootstrap)]
        subprocess.run(args, input=json.dumps(payload), text=True, check=True, timeout=1000)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"::error::Deployment failed: {error}", file=sys.stderr)
        sys.exit(1)
