"""Certificate lifecycle tests: no Docker daemon, ACME account or system units are changed."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import MagicMock, patch

PROJECT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, PROJECT / "deployment" / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


tls = load("tls_manager")
remote = load("remote")
DOMAIN = "www.sendmethefuckinglog.top"


class CertificateTests(unittest.TestCase):
    def setUp(self):
        (PROJECT / "obj").mkdir(exist_ok=True)
        temp = tempfile.TemporaryDirectory(prefix="tls-test-", dir=PROJECT / "obj")
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.served = self.root / "tls" / "served"
        self.served.mkdir(parents=True)
        self.manifest = self.served / "active.json"
        self.enterContext(patch.object(tls, "ROOT", self.root))
        self.enterContext(patch.object(tls.os, "chown", create=True))
        self.enterContext(patch.object(tls, "sync_directory"))
        self.generation = "a" * 64
        self.new = json.dumps({"version": 1, "generation": self.generation}).encode("ascii")
        self.old = json.dumps({"version": 1, "generation": "b" * 64}).encode("ascii")
        self.enterContext(patch.object(tls, "validated_certificate", return_value=(b"certificate", b"private-key", self.generation, "fingerprint")))
        self.restart = self.enterContext(patch.object(tls, "restart"))
        self.verify = self.enterContext(patch.object(tls, "verify_https"))

    def test_new_certificate_activated_only_after_complete_files_exist(self):
        def check():
            self.assertEqual(self.manifest.read_bytes(), self.new)
            self.assertEqual((self.served / self.generation / "fullchain.pem").read_bytes(), b"certificate")
            self.assertEqual((self.served / self.generation / "privkey.pem").read_bytes(), b"private-key")
        self.restart.side_effect = check
        tls.activate(DOMAIN)
        self.restart.assert_called_once()
        self.verify.assert_called_once_with(DOMAIN, "fingerprint")

    def test_unchanged_certificate_does_not_restart(self):
        self.manifest.write_bytes(self.new)
        tls.activate(DOMAIN)
        self.restart.assert_not_called()
        self.verify.assert_called_once()

    def test_failed_certificate_switch_restores_old_and_restarts(self):
        self.manifest.write_bytes(self.old)
        self.verify.side_effect = RuntimeError("bad certificate")
        with self.assertRaisesRegex(RuntimeError, "bad certificate"):
            tls.activate(DOMAIN)
        self.assertEqual(self.manifest.read_bytes(), self.old)
        self.assertEqual(self.restart.call_count, 2)

    def test_interrupted_activation_recovers_when_manifest_was_already_written(self):
        self.manifest.write_bytes(self.new)
        self.verify.side_effect = [RuntimeError("old certificate still loaded"), None]
        tls.activate(DOMAIN)
        self.restart.assert_called_once()
        self.assertEqual(self.verify.call_count, 2)

    def test_failed_first_switch_restores_validation_only_bootstrap(self):
        self.verify.side_effect = RuntimeError("bad certificate")
        with self.assertRaises(RuntimeError):
            tls.activate(DOMAIN)
        self.assertFalse(self.manifest.exists())
        self.assertEqual(self.restart.call_count, 2)

    def test_issuance_failure_never_changes_certificate_or_restarts(self):
        self.manifest.write_bytes(self.old)
        with patch.object(tls, "issue_or_renew", side_effect=RuntimeError("ACME unavailable")):
            with self.assertRaises(RuntimeError):
                tls.ensure_certificate(DOMAIN)
        self.assertEqual(self.manifest.read_bytes(), self.old)
        self.restart.assert_not_called()

    def test_invalid_certificate_never_publishes_manifest(self):
        self.manifest.write_bytes(self.old)
        with patch.object(tls, "validated_certificate", side_effect=RuntimeError("key mismatch")):
            with self.assertRaises(RuntimeError):
                tls.activate(DOMAIN)
        self.assertEqual(self.manifest.read_bytes(), self.old)
        self.restart.assert_not_called()

    def test_only_own_stale_certbot_container_can_be_removed(self):
        with patch.object(tls, "run", side_effect=[MagicMock(stdout="123"), MagicMock(stdout=json.dumps([{"Config": {"Labels": {}}}]))]) as run:
            with self.assertRaisesRegex(RuntimeError, "unrelated"):
                tls.remove_stale_certbot()
            self.assertEqual(run.call_count, 2)

    def test_temporary_certbot_uses_webroot_and_timeout_always_cleans_up(self):
        with patch.object(tls, "remove_stale_certbot") as cleanup, \
                patch.object(tls, "run", side_effect=subprocess.TimeoutExpired("docker", 240)) as run:
            with self.assertRaises(subprocess.TimeoutExpired):
                tls.issue_or_renew(DOMAIN)
            args = run.call_args.args
            self.assertIn("--webroot", args)
            self.assertIn("certonly", args)
            self.assertNotIn("--publish", args)
            self.assertNotIn("--force-renewal", args)
            self.assertEqual(cleanup.call_count, 2)

    def test_renew_existing_lineage_and_never_force_issue(self):
        renewal = self.root / "tls" / "certbot" / "renewal" / (DOMAIN + ".conf")
        renewal.parent.mkdir(parents=True)
        renewal.write_text("test lineage", encoding="utf-8")
        with patch.object(tls, "remove_stale_certbot"), patch.object(tls, "run") as run:
            tls.issue_or_renew(DOMAIN)
            self.assertIn("renew", run.call_args.args)
            self.assertNotIn("certonly", run.call_args.args)

    def test_settings_reject_shell_and_url_injection(self):
        self.assertEqual(tls.settings(json.dumps({"Domain": DOMAIN})), DOMAIN)
        for bad in ("https://" + DOMAIN, DOMAIN + ":443", "$(bad)", "x\ny", "../bad", ""):
            with self.assertRaises(ValueError):
                tls.settings(json.dumps({"Domain": bad}))


class InstallTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="tls-install-test-", dir=PROJECT / "obj")
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.units = self.root / "units"
        self.units.mkdir()
        self.payload = {"tls": {name: (PROJECT / "deployment" / name).read_text(encoding="utf-8")
                                  for name in ("tls_manager.py", "tls.json", remote.TLS_SERVICE, remote.TLS_TIMER)}}
        self.enterContext(patch.object(remote, "ROOT", self.root))
        self.enterContext(patch.object(remote, "UNIT_DIRECTORY", self.units))
        self.enterContext(patch.object(remote, "require_root_owner"))
        if os.name == "nt":
            self.enterContext(patch.object(remote.os, "chmod"))
        self.manager = MagicMock()
        self.manager.settings.return_value = DOMAIN
        self.enterContext(patch.object(remote.importlib.util, "module_from_spec", return_value=self.manager))
        self.enterContext(patch.object(remote.importlib.util, "spec_from_file_location", return_value=MagicMock()))
        self.run = self.enterContext(patch.object(remote, "run"))

    def test_install_verifies_timer_and_preserves_existing_certificates(self):
        stored = self.root / "tls" / "served" / "active.json"
        stored.parent.mkdir(parents=True)
        stored.write_text("existing certificate", encoding="utf-8")
        remote.install_tls(self.payload)
        remote.install_tls(self.payload)
        self.assertEqual(stored.read_text(), "existing certificate")
        self.run.assert_any_call("systemctl", "is-enabled", "--quiet", remote.TLS_TIMER)
        self.run.assert_any_call("systemctl", "is-active", "--quiet", remote.TLS_TIMER)

    def test_foreign_tls_service_never_overwritten(self):
        unit = self.units / remote.TLS_SERVICE
        unit.write_text("foreign service", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "unrelated"):
            remote.install_tls(self.payload)
        self.assertEqual(unit.read_text(), "foreign service")
        self.run.assert_not_called()

    def test_timer_failure_is_reported(self):
        def fail(*args, **kwargs):
            if "is-active" in args:
                raise subprocess.CalledProcessError(1, args)
        self.run.side_effect = fail
        with self.assertRaises(subprocess.CalledProcessError):
            remote.install_tls(self.payload)


if __name__ == "__main__":
    unittest.main()
