"""Safety checks for destructive cleanup and deployment identity, without Docker/network."""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import MagicMock, patch


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).resolve().parents[1] / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


registry = load("registry")
remote = load("remote")
DIGEST = "sha256:" + "a" * 64


class RegistryTests(unittest.TestCase):
    def test_cleanup_keeps_exact_digest_including_untagged_versions(self):
        items = [{"id": 1, "name": "sha256:" + "b" * 64}, {"id": 2, "name": DIGEST},
                 {"id": 3, "name": "sha256:" + "c" * 64}]
        self.assertEqual(registry.cleanup_plan(items, DIGEST), [1, 3])

    def test_missing_or_duplicate_current_digest_blocks_all_deletion(self):
        for items in ([], [{"id": 1, "name": DIGEST}, {"id": 2, "name": DIGEST}]):
            with self.assertRaises(RuntimeError):
                registry.cleanup_plan(items, DIGEST)

    def test_invalid_digest_blocks_cleanup(self):
        with self.assertRaises(ValueError):
            registry.cleanup_plan([], "latest")

    def test_pagination_reads_all_versions_before_deleting(self):
        with patch.object(registry, "api", side_effect=[[{"id": n} for n in range(100)], [{"id": 100}]]) as api:
            self.assertEqual(len(registry.versions()), 101)
            self.assertIn("page=2", api.call_args.args[0])

    def test_foreign_private_or_unlinked_packages_are_rejected(self):
        good = {"name": registry.PACKAGE, "package_type": "container", "owner": {"login": registry.OWNER},
                "visibility": "public", "repository": {"full_name": registry.REPOSITORY}}
        for field, bad in [("name", "other"), ("owner", {"login": "other"}), ("visibility", "private"),
                           ("repository", {"full_name": "xyzkljl1/other"}), ("repository", None)]:
            with patch.object(registry, "api", return_value={**good, field: bad}):
                with self.assertRaises(RuntimeError):
                    registry.check_package()
        with patch.object(registry, "api", return_value=good):
            registry.check_package()


class RemoteTests(unittest.TestCase):
    def test_only_fixed_image_digest_is_accepted(self):
        payload = {"image": remote.IMAGE + "@" + DIGEST, "run": "12", "revision": "a" * 40, "actor": "xyzkljl1"}
        remote.validate_payload(payload)
        for value in (remote.IMAGE + ":latest", "ghcr.io/other/app@" + DIGEST, "$(touch /tmp/oops)"):
            with self.assertRaises(ValueError):
                remote.validate_payload({**payload, "image": value})

    def test_unmanaged_container_requires_explicit_migration(self):
        with self.assertRaises(RuntimeError):
            remote.check_container({"Config": {"Labels": {}}}, "12")

    def test_stale_workflow_cannot_replace_newer_deployment(self):
        labels = {remote.APP_LABEL: remote.APP, "com.docker.compose.project": remote.APP,
                  "com.docker.compose.service": "web", "io.github.xyzkljl1.deployment-run": "15"}
        container = {"Config": {"Labels": labels}}
        with self.assertRaises(RuntimeError):
            remote.check_container(container, "14")
        remote.check_container(container, "15")

    def test_old_image_used_by_any_container_is_never_force_deleted(self):
        info = {"Config": {"Labels": {"org.opencontainers.image.source": remote.SOURCE}},
                "RepoTags": [], "RepoDigests": [remote.IMAGE + "@" + DIGEST]}
        import json
        with patch.object(remote, "output", side_effect=["old", json.dumps([info]), "other-container"]), \
                patch.object(remote, "run") as run:
            with self.assertRaises(RuntimeError):
                remote.cleanup_images("current")
            run.assert_not_called()

    def test_image_with_foreign_tag_is_not_deleted(self):
        import json
        info = {"Config": {"Labels": {"org.opencontainers.image.source": remote.SOURCE}},
                "RepoTags": ["another-project:latest"], "RepoDigests": []}
        with patch.object(remote, "output", side_effect=["old", json.dumps([info])]), \
                patch.object(remote, "run") as run:
            with self.assertRaises(RuntimeError):
                remote.cleanup_images("current")
            run.assert_not_called()

    def test_cleanup_only_removes_old_project_image(self):
        import json
        info = {"Config": {"Labels": {"org.opencontainers.image.source": remote.SOURCE}},
                "RepoTags": [], "RepoDigests": [remote.IMAGE + "@" + DIGEST]}
        with patch.object(remote, "output", side_effect=["current\nold", json.dumps([info]), "", "current\nold", "current"]), \
                patch.object(remote, "run") as run:
            remote.cleanup_images("current")
            run.assert_called_once_with("docker", "image", "rm", "old")

    def test_failed_container_start_never_cleans_old_images(self):
        self.exercise_deployment(start_fails=True)

    def test_cleanup_runs_only_after_healthy_matching_container(self):
        self.exercise_deployment(start_fails=False)

    def exercise_deployment(self, start_fails):
        from contextlib import ExitStack
        import subprocess

        payload = {"image": remote.IMAGE + "@" + DIGEST, "run": "12", "revision": "a" * 40,
                   "actor": "xyzkljl1", "token": "ephemeral-test-token", "compose": "test config"}
        image = {"Id": "current", "Architecture": "amd64", "Config": {"Healthcheck": {"Test": ["CMD", "bash"]},
                 "Labels": {remote.APP_LABEL: remote.APP, "org.opencontainers.image.source": remote.SOURCE,
                            "org.opencontainers.image.revision": payload["revision"]}}}
        container = {"Image": "current", "State": {"Running": True, "Health": {"Status": "healthy"}}}
        fake_root = MagicMock()
        fake_root.__truediv__.return_value.__truediv__.return_value.stat.return_value.st_uid = 1000

        def run(*args, **kwargs):
            if start_fails and "up" in args:
                raise subprocess.CalledProcessError(1, args)

        with ExitStack() as stack:
            stack.enter_context(patch.dict("sys.modules", {"fcntl": SimpleNamespace(LOCK_EX=1, LOCK_NB=2, flock=MagicMock())}))
            stack.enter_context(patch.object(remote, "ROOT", fake_root))
            stack.enter_context(patch.object(remote, "plain"))
            stack.enter_context(patch.object(remote, "atomic_write"))
            stack.enter_context(patch.object(remote, "install_traffic_monitor"))
            stack.enter_context(patch.object(remote.os, "geteuid", return_value=0, create=True))
            stack.enter_context(patch.object(remote.os, "uname", return_value=SimpleNamespace(machine="x86_64"), create=True))
            stack.enter_context(patch.object(remote.os, "umask"))
            stack.enter_context(patch.object(remote.os, "chmod"))
            stack.enter_context(patch.object(remote.tempfile, "TemporaryDirectory"))
            stack.enter_context(patch.object(remote, "run", side_effect=run))
            stack.enter_context(patch.object(remote, "output", return_value=json.dumps([image])))
            stack.enter_context(patch.object(remote, "inspect_container", side_effect=[None, None, container]))
            reset = stack.enter_context(patch.object(remote, "reset_request_counts"))
            cleanup = stack.enter_context(patch.object(remote, "cleanup_images"))
            if start_fails:
                with self.assertRaises(subprocess.CalledProcessError):
                    remote.main(payload)
                cleanup.assert_not_called()
            else:
                remote.main(payload)
                cleanup.assert_called_once_with("current")
            reset.assert_called_once_with()


if __name__ == "__main__":
    unittest.main()
