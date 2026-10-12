"""Credential-free tests for guarded Authentik setup finalization."""

import contextlib
import io
import os
from pathlib import Path
import runpy
import types
import unittest
from unittest.mock import MagicMock, patch


SCRIPT = Path(__file__).resolve().parents[1] / "install" / "complete-authentik-setup.py"


class SetupFinalizationTests(unittest.TestCase):
    def setUp(self):
        self.setup = MagicMock()
        self.setup.get.return_value = False
        self.admin = MagicMock(is_superuser=True)
        self.admin.has_usable_password.return_value = True
        self.user = MagicMock()
        self.user.objects.filter.return_value.first.return_value = self.admin
        self.application = MagicMock()
        self.application.objects.filter.return_value.values_list.return_value = [
            "whaledeck-internal", "whaledeck-external",
        ]
        self.flow = MagicMock()
        self.flow.objects.filter.return_value.exists.return_value = True
        self.blueprint = MagicMock()
        tenant = MagicMock(schema_name="public")
        self.tenant = MagicMock()
        self.tenant.objects.filter.return_value = [tenant]
        self.modules = {
            "django.db": types.SimpleNamespace(transaction=types.SimpleNamespace(atomic=contextlib.nullcontext)),
            "django_tenants.utils": types.SimpleNamespace(
                get_public_schema_name=lambda: "public", schema_context=lambda _: contextlib.nullcontext(),
            ),
            "authentik.blueprints.models": types.SimpleNamespace(BlueprintInstance=self.blueprint),
            "authentik.core.apps": types.SimpleNamespace(Setup=self.setup),
            "authentik.core.models": types.SimpleNamespace(Application=self.application, User=self.user),
            "authentik.flows.models": types.SimpleNamespace(
                Flow=self.flow, FlowAuthenticationRequirement=types.SimpleNamespace(REQUIRE_SUPERUSER="require_superuser"),
            ),
            "authentik.tenants.models": types.SimpleNamespace(Tenant=self.tenant),
        }

    def run_script(self, apply=False):
        output = io.StringIO()
        with patch.dict("sys.modules", self.modules), patch.dict(os.environ, {
            "WHALEDECK_COMPLETE_SETUP": "true" if apply else "false",
        }), contextlib.redirect_stdout(output):
            runpy.run_path(str(SCRIPT), run_name="__main__")
        return output.getvalue()

    def test_dry_run_does_not_modify_state(self):
        self.assertIn('"changed": false', self.run_script())
        self.setup.set.assert_not_called()
        self.flow.objects.filter.return_value.update.assert_not_called()
        self.blueprint.objects.filter.return_value.update.assert_not_called()

    def test_completed_instance_is_idempotent(self):
        self.setup.get.return_value = True
        self.assertIn('"completed": true', self.run_script(apply=True))
        self.setup.set.assert_not_called()
        self.user.objects.filter.assert_not_called()

    def test_valid_instance_finalizes_without_changing_credentials(self):
        self.assertIn('"changed": true', self.run_script(apply=True))
        self.setup.set.assert_called_once()
        self.flow.objects.filter.return_value.update.assert_called_once_with(authentication="require_superuser")
        self.blueprint.objects.filter.return_value.update.assert_called_once_with(enabled=False)
        self.admin.set_password.assert_not_called()
        self.admin.save.assert_not_called()

    def test_missing_password_blocks_repair(self):
        self.admin.has_usable_password.return_value = False
        with self.assertRaisesRegex(RuntimeError, "administrator"):
            self.run_script(apply=True)
        self.setup.set.assert_not_called()

    def test_non_admin_blocks_repair(self):
        self.admin.is_superuser = False
        with self.assertRaisesRegex(RuntimeError, "administrator"):
            self.run_script(apply=True)
        self.setup.set.assert_not_called()

    def test_missing_oidc_application_blocks_repair(self):
        self.application.objects.filter.return_value.values_list.return_value = ["whaledeck-internal"]
        with self.assertRaisesRegex(RuntimeError, "provisioning"):
            self.run_script(apply=True)
        self.setup.set.assert_not_called()

    def test_missing_login_flow_blocks_repair(self):
        self.flow.objects.filter.return_value.exists.return_value = False
        with self.assertRaisesRegex(RuntimeError, "authentication flow"):
            self.run_script(apply=True)
        self.setup.set.assert_not_called()


if __name__ == "__main__":
    unittest.main()
