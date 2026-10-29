"""Fixed copy-helper boundary checks; invalid invocations never start services."""
import os
from pathlib import Path
import subprocess
import unittest

HOST = Path(__file__).resolve().parents[1] / 'host'


class MoveProfileTests(unittest.TestCase):
    def test_copy_budget_does_not_expand_existing_directory_budget(self):
        source = (HOST / 'whaledeck-privileged').read_text()
        copy = source.split('  container-file-moves)\n', 1)[1].split('    ;;', 1)[0]
        directory = source.split('  container-file-directories)\n', 1)[1].split('    ;;', 1)[0]
        for expected in ('RuntimeMaxSec=640', 'MemoryMax=384M', 'LimitNOFILE=128', 'LimitFSIZE=17179869184',
                         'BindsTo=whaledeck-agent.service', 'RestrictAddressFamilies=AF_UNIX', 'ProtectSystem=strict',
                         'KillMode=control-group', 'Restart=no', 'ReadWritePaths=/var/lib/whaledeck-container-moves /data/WhaleDeck/apps',
                         '/usr/bin/python3 -I /usr/local/libexec/whaledeck-container-move-transport.py "$2"'):
            self.assertIn(expected, copy)
        for preserved in ('RuntimeMaxSec=35', 'MemoryMax=128M', 'LimitNOFILE=64', 'LimitFSIZE=65536'):
            self.assertIn(preserved, directory)
        self.assertNotIn('docker.sock', copy); self.assertNotIn('sh -c', copy)

    @unittest.skipUnless(os.name == 'posix' and hasattr(os, 'geteuid') and os.geteuid() == 0, 'requires Linux root for helper argument rejection')
    def test_invalid_commands_units_invocations_and_extra_paths_are_rejected_before_systemd(self):
        valid = 'a' * 32
        for args in (('container-file-moves', 'shell', valid), ('container-file-moves', 'apply;true', valid),
                     ('container-file-moves', 'apply', '../unit'), ('container-file-moves', 'apply', valid, '/etc'),
                     ('container-file-moves-stop', valid, '../other'), ('container-file-moves-stop', valid.upper(), valid),
                     ('container-file-moves-stop', valid, valid, '/etc')):
            with self.subTest(args=args):
                result = subprocess.run(['bash', str(HOST / 'whaledeck-privileged'), *args], capture_output=True, timeout=5)
                self.assertEqual(2, result.returncode); self.assertEqual(b'', result.stdout)


if __name__ == '__main__': unittest.main()
