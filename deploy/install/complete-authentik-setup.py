"""Finalize an already provisioned instance, never bootstrap or reset credentials.

Run through ``ak shell --no-startup --no-imports``. Read-only by default;
WHALEDECK_COMPLETE_SETUP=true explicitly enables the guarded repair.
Only non-secret state is printed.
"""

import json
import os

from django.db import transaction
from django_tenants.utils import get_public_schema_name, schema_context

from authentik.blueprints.models import BlueprintInstance
from authentik.core.apps import Setup
from authentik.core.models import Application, User
from authentik.flows.models import Flow, FlowAuthenticationRequirement
from authentik.tenants.models import Tenant

apply = os.environ.get("WHALEDECK_COMPLETE_SETUP") == "true"

for tenant in Tenant.objects.filter(ready=True):
    with tenant:
        completed = bool(Setup.get(tenant=tenant))
        changed = False
        if not completed:
            admin = User.objects.filter(username="akadmin", is_active=True).first()
            if not admin or not admin.is_superuser or not admin.has_usable_password():
                raise RuntimeError("An active configured administrator is required; use initial setup.")
            expected_apps = {"whaledeck-internal", "whaledeck-external"}
            configured_apps = set(Application.objects.filter(
                slug__in=expected_apps, provider__isnull=False,
            ).values_list("slug", flat=True))
            if configured_apps != expected_apps:
                raise RuntimeError("Whale Deck OIDC provisioning must finish before completing setup.")
            if not Flow.objects.filter(slug="default-authentication-flow").exists():
                raise RuntimeError("The default authentication flow is missing.")
            if apply:
                # Match Authentik's PostSetupStageView finalization, preserving
                # the tenant's URL, all users, passwords, groups and providers.
                with transaction.atomic():
                    with schema_context(get_public_schema_name()):
                        BlueprintInstance.objects.filter(**{
                            "metadata__labels__blueprints.goauthentik.io/system-oobe": "true",
                        }).update(enabled=False)
                    Flow.objects.filter(slug="initial-setup").update(
                        authentication=FlowAuthenticationRequirement.REQUIRE_SUPERUSER,
                    )
                    Setup.set(True, tenant=tenant)
                completed = True
                changed = True
        print("AUTHENTIK_SETUP=" + json.dumps({
            "tenant": tenant.schema_name, "completed": completed, "changed": changed,
        }))
