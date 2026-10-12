"""Inspect setup state without exposing credentials or changing Authentik."""

import json

from authentik.core.apps import Setup
from authentik.core.models import Application, User
from authentik.flows.models import Flow
from authentik.policies.models import PolicyBinding
from authentik.tenants.models import Tenant

for tenant in Tenant.objects.filter(ready=True):
    with tenant:
        admin = User.objects.filter(username="akadmin", is_active=True).first()
        flow = Flow.objects.filter(slug="initial-setup").first()
        bindings = PolicyBinding.objects.filter(target=flow).select_related("policy") if flow else []
        print("SETUP_STATE=" + json.dumps({
            "tenant": tenant.schema_name,
            "completed": Setup.get(tenant=tenant),
            "admin_has_password": bool(admin and admin.has_usable_password()),
            "admin_is_superuser": bool(admin and admin.is_superuser),
            "flow_authentication": flow.authentication if flow else None,
            "flow_policies": [binding.policy.name for binding in bindings if binding.policy],
            "whaledeck_apps": Application.objects.filter(slug__startswith="whaledeck-").count(),
        }))
