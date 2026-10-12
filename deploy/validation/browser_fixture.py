#!/usr/bin/env python3
"""Browser-QA identity. Credentials arrive on SSH stdin; API token stays remote.

Only a numeric user ID is returned. Caller must delete the fixture after QA.
"""
import json
import re
import sys
import requests
from run_with_ephemeral_admin import container_environment


def main() -> None:
    data = json.load(sys.stdin)
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
            "groups": [values["Authentication__Authentik__AdministratorGroupId"]],
        }, timeout=20)
        response.raise_for_status()
        user_id = response.json()["pk"]
        try:
            response = client.post(api + f"{user_id}/set_password/", json={"password": data["password"]}, timeout=20)
            response.raise_for_status()
        except Exception:
            client.delete(api + f"{user_id}/", timeout=20)
            raise
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
