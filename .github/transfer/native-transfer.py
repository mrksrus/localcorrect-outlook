"""Promote one pinned CI artifact to a draft release, without rebuilding it."""
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import struct
import subprocess
import tempfile
import time
from zipfile import BadZipFile, ZipFile

REPOSITORY = "mrksrus/localcorrect-outlook"
BRANCH = "ops/rc6-asset-transfer"
TARGET = "7ab5510c6d88d8361b10571a7f59ed2bbbff611c"
TESTED_MERGE = "a7886a6cc0d1524464e7330dd947d9576e25fbe9"
TARGET_TREE = "d9a14622b1cb31a479149bd036a259ceb495c8a5"
CORE = "3a7b7aecfc628a49e2dd65f987e79bd360d67cfb"
RUN_ID = 42242529069
ARTIFACT_ID = 13518675080
ARTIFACT_SIZE = 363677180
ARTIFACT_SHA = "a677e8a4f5f7ca275381cf240d9ded6cb820d32a182575253ad7a5aeca1711d6"
MODEL_SHA = "899628b5f9ab75a075f158ffeadc9e30aea36e5f03e9c24facb11e87241e9e59"
ORT_SHA = "cb2fa518b18356617f6f9f460c012501da2819ec7e5c1d80bd8f5de20492db0c"
POLICY_SHA = "808e18d8eea3afb329597b0eefc2966a4308936e93ce97eb0cde0c1db8db5d63"
METADATA_MODULE_SHA = "444f875110445c051982815ebcfb06e4183975d0f967afa56c13c9f22bf0a3b6"
BENCHMARK_SHA = "211dd3cc8a7d4056e1358259fdc098ba7ee572f0cd1cd88a4833f8806cee3189"
TAG = "v1.0.0-rc.6"
EXE = "LocalCorrect-Outlook-Setup.exe"
CHECKSUM = EXE + ".sha256"
RECEIPT = "native-promotion-receipt.json"
BENCHMARK = "native-helper-benchmark.json"
EXPECTED_NAMES = {EXE, CHECKSUM, RECEIPT, BENCHMARK}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def api(endpoint, method="GET", data=None, optional=False):
    command = ["gh", "api", "--method", method, f"repos/{REPOSITORY}/{endpoint}"]
    if data is not None:
        command += ["--input", "-"]
    result = subprocess.run(command, input=None if data is None else json.dumps(data).encode(),
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120)
    if result.returncode:
        if optional and b"HTTP 404" in result.stderr:
            return None
        raise RuntimeError(f"GitHub API request failed: {method} {endpoint}")
    return json.loads(result.stdout) if result.stdout.strip() else None


def guard_execution():
    require(os.environ.get("GITHUB_ACTIONS") == "true", "Run only in the guarded GitHub Actions job.")
    require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY and os.environ.get("GH_REPO") == REPOSITORY,
            "Unexpected repository.")
    require(os.environ.get("GITHUB_EVENT_NAME") == "pull_request", "A same-repository PR is required.")
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    head = event["pull_request"]["head"]
    require(head["repo"]["full_name"] == REPOSITORY and head["ref"] == BRANCH,
            "Unexpected PR source.")
    checked_out = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    require(checked_out == head["sha"], "Transfer helper must be checked out from the PR head.")
    require(bool(os.environ.get("GH_TOKEN")), "The built-in repository token is required.")


def verify_ci():
    run = api(f"actions/runs/{RUN_ID}")
    require(run["id"] == RUN_ID and run["status"] == "completed" and run["conclusion"] == "success"
            and run["head_sha"] == TARGET and run["path"] == ".github/workflows/native.yml",
            "Pinned native CI run is not the expected successful candidate.")
    jobs = api(f"actions/runs/{RUN_ID}/jobs?per_page=100")
    require(jobs["total_count"] == 3 and len(jobs["jobs"]) == 3
            and all(job["status"] == "completed" and job["conclusion"] == "success" for job in jobs["jobs"]),
            "All three pinned native CI jobs must have succeeded.")
    for commit in (TARGET, TESTED_MERGE):
        require(api(f"git/commits/{commit}")["tree"]["sha"] == TARGET_TREE,
                "Candidate and tested merge trees differ.")
    artifact = api(f"actions/artifacts/{ARTIFACT_ID}")
    require(artifact["id"] == ARTIFACT_ID and artifact["name"] == "native-outlook-candidate"
            and artifact["size_in_bytes"] == ARTIFACT_SIZE and artifact["digest"] == "sha256:" + ARTIFACT_SHA
            and not artifact["expired"] and artifact["workflow_run"]["id"] == RUN_ID
            and artifact["workflow_run"]["head_sha"] == TARGET,
            "Pinned CI artifact metadata differs.")
    return sorted(job["name"] for job in jobs["jobs"])


def safe_zip(archive):
    names = set()
    total = 0
    for entry in archive.infolist():
        name = entry.filename
        path = PurePosixPath(name)
        require(name and not path.is_absolute() and "\\" not in name and ":" not in name
                and "\x00" not in name and all(part not in ("", ".", "..") for part in name.split("/"))
                and path.as_posix() == name, "Unsafe embedded ZIP path.")
        require(name.casefold() not in names, "Duplicate embedded ZIP path.")
        names.add(name.casefold())
        require(not stat.S_ISLNK(entry.external_attr >> 16) and not entry.is_dir()
                and not entry.flag_bits & 1, "Unsupported embedded ZIP entry.")
        total += entry.file_size
        require(0 <= entry.file_size <= 512 * 1024 * 1024 and total <= 4 * 1024 * 1024 * 1024,
                "Unexpected embedded ZIP size.")
    require(archive.testzip() is None, "Embedded ZIP CRC verification failed.")


def embedded_payload(executable):
    # .NET manifest resources contain the exact payload ZIP as contiguous bytes.
    # Locate its central directory without running or loading the PE assembly.
    candidates = []
    offset = executable.find(b"PK\x05\x06")
    while offset >= 0:
        if offset + 22 <= len(executable):
            _, disk, central_disk, disk_count, count, size, relative, comment = struct.unpack_from("<4s4H2IH", executable, offset)
            start = offset - size - relative
            end = offset + 22 + comment
            if disk == central_disk == 0 and disk_count == count and count > 0 and start >= 0 and end <= len(executable) and executable[start:start + 4] == b"PK\x03\x04":
                try:
                    with ZipFile(io.BytesIO(executable[start:end])) as archive:
                        if "build-info.json" in archive.namelist():
                            candidates.append((start, end))
                except (BadZipFile, ValueError, OSError):
                    pass
        offset = executable.find(b"PK\x05\x06", offset + 1)
    require(len(candidates) == 1, "Expected exactly one embedded native payload ZIP.")
    start, end = candidates[0]
    return ZipFile(io.BytesIO(executable[start:end]))


def archive_digest(archive, name):
    with archive.open(name) as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_installer(executable):
    require(executable[:2] == b"MZ", "The installer is not a Windows PE executable.")
    with embedded_payload(executable) as archive:
        safe_zip(archive)
        info = json.loads(archive.read("build-info.json"))
        require(info["version"] == "1.0.0-rc.6" and info["coreCommit"] == CORE
                and info["coreDirty"] is False and info["release"] is True
                and info["parityCases"] == 6844 and info["parityCasesByScope"] == {"mixed": 6622, "selected": 222}
                and info["contextModel"]["sha256"] == MODEL_SHA and info["vcRuntime"]["onnxRuntimeSha256"] == ORT_SHA,
                "Embedded native build identity or tested parity differs.")
        require(archive_digest(archive, "native-helper/models/context-encoder.onnx") == MODEL_SHA,
                "Bundled ONNX model digest differs.")
        require(archive_digest(archive, "native-helper/onnxruntime.dll") == ORT_SHA,
                "Bundled ONNX Runtime digest differs.")
        for name, expected in (("context-policy.js", POLICY_SHA), ("context-encoder-data.js", METADATA_MODULE_SHA)):
            source = archive.read("source/browser/packages/core/src/" + name).replace(b"\r\n", b"\n")
            require(digest(source) == expected, "Bundled neural policy or metadata module differs.")
        return info


def prepare_assets(directory):
    download = directory / "candidate.zip"
    with download.open("wb") as output:
        subprocess.run(["gh", "api", f"repos/{REPOSITORY}/actions/artifacts/{ARTIFACT_ID}/zip"],
                       stdout=output, check=True, timeout=900)
    require(download.stat().st_size == ARTIFACT_SIZE and file_digest(download) == ARTIFACT_SHA,
            "Downloaded CI ZIP size or digest differs.")
    with ZipFile(download) as outer:
        require(len(outer.infolist()) == 2 and set(outer.namelist()) == {EXE, CHECKSUM},
                "CI artifact must contain only the installer and its checksum.")
        safe_zip(outer)
        checksum = outer.read(CHECKSUM)
        match = re.fullmatch(rb"([0-9a-f]{64})  LocalCorrect-Outlook-Setup\.exe\r?\n?", checksum)
        require(match is not None, "Unexpected installer checksum format.")
        executable = outer.read(EXE)
        require(digest(executable) == match[1].decode("ascii"), "Inner installer checksum differs.")
        info = verify_installer(executable)
        (directory / EXE).write_bytes(executable)
        (directory / CHECKSUM).write_bytes(checksum)
    benchmark = Path(__file__).with_name(BENCHMARK).read_bytes()
    require(digest(benchmark) == BENCHMARK_SHA, "Reviewed aggregate benchmark digest differs.")
    measured = json.loads(benchmark)
    require(measured["nativeCommit"] == TARGET and measured["browserCommit"] == CORE
            and measured["modelSha256"] == MODEL_SHA and measured["physicalOutlookAcceptance"] is False,
            "Aggregate benchmark identity differs.")
    (directory / BENCHMARK).write_bytes(benchmark)
    assets = {name: {"sha256": file_digest(directory / name), "size": (directory / name).stat().st_size}
              for name in (EXE, CHECKSUM, BENCHMARK)}
    receipt = {
        "operation": "Promote the exact successful CI artifact without rebuilding; leave the release draft.",
        "repository": REPOSITORY, "nativeCommit": TARGET, "testedMergeCommit": TESTED_MERGE,
        "testedTree": TARGET_TREE, "coreCommit": CORE, "ciRunId": RUN_ID,
        "ciRunUrl": f"https://github.com/{REPOSITORY}/actions/runs/{RUN_ID}",
        "artifact": {"id": ARTIFACT_ID, "name": "native-outlook-candidate", "size": ARTIFACT_SIZE, "sha256": ARTIFACT_SHA},
        "release": {"tag": TAG, "target": TARGET, "draft": True, "prerelease": True},
        "version": info["version"], "parityCases": info["parityCases"], "parityCasesByScope": info["parityCasesByScope"],
        "modelSha256": MODEL_SHA, "onnxRuntimeSha256": ORT_SHA,
        "policySourceSha256": POLICY_SHA, "metadataModuleSourceSha256": METADATA_MODULE_SHA, "assets": assets,
        "physicalOutlookAcceptance": "Unverified; hosted CI is not Outlook COM acceptance."
    }
    (directory / RECEIPT).write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
    return [directory / name for name in (EXE, CHECKSUM, RECEIPT, BENCHMARK)]


def verify_tag():
    ref = api(f"git/ref/tags/{TAG}", optional=True)
    if ref is None:
        return
    obj = ref["object"]
    for _ in range(4):
        if obj["type"] == "commit":
            require(obj["sha"] == TARGET, "Existing tag points to another candidate; never overwrite it.")
            return
        require(obj["type"] == "tag", "Unsupported existing tag target.")
        obj = api(f"git/tags/{obj['sha']}")["object"]
    raise RuntimeError("Cannot resolve existing release tag safely.")


def require_draft(release):
    require(release["tag_name"] == TAG and release["target_commitish"] == TARGET
            and release["draft"] is True and release["prerelease"] is True,
            "Only the exact candidate's draft prerelease may receive assets.")


def draft_release():
    verify_tag()
    release = api(f"releases/tags/{TAG}", optional=True)
    if release is None:
        release = api("releases", "POST", {
            "tag_name": TAG, "target_commitish": TARGET, "name": "LocalCorrect native Outlook RC6",
            "body": "Draft artifact transfer from the successful native CI run. Owner verification and publication are pending.",
            "draft": True, "prerelease": True, "generate_release_notes": False
        })
    require_draft(release)
    return release["id"]


def assets_for(release_id):
    require_draft(api(f"releases/{release_id}"))
    assets = []
    page = 1
    while True:
        batch = api(f"releases/{release_id}/assets?per_page=100&page={page}")
        assets.extend(batch)
        if len(batch) < 100:
            return assets
        page += 1


def matching_asset(asset, path):
    return asset["state"] == "uploaded" and asset["size"] == path.stat().st_size and asset.get("digest") == "sha256:" + file_digest(path)


def upload(release_id, path):
    require(path.name in EXPECTED_NAMES, "Unowned release asset name.")
    matches = [asset for asset in assets_for(release_id) if asset["name"] == path.name]
    require(len(matches) <= 1, "Duplicate release asset names.")
    if matches:
        asset = matches[0]
        if matching_asset(asset, path):
            print(f"Already verified: {path.name}")
            return
        require(asset["state"] == "starter", "Existing uploaded asset has another digest; refusing to overwrite it.")
        api(f"releases/assets/{asset['id']}", "DELETE")
    subprocess.run(["gh", "release", "upload", TAG, str(path), "--repo", REPOSITORY], check=True, timeout=900)
    for _ in range(10):
        matches = [asset for asset in assets_for(release_id) if asset["name"] == path.name]
        if len(matches) == 1 and matching_asset(matches[0], path):
            print(f"Uploaded and server digest verified: {path.name}")
            return
        time.sleep(2)
    raise RuntimeError(f"Release server digest did not verify: {path.name}")


def main():
    guard_execution()
    jobs = verify_ci()
    with tempfile.TemporaryDirectory(prefix="native-rc6-promotion-") as temporary:
        paths = prepare_assets(Path(temporary))
        release_id = draft_release()
        for path in paths:
            upload(release_id, path)
        server = assets_for(release_id)
        for path in paths:
            matches = [asset for asset in server if asset["name"] == path.name]
            require(len(matches) == 1 and matching_asset(matches[0], path), "Final release asset verification failed.")
    print(f"Verified pinned native CI jobs: {', '.join(jobs)}")
    print(f"Draft prerelease {TAG} contains verified assets. Publication remains manual.")


if __name__ == "__main__":
    main()
