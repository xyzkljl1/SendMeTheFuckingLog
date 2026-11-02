"""Exercise real Kestrel HTTP/TLS with test-only PEM files isolated under obj."""
import hashlib
import argparse
import http.client
import json
import os
from pathlib import Path
import shutil
import socket
import ssl
import subprocess
import tempfile
import time

DOMAIN = "www.sendmethefuckinglog.top"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", help="Test the production Linux image instead of the local Debug build")
    image = parser.parse_args().image
    project = Path(__file__).resolve().parents[1]
    (project / "obj").mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="https-test-", dir=project / "obj") as directory:
        root = Path(directory)
        app = root / "app"
        if image:
            app.mkdir()
            for name in ("data", "tmp", "tls", "acme"):
                (app / name).mkdir()
                (app / name).chmod(0o777 if name in ("data", "tmp") else 0o755)
        else:
            shutil.copytree(project / "bin" / "Debug" / "net10.0", app,
                            ignore=shutil.ignore_patterns("data", "tmp", "tls", "acme"))
        cert, key = root / "cert.pem", root / "key.pem"
        config = root / "openssl.cnf"
        config.write_text("[req]\ndistinguished_name=subject\nx509_extensions=extensions\nprompt=no\n"
                          "[subject]\nCN=" + DOMAIN + "\n[extensions]\nsubjectAltName=DNS:" + DOMAIN
                          + "\nbasicConstraints=critical,CA:TRUE\n"
                          "keyUsage=critical,digitalSignature,keyEncipherment,keyCertSign\n"
                          "extendedKeyUsage=serverAuth\nsubjectKeyIdentifier=hash\nauthorityKeyIdentifier=keyid:always\n",
                          encoding="ascii")
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-sha256",
                        "-days", "3", "-config", str(config),
                        "-keyout", str(key), "-out", str(cert)], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                       env={**os.environ, "RANDFILE": str(root / "random.seed")})
        http_socket, https_socket = socket.socket(), socket.socket()
        http_socket.bind(("127.0.0.1", 0))
        https_socket.bind(("127.0.0.1", 0))
        http_port, https_port = http_socket.getsockname()[1], https_socket.getsockname()[1]
        http_socket.close()
        https_socket.close()
        application_environment = {"Tls__Enabled": "true", "Tls__Domain": DOMAIN,
                       "Tls__HttpPort": "8080" if image else str(http_port),
                       "Tls__HttpsPort": "8443" if image else str(https_port),
                       "ASPNETCORE_ENVIRONMENT": "Development", "Logging__LogLevel__Default": "Warning",
                       "Logging__LogLevel__Microsoft.AspNetCore": "Warning",
                       "GlobalRateLimit__MaxRequestsPerHour": "30",
                       "GlobalRateLimit__MaxRequestsPerDay": "50", "GlobalRateLimit__MaxRequestsPerWeek": "70",
                       "DOTNET_EnableDiagnostics": "0", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
        environment = {**os.environ, **application_environment, "DOTNET_CLI_HOME": str(project / ".dotnet")}
        context = ssl.create_default_context(cafile=str(cert))
        container = "send-me-the-log-tls-smoke"

        def request(path, secure=False, method="GET", host=DOMAIN):
            connection = http.client.HTTPConnection("127.0.0.1", https_port if secure else http_port, timeout=3)
            if secure:
                raw = socket.create_connection(("127.0.0.1", https_port), timeout=3)
                connection.sock = context.wrap_socket(raw, server_hostname=DOMAIN)
            try:
                connection.request(method, path, headers={"Host": host})
                response = connection.getresponse()
                return response.status, dict(response.getheaders()), response.read()
            finally:
                connection.close()

        def healthy():
            if not image:
                return request("/healthz")[0] == 200
            return subprocess.run(["docker", "exec", container, "bash", "/app/healthcheck.sh"],
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0

        def start():
            command = ["dotnet", str(app / "SendMeTheFuckingLog.dll")]
            if image:
                command = ["docker", "run", "--rm", "--name", container, "--init", "--read-only",
                           "--cap-drop", "ALL", "--security-opt", "no-new-privileges:true",
                           "--memory", "128m", "--memory-swap", "128m", "--pids-limit", "128",
                           "--publish", f"127.0.0.1:{http_port}:8080", "--publish", f"127.0.0.1:{https_port}:8443"]
                for name in ("data", "tmp", "tls", "acme"):
                    command += ["--mount", f"type=bind,src={app / name},dst=/app/{name}" + (",readonly" if name in ("tls", "acme") else "")]
                for name, value in application_environment.items():
                    command += ["--env", name + "=" + value]
                command.append(image)
            process = subprocess.Popen(command, cwd=project,
                                       env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            last = "not ready"
            for _ in range(100):
                if process.poll() is not None:
                    raise RuntimeError(process.communicate()[0].decode(errors="replace"))
                try:
                    last = healthy()
                    if last:
                        return process
                except OSError as error:
                    last = str(error)
                time.sleep(0.1)
            if image:
                subprocess.run(["docker", "rm", "--force", container], stdout=subprocess.DEVNULL, check=True)
            else:
                process.terminate()
            raise RuntimeError("TLS test startup failed: " + str(last) + "\n" + process.communicate(timeout=10)[0].decode(errors="replace"))

        def stop(process):
            if image:
                subprocess.run(["docker", "stop", "--time", "150", container], check=True, stdout=subprocess.DEVNULL)
            else:
                process.terminate()
            process.communicate(timeout=10)

        token = app / "acme" / ".well-known" / "acme-challenge" / "test_token-123"
        token.parent.mkdir(parents=True)
        token.write_text("test authorization", encoding="ascii")
        challenge = "/.well-known/acme-challenge/" + token.name
        process = start()
        try:
            assert request("/")[0] == 503
            assert request("/api/uploads", method="POST")[0] == 503
            assert request(challenge)[2] == b"test authorization"
            assert request(challenge, method="HEAD")[2] == b""
            assert request(challenge, method="POST")[0] == 404
            assert request("/.well-known/acme-challenge/../private.pem")[0] in (404, 503)
            assert request("/.well-known/acme-challenge/missing")[0] == 404
        finally:
            stop(process)

        if os.name == "nt" and not image:
            print("PASS: HTTP validation-only bootstrap and challenge path isolation.")
            print("SKIP: Windows Schannel requires persisted private keys. Full TLS runs in the Linux image test.")
            return

        generation = hashlib.sha256(cert.read_bytes() + key.read_bytes()).hexdigest()
        destination = app / "tls" / generation
        destination.mkdir(parents=True)
        shutil.copyfile(cert, destination / "fullchain.pem")
        shutil.copyfile(key, destination / "privkey.pem")
        manifest = app / "tls" / "active.json"
        manifest.write_text(json.dumps({"version": 1, "generation": generation}), encoding="ascii")
        process = start()
        try:
            status, headers, _ = request("/path?q=1", host="attacker.invalid")
            assert status == 308 and headers["Location"] == "https://" + DOMAIN + "/path?q=1"
            assert request("/", secure=True)[0] == 200
            assert request("/", secure=True, host="attacker.invalid")[0] == 421
            assert request("/tls/" + generation + "/privkey.pem", secure=True)[0] == 404
            assert request(challenge, secure=True)[2] == b"test authorization"
            for _ in range(35):
                request("/", secure=True)
            assert request("/", secure=True)[0] == 429
            assert request(challenge)[2] == b"test authorization"
            assert request("/.well-known/acme-challenge/nonexistent")[0] == 429
            assert healthy()
            # Windows TerminateProcess cannot deliver SIGTERM: await the regular durable checkpoint.
            checkpoint = app / "data" / ".request-counts.json"
            for _ in range(400):
                if checkpoint.exists() and sum(json.loads(checkpoint.read_text())["Counts"]) == 30:
                    break
                time.sleep(0.1)
            else:
                raise RuntimeError("TLS test quota was not checkpointed.")
        finally:
            stop(process)
        process = start()
        try:
            assert request("/", secure=True)[0] == 429
            assert request(challenge)[2] == b"test authorization"
        finally:
            stop(process)
        print("PASS: bootstrap, trusted test TLS, canonical redirect, key isolation, ACME under exhausted quota, restart persistence.")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        raise SystemExit(1)
