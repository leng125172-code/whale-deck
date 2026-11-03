"""Candidate privilege bounds; invalid argv never reaches systemd or files."""
import os
from pathlib import Path
import re
import subprocess
import unittest

HOST=Path(__file__).resolve().parents[1]/'host'


class RecursiveAclProfileTests(unittest.TestCase):
    def test_recursive_scope_cannot_gain_mount_shell_or_unbounded_file_write(self):
        source=(HOST/'whaledeck-privileged').read_text()
        block=source.split('  container-file-recursive-acl)\n',1)[1].split('    ;;',1)[0]
        caps=re.search(r'CapabilityBoundingSet=([^"\n]+)',block)[1].split()
        self.assertEqual(set(caps),{'CAP_DAC_OVERRIDE','CAP_DAC_READ_SEARCH','CAP_FOWNER','CAP_SYS_PTRACE'})
        paths=re.search(r'ReadWritePaths=([^"\n]+)',block)[1].split()
        self.assertEqual(set(paths),{'/var/lib/whaledeck-container-recursive-acl','/data/WhaleDeck/apps','/data/DockerData/whaledeck/file-backups'})
        self.assertLessEqual(int(re.search(r'RuntimeMaxSec=(\d+)',block)[1]),340)
        self.assertLessEqual(int(re.search(r'LimitFSIZE=(\d+)',block)[1]),16*1024**2)
        for constraint in ('ProtectSystem=strict','NoNewPrivileges=yes','RestrictAddressFamilies=AF_UNIX','PrivateDevices=yes','KillMode=control-group','Restart=no','BindsTo=whaledeck-agent.service'):
            self.assertIn(constraint,block)
        for forbidden in ('sh -c','bash -c','docker.sock','CAP_SYS_ADMIN','CAP_CHOWN'):
            self.assertNotIn(forbidden,block)
        old=source.split('  container-file-permissions)\n',1)[1].split('    ;;',1)[0]
        self.assertIn('RuntimeMaxSec=70',old);self.assertIn('LimitFSIZE=65536',old)
        chmod=source.split('  container-file-recursive-permissions)\n',1)[1].split('    ;;',1)[0]
        self.assertNotIn('CAP_CHOWN',chmod)

    @unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'Linux root required for exact helper argument rejection')
    def test_extra_paths_unsupported_actions_and_foreign_invocations_fail_before_systemd(self):
        valid='a'*32
        for args in [('container-file-recursive-acl','shell',valid),('container-file-recursive-acl','apply;true',valid),
                ('container-file-recursive-acl','apply','../unit'),('container-file-recursive-acl','apply',valid,'/etc'),
                ('container-file-recursive-acl-stop',valid,'../unit'),('container-file-recursive-acl-stop',valid.upper(),valid),
                ('container-file-recursive-acl-stop',valid,valid,'/etc')]:
            with self.subTest(args=args):
                result=subprocess.run(['bash',str(HOST/'whaledeck-privileged'),*args],capture_output=True,timeout=5)
                self.assertEqual(result.returncode,2);self.assertEqual(result.stdout,b'')
