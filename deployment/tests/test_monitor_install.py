import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

PROJECT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("monitor_remote", PROJECT / "deployment" / "remote.py")
remote = importlib.util.module_from_spec(spec)
spec.loader.exec_module(remote)


class MonitorInstallTests(unittest.TestCase):
    def setUp(self):
        (PROJECT / "obj").mkdir(exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(prefix="monitor-install-test-", dir=PROJECT / "obj")
        self.addCleanup(self.directory.cleanup)
        root = Path(self.directory.name)
        self.app = root / "app"
        self.app.mkdir()
        self.units = root / "units"
        self.units.mkdir()
        self.payload = {"traffic": {name: (PROJECT / "deployment" / name).read_text(encoding="utf-8")
                                   for name in ("traffic_monitor.py", "traffic-limits.json",
                                                remote.TRAFFIC_SERVICE, remote.TRAFFIC_TIMER)}}
        self.enterContext(patch.object(remote, "ROOT", self.app))
        self.enterContext(patch.object(remote, "UNIT_DIRECTORY", self.units))
        self.enterContext(patch.object(remote, "require_root_owner"))
        # Windows has no root UID; all paths stay in the isolated project test directory.
        if os.name == "nt":
            self.enterContext(patch.object(remote.os, "chmod"))
        self.commands = []
        self.enterContext(patch.object(remote, "run", side_effect=lambda *args, **kwargs: self.commands.append(args)))
        self.enterContext(patch.object(remote, "output", return_value="success"))

    def test_install_and_update_preserve_traffic_totals_and_verify_timer(self):
        remote.install_traffic_monitor(self.payload)
        state = self.app / "traffic-monitor" / "traffic-state.json"
        content = '{"daily_bytes":12345,"monthly_bytes":67890}'
        state.write_text(content, encoding="utf-8")
        remote.install_traffic_monitor(self.payload)
        self.assertEqual(state.read_text(encoding="utf-8"), content)
        for command in (("systemctl", "is-enabled", "--quiet", remote.TRAFFIC_TIMER),
                        ("systemctl", "is-active", "--quiet", remote.TRAFFIC_TIMER),
                        ("systemctl", "stop", remote.TRAFFIC_TIMER, remote.TRAFFIC_SERVICE)):
            self.assertIn(command, self.commands)
        self.assertEqual(json.loads((state.parent / "traffic-limits.json").read_text()),
                         {"DailyTrafficLimitGB": 25, "MonthlyTrafficLimitGB": 900})

    def test_foreign_systemd_unit_is_not_overwritten(self):
        path = self.units / remote.TRAFFIC_SERVICE
        path.write_text("unrelated service", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "unrelated"):
            remote.install_traffic_monitor(self.payload)
        self.assertEqual(path.read_text(), "unrelated service")
        self.assertEqual(self.commands, [])

    def test_failed_first_check_is_not_reported_as_success(self):
        with patch.object(remote, "output", return_value="exit-code"):
            with self.assertRaisesRegex(RuntimeError, "first check failed"):
                remote.install_traffic_monitor(self.payload)

    def test_disabled_timer_verification_fails_deployment(self):
        def fail(*args, **kwargs):
            if "is-enabled" in args:
                raise subprocess.CalledProcessError(1, args)
        with patch.object(remote, "run", side_effect=fail):
            with self.assertRaises(subprocess.CalledProcessError):
                remote.install_traffic_monitor(self.payload)

    def test_request_reset_removes_only_request_files(self):
        data = self.app / "app" / "data"
        data.mkdir(parents=True)
        for name in (".request-counts.json", ".request-counts.json.new", "upload.log", "traffic-state.json"):
            (data / name).write_text("keep", encoding="utf-8")
        remote.reset_request_counts()
        self.assertEqual(sorted(path.name for path in data.iterdir()), ["traffic-state.json", "upload.log"])

    def test_units_have_immediate_check_and_no_disable_or_grace_setting(self):
        timer = self.payload["traffic"][remote.TRAFFIC_TIMER]
        self.assertIn("OnBootSec=0", timer)
        self.assertIn("OnUnitActiveSec=60s", timer)
        self.assertEqual(set(json.loads(self.payload["traffic"]["traffic-limits.json"])),
                         {"DailyTrafficLimitGB", "MonthlyTrafficLimitGB"})


if __name__ == "__main__":
    unittest.main()
