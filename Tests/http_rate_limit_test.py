"""Run the built app against isolated data; never uploads files or touches a live instance."""
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request


def main():
    project = Path(__file__).resolve().parents[1]
    build = project / "bin" / "Debug" / "net10.0"
    with tempfile.TemporaryDirectory(prefix="http-limit-test-", dir=project / "obj") as directory:
        app = Path(directory) / "app"
        shutil.copytree(build, app, ignore=shutil.ignore_patterns("data", "tmp"))
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        environment = {**os.environ, "ASPNETCORE_URLS": f"http://127.0.0.1:{port}",
                       "ASPNETCORE_ENVIRONMENT": "Development", "GlobalRateLimit__MaxRequestsPerHour": "3",
                       "GlobalRateLimit__MaxRequestsPerDay": "5", "GlobalRateLimit__MaxRequestsPerWeek": "7",
                       "DOTNET_CLI_HOME": str(project / ".dotnet"), "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
        client = urllib.request.build_opener(urllib.request.ProxyHandler({}))

        def request(path, method="GET"):
            query = urllib.request.Request(f"http://127.0.0.1:{port}{path}", method=method)
            try:
                with client.open(query, timeout=2) as response:
                    return response.status, response.read()
            except urllib.error.HTTPError as response:
                return response.code, response.read()

        def start():
            process = subprocess.Popen(["dotnet", str(app / "SendMeTheFuckingLog.dll")], cwd=project,
                                       env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                       creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
            for _ in range(100):
                if process.poll() is not None:
                    raise RuntimeError("Isolated test application exited during startup.")
                try:
                    if request("/healthz")[0] == 200:
                        return process
                except (OSError, urllib.error.URLError):
                    pass
                time.sleep(0.1)
            process.terminate()
            process.wait(timeout=10)
            raise RuntimeError("Isolated application did not start.")

        process = start()
        try:
            assert request("/")[0] == 200
            assert request("/css/site.css")[0] == 200
            assert request("/missing-test-path")[0] == 404
            status, body = request("/api/uploads", "POST")
            assert status == 429 and json.loads(body)["error"] == "siteBusy"
            assert request("/")[0] == 429
            assert request("/css/site.css")[0] == 429
            assert request("/healthz")[0] == 200
            checkpoint = app / "data" / ".request-counts.json"
            for _ in range(400):
                if checkpoint.exists() and sum(json.loads(checkpoint.read_text())["Counts"]) == 3:
                    break
                time.sleep(0.1)
            else:
                raise RuntimeError("Counter checkpoint did not persist the three admitted requests.")
        finally:
            process.terminate()
            process.wait(timeout=10)
        process = start()
        try:
            assert request("/")[0] == 429
            assert request("/healthz")[0] == 200
        finally:
            process.terminate()
            process.wait(timeout=10)
        print("PASS: page/static/404 share quota, API 429 JSON, health checks bypass quota, checkpoint survives process restart.")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        raise SystemExit(1)
