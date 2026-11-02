import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "secret_scan.py"
spec = importlib.util.spec_from_file_location("secret_scan", SCRIPT)
scanner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scanner)

class ScannerTests(unittest.TestCase):
    def test_secret_rules_and_redaction(self):
        token = "gh" + "p_" + "A" * 36
        source = ('api_key = "' + token + '"\n' + 'password = "' + 'actual' + 'credential123"\n' + '-----BEGIN ' + 'PRIVATE KEY-----').encode()
        findings, _ = scanner.inspect("app.cs", source, "worktree", set())
        rules = {f["rule"] for f in findings}
        self.assertTrue({"github-token", "credential-assignment", "private-key"} <= rules)
        self.assertNotIn(token, json.dumps(findings))
        self.assertNotIn("credential123", json.dumps(findings))

    def test_placeholders_constants_and_exact_exceptions(self):
        findings, inventory = scanner.inspect("app.cs", b'const int Register = 42;\npassword = "example-value";\nhttps://example.org/api', "worktree", set())
        self.assertEqual([], findings)
        self.assertEqual({"constant", "endpoint"}, {x["kind"] for x in inventory})
        data = ('pwd="' + 'real' + 'value123"').encode()
        findings, _ = scanner.inspect("app.cs", data, "worktree", set())
        f = findings[0]
        exceptions = {(f["rule"], f["path"], f["fingerprint"])}
        self.assertEqual([], scanner.inspect("app.cs", data, "worktree", exceptions)[0])
        self.assertTrue(scanner.inspect("other.cs", data, "worktree", exceptions)[0])

    def test_history_and_exclusions(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            def git(*args):
                return subprocess.check_output(["git", "-C", folder, *args], stderr=subprocess.PIPE)
            git("init", "-q")
            git("config", "user.name", "Scanner fixture")
            git("config", "user.email", "fixture@example.invalid")
            (root / "app.cs").write_text('access_token = "' + "gh" + "p_" + "B" * 36 + '"')
            git("add", "."); git("commit", "-qm", "synthetic fixture")
            (root / "app.cs").write_text('const int Register = 42;')
            (root / "node_modules").mkdir()
            (root / "node_modules" / "ignored.js").write_text('password="' + 'real' + 'value123"')
            self.assertEqual([], scanner.scan(root)["findings"])
            clean = subprocess.run([__import__("sys").executable, str(SCRIPT), "--path", folder], capture_output=True, text=True)
            self.assertEqual(0, clean.returncode)
            (root / "exceptions.json").write_text("not JSON")
            invalid = subprocess.run([__import__("sys").executable, str(SCRIPT), "--path", folder, "--exceptions", str(root / "exceptions.json")], capture_output=True, text=True)
            self.assertEqual(2, invalid.returncode)
            (root / "exceptions.json").unlink()
            result = scanner.scan(root, history=True)
            self.assertTrue(result["findings"])
            self.assertTrue(all(f["source"] != "worktree" for f in result["findings"]))
            self.assertFalse(any("node_modules" in f["path"] for f in result["findings"]))
            run = subprocess.run([__import__("sys").executable, str(SCRIPT), "--path", folder, "--history"], capture_output=True, text=True)
            self.assertEqual(1, run.returncode)
            self.assertNotIn("B" * 36, run.stdout)
            self.assertNotIn("B" * 36, run.stderr)

if __name__ == "__main__": unittest.main()
