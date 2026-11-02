"""Deployment identity and site settings; personal values come from the environment."""
import ipaddress
import json
import os
import re
from urllib.parse import urlsplit

APP_LABEL = "io.sendmethelog.application"
RUN_LABEL = "io.sendmethelog.deployment-run"


def identity(repository):
    if not isinstance(repository, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9][A-Za-z0-9_.-]*", repository):
        raise ValueError("GITHUB_REPOSITORY must identify the deploying repository.")
    package = repository.split("/", 1)[1].lower()
    if not re.fullmatch(r"[a-z0-9]+(?:[._-][a-z0-9]+)*", package):
        raise ValueError("Repository name must also be a valid GHCR package name.")
    owner = repository.split("/", 1)[0].lower()
    return {"repository": repository, "source": "https://github.com/" + repository,
            "owner": owner, "package": package, "image": f"ghcr.io/{owner}/{package}"}


def load_identity(environment=None):
    environment = os.environ if environment is None else environment
    return identity(environment.get("GITHUB_REPOSITORY", ""))


def domain(value):
    if not isinstance(value, str) or len(value) > 253 or not re.fullmatch(r"(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}", value):
        raise ValueError("SITE_DOMAIN must be a lowercase DNS hostname.")
    return value


def site_config(environment=None):
    environment = os.environ if environment is None else environment
    host = domain(environment.get("SITE_DOMAIN", ""))
    author = environment.get("AUTHOR_NAME", "").strip()
    if not author or len(author) > 120 or any(ord(char) < 32 for char in author):
        raise ValueError("AUTHOR_NAME must contain 1 to 120 printable characters.")
    image_url = environment.get("IMAGE_BASE_URL", "").strip()
    try:
        parsed = urlsplit(image_url)
        port = parsed.port
    except ValueError:
        raise ValueError("IMAGE_BASE_URL must be an HTTPS origin.") from None
    if (parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password
            or parsed.path not in ("", "/") or parsed.query or parsed.fragment
            or any(char.isspace() or ord(char) < 32 for char in image_url) or "\\" in image_url
            or port == 0):
        raise ValueError("IMAGE_BASE_URL must be an HTTPS origin.")
    try:
        clients = json.loads(environment.get("AUTHOR_ALLOWED_CLIENTS", "[]") or "[]")
    except (ValueError, TypeError):
        raise ValueError("AUTHOR_ALLOWED_CLIENTS must be a JSON array.") from None
    if not isinstance(clients, list) or len(clients) > 32:
        raise ValueError("AUTHOR_ALLOWED_CLIENTS must be a JSON array of at most 32 entries.")
    for client in clients:
        if not isinstance(client, str) or not client or client != client.strip():
            raise ValueError("Invalid author access entry.")
        if client == "*":
            continue
        try:
            ipaddress.ip_address(client)
        except ValueError:
            try:
                domain(client.lower())
            except ValueError:
                raise ValueError("Invalid author access entry.") from None
    return {"UploadPage": {"AuthorName": author}, "ImageHosting": {"BaseUrl": image_url},
            "AuthorAccess": {"AllowedClients": clients}, "Tls": {"Enabled": True, "Domain": host}}


if __name__ == "__main__":
    import sys
    try:
        deployment = load_identity()
        site_config()  # Validate without printing personal settings.
        with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as output:
            output.write(f"IMAGE={deployment['image']}\nSOURCE_URL={deployment['source']}\n")
        print("Deployment settings validated.")
    except Exception:
        print("::error::Invalid deployment settings. Check the repository name and site variables/secrets.", file=sys.stderr)
        sys.exit(1)
