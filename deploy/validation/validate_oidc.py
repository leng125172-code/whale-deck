#!/usr/bin/env python3
"""Exercise the real Whale Deck -> Authentik OIDC browser flow.

Credentials are accepted only through environment variables and are never
printed.  The script keeps all cookies in memory and reports stage names and
HTTP outcomes suitable for workstation acceptance logs.
"""

from __future__ import annotations

import html.parser
import os
import re
import sys
import time
from dataclasses import dataclass, field
from urllib.parse import parse_qs, urljoin, urlparse

import requests


REDIRECT_STATUSES = {301, 302, 303, 307, 308}


@dataclass
class FormPostParser(html.parser.HTMLParser):
    action: str | None = None
    fields: dict[str, str] = field(default_factory=dict)

    def __post_init__(self) -> None:
        super().__init__()

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "form" and values.get("method", "").lower() == "post":
            self.action = values.get("action")
        elif tag == "input" and values.get("name"):
            self.fields[values["name"]] = values.get("value", "")


def required_environment(name: str) -> str:
    value = os.environ.get(name, "")
    if not value:
        raise RuntimeError(f"Missing required environment variable: {name}")
    return value


def csrf_headers(session: requests.Session) -> dict[str, str]:
    headers = {"Accept": "application/json", "Content-Type": "application/json"}
    for cookie in session.cookies:
        if "csrf" in cookie.name.lower():
            headers["X-authentik-CSRF"] = cookie.value
            headers["X-CSRFToken"] = cookie.value
            break
    return headers


def follow_redirects(session: requests.Session, response: requests.Response) -> requests.Response:
    for _ in range(16):
        if response.status_code not in REDIRECT_STATUSES:
            return response
        target = urljoin(response.url, response.headers["Location"])
        response = session.get(target, allow_redirects=False, timeout=20)
    raise RuntimeError("Redirect limit exceeded")


def solve_flow(session: requests.Session, flow_url: str, username: str, password: str) -> str:
    parsed = urlparse(flow_url)
    print(f"Flow query keys: {sorted(parse_qs(parsed.query))}")
    slug = parsed.path.rstrip("/").split("/")[-1]
    executor = f"{parsed.scheme}://{parsed.netloc}/api/v3/flows/executor/{slug}/"
    executor_parameters = {"query": parsed.query}

    response = session.get(
        executor,
        params=executor_parameters,
        headers={"Accept": "application/json"},
        timeout=20,
    )
    response.raise_for_status()
    challenge = response.json()

    for step in range(20):
        component = challenge.get("component")
        print(f"OIDC stage {step + 1}: {component}")
        if challenge.get("response_errors"):
            raise RuntimeError(f"Authentik rejected stage {component}: {challenge['response_errors']}")

        if component == "ak-stage-identification":
            payload = {"component": component, "uid_field": username}
        elif component == "ak-stage-password":
            payload = {"component": component, "password": password}
        elif component in {"ak-stage-user-login", "ak-stage-dummy"}:
            payload = {"component": component}
        elif component == "ak-stage-autosubmit":
            target = challenge.get("url")
            if not target:
                raise RuntimeError("Authentik autosubmit stage has no destination")
            response = session.post(
                urljoin(flow_url, target),
                data=challenge.get("attrs") or {},
                allow_redirects=False,
                timeout=20,
            )
            if response.status_code not in REDIRECT_STATUSES:
                raise RuntimeError(f"OIDC callback returned HTTP {response.status_code}")
            return urljoin(response.url, response.headers["Location"])
        elif component == "ak-stage-prompt":
            payload = {"component": component}
            ignored_types = {"static", "separator", "alert-info", "alert-warning", "alert-danger"}
            print(
                "Prompt fields:",
                [
                    {
                        "key": prompt.get("field_key"),
                        "type": prompt.get("type"),
                        "required": prompt.get("required"),
                        "choices": [choice.get("value") for choice in (prompt.get("choices") or [])],
                    }
                    for prompt in challenge.get("fields", [])
                ],
            )
            for prompt in challenge.get("fields", []):
                field_key = prompt.get("field_key")
                prompt_type = prompt.get("type", "")
                if not field_key or prompt_type in ignored_types:
                    continue
                initial = prompt.get("initial_value", "")
                choices = prompt.get("choices") or []
                if choices:
                    selected = next(
                        (choice.get("value") for choice in choices if choice.get("value") == initial),
                        choices[0].get("value", ""),
                    )
                    payload[field_key] = selected
                elif prompt_type == "checkbox":
                    if initial:
                        payload[field_key] = "on"
                else:
                    payload[field_key] = initial
        elif component == "ak-stage-consent":
            token = challenge.get("token")
            if not token:
                raise RuntimeError("Authentik consent stage has no token")
            payload = {"component": component, "token": token}
        elif component == "xak-flow-redirect":
            target = challenge.get("to")
            if not target:
                raise RuntimeError("Authentik redirect stage has no destination")
            target_parts = urlparse(target)
            print(
                f"Authentik redirect: {target_parts.path}; "
                f"query keys: {sorted(parse_qs(target_parts.query))}"
            )
            return urljoin(flow_url, target)
        else:
            raise RuntimeError(f"Unsupported Authentik stage: {component}")

        response = session.post(
            executor,
            params=executor_parameters,
            json=payload,
            headers=csrf_headers(session),
            timeout=20,
        )
        response.raise_for_status()
        challenge = response.json()

    raise RuntimeError("Authentik stage limit exceeded")


def authenticate(
    base_url: str,
    username: str,
    password: str,
    *,
    require_administrator: bool = True,
) -> tuple[requests.Session, dict[str, object]]:
    session = requests.Session()
    # Workstation shell profiles may point HTTP(S)_PROXY at a local desktop
    # proxy.  Acceptance traffic is entirely local and must never depend on it.
    session.trust_env = False
    session.headers["User-Agent"] = "WhaleDeck-OIDC-Validation/1.0"

    deadline = time.monotonic() + 60
    while True:
        try:
            health = session.get(f"{base_url}/health/live", timeout=5)
            if health.status_code == 200:
                break
        except requests.RequestException:
            pass
        if time.monotonic() >= deadline:
            raise RuntimeError("Whale Deck did not become ready within 60 seconds")
        time.sleep(1)

    response = session.get(
        f"{base_url}/api/v1/auth/login?returnUrl=%2F",
        allow_redirects=False,
        timeout=20,
    )
    if response.status_code not in REDIRECT_STATUSES:
        raise RuntimeError(f"Whale Deck login returned HTTP {response.status_code}")

    response = follow_redirects(session, response)
    if "/if/flow/" not in response.url:
        raise RuntimeError(f"Expected Authentik flow, got {urlparse(response.url).path}")

    next_url = solve_flow(session, response.url, username, password)
    for _ in range(8):
        response = session.get(next_url, allow_redirects=False, timeout=20)
        if response.status_code in REDIRECT_STATUSES:
            next_url = urljoin(response.url, response.headers["Location"])
            if "/if/flow/" in next_url:
                response = session.get(next_url, timeout=20)
                next_url = solve_flow(session, response.url, username, password)
            continue

        parser = FormPostParser()
        parser.feed(response.text)
        if parser.action:
            response = session.post(
                urljoin(response.url, parser.action),
                data=parser.fields,
                allow_redirects=False,
                timeout=20,
            )
            next_url = urljoin(response.url, response.headers.get("Location", "/"))
            continue
        break
    else:
        raise RuntimeError("OIDC completion redirect limit exceeded")

    me = session.get(f"{base_url}/api/v1/auth/me", timeout=20)
    me.raise_for_status()
    profile = me.json()
    print(
        "Current user response:",
        {
            "keys": sorted(profile),
            "isAdministrator": profile.get("isAdministrator"),
            "administrator": profile.get("administrator"),
        },
    )
    if not profile.get("subject") or (require_administrator and not profile.get("isAdministrator")):
        raise RuntimeError("OIDC session does not have the expected authentication level")

    return session, profile


def main() -> int:
    base_url = os.environ.get("WHALEDECK_URL", "http://192.168.100.13:8080").rstrip("/")
    username = required_environment("VALIDATION_USERNAME")
    password = required_environment("VALIDATION_PASSWORD")
    session, _ = authenticate(base_url, username, password)

    containers = session.get(f"{base_url}/api/v1/containers", timeout=20)
    containers.raise_for_status()
    container_payload = containers.json()
    if not isinstance(container_payload, list):
        raise RuntimeError("Administrator container endpoint returned an unexpected payload")
    print(f"Administrator policy passed: {len(container_payload)} containers visible")

    metrics = session.get(f"{base_url}/api/v1/metrics", params={
        "kinds": ["network.receive", "network.send", "disk.read", "disk.write"], "take": 60,
    }, timeout=20)
    metrics.raise_for_status()
    history = metrics.json()
    physical_network = [item for item in history if item["kind"].startswith("network.") and
                        not re.match(r"^(lo|veth|docker|br-|virbr|cni|flannel|tun|tap)", item["deviceId"])]
    if not physical_network or {item["kind"] for item in physical_network} != {"network.receive", "network.send"}:
        raise RuntimeError("Live physical network history was displaced by old virtual interfaces.")
    if any(len(item["points"]) < 2 for item in physical_network):
        raise RuntimeError("Physical network history is not initialized.")
    print(f"Metrics history passed: {len(history)} active series, {len(physical_network)} physical network directions")

    csrf = session.get(f"{base_url}/api/v1/auth/csrf", timeout=20)
    csrf.raise_for_status()
    csrf_payload = csrf.json()
    header_name = csrf_payload.get("headerName")
    token = csrf_payload.get("token")
    if not header_name or not token:
        raise RuntimeError("Whale Deck did not issue an antiforgery token")

    rejected = session.post(
        f"{base_url}/api/v1/auth/logout",
        headers={"Origin": base_url},
        allow_redirects=False,
        timeout=20,
    )
    if rejected.status_code != 400:
        raise RuntimeError(f"Logout without CSRF token returned HTTP {rejected.status_code}, expected 400")

    logout = session.post(
        f"{base_url}/api/v1/auth/logout",
        headers={"Origin": base_url, header_name: token},
        allow_redirects=False,
        timeout=20,
    )
    if logout.status_code not in REDIRECT_STATUSES:
        raise RuntimeError(f"Authenticated logout returned HTTP {logout.status_code}")

    logout_target = urljoin(logout.url, logout.headers["Location"])
    if "/application/o/whaledeck-external/end-session/" not in logout_target:
        raise RuntimeError("Logout did not navigate to the expected OIDC provider.")
    if not parse_qs(urlparse(logout_target).query).get("id_token_hint"):
        raise RuntimeError("RP logout is missing its server-side ID token hint.")
    response = follow_redirects(session, logout)
    for _ in range(8):
        if "/if/flow/" not in response.url:
            break
        parsed = urlparse(response.url)
        slug = parsed.path.rstrip("/").split("/")[-1]
        executor = f"{parsed.scheme}://{parsed.netloc}/api/v3/flows/executor/{slug}/"
        stage = session.get(executor, params={"query": parsed.query}, timeout=20)
        stage.raise_for_status()
        challenge = stage.json()
        if challenge.get("component") != "xak-flow-redirect":
            raise RuntimeError(f"Unexpected logout stage: {challenge.get('component')}")
        target = urljoin(response.url, challenge["to"])
        response = follow_redirects(session, session.get(target, allow_redirects=False, timeout=20))
    if urlparse(response.url).path != "/login":
        raise RuntimeError("Logout did not return to the Whale Deck login page.")
    if session.get(f"{base_url}/api/v1/auth/me", timeout=20).status_code != 401:
        raise RuntimeError("Whale Deck session is still authenticated after logout.")
    identity_url = f"{urlparse(base_url).scheme}://{urlparse(base_url).hostname}:8081/api/v3/core/users/me/"
    if session.get(identity_url, timeout=20).status_code not in {401, 403}:
        raise RuntimeError("Authentik session is still authenticated after logout.")

    print("OIDC validation passed: administrator, Origin/CSRF, full Authentik logout and /login redirect verified")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:  # concise acceptance output; never dump cookies or credentials
        print(f"OIDC validation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
