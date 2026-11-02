"""Read-only page checks against synthetic uploads and host snapshots under obj."""
from datetime import datetime, timedelta, timezone
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile
import time


def main():
    project = Path(__file__).resolve().parents[1]
    with tempfile.TemporaryDirectory(prefix="status-page-test-", dir=project / "obj") as directory:
        app = Path(directory) / "app"
        shutil.copytree(project / "bin" / "Debug" / "net10.0", app,
                        ignore=shutil.ignore_patterns("data", "tmp", "tls", "acme", "monitor-status"))
        data = app / "data"
        data.mkdir()
        folder = "2026-11-02_010203_phantom-blade-zero_aaaaaaaaaaaaaaaa"
        (data / folder).mkdir()
        for name in ("private-name.log", "private-config.ini", "submission.json"):
            (data / folder / name).write_text("PRIVATE_UPLOAD_CONTENT", encoding="utf-8")
        for index in range(50):
            other = data / f"2026-11-01_000000_demo-game_{index:016x}"
            other.mkdir()
            (other / "private-name.log").write_text("PRIVATE_UPLOAD_CONTENT", encoding="utf-8")
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        environment = {**os.environ, "ASPNETCORE_URLS": f"http://127.0.0.1:{port}",
                       "ASPNETCORE_ENVIRONMENT": "Development", "Tls__Enabled": "false",
                       "GlobalRateLimit__MaxRequestsPerHour": "20", "GlobalRateLimit__MaxRequestsPerDay": "40",
                       "GlobalRateLimit__MaxRequestsPerWeek": "80", "DOTNET_EnableDiagnostics": "0",
                       "DOTNET_CLI_HOME": str(project / ".dotnet")}

        def request(path, method="GET"):
            client = http.client.HTTPConnection("127.0.0.1", port, timeout=3)
            try:
                client.request(method, path)
                response = client.getresponse()
                return response.status, dict(response.getheaders()), response.read().decode("utf-8")
            finally:
                client.close()

        process = subprocess.Popen(["dotnet", str(app / "SendMeTheFuckingLog.dll")], cwd=project,
                                   env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
        try:
            for _ in range(100):
                if process.poll() is not None:
                    raise RuntimeError("Test instance exited during startup.")
                try:
                    if request("/healthz")[0] == 200:
                        break
                except OSError:
                    pass
                time.sleep(0.1)
            else:
                raise RuntimeError("Test instance did not start.")
            # Incomplete uploads must never appear among completed submissions.
            pending = data / (folder.replace("aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb") + ".uploading")
            pending.mkdir()
            (pending / "unfinished.log").write_text("PRIVATE_UPLOAD_CONTENT", encoding="utf-8")
            status, headers, html = request("/Status?lang=en")
            assert status == 200 and headers["Cache-Control"] == "no-store"
            assert "Unknown" in html and "No host monitoring data received" in html
            assert re.search(re.escape(folder) + r"</code></td><td>2</td>", html)
            assert "private-name.log" not in html and "PRIVATE_UPLOAD_CONTENT" not in html
            assert ".uploading" not in html and "submission.json" not in html
            assert html.count('class="folder-name"') == 50
            second = request("/Status?lang=en&number=2")[2]
            assert second.count('class="folder-name"') == 1
            (app / "monitor-status").mkdir()
            state_file = app / "monitor-status" / "traffic.json"
            state = {"Version": 1, "Healthy": True, "ObservedAt": datetime.now(timezone.utc).isoformat(),
                     "DailyBytes": 1_000_000_000, "MonthlyBytes": 2_000_000_000,
                     "DailyLimitBytes": 25_000_000_000, "MonthlyLimitBytes": 900_000_000_000,
                     "Day": "2026-11-02", "Month": "2026-11"}
            state_file.write_text(json.dumps(state), encoding="utf-8")
            html = request("/Status?lang=en")[2]
            assert "Recent check successful" in html and "1.000 GB / 25.000 GB" in html
            state["ObservedAt"] = (datetime.now(timezone.utc) - timedelta(minutes=4)).isoformat()
            state_file.write_text(json.dumps(state), encoding="utf-8")
            assert "Stale data; monitor status unknown" in request("/Status?lang=en")[2]
            state["Healthy"] = False
            state_file.write_text(json.dumps(state), encoding="utf-8")
            assert "Last check failed" in request("/Status?lang=en")[2]
            state_file.write_text("broken snapshot", encoding="utf-8")
            assert "Unable to read monitor data" in request("/Status?lang=en")[2]
            for _ in range(25):
                request("/missing")
            assert request("/")[0] == 429
            status, headers, html = request("/Status?lang=en")
            assert status == 429 and "Limit reached" in html and folder in html
            assert request("/Status?lang=en", "HEAD")[0] == 429
            assert request("/Status/not-a-page")[0] == 429
            assert json.loads(request("/api/uploads", "POST")[2])["error"] == "siteBusy"
            print("PASS: public status, folder counts and pagination, privacy, absent/stale/failed monitoring, status rendering during 429.")
        finally:
            if process.poll() is None:
                process.terminate()
            process.wait(timeout=10)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        import traceback
        traceback.print_exc()
        raise SystemExit(1)
