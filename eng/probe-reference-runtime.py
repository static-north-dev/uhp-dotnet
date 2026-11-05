#!/usr/bin/env python3
"""Exercise Codex behavior beyond the unmodified official UHP checks."""
import hashlib
import json
import os
import secrets
import sys
import urllib.error
import urllib.request


def probe(address, token):
    def call(method, path, body=None, expect=200, binary=False):
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(address + path, data, method=method, headers={
            "Authorization": "Bearer " + token,
            "Content-Type": "application/json",
        })
        try:
            response = urllib.request.urlopen(request, timeout=180)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            payload = response.read()
            if response.status != expect:
                raise AssertionError(f"{method} {path} returned {response.status}, expected {expect}")
            return payload if binary else json.loads(payload) if payload else {}

    def task(body):
        return call("POST", "/v1/responses", body)

    def output(response):
        return "\n".join(part["text"] for item in response["output"]
                         for part in item.get("content", []) if "text" in part)

    def delete_session(response):
        call("DELETE", "/v1/sessions/" + response["metadata"]["session_id"])

    question = task({"input": "Call ask_user immediately with kind secret and prompt 'Enter a test value'. "
                              "After the continuation, call use_secret with the supplied opaque reference, "
                              "then reply with only the exact SHA-256 fingerprint returned by that tool."})
    assert question["status"] == "incomplete"
    pending = next(item for item in question["output"] if item["type"] == "input_request")
    assert pending["kind"] == "secret"
    secret = secrets.token_urlsafe(36)
    fingerprint = hashlib.sha256(secret.encode()).hexdigest()
    answer = task({"previous_response_id": question["id"], "input": [
        {"type": "input_response", "input_request_id": pending["id"], "answer": secret},
        {"role": "user", "content": "Call use_secret now with the opaque reference, and reply with only its returned fingerprint."},
    ]})
    try:
        assert answer["status"] == "completed" and fingerprint in output(answer)
        stored = call("GET", "/v1/responses/" + answer["id"])
        assert all(secret not in json.dumps(value) for value in (question, answer, stored))
    finally:
        delete_session(answer)

    harness = call("GET", "/v1/harnesses")["harnesses"][0]["id"]
    harness_memories = f"/v1/harnesses/{harness}/memories"
    agent = call("GET", harness_memories)["principal"]
    memory = call("POST", "/v1/memories", {"name": "runtime probe", "restricted": True})
    route = "/v1/memories/" + memory["id"]
    try:
        call("POST", "/v1/responses", {"input": "must not start", "metadata": {"memory": memory["id"]}}, expect=404)
        call("POST", route + "/grants", {"principal": agent, "privileges": ["read", "write"]})
        call("PUT", harness_memories, {"default_memory_id": memory["id"], "observe": False})
        code = "standing-" + secrets.token_hex(8)
        call("POST", route + "/records", {"title": code, "attributes": {"standing": True}})
        marker = "remembered-" + secrets.token_hex(8)
        turn = task({"input": f"From the primed memory data, find the record title beginning with standing-. "
                              f"Call memory_remember with text '{marker}' and omit the memory ID to use the default. "
                              f"Then reply with the exact standing title. Do not call a tool to fetch the title."})
        try:
            assert turn["status"] == "completed" and code in output(turn)
            records = call("GET", route + "/records")["data"]
            written = next(record for record in records if any(part.get("text") == marker for part in record["content"]))
            assert written["written_by"]["kind"] + ":" + written["written_by"]["id"] == agent
        finally:
            delete_session(turn)
    finally:
        call("PUT", harness_memories, {"observe": False})
        call("DELETE", route)

    artifact = "artifact-" + secrets.token_hex(8)
    exported = task({"input": f"Use a shell command to write the exact text {artifact} to result.txt in the current workspace, "
                              "with no trailing newline. Then reply 'Artifact ready'."})
    try:
        assert exported["status"] == "completed"
        citations = [annotation for item in exported["output"] for part in item.get("content", [])
                     for annotation in part.get("annotations", [])]
        file = next(annotation for annotation in citations if annotation["filename"] == "result.txt")
        assert file["type"] == "container_file_citation" and file["container_id"].startswith("cntr_")
        assert file["download_url"].startswith(address + "/")
        download = file["download_url"][len(address):]
        assert call("GET", download, binary=True) == artifact.encode()
    finally:
        delete_session(exported)

    call("GET", download, expect=404)
    print("Runtime probes passed: private secret consumption, granted memory, and artifact citation/download/deletion.", flush=True)


if __name__ == "__main__":
    probe(sys.argv[1], os.environ["UHP_API_KEY"])
