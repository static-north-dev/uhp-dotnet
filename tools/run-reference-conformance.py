#!/usr/bin/env python3
"""Run the unmodified pinned suite against this SDK's real Codex reference host."""
import argparse
import os
from pathlib import Path
import secrets
import signal
import shutil
import socket
import subprocess
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--class", dest="profile", default="full", choices=("core", "extended", "full"))
    parser.add_argument("--only")
    parser.add_argument("--host-executable", type=Path)
    parser.add_argument("--probe-runtime", action="store_true")
    parser.add_argument("--report", default="artifacts/conformance/reference-report.json")
    args = parser.parse_args()
    run_root = ROOT / "artifacts/conformance/runs" / secrets.token_hex(8)
    run_root.mkdir(parents=True)
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    address = f"http://127.0.0.1:{port}"
    environment = dict(os.environ, UHP_HOST_TOKEN=secrets.token_urlsafe(32))
    environment["UHP_API_KEY"] = environment["UHP_HOST_TOKEN"]
    host_command = ([str(args.host_executable.resolve())] if args.host_executable else
                    ["dotnet", "run", "--project", "tools/StaticNorth.Uhp.ConformanceHost",
                     "-c", "Release", "--no-build", "--"])
    with (run_root / "host.log").open("w") as log:
        host = subprocess.Popen(host_command + ["--urls", address, "--state-root", str(run_root)],
                                cwd=ROOT, env=environment,
                                stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            for _ in range(300):
                if host.poll() is not None:
                    raise RuntimeError(f"Reference host exited; inspect {run_root / 'host.log'}")
                try:
                    with urllib.request.urlopen(address + "/v1/uhp", timeout=1) as response:
                        if response.status == 200:
                            break
                except (OSError, urllib.error.URLError):
                    time.sleep(.2)
            else:
                raise TimeoutError("Reference host did not become ready")
            if args.probe_runtime:
                subprocess.run(["python3", "tools/probe-reference-runtime.py", address], cwd=ROOT, env=environment, check=True)
            command = ["uv", "--cache-dir", "artifacts/conformance/uv-cache", "run", "--frozen",
                       "--project", "tools/conformance", "uhp-conformance", "--base-url", address,
                       "--class", args.profile, "--json", args.report, "--plain",
                       "--label", "StaticNorth.Uhp Codex reference host"]
            if args.only:
                command += ["--only", args.only]
            return subprocess.run(command, cwd=ROOT, env=environment).returncode
        finally:
            try:
                os.killpg(host.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                host.wait(timeout=15)
            except subprocess.TimeoutExpired:
                os.killpg(host.pid, signal.SIGKILL)
                host.wait()
            try:
                os.killpg(host.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            # Authentication copies are never part of retained conformance evidence.
            for credentials in run_root.rglob("codex-home"):
                shutil.rmtree(credentials)


if __name__ == "__main__":
    raise SystemExit(main())
