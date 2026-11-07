"""Call the installed Micu MCP over its native STDIO protocol.

Usage: python Tools/micu_call.py request.json result.json
The request is a tools/call parameter object, or {"list_tools": true}.
Credentials remain in the server's environment/system credential store.
"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tomllib


def main():
    request = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
    config = tomllib.loads((Path.home() / ".codex/config.toml").read_text(encoding="utf-8"))
    server = config["mcp_servers"]["micu-image"]
    environment = os.environ.copy()
    environment.update(server.get("env", {}))
    with subprocess.Popen([server["command"], *server.get("args", [])], stdin=subprocess.PIPE,
                          stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                          text=True, encoding="utf-8", env=environment) as process:
        def send(message):
            process.stdin.write(json.dumps(message, ensure_ascii=False) + "\n")
            process.stdin.flush()

        def receive(identifier):
            for line in process.stdout:
                message = json.loads(line)
                if message.get("id") == identifier:
                    return message
            raise RuntimeError("MCP process ended before returning its result")

        send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "DGameAssetAuthoring", "version": "1.0"}}})
        initialized = receive(1)
        if "error" in initialized:
            raise RuntimeError(initialized["error"])
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})
        send({"jsonrpc": "2.0", "id": 2, "method": "tools/list" if request.get("list_tools") else "tools/call",
              "params": {} if request.get("list_tools") else request})
        result = receive(2)
        Path(sys.argv[2]).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        process.stdin.close()
        process.wait(timeout=10)
    print(json.dumps(result, ensure_ascii=False))
    return int("error" in result or result.get("result", {}).get("isError", False))


if __name__ == "__main__":
    sys.exit(main())
