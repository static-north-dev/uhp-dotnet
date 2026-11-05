#!/usr/bin/env python3
"""A skipped real-server operation never satisfies the publishing gate."""
import json
from pathlib import Path
import sys

folder = Path(sys.argv[1])
for server in ("harnessrouter", "superqode"):
    path = folder / (server + ".json")
    if not path.is_file():
        raise SystemExit(f"Missing interoperability evidence: {path}")
    report = json.loads(path.read_text())
    if not report.get("ServerVersion") or not report.get("NegotiatedVersion") or any(value != "pass" for value in report["Operations"].values()):
        raise SystemExit(f"Publishing gate failed: {server} has missing, failed, or skipped evidence.")
print("Both pinned real-server interoperability reports passed.")
