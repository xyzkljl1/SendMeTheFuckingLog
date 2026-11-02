"""Host-only traffic guard. GB means 1,000,000,000 bytes; periods use UTC."""
import copy
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
STATE = ROOT / "traffic-state.json"
CONFIG = ROOT / "traffic-limits.json"
GB = 1_000_000_000


def plain(path):
    for part in (path, *path.parents):
        if part.is_symlink():
            raise RuntimeError(f"Refusing symbolic link: {part}")


def read_json(path):
    plain(path)
    if path.stat().st_size > 256 * 1024:
        raise ValueError(f"File too large: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def atomic_save(path, value, public=False):
    plain(path)
    staging = path.with_name(path.name + ".new")
    plain(staging)
    with staging.open("w", encoding="utf-8") as stream:
        os.chmod(staging, 0o644 if public else 0o600)
        json.dump(value, stream, separators=(",", ":"))
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(staging, path)
    descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def limits_from(config):
    if set(config) != {"DailyTrafficLimitGB", "MonthlyTrafficLimitGB"}:
        raise ValueError("Expected only DailyTrafficLimitGB and MonthlyTrafficLimitGB.")
    for value in config.values():
        if type(value) is not int or not 1 <= value <= 1_000_000:
            raise ValueError("Traffic limits must be positive integer GB values.")
    return config["DailyTrafficLimitGB"] * GB, config["MonthlyTrafficLimitGB"] * GB


def publish_status(state=None, limits=None):
    # Export only aggregate statistics. The website never receives the private accounting file.
    directory = ROOT / "status"
    plain(directory)
    directory.mkdir(mode=0o755, exist_ok=True)
    os.chmod(directory, 0o755)
    path = directory / "traffic.json"
    if state is None:
        if not path.exists():
            return  # An absent snapshot is explicitly shown as unknown by the website.
        value = read_json(path)
        value["Healthy"] = False
    else:
        value = {"Version": 1, "Healthy": True,
                 "ObservedAt": datetime.fromtimestamp(state["observed_at"], timezone.utc).isoformat(),
                 "DailyBytes": state["daily_bytes"], "MonthlyBytes": state["monthly_bytes"],
                 "DailyLimitBytes": limits[0], "MonthlyLimitBytes": limits[1],
                 "Day": state["day"], "Month": state["month"]}
    atomic_save(path, value, public=True)


def read_counters():
    # Default-route interfaces cover IPv4 and IPv6 without adding Docker bridge/veth traffic.
    names = set()
    for family in ("-4", "-6"):
        result = subprocess.run(["ip", "-j", family, "route", "show", "default"],
                                check=True, capture_output=True, text=True, timeout=10)
        for route in json.loads(result.stdout):
            if route.get("dev"):
                names.add(route["dev"])
            names.update(hop["dev"] for hop in route.get("nexthops", []) if hop.get("dev"))
    if not names:
        raise RuntimeError("No default-route network interface; cannot measure traffic safely.")
    counters = {}
    for name in sorted(names):
        if not re.fullmatch(r"[a-zA-Z0-9_.:-]{1,64}", name) or name == "lo":
            raise ValueError("Invalid external interface name.")
        interface = Path("/sys/class/net") / name
        # Refuse virtual bridges/tunnels instead of silently counting forwarded bytes twice.
        if not (interface / "device").exists():
            raise RuntimeError(f"Default route {name} is not a device-backed interface.")
        values = {field: int((interface / filename).read_text().strip()) for field, filename in (
            ("index", "ifindex"), ("rx", "statistics/rx_bytes"), ("tx", "statistics/tx_bytes"))}
        if values["index"] <= 0 or min(values["rx"], values["tx"]) < 0:
            raise ValueError("Invalid network counters.")
        counters[name] = values
    return counters


def update_state(previous, now, boot, counters):
    """Pure accounting, also used by tests. Never silently discard corrupt saved totals."""
    day, month = now.strftime("%Y-%m-%d"), now.strftime("%Y-%m")
    if previous is None:
        state = {"version": 1, "day": day, "month": month, "daily_bytes": 0,
                 "monthly_bytes": 0, "boot": boot, "interfaces": {}, "observed_at": now.timestamp()}
        # We cannot infer which day/month pre-installation traffic belongs to.
        # Establish a baseline once; subsequent restarts keep persisted totals.
        state["interfaces"] = copy.deepcopy(counters)
        return state
    else:
        state = copy.deepcopy(previous)
        if (state.get("version") != 1 or not isinstance(state.get("boot"), str)
                or not isinstance(state.get("interfaces"), dict)):
            raise ValueError("Invalid traffic state; refusing to reset counters.")
        for key in ("daily_bytes", "monthly_bytes"):
            if type(state.get(key)) is not int or state[key] < 0:
                raise ValueError("Invalid saved traffic totals.")
        observed = state["observed_at"]
        stamp = datetime.fromtimestamp(observed, timezone.utc)
        if (stamp.strftime("%Y-%m-%d") != state["day"]
                or stamp.strftime("%Y-%m") != state["month"]):
            raise ValueError("Inconsistent traffic state dates.")
        # Hold the last period on clock rollback instead of repeatedly resetting allowances.
        now = max(now, stamp)
        day, month = now.strftime("%Y-%m-%d"), now.strftime("%Y-%m")
        if state["day"] != day:
            state["day"], state["daily_bytes"] = day, 0
        if state["month"] != month:
            state["month"], state["monthly_bytes"] = month, 0
    delta = 0
    for name, current in counters.items():
        prior = state["interfaces"].get(name) if state["boot"] == boot else None
        if prior is not None:
            if (not isinstance(prior, dict) or any(type(prior.get(k)) is not int or prior[k] < 0
                                                 for k in ("index", "rx", "tx"))):
                raise ValueError("Invalid saved interface counters.")
        for field in ("rx", "tx"):
            if prior and prior["index"] == current["index"] and current[field] >= prior[field]:
                delta += current[field] - prior[field]
            else:
                # New boot/interface/reset: include its current counters, never subtract old totals.
                delta += current[field]
    state["daily_bytes"] += delta
    state["monthly_bytes"] += delta
    # Keep removed interfaces until reboot so a route temporarily disappearing does not double count.
    if state["boot"] != boot:
        state["interfaces"] = {}
    state["interfaces"].update(counters)
    state["boot"], state["observed_at"] = boot, now.timestamp()
    return state


def exceeded(state, limits):
    reasons = []
    if state["daily_bytes"] >= limits[0]:
        reasons.append("daily traffic limit reached")
    if state["monthly_bytes"] >= limits[1]:
        reasons.append("monthly traffic limit reached")
    return "; ".join(reasons)


def check_once():
    limits = limits_from(read_json(CONFIG))
    boot = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
    previous = read_json(STATE) if STATE.exists() else None
    state = update_state(previous, datetime.now(timezone.utc), boot, read_counters())
    reason = exceeded(state, limits)
    if reason:
        # This is diagnostic history, not a startup-blocking marker.
        state["last_shutdown"] = {"at": state["observed_at"], "reason": reason}
    try:
        atomic_save(STATE, state)
    except OSError:
        if not reason:
            raise
        print("Cannot save counters, but a known traffic limit was reached; powering off.", file=sys.stderr)
    if reason:
        try:
            publish_status(state, limits)
        except Exception as error:
            # Reporting must never prevent the protection action.
            print(f"Cannot publish shutdown status: {error}", file=sys.stderr)
        print(f"TRAFFIC PROTECTION: {reason}; daily={state['daily_bytes']}, monthly={state['monthly_bytes']} bytes",
              flush=True)
        subprocess.run(["systemctl", "poweroff"], check=True, timeout=20)
    else:
        publish_status(state, limits)


def main():
    import fcntl

    if os.geteuid() != 0:
        raise PermissionError("The traffic monitor must run as root on the host.")
    lock_path = ROOT / ".traffic.lock"
    plain(lock_path)
    with lock_path.open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            check_once()
        except Exception:
            try:
                publish_status()
            except Exception as error:
                print(f"Cannot publish monitoring failure: {error}", file=sys.stderr)
            raise


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Traffic monitoring failed: {error}", file=sys.stderr)
        sys.exit(1)
