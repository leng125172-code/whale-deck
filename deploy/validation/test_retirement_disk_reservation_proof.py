"""Keep hard caps and full reservation while allowing host extent metadata."""
import copy
import unittest
from validate_workspace_retirement_business_installed import require_disks_preserved


class DiskProofTests(unittest.TestCase):
    def facts(self):
        return dict(runtime=dict(bytes=120000000000, reservedBytes=120000045056, inode=41,
            filesystemBytes=119999000000, mount=dict(uuid='fixed', options='rw,prjquota', source='fixed-device')),
            archive=dict(bytes=300000000000, reservedBytes=300000040960, inode=42,
            filesystemBytes=299999000000, mount=dict(uuid='other-fixed', options='rw', source='other-device')))

    def test_exact_state_and_additional_extent_metadata_preserve_caps(self):
        old = self.facts(); require_disks_preserved(old, copy.deepcopy(old))
        new = copy.deepcopy(old); new['runtime']['reservedBytes'] += 4096
        require_disks_preserved(old, new)

    def test_reservation_loss_even_above_cap_is_not_accepted(self):
        old = self.facts(); new = copy.deepcopy(old); new['runtime']['reservedBytes'] -= 4096
        with self.assertRaises(AssertionError): require_disks_preserved(old, new)

    def test_initial_under_reservation_and_malformed_values_are_rejected(self):
        for value in (119999999999, True, '120000045056'):
            old = self.facts(); old['runtime']['reservedBytes'] = value
            with self.subTest(value=value), self.assertRaises(AssertionError):
                require_disks_preserved(old, copy.deepcopy(old))

    def test_larger_cap_new_inode_mount_or_filesystem_capacity_never_passes(self):
        for key, value in [('bytes', 120000000001), ('inode', 99), ('filesystemBytes', 120000000001),
            ('mount', dict(uuid='changed', options='rw', source='different'))]:
            old = self.facts(); new = copy.deepcopy(old); new['runtime'][key] = value
            with self.subTest(key=key), self.assertRaises(AssertionError): require_disks_preserved(old, new)

    def test_missing_or_extra_disk_is_rejected(self):
        old = self.facts(); new = copy.deepcopy(old); del new['archive']
        with self.assertRaises(AssertionError): require_disks_preserved(old, new)


if __name__ == '__main__': unittest.main()
