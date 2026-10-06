"""Isolated loopback llama-server replay; never changes production settings."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import time
import urllib.request
import urllib.error

p = argparse.ArgumentParser(description=__doc__)
for name in ("server", "model", "source", "output"):
    p.add_argument("--" + name, type=Path, required=True)
p.add_argument("--repeat", type=int, choices=range(1, 4), default=1)
a = p.parse_args()
with a.model.open("rb") as f:
    model_hash = hashlib.file_digest(f, "sha256").hexdigest()
assert model_hash == "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597"
prompts = sorted(a.source.glob("*.prompt.txt"), key=lambda f: int(f.name.split('.')[0]))
assert prompts
a.output.mkdir(parents=True, exist_ok=False)
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
key = secrets.token_urlsafe(32)
env = os.environ.copy()
env["LLAMA_API_KEY"] = key
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
def request(path, body=None):
    req = urllib.request.Request(f"http://127.0.0.1:{port}" + path,
        data=None if body is None else json.dumps(body).encode(),
        headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    with opener.open(req, timeout=60) as response:
        return json.load(response)

report = {"modelSha256": model_hash, "scope": "loopback replay; semantic review required", "responses": []}
with (a.output / "runtime.log").open("wb") as log:
    process = subprocess.Popen([str(a.server), "--model", str(a.model), "--host", "127.0.0.1",
        "--port", str(port), "--no-webui", "--parallel", "1", "--ctx-size", "2048",
        "--gpu-layers", "20", "--cache-ram", "0"], env=env, stdin=subprocess.DEVNULL,
        stdout=log, stderr=log, creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    try:
        started = time.perf_counter()
        while True:
            if process.poll() is not None:
                raise RuntimeError("Server exited during startup")
            try:
                if request("/health").get("status") == "ok":
                    break
            except (OSError, ValueError):
                pass
            if time.perf_counter() - started > 45:
                raise TimeoutError("Server startup")
            time.sleep(.2)
        report["startupMs"] = round((time.perf_counter() - started) * 1000)
        try:
            opener.open(urllib.request.Request(f"http://127.0.0.1:{port}/v1/chat/completions",
                data=b'{}', headers={"Content-Type": "application/json"}), timeout=5).close()
            raise RuntimeError("Unauthenticated request unexpectedly accepted")
        except urllib.error.HTTPError as error:
            if error.code != 401:
                raise
            report["unauthenticatedRejected"] = True
        for iteration, prompt in ((iteration, prompt) for iteration in range(a.repeat) for prompt in prompts):
            started = time.perf_counter()
            result = request("/v1/chat/completions", {"messages": [{"role": "user", "content": prompt.read_text(encoding="utf-8-sig")}],
                "temperature": 0, "seed": 0, "max_tokens": 512, "stream": False, "cache_prompt": False})
            report["responses"].append({"iteration": iteration + 1, "prompt": prompt.name, "elapsedMs": round((time.perf_counter() - started) * 1000), "result": result})
    finally:
        if process.poll() is None:
            process.terminate()
        process.wait(timeout=10)
        report["serverExited"] = process.poll() is not None
        (a.output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print("Loopback replay finished; semantic review required.")
