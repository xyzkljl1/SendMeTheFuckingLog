import importlib.util
from datetime import datetime, timedelta, timezone
from pathlib import Path
import unittest
import os
import tempfile
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("traffic_monitor", Path(__file__).resolve().parents[1] / "traffic_monitor.py")
monitor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(monitor)


class TrafficTests(unittest.TestCase):
    def setUp(self):
        self.now = datetime(2026, 11, 2, tzinfo=timezone.utc)

    def sample(self, state=None, rx=100, tx=200, boot="a", now=None, index=2):
        return monitor.update_state(state, now or self.now, boot, {"ens5": {"index": index, "rx": rx, "tx": tx}})

    def test_delta_and_same_sample_not_double_counted(self):
        state = self.sample()
        state = self.sample(state, rx=150, tx=250)
        self.assertEqual(state["daily_bytes"], 100)
        self.assertEqual(self.sample(state, rx=150, tx=250)["daily_bytes"], 100)

    def test_reboot_and_interface_reset_preserve_totals(self):
        for options in ({"boot": "b"}, {"index": 3}, {}):
            previous = self.sample(self.sample(rx=0, tx=0))
            state = self.sample(previous, rx=10, tx=20, **options)
            self.assertEqual(state["daily_bytes"], 330)
            self.assertEqual(state["monthly_bytes"], 330)

    def test_daily_boundary_preserves_monthly_total(self):
        state = self.sample(self.sample(self.sample(rx=0, tx=0)), rx=110, tx=220, now=self.now + timedelta(days=1))
        self.assertEqual(state["daily_bytes"], 30)
        self.assertEqual(state["monthly_bytes"], 330)

    def test_month_boundary_resets_both_without_losing_delta(self):
        state = self.sample(self.sample(), rx=110, tx=220, now=datetime(2026, 12, 1, tzinfo=timezone.utc))
        self.assertEqual(state["daily_bytes"], 30)
        self.assertEqual(state["monthly_bytes"], 30)

    def test_clock_rollback_keeps_period(self):
        state = self.sample(self.sample(), rx=110, tx=220, now=self.now - timedelta(days=1))
        self.assertEqual(state["day"], "2026-11-02")
        self.assertEqual(state["daily_bytes"], 30)

    def test_both_thresholds_independently_and_exact_boundary(self):
        limits = monitor.limits_from({"DailyTrafficLimitGB": 25, "MonthlyTrafficLimitGB": 900})
        for day, month, expected in ((25, 25, "daily"), (1, 900, "monthly"), (24, 899, "")):
            reason = monitor.exceeded({"daily_bytes": day * monitor.GB, "monthly_bytes": month * monitor.GB}, limits)
            self.assertTrue(reason.startswith(expected))
            if not expected:
                self.assertEqual(reason, "")

    def test_corrupt_state_and_disable_options_rejected(self):
        for state in ({}, {**self.sample(), "daily_bytes": -1}, {**self.sample(), "month": "wrong"}):
            with self.assertRaises((ValueError, KeyError)):
                self.sample(state)
        with self.assertRaises(ValueError):
            monitor.limits_from({"DailyTrafficLimitGB": 25, "MonthlyTrafficLimitGB": 900, "Enabled": False})

    def test_check_saves_before_poweroff_and_has_no_boot_grace(self):
        counters = {"ens5": {"index": 2, "rx": 25 * monitor.GB, "tx": 0}}
        actions = []
        previous = self.sample(rx=0, tx=0)
        previous["daily_bytes"] = 25 * monitor.GB
        previous["monthly_bytes"] = 25 * monitor.GB
        with patch.object(monitor, "read_json", side_effect=[{"DailyTrafficLimitGB": 25, "MonthlyTrafficLimitGB": 900}, previous]), \
                patch.object(monitor.Path, "exists", return_value=True), \
                patch.object(monitor.Path, "read_text", return_value="boot-id"), \
                patch.object(monitor, "read_counters", return_value=counters), \
                patch.object(monitor, "atomic_save", side_effect=lambda *args: actions.append("save")), \
                patch.object(monitor.subprocess, "run", side_effect=lambda *args, **kwargs: actions.append(args[0])):
            with patch.object(monitor, "publish_status", side_effect=OSError("test summary failure")):
                monitor.check_once()
        self.assertEqual(actions, ["save", ["systemctl", "poweroff"]])

    def test_below_threshold_never_requests_poweroff(self):
        with patch.object(monitor, "read_json", return_value={"DailyTrafficLimitGB": 25, "MonthlyTrafficLimitGB": 900}), \
                patch.object(monitor.Path, "exists", return_value=False), \
                patch.object(monitor.Path, "read_text", return_value="boot-id"), \
                patch.object(monitor, "read_counters", return_value={"ens5": {"index": 2, "rx": 1, "tx": 2}}), \
                patch.object(monitor, "atomic_save"), patch.object(monitor.subprocess, "run") as poweroff:
            with patch.object(monitor, "publish_status") as publish:
                monitor.check_once()
                publish.assert_called_once()
            poweroff.assert_not_called()

    def test_public_status_excludes_accounting_details_and_marks_failed_checks(self):
        project = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory(prefix="traffic-public-test-", dir=project / "obj") as directory:
            saved = []
            with patch.object(monitor, "ROOT", Path(directory)), \
                    patch.object(monitor, "atomic_save", side_effect=lambda path, value, **kwargs: saved.append(value)):
                monitor.publish_status(self.sample(), (25 * monitor.GB, 900 * monitor.GB))
                self.assertEqual(set(saved[0]), {"Version", "Healthy", "ObservedAt", "DailyBytes", "MonthlyBytes",
                                               "DailyLimitBytes", "MonthlyLimitBytes", "Day", "Month"})
                with patch.object(monitor.Path, "exists", return_value=True), \
                        patch.object(monitor, "read_json", return_value=saved[0].copy()):
                    monitor.publish_status()
                self.assertFalse(saved[-1]["Healthy"])
                self.assertEqual(saved[0]["ObservedAt"], saved[-1]["ObservedAt"])

    def test_persisted_file_restores_totals_after_reboot(self):
        project = Path(__file__).resolve().parents[2]
        (project / "obj").mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="traffic-state-test-", dir=project / "obj") as directory:
            path = Path(directory) / "state.json"
            state = self.sample(self.sample(rx=0, tx=0))
            # Windows cannot fsync a directory. All other checkpoint I/O is real and isolated.
            if os.name == "nt":
                with patch.object(monitor.os, "O_DIRECTORY", 0, create=True), \
                        patch.object(monitor.os, "open", return_value=123), \
                        patch.object(monitor.os, "fsync"), patch.object(monitor.os, "close"):
                    monitor.atomic_save(path, state)
            else:
                monitor.atomic_save(path, state)
            restored = self.sample(monitor.read_json(path), rx=10, tx=20, boot="new-boot")
            self.assertEqual(restored["daily_bytes"], 330)
            self.assertEqual(restored["monthly_bytes"], 330)

    def test_first_install_does_not_misattribute_older_boot_traffic(self):
        state = self.sample(rx=100 * monitor.GB, tx=200 * monitor.GB)
        self.assertEqual(state["daily_bytes"], 0)
        self.assertEqual(state["monthly_bytes"], 0)


if __name__ == "__main__":
    unittest.main()
