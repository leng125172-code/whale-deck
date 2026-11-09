import hashlib
import importlib.util
import io
import ipaddress
from pathlib import Path
import socket
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('workspace_git', Path(__file__).resolve().parents[1] /
                                              'host' / 'whaledeck-development-git.py')
git = importlib.util.module_from_spec(spec)
spec.loader.exec_module(git)


class DevelopmentGitTests(unittest.TestCase):
    def test_exact_repository_and_credential_binding(self):
        origin = 'https://git.example.test/group/project.git'
        entry = {'url': origin, 'allowedCidrs': ['10.0.1.2/32'], 'credentialRef': 'account-a'}
        registry = {'schemaVersion': 1, 'repositories': {'approved': entry}}
        self.assertEqual('/group/project.git', git.repository(registry, 'approved')[0])
        for value in ('http://git.example.test/group/project.git', origin + '?q=1', origin + '/..',
                      'https://user@git.example.test/repo.git', 'https://127.0.0.1/repo.git',
                      'https://git.example.test/encoded%2fpath'):
            with self.subTest(value=value), self.assertRaisesRegex(git.GitSourceError, 'URL_INVALID'):
                git.repository({'schemaVersion': 1, 'repositories': {'approved': dict(entry, url=value)}}, 'approved')
        with self.assertRaisesRegex(git.GitSourceError, 'NOT_APPROVED'):
            git.repository(registry, 'other')
        with self.assertRaisesRegex(git.GitSourceError, 'CREDENTIAL_SCOPE'):
            git.authorization('account-a', {'ref': 'account-b'})
        with self.assertRaisesRegex(git.GitSourceError, 'CREDENTIAL_INVALID'):
            git.authorization('account-a', {'ref': 'account-a', 'username': 'qa', 'token': 'fixture\nother'})

    def test_destination_must_be_approved_before_pinning(self):
        private = (ipaddress.ip_network('10.0.1.2/32'),)
        addresses = [ipaddress.ip_address(value) for value in
                     ('127.0.0.1', '169.254.169.254', '224.0.0.1', '::ffff:10.0.1.2', '64:ff9b::a00:102')]
        for address in addresses:
            self.assertFalse(git.address_allowed(address, private))
        self.assertTrue(git.address_allowed(ipaddress.ip_address('10.0.1.2'), private))
        self.assertFalse(git.address_allowed(ipaddress.ip_address('10.0.1.3'), private))
        rows = [(socket.AF_INET, socket.SOCK_STREAM, socket.IPPROTO_TCP, '', ('10.0.1.2', 443)),
                (socket.AF_INET, socket.SOCK_STREAM, socket.IPPROTO_TCP, '', ('10.0.1.3', 443))]
        with patch.object(git.socket, 'getaddrinfo', return_value=rows):
            with self.assertRaisesRegex(git.GitSourceError, 'DESTINATION_NOT_APPROVED'):
                git.pinned_address('git.example.test', 443, private)
        with patch.object(git.socket, 'getaddrinfo', return_value=rows[:1]):
            self.assertEqual('10.0.1.2', git.pinned_address('git.example.test', 443, private))

    def test_export_preserves_bytes_modes_and_attributes_and_rejects_special_members(self):
        with tempfile.TemporaryDirectory() as folder:
            repo = Path(folder) / 'repo'
            subprocess.run([git.GIT, 'init', '--quiet', str(repo)], check=True)
            paths = {'keep.txt': b'$Format:%H$\n', 'hidden.txt': b'raw',
                     '.gitattributes': b'keep.txt export-subst\nhidden.txt export-ignore\n'}
            for name, data in paths.items():
                (repo / name).write_bytes(data)
            (repo / 'keep.txt').chmod(0o700)
            subprocess.run([git.GIT, '-C', str(repo), 'add', '--all'], check=True)
            subprocess.run([git.GIT, '-C', str(repo), '-c', 'user.name=fixture',
                            '-c', 'user.email=fixture@example.invalid', 'commit', '--quiet', '-m', 'fixture'], check=True)
            content = io.BytesIO()
            writer = git.LimitedArchive(content)
            self.assertEqual((3, sum(map(len, paths.values()))), git.export_tree(repo, repo / '.git', 'HEAD', writer))
            self.assertEqual(hashlib.sha256(content.getvalue()).hexdigest(), writer.digest.hexdigest())
            with tarfile.open(fileobj=io.BytesIO(content.getvalue())) as archive:
                for name, expected in paths.items():
                    self.assertEqual(expected, archive.extractfile(name).read())
                self.assertEqual(0o700, archive.getmember('keep.txt').mode)
            (repo / 'link').symlink_to('keep.txt')
            subprocess.run([git.GIT, '-C', str(repo), 'add', 'link'], check=True)
            subprocess.run([git.GIT, '-C', str(repo), '-c', 'user.name=fixture',
                            '-c', 'user.email=fixture@example.invalid', 'commit', '--quiet', '-m', 'link'], check=True)
            with self.assertRaisesRegex(git.GitSourceError, 'SPECIAL_OBJECT'):
                git.export_tree(repo, repo / '.git', 'HEAD', git.LimitedArchive(io.BytesIO()))


if __name__ == '__main__':
    unittest.main()
