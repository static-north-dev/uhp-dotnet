#!/usr/bin/env python3
"""Install packed consumers, publish both as Native AOT, and run the shared lifecycle scenario."""
import argparse
import json
import os
from pathlib import Path
import platform
import socket
import subprocess
import tempfile
import time
import urllib.request
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RUNTIME_VERSIONS = {"net10.0": "10.0.15", "net11.0": "11.0.0"}


def run(*args):
    print("+ " + " ".join(map(str, args)), flush=True)
    subprocess.run(list(map(str, args)), cwd=ROOT, check=True)


def check_packages(version):
    for name in ("Core", "Client", "Server"):
        package = ROOT / f"artifacts/packages/StaticNorth.Uhp.{name}.{version}.nupkg"
        symbols = package.with_suffix(".snupkg")
        assert symbols.is_file(), f"Missing symbols: {symbols}"
        with zipfile.ZipFile(package) as archive:
            assemblies = [path for path in archive.namelist() if path.startswith("lib/") and path.endswith(".dll")]
            assert assemblies == [f"lib/net10.0/StaticNorth.Uhp.{name}.dll"], assemblies
            assert "README.md" in archive.namelist()
            assert any(path.startswith("licenses/") and path.endswith("LICENSE") for path in archive.namelist())
            assert any(path.startswith("licenses/") and path.endswith("NOTICE") for path in archive.namelist())
            metadata = ET.fromstring(archive.read(f"StaticNorth.Uhp.{name}.nuspec"))
            ns = {"n": metadata.tag.split("}")[0].strip("{")}
            assert metadata.findtext(".//n:icon", namespaces=ns) == "nuget-icon.png"
            assert archive.read("nuget-icon.png") == (ROOT / "assets/nuget-icon.png").read_bytes()
            assert metadata.find(".//n:license", ns).text == "Apache-2.0"
            assert metadata.find(".//n:repository", ns).get("url") == "https://github.com/static-north-dev/uhp-dotnet"
            dependencies = [item.get("id") for item in metadata.findall(".//n:dependency", ns)]
            if name == "Core":
                assert dependencies == [], dependencies
            if name == "Client":
                assert dependencies == ["StaticNorth.Uhp.Core"], dependencies
                assert metadata.find(".//n:description", ns).text == "community .NET SDK for connecting to UHP servers"
            if name == "Server":
                assert "StaticNorth.Valiant.Endpoints.AspNetCore" in dependencies
                assert "StaticNorth.Valiant.Validation" in dependencies


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", default=("osx" if platform.system() == "Darwin" else "linux") + ("-arm64" if platform.machine() in ("arm64", "aarch64") else "-x64"))
    parser.add_argument("--offline-cache", type=Path)
    parser.add_argument("--framework", choices=RUNTIME_VERSIONS, default="net11.0")
    parser.add_argument("--runtime-version")
    parser.add_argument("--version", default=ET.parse(ROOT / "Directory.Build.props").findtext("./PropertyGroup/Version"))
    parser.add_argument("--reference-host", action="store_true")
    args = parser.parse_args()
    check_packages(args.version)
    runtime_version = args.runtime_version or RUNTIME_VERSIONS[args.framework]
    output = ROOT / "artifacts/aot"
    if args.framework == "net10.0":
        output /= args.framework

    build_properties = [
        f"-p:PackageSmokeTargetFramework={args.framework}",
        "-p:PublishAot=true",
        "-p:UseUhpPackages=true",
        f"-p:RuntimeFrameworkVersion={runtime_version}",
        f"-p:Version={args.version}",
    ]
    sources = ["--source", str(ROOT / "artifacts/packages")]
    if args.offline_cache:
        sources += ["--source", str(args.offline_cache)]
    else:
        sources += ["--source", "https://api.nuget.org/v3/index.json"]

    with tempfile.TemporaryDirectory(prefix="uhp-package-consumer-") as packages:
        projects = [("samples", "StaticNorth.Uhp.Console"), ("samples", "StaticNorth.Uhp.FixtureServer")]
        if args.reference_host:
            projects.append(("tools", "StaticNorth.Uhp.ConformanceHost"))
        for directory, name in projects:
            project = ROOT / directory / name
            lock = Path(packages) / (name + ".native.lock.json")
            run("dotnet", "restore", project, "--runtime", args.rid, "--packages", packages,
                *build_properties,
                "-p:RestorePackagesWithLockFile=true", "-p:NuGetLockFilePath=" + str(lock), *sources)
            run("dotnet", "publish", project, "--configuration", "Release", "--runtime", args.rid,
                "--no-restore", *build_properties,
                "-p:TrimmerSingleWarn=false", "-m:1", "/nr:false",
                "--output", output / name)

        with socket.socket() as endpoint:
            endpoint.bind(("127.0.0.1", 0))
            port = endpoint.getsockname()[1]
        url = f"http://127.0.0.1:{port}"
        log_path = output / "fixture.log"
        with log_path.open("w") as log:
            server = subprocess.Popen([str(output / "StaticNorth.Uhp.FixtureServer/StaticNorth.Uhp.FixtureServer"), "--urls", url], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
            try:
                ready = False
                for _ in range(200):
                    if server.poll() is not None:
                        raise RuntimeError("Native fixture exited: " + log_path.read_text())
                    try:
                        with urllib.request.urlopen(url + "/health", timeout=0.2) as response:
                            ready = response.status == 200
                    except OSError:
                        time.sleep(0.05)
                    if ready:
                        break
                assert ready, "The Native AOT server did not become ready."
                env = os.environ.copy()
                env.update(UHP_BASE_URL=url + "/api/harness", UHP_TOKEN="fixture-alice", UHP_HARNESS_ID="chrn_fixture")
                subprocess.run([str(output / "StaticNorth.Uhp.Console/StaticNorth.Uhp.Console")], cwd=ROOT, env=env, check=True, timeout=60)
            finally:
                server.terminate()
                try:
                    server.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    server.kill()
                    server.wait()

    report = {"framework": args.framework, "runtime": args.rid, "runtimeVersion": runtime_version, "nativeAotClient": "pass", "nativeAotServer": "pass", "packageInstall": "pass", "scenario": "discovery, catalogs, run, stream, readback, continuation, response cancellation, session cancellation", "fixtureOnly": True}
    (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    print("Package installation and Native AOT client/server lifecycle passed.")


if __name__ == "__main__":
    main()
