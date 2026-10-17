#!/usr/bin/env python3
"""Browser-QA identity. Credentials arrive on SSH stdin; API token stays remote.

Only a numeric user ID is returned. Caller must delete the fixture after QA.
"""
import json
import base64
import re
import secrets
import sys
import requests
from run_with_ephemeral_admin import container_environment


def main() -> None:
    bootstrap = len(sys.argv) in (2, 3)
    if len(sys.argv) == 3 and sys.argv[2] not in ('admin', 'ordinary'):
        raise RuntimeError('Unexpected browser fixture role.')
    administrator = len(sys.argv) != 3 or sys.argv[2] != 'ordinary'
    data = {
        "action": "create", "username": "whaledeck-browser-" + secrets.token_hex(8),
        "password": "Wd!" + secrets.token_urlsafe(20) + "9Aa",
    } if bootstrap else json.load(sys.stdin)
    username = data["username"]
    if not re.fullmatch(r"whaledeck-browser-[a-f0-9]{16}", username):
        raise RuntimeError("Unexpected fixture identity.")
    values = container_environment()
    api = "http://127.0.0.1:8081/api/v3/core/users/"
    client = requests.Session()
    client.trust_env = False
    client.headers["Authorization"] = "Bearer " + values["Authentication__Authentik__ApiToken"]
    if data["action"] == "create":
        response = client.post(api, json={
            "username": username, "name": "Whale Deck browser QA", "is_active": True,
            "groups": [values["Authentication__Authentik__AdministratorGroupId"]] if administrator else [],
        }, timeout=20)
        response.raise_for_status()
        user_id = response.json()["pk"]
        try:
            response = client.post(api + f"{user_id}/set_password/", json={"password": data["password"]}, timeout=20)
            response.raise_for_status()
        except Exception:
            client.delete(api + f"{user_id}/", timeout=20)
            raise
        if bootstrap:
            # For browser runtimes without native SSH child-process support,
            # return only an RSA-OAEP encrypted transient credential envelope.
            # The private key exists solely in the caller's browser-QA memory.
            from cryptography.hazmat.primitives import hashes, serialization
            from cryptography.hazmat.primitives.asymmetric import padding
            key = serialization.load_der_public_key(base64.b64decode(sys.argv[1], validate=True))
            payload = json.dumps({"userId": user_id, "username": username, "password": data["password"]}).encode()
            encrypted = key.encrypt(payload, padding.OAEP(mgf=padding.MGF1(hashes.SHA256()), algorithm=hashes.SHA256(), label=None))
            print(base64.b64encode(encrypted).decode())
        else:
            print(json.dumps({"userId": user_id}))
    elif data["action"] == "delete":
        user_id = int(data["userId"])
        response = client.get(api + f"{user_id}/", timeout=20)
        if response.status_code == 404:
            return
        response.raise_for_status()
        if response.json()["username"] != username:
            raise RuntimeError("Fixture identity does not match.")
        response = client.delete(api + f"{user_id}/", timeout=20)
        response.raise_for_status()
        print("Browser fixture deleted.")
    else:
        raise RuntimeError("Unknown fixture action.")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        raise SystemExit("Browser fixture operation failed.")
