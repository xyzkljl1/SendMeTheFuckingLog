"""Resolve complete public defaults and optional instance/Actions overrides without changing source files."""
from copy import deepcopy
import ipaddress
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parent.parent
PREPARED = ".deployment-config/instance.json"
APPLICATION_KEYS = ("Logging", "Tls", "ReverseProxy", "UploadPage", "ImageHosting",
                    "GlobalRateLimit", "AuthorAccess", "Storage")


def unique_object(pairs):
    result = {}
    seen = set()
    for key, value in pairs:
        if key.casefold() in seen:
            raise ValueError("Duplicate configuration key.")
        seen.add(key.casefold())
        result[key] = value
    return result


def read(path):
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 512 * 1024:
        raise ValueError(f"Missing, linked or oversized configuration file: {path.name}")
    value = json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique_object)
    if not isinstance(value, dict):
        raise ValueError("Configuration must be a JSON object.")
    return value


def merge(base, patch, prefix=""):
    for key, value in patch.items():
        original = next((name for name in base if name.casefold() == key.casefold()), None)
        if original is None and prefix.casefold() != "logging:loglevel":
            raise ValueError(f"Unknown configuration key: {prefix}:{key}")
        key = original or key
        previous = base.get(key)
        if isinstance(previous, dict) and isinstance(value, dict):
            merge(previous, value, f"{prefix}:{key}".strip(":"))
        else:
            numeric = lambda item: type(item) in (int, float)
            if previous is not None and value is not None and type(previous) is not type(value) \
                    and not (numeric(previous) and numeric(value)):
                raise ValueError(f"Invalid configuration type: {prefix}:{key}")
            base[key] = deepcopy(value)
    return base


def repository_name(value):
    if not isinstance(value, str):
        raise ValueError("CONFIG_REPOSITORY must be a repository name or GitHub HTTPS URL.")
    value = value.strip()
    if value.startswith("https://github.com/"):
        value = value[len("https://github.com/"):].removesuffix("/").removesuffix(".git")
    if value and not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9][A-Za-z0-9_.-]*", value):
        raise ValueError("CONFIG_REPOSITORY must be OWNER/REPO or https://github.com/OWNER/REPO.git.")
    return value


def variables():
    value = json.loads(os.environ.get("CONFIG_VARIABLES_JSON", "{}"), object_pairs_hook=unique_object)
    if not isinstance(value, dict) or any(not isinstance(v, str) for v in value.values()):
        raise ValueError("Actions configuration variables must be strings.")
    return value


def source(root=ROOT):
    defaults = read(root / "appsettings.json")
    return repository_name(os.environ.get("CONFIG_REPOSITORY", "").strip()
                           or defaults["CONFIG_REPOSITORY"])


def apply_variables(settings, values):
    aliases = {"DEPLOY_HOST": "Deployment__Host", "DEPLOY_USER": "Deployment__User",
               "DEPLOY_PORT": "Deployment__Port", "TLS_DOMAIN": "Tls__Domain"}
    for name, value in values.items():
        if value == "" or name == "CONFIG_REPOSITORY":
            continue
        key = aliases.get(name, name)
        if "__" not in key:
            continue
        parts = key.split("__")
        target = settings
        for part in parts[:-1]:
            actual = next((item for item in target if item.casefold() == part.casefold()), None)
            if actual is None or not isinstance(target[actual], dict):
                raise ValueError(f"Unknown configuration variable: {name}; replace lists as a JSON array.")
            target = target[actual]
        actual = next((item for item in target if item.casefold() == parts[-1].casefold()), None)
        if actual is None:
            raise ValueError(f"Unknown configuration variable: {name}")
        parsed = value if isinstance(target[actual], str) else json.loads(value)
        merge(target, {actual: parsed})


def resolve(root=ROOT):
    settings = read(root / "appsettings.json")
    selected = source(root)
    if selected:
        private_root = root / ".private-config"
        if private_root.is_symlink():
            raise ValueError("Private configuration directory cannot be a symlink.")
        merge(settings, read(private_root / "instance.json"))
    apply_variables(settings, variables())
    # Existing connection variables remain valid and take precedence over file values.
    apply_variables(settings, {name: os.environ.get(name, "") for name in
                              ("DEPLOY_HOST", "DEPLOY_USER", "DEPLOY_PORT")})
    settings["CONFIG_REPOSITORY"] = selected
    return settings


def tls_settings(settings):
    return {"Domain": settings["Tls"]["Domain"], "Mode": settings["Deployment"]["Mode"],
            "KnownProxies": settings["ReverseProxy"]["KnownProxies"]}


def validate(settings):
    from tls_manager import deployment_settings
    deployment_settings(json.dumps(tls_settings(settings)))
    connection, tls = settings["Deployment"], settings["Tls"]
    if not isinstance(connection["Host"], str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]*", connection["Host"]):
        raise ValueError("Set Deployment.Host or DEPLOY_HOST to the destination server.")
    if not isinstance(connection["User"], str) or not re.fullmatch(r"[a-z_][a-z0-9_-]*", connection["User"]):
        raise ValueError("Invalid Deployment.User.")
    for value in (connection["Port"], tls["HttpPort"], tls["HttpsPort"]):
        if type(value) is not int or not 1 <= value <= 65535:
            raise ValueError("Connection and listening ports must be integers between 1 and 65535.")
    if tls["HttpPort"] == tls["HttpsPort"]:
        raise ValueError("HTTP and HTTPS listening ports must differ.")
    if tls["Domain"] == "example.com" or tls["Domain"].endswith(".example.com"):
        raise ValueError("Replace Tls.Domain with the website's real DNS hostname before deployment.")
    if type(tls["Enabled"]) is not bool or type(settings["ReverseProxy"]["Enabled"]) is not bool:
        raise ValueError("TLS and reverse-proxy flags must be booleans.")
    name = settings["UploadPage"]["AuthorName"]
    if not isinstance(name, str) or not name.strip():
        raise ValueError("UploadPage.AuthorName cannot be empty.")
    image_origin = settings["ImageHosting"]["BaseUrl"]
    if not isinstance(image_origin, str) or any(ord(c) < 32 for c in image_origin) or "\\" in image_origin:
        raise ValueError("Invalid ImageHosting.BaseUrl.")
    image = urlsplit(image_origin)
    if image.scheme != "https" or not image.hostname or image.username or image.password \
            or image.path not in ("", "/") or image.query or image.fragment:
        raise ValueError("ImageHosting.BaseUrl must be an HTTPS origin.")
    clients = settings["AuthorAccess"]["AllowedClients"]
    if not isinstance(clients, list) or len(clients) > 32:
        raise ValueError("AuthorAccess.AllowedClients must contain at most 32 entries.")
    for client in clients:
        if not isinstance(client, str) or not client.strip() or len(client) > 253:
            raise ValueError("Invalid AuthorAccess.AllowedClients entry.")
        try:
            ipaddress.ip_address(client)
        except ValueError:
            if client != "*" and not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?", client):
                raise ValueError("Invalid author access hostname or IP address.")
    limits = settings["Storage"]
    values = [limits[name] for name in ("MaxFileMiB", "MaxSubmissionMiB", "MaxStorageMiB")]
    if any(type(value) not in (int, float) or not math.isfinite(value) for value in values) \
            or not 0 < values[0] <= values[1] <= values[2] \
            or values[0] * 1048576 < 1 or values[2] * 1048576 >= 2 ** 63 - 1048576:
        raise ValueError("Invalid Storage limits.")
    if any(type(value) is not int or not 1 <= value <= 1_000_000_000_000
           for value in settings["GlobalRateLimit"].values()):
        raise ValueError("Invalid GlobalRateLimit values.")
    if any(type(value) is not int or not 1 <= value <= 1_000_000
           for value in settings["TrafficMonitor"].values()):
        raise ValueError("Invalid TrafficMonitor limits.")


def application_settings(settings):
    result = {key: deepcopy(settings[key]) for key in APPLICATION_KEYS}
    direct = settings["Deployment"]["Mode"] == "Direct"
    result["Tls"]["Enabled"] = direct
    result["ReverseProxy"]["Enabled"] = not direct
    return result


def prepared(root=ROOT):
    value = read(root / PREPARED)
    validate(value)
    return value


def prepare(root=ROOT):
    settings = resolve(root)
    validate(settings)
    directory = root / ".deployment-config"
    if directory.is_symlink():
        raise ValueError("Generated configuration directory cannot be a symlink.")
    directory.mkdir(mode=0o700, exist_ok=True)
    destination = root / PREPARED
    if destination.is_symlink():
        raise ValueError("Generated configuration file cannot be a symlink.")
    with destination.open("w", encoding="utf-8", newline="\n") as stream:
        os.chmod(destination, 0o600)
        stream.write(json.dumps(settings, ensure_ascii=False, indent=2) + "\n")
    if os.environ.get("GITHUB_ENV"):
        with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as stream:
            for key, value in {"DEPLOY_HOST": settings["Deployment"]["Host"],
                               "DEPLOY_USER": settings["Deployment"]["User"],
                               "DEPLOY_PORT": settings["Deployment"]["Port"],
                               "TLS_DOMAIN": settings["Tls"]["Domain"],
                               "APP_HTTP_PORT": settings["Tls"]["HttpPort"],
                               "APP_HTTPS_PORT": settings["Tls"]["HttpsPort"]}.items():
                stream.write(f"{key}={value}\n")
    if settings["CONFIG_REPOSITORY"]:
        revision = subprocess.run(["git", "-C", str(root / ".private-config"), "rev-parse", "HEAD"],
                                  check=True, capture_output=True, text=True).stdout.strip()
        print("Configuration revision: " + revision)
    print("Configuration validated; public source files and image contents are unchanged.")


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in ("source", "prepare"):
        raise ValueError("Usage: configuration.py source|prepare")
    if sys.argv[1] == "prepare":
        prepare()
        return
    repository = source()
    if repository and os.environ.get("CONFIG_READ_KEY_PRESENT") != "true":
        raise ValueError("CONFIG_REPOSITORY requires the CONFIG_READ_KEY repository secret.")
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as stream:
        stream.write("repository=" + repository + "\n")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"::error::Configuration failed: {error}", file=sys.stderr)
        sys.exit(1)
