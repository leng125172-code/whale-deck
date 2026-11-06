import copy
import unittest
from archive_release_proof import verify, KEY, LEDGER, CONFIG, APPS

TARGET = 'f' * 40


def fixture():
    core = {'/database-platform-' + str(i): [str(i), 'image', 'running'] for i in range(8)}
    core.update({name: [name, 'old', 'running'] for name in APPS})
    paths = CONFIG | {LEDGER, '/etc/sudoers.d/90-whaledeck-development-user', '/data/GitRepos/whale-deck/.env'}
    before = dict(core=core, health={name: 'healthy' for name in core}, hosts={name: {'pid': i + 1, 'sourceRevision': 'a' * 40} for i, name in enumerate(('agent', 'maintenance'))},
                  protected={p: {'identity': [1, 2, 33184, 0, 0, 1, 32, 8, 9], 'sha256': 'A' * 64} for p in paths},
                  archiveRoot={'id.json': 'immutable'}, recycleHdd={'old.data': 'immutable'}, business={'old': 'hash'})
    after = copy.deepcopy(before)
    for name in APPS:
        after['core'][name] = [name + '-new', 'new', 'running']
    for value in after['hosts'].values():
        value.update(pid=value['pid'] + 10, sourceRevision=TARGET)
    after['protected'][LEDGER]['sha256'] = 'B' * 64
    after['protected'][KEY]['identity'][8] += 1
    return before, after


class ArchiveReleaseProofTests(unittest.TestCase):
    def test_boolean_cannot_masquerade_as_an_unchanged_numeric_fact(self):
        before, after = fixture()
        before['business']['count'] = 0; after['business']['count'] = False
        with self.assertRaises(AssertionError): verify(before, after, TARGET)

    def test_only_standard_release_delta_passes(self):
        before, after = fixture()
        self.assertEqual(3, verify(before, after, TARGET)['applicationContainersReplaced'])

    def test_root_hdd_business_and_database_changes_are_rejected(self):
        for key in ('archiveRoot', 'recycleHdd', 'business', 'core', 'health'):
            with self.subTest(key=key):
                before, after = fixture()
                if key == 'core':
                    after[key]['/database-platform-0'][0] = 'changed'
                elif key == 'health':
                    after[key]['/database-platform-0'] = 'unhealthy'
                else:
                    after[key]['extra'] = 'changed'
                with self.assertRaises(AssertionError): verify(before, after, TARGET)

    def test_key_and_unrelated_private_metadata_are_never_relaxed(self):
        for path, index in ((KEY, 1), (KEY, 7), ('/etc/sudoers.d/90-whaledeck-development-user', 8)):
            before, after = fixture(); after['protected'][path]['identity'][index] += 1
            with self.assertRaises(AssertionError): verify(before, after, TARGET)
        before, after = fixture(); after['protected'][KEY]['sha256'] = 'C' * 64
        with self.assertRaises(AssertionError): verify(before, after, TARGET)


if __name__ == '__main__': unittest.main()
