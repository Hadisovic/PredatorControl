"""Redacted source/history scanner. Python 3.10+, Git; no third-party packages."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

EXCLUDED = {".git", "node_modules", "bin", "obj", "dist", ".venv", "venv", "publish", "win-x64", "runtimes"}
TEXT_EXTENSIONS = {".cs", ".csproj", ".json", ".md", ".txt", ".ts", ".tsx", ".js", ".mjs", ".css", ".html", ".xml", ".manifest", ".ps1", ".bat", ".cmd", ".py", ".yml", ".yaml", ".sh", ".ini", ".config", ".env", ".key", ".secret", ".token", ".pem"}
MAX_BYTES = 2 * 1024 * 1024
RULES = {
    "private-key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----"),
    "github-token": re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{40,255})\b"),
    "cloud-access-key": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "provider-token": re.compile(r"\b(?:sk-(?:proj-)?[A-Za-z0-9_-]{20,}|xox[baprs]-[A-Za-z0-9-]{20,})\b"),
    "credential-assignment": re.compile(r"(?i)[\"']?\b(?:password|passwd|pwd|api[_-]?key|client[_-]?secret|access[_-]?token|auth[_-]?token)\b[\"']?\s*[:=]\s*[\"']([^\"'\r\n]{4,})[\"']"),
    "connection-password": re.compile(r"(?i)\b(?:password|pwd)\s*=\s*([^;\s\"']{4,})"),
    "url-credentials": re.compile(r"(?i)https?://[^\s/:'\"]+:[^\s/@'\"]+@"),
}
INVENTORY = {
    "endpoint": re.compile(r"https?://[^\s\"'<>]+"),
    "machine-path": re.compile(r"(?i)\b[A-Z]:[\\/](?:Users|Windows|Program Files|ProgramData)[^\r\n\"']*"),
    "executable": re.compile(r"(?i)\b[\w.-]+\.(?:exe|dll)\b"),
    "constant": re.compile(r"\bconst\s+(?:string|int|uint|byte|ushort|long)\s+\w+\s*="),
}
PLACEHOLDER = re.compile(r"(?i)^(?:example|sample|test|dummy|fake|placeholder|changeme|your[_ -]|<|\$\{|%|Environment\.|process\.env)")


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], stderr=subprocess.PIPE)


def eligible(path):
    p = Path(path)
    return not any(part in EXCLUDED or part.startswith("publish_") for part in p.parts) and (p.suffix.lower() in TEXT_EXTENSIONS or p.name in {".gitignore", "pre-commit"} or p.name.startswith(".env"))


def inspect(path, data, source, exceptions):
    findings, inventory = [], []
    text = data.decode("utf-8", errors="replace")
    for number, line in enumerate(text.splitlines(), 1):
        fingerprint = hashlib.sha256(line.encode()).hexdigest()
        for rule, pattern in RULES.items():
            matches = list(pattern.finditer(line))
            if rule in {"credential-assignment", "connection-password"}:
                matches = [m for m in matches if not PLACEHOLDER.match(m.group(1))]
            if matches and (rule, path, fingerprint) not in exceptions:
                findings.append(dict(source=source, path=path, line=number, rule=rule, fingerprint=fingerprint))
        if source == "worktree":
            for kind, pattern in INVENTORY.items():
                if pattern.search(line): inventory.append(dict(path=path, line=number, kind=kind))
    return findings, inventory


def history_blobs(root):
    objects = git(root, "rev-list", "--objects", "--all").decode().splitlines()
    process = subprocess.Popen(["git", "-C", str(root), "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    try:
        for entry in objects:
            oid, _, path = entry.partition(" ")
            if not path or not eligible(path): continue
            process.stdin.write((oid + "\n").encode()); process.stdin.flush()
            header = process.stdout.readline().split()
            if len(header) != 3: raise ValueError("Could not inspect Git object")
            size = int(header[2])
            if size > MAX_BYTES or header[1] != b"blob":
                left = size
                while left:
                    chunk = process.stdout.read(min(left, 65536))
                    if not chunk: raise ValueError("Incomplete Git object")
                    left -= len(chunk)
                process.stdout.read(1)
                yield path, None, oid
            else:
                data = process.stdout.read(size); process.stdout.read(1)
                if len(data) != size: raise ValueError("Incomplete Git object")
                yield path, data, oid
    finally:
        process.stdin.close(); process.stdout.close()
        process.wait()


def scan(root, history=False, exception_file=None):
    exceptions = set()
    if exception_file and exception_file.exists():
        for item in json.loads(exception_file.read_text(encoding="utf-8-sig")):
            if not item.get("reason", "").strip(): raise ValueError("Every exception requires a reason")
            if item["rule"] not in RULES and item["rule"] != "private-artifact": raise ValueError("Unknown exception rule")
            if not re.fullmatch(r"[a-f0-9]{64}", item["fingerprint"]): raise ValueError("Invalid exception fingerprint")
            exceptions.add((item["rule"], item["path"], item["fingerprint"]))
    findings, inventory, skipped = [], [], []
    paths = set(git(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard").decode().split("\0"))
    for path in sorted(paths):
        if not path or not eligible(path): continue
        file = root / path
        if re.search(r"(?i)(?:_EXPORT\.md$|TRANSCRIPT.*\.md$|CONVERSATION.*\.md$|\.(?:secret|token|env)$)", path):
            fingerprint = hashlib.sha256(path.encode()).hexdigest()
            if ("private-artifact", path, fingerprint) not in exceptions:
                findings.append(dict(source="worktree", path=path, line=1, rule="private-artifact", fingerprint=fingerprint))
        if not file.exists(): continue  # tracked deletion
        if file.is_symlink(): raise ValueError("Refusing symlink: " + path)
        if file.stat().st_size > MAX_BYTES: skipped.append(path); continue
        data = file.read_bytes()
        if b"\0" in data: skipped.append(path); continue
        found, constants = inspect(path, data, "worktree", exceptions)
        findings.extend(found); inventory.extend(constants)
    if history:
        for path, data, oid in history_blobs(root):
            if data is None or b"\0" in data: skipped.append(oid + ":" + path); continue
            found, _ = inspect(path, data, oid, exceptions)
            findings.extend(found)
    return dict(findings=findings, inventory=inventory, skipped=skipped, history=history)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--path", type=Path, default=Path.cwd())
    parser.add_argument("--history", action="store_true", help="Also scan reachable history across all fetched refs")
    parser.add_argument("--exceptions", type=Path, help="Exact rule/path/line-fingerprint exceptions with reasons")
    parser.add_argument("--output", type=Path, help="Write redacted JSON findings and constant inventory")
    args = parser.parse_args()
    try:
        root = Path(git(args.path.resolve(), "rev-parse", "--show-toplevel").decode().strip())
        result = scan(root, args.history, args.exceptions or root / ".secret-scan-exceptions.json")
        if args.output: args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
        for finding in result["findings"]:
            print("{source}:{path}:{line}: {rule} [REDACTED] fingerprint={fingerprint}".format(**finding))
        print(f"{len(result['findings'])} secret findings; {len(result['inventory'])} hardcoded-value locations; {len(result['skipped'])} binary/large objects skipped.")
        return 1 if result["findings"] else 0
    except (OSError, subprocess.CalledProcessError, ValueError, KeyError) as error:
        # Never echo subprocess output, which may contain source text.
        print("Scan failed: " + type(error).__name__, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
