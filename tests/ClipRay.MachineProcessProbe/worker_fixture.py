import json
import sys
import time
import os

request = json.loads(sys.stdin.readline())
mode = request["texts"][0]
if len(request["texts"]) > 1:
    with open(request["texts"][1], "w") as report:
        report.write(str(os.getpid()))
if mode == "stderr":
    sys.stderr.write("x" * (1024 * 1024))
    sys.stderr.flush()
elif mode in ("cancel", "timeout"):
    time.sleep(30)
elif mode == "invalid":
    sys.stdout.write("{\n")
    sys.stdout.flush()
    sys.exit(0)
sys.stdout.buffer.write(json.dumps({"texts": ["显示更多"]}, ensure_ascii=False).encode("utf-8") + b"\n")
sys.stdout.buffer.flush()
