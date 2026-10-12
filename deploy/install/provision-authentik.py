"""Idempotently provision Whale Deck in an existing Authentik instance.

Executed through ``ak shell`` by provision-authentik.sh.  Output is a private
environment fragment consumed by the installer; callers must redirect stdout
to a mode-0600 file and must never print it.
"""

import os
import secrets

from authentik.core.models import Application, Group, Token, User
from authentik.crypto.models import CertificateKeyPair
from authentik.flows.models import Flow
from authentik.providers.oauth2.models import OAuth2Provider, ScopeMapping


def require_flow(slug: str) -> Flow:
    return Flow.objects.get(slug=slug)


signing_key = CertificateKeyPair.objects.filter(name="authentik Internal JWT Certificate").first()
if signing_key is None:
    signing_key = CertificateKeyPair.objects.filter(private_key_data__isnull=False).order_by("name").first()
if signing_key is None:
    raise RuntimeError("No Authentik JWT signing certificate exists.")

scope_mappings = list(ScopeMapping.objects.filter(scope_name__in=["openid", "profile", "email"]))
if {mapping.scope_name for mapping in scope_mappings} != {"openid", "profile", "email"}:
    raise RuntimeError("Authentik default openid/profile/email scope mappings are incomplete.")


def ensure_provider(slug: str, address: str, callback: str, signout_callback: str):
    provider_name = f"Whale Deck {slug.title()}"
    redirect_uris = [
        {
            "matching_mode": "strict",
            "url": f"http://{address}:8080{callback}",
            "redirect_uri_type": "authorization",
        },
        {
            "matching_mode": "strict",
            "url": f"http://{address}:8080{signout_callback}",
            "redirect_uri_type": "logout",
        },
    ]
    if slug == "external":
        # The workstation may be reached through a PCIe NIC, USB Ethernet or a
        # replacement adapter. Keep the browser callback IP-based, but do not
        # couple the provider to addresses that can change with the network.
        redirect_uris.extend(
            [
                {
                    "matching_mode": "regex",
                    "url": r"^http://(?:[0-9]{1,3}\.){3}[0-9]{1,3}:8080/signin-oidc/external$",
                    "redirect_uri_type": "authorization",
                },
                {
                    "matching_mode": "regex",
                    "url": r"^http://(?:[0-9]{1,3}\.){3}[0-9]{1,3}:8080/signout-callback-oidc/external$",
                    "redirect_uri_type": "logout",
                },
            ]
        )
    provider = OAuth2Provider.objects.filter(name=provider_name).first()
    if provider is None:
        provider = OAuth2Provider(
            name=provider_name,
            client_id=secrets.token_urlsafe(32),
            client_secret=secrets.token_urlsafe(48),
        )
    provider.authentication_flow = None
    provider.authorization_flow = require_flow("default-provider-authorization-explicit-consent")
    # End the Authentik session too, not just this provider's application session.
    provider.invalidation_flow = require_flow("default-invalidation-flow")
    provider.client_type = "confidential"
    provider.grant_types = ["authorization_code", "refresh_token"]
    provider._redirect_uris = redirect_uris
    provider.include_claims_in_id_token = True
    # Whale Deck revalidates authorization through Authentik's management API.
    # A stable UUID subject can be resolved authoritatively; the one-way hashed
    # subject cannot be mapped back to a user and would silently remove admin
    # permissions after login.
    provider.sub_mode = "user_uuid"
    provider.issuer_mode = "per_provider"
    provider.signing_key = signing_key
    provider.save()
    provider.property_mappings.set(scope_mappings)

    application, _ = Application.objects.update_or_create(
        slug=f"whaledeck-{slug}",
        defaults={
            "name": provider_name,
            "provider": provider,
            "meta_launch_url": f"http://{address}:8080/api/v1/auth/login?returnUrl=%2F",
            "open_in_new_tab": False,
        },
    )
    if application.provider_id != provider.pk:
        application.provider = provider
        application.save(update_fields=["provider"])
    return provider


admin = User.objects.filter(username="akadmin", is_active=True).first()
if admin is None:
    admin = User.objects.filter(groups__is_superuser=True, is_active=True).order_by("pk").first()
if admin is None:
    raise RuntimeError("No active Authentik administrator exists.")

admin_group = Group.objects.filter(name="authentik Admins", is_superuser=True).first()
if admin_group is None:
    admin_group = Group.objects.filter(is_superuser=True).order_by("name").first()
if admin_group is None:
    raise RuntimeError("No Authentik superuser group exists.")
if not admin.groups.filter(pk=admin_group.pk).exists():
    admin.groups.add(admin_group)

api_token, _ = Token.objects.get_or_create(
    identifier="whaledeck-api",
    defaults={
        "user": admin,
        "intent": "api",
        "expiring": False,
        "description": "Whale Deck management API",
        "key": secrets.token_urlsafe(48),
    },
)
changed = False
if api_token.user_id != admin.pk:
    api_token.user = admin
    changed = True
if api_token.intent != "api" or api_token.expiring:
    api_token.intent = "api"
    api_token.expiring = False
    changed = True
if os.environ.get("WHALEDECK_ROTATE_API_TOKEN") == "true":
    api_token.key = secrets.token_urlsafe(48)
    changed = True
if changed:
    api_token.save()

internal = ensure_provider("internal", "192.168.22.19", "/signin-oidc/internal", "/signout-callback-oidc/internal")
external = ensure_provider("external", "192.168.100.13", "/signin-oidc/external", "/signout-callback-oidc/external")

values = {
    "WHALEDECK_OIDC_INTERNAL_AUTHORITY": "http://192.168.22.19:8081/application/o/whaledeck-internal/",
    "WHALEDECK_OIDC_INTERNAL_CLIENT_ID": internal.client_id,
    "WHALEDECK_OIDC_INTERNAL_CLIENT_SECRET": internal.client_secret,
    "WHALEDECK_OIDC_EXTERNAL_AUTHORITY": "http://192.168.100.13:8081/application/o/whaledeck-external/",
    "WHALEDECK_OIDC_EXTERNAL_CLIENT_ID": external.client_id,
    "WHALEDECK_OIDC_EXTERNAL_CLIENT_SECRET": external.client_secret,
    "WHALEDECK_AUTHENTIK_API_TOKEN": api_token.key,
    "WHALEDECK_AUTHENTIK_ADMIN_GROUP_ID": str(admin_group.pk),
}
for key, value in values.items():
    if "\n" in value or "\r" in value or "'" in value:
        raise RuntimeError(f"Unsafe generated value for {key}")
    print(f"{key}='{value}'")
