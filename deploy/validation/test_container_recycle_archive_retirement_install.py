"""Execute copied helper with a systemd-run double. Never start host units.

Argument/stop contracts are not evidence of effective kernel sandboxing or an
installed RPC. Source installer/validator snippets run only in private temp dirs.
"""
import json
import os
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest

HOST=Path(__file__).resolve().parents[1]/'host'
ACTION='container-file-recycle-archive-retirements'
STATE='/var/lib/whaledeck-container-recycle-archive-retirements'
HDD='/data/DockerData/whaledeck/file-backups/container-recycling-v1'
MODULES=('whaledeck-container-recycle-archive-retention.py','whaledeck-container-recycle-archive-retirement-fences.py','whaledeck-container-recycle-archive-retirements.py','whaledeck-container-recycle-archive-retirement-transport.py','whaledeck-container-recycle-retired-history.py','whaledeck-container-recycle-archive-retirement-stop.py')


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RetirementHelperContractTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='retirement-helper-contract-');self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name);self.calls=self.root/'calls.jsonl'
        run=self.root/'systemd-run';run.write_text('''#!/usr/bin/python3
import json,os,sys
value=dict(command='run',args=sys.argv[1:]);open(os.environ['QA_CALLS'],'a').write(json.dumps(value)+'\\n');print(json.dumps(dict(stdinBytes=len(sys.stdin.buffer.read()),args=sys.argv[1:])))
''');run.chmod(0o700)
        source=(HOST/'whaledeck-privileged').read_text().replace('/usr/bin/systemd-run',shlex.quote(str(run)))
        self.helper=self.root/'helper';self.helper.write_text(source)

    def execute(self,args):
        env=dict(PATH=os.defpath,QA_CALLS=str(self.calls))
        result=subprocess.run(['/bin/bash',str(self.helper),*args],input=b'private-owned-packet\n',capture_output=True,env=env,timeout=5)
        calls=[json.loads(line) for line in self.calls.read_text().splitlines()] if self.calls.exists() else []
        return result,calls

    def test_each_action_has_exact_private_unit_and_minimum_write_paths(self):
        for action in ('plan','reserve','apply','inspect','discard'):
            if self.calls.exists():self.calls.unlink()
            with self.subTest(action=action):
                result,calls=self.execute([ACTION,action,'a'*32]);self.assertEqual(result.returncode,0,result.stderr)
                self.assertEqual(len(calls),1);args=calls[0]['args'];self.assertEqual(args[-5:],['/usr/bin/python3','-B','-I','/usr/local/libexec/whaledeck-container-recycle-archive-retirement-transport.py',action])
                self.assertIn('--unit=whaledeck-container-recycle-archive-retirements-'+'a'*32,args)
                rw=next(a.split('=',2)[2] for a in args if a.startswith('--property=ReadWritePaths='))
                self.assertEqual(rw.split(),[] if action=='plan' else [STATE,HDD] if action=='apply' else [STATE])
                self.assertEqual(json.loads(result.stdout)['stdinBytes'],len(b'private-owned-packet\n'))

    def test_bounds_lifecycle_capabilities_and_original_readonly_paths_are_fixed(self):
        result,calls=self.execute([ACTION,'apply','a'*32]);self.assertEqual(result.returncode,0);args=calls[0]['args']
        for value in ('Slice=system.slice','ProtectSystem=strict','ProtectHome=yes','PrivateTmp=yes','PrivateDevices=yes','NoNewPrivileges=yes','RestrictSUIDSGID=no','RestrictNamespaces=yes','RestrictAddressFamilies=AF_UNIX','RuntimeMaxSec=130','TimeoutStopSec=5','KillMode=control-group','Restart=no','TasksMax=8','MemoryMax=192M','CPUQuota=50%','LimitNOFILE=128','LimitFSIZE=131072','BindsTo=whaledeck-agent.service','After=whaledeck-agent.service'):
            self.assertIn('--property='+value,args)
        caps=next(a for a in args if a.startswith('--property=CapabilityBoundingSet='));self.assertEqual(caps,'--property=CapabilityBoundingSet=CAP_DAC_READ_SEARCH CAP_SYS_PTRACE')
        ro=next(a.split('=',2)[2].split() for a in args if a.startswith('--property=ReadOnlyPaths='))
        self.assertEqual(ro,['/var/lib/whaledeck-container-recycling','/var/lib/whaledeck-container-recycle-restores/claims','/var/lib/whaledeck-container-recycle-purges','/var/lib/whaledeck-container-recycle-archives','/data/WhaleDeck/apps'])

    def test_bad_actions_ids_or_argument_counts_start_no_units(self):
        for args in ([ACTION,'shell','a'*32],[ACTION,'apply','A'*32],[ACTION,'apply','../x'],[ACTION,'apply','0'*32],[ACTION+'-stop','0'*32,'b'*32],[ACTION,'apply','a'*32,'extra'],[ACTION+'-stop','a'*32,'B'*32]):
            with self.subTest(args=args):
                result,calls=self.execute(args);self.assertEqual(result.returncode,2);self.assertEqual(calls,[])

    def test_stop_delegates_to_read_only_unit_with_local_ptrace_capability(self):
        result,calls=self.execute([ACTION+'-stop','a'*32,'b'*32])
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(len(calls),1)
        args=calls[0]['args']
        self.assertEqual(args[-6:],['/usr/bin/python3','-B','-I','/usr/local/libexec/whaledeck-container-recycle-archive-retirement-stop.py','a'*32,'b'*32])
        self.assertIn('--unit=whaledeck-container-recycle-archive-retirements-stop-'+'a'*32,args)
        self.assertIn('--wait',args)
        self.assertNotIn('--pipe',args)
        self.assertFalse(any(a.startswith('--property=ReadWritePaths=') for a in args))
        for value in ('ProtectSystem=strict','NoNewPrivileges=yes','CapabilityBoundingSet=CAP_SYS_PTRACE','RuntimeMaxSec=8','TimeoutStopSec=1','StandardInput=null','StandardOutput=null','StandardError=null'):
            self.assertIn('--property='+value,args)


@unittest.skipUnless(os.name=='posix' and os.geteuid()==0,'isolated Linux root required')
class RetirementInstallFilesystemTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='retirement-install-owned-');self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
        self.lib=self.root/'libexec';self.lib.mkdir();self.originals=[]
        for name in ('whaledeck-container-recycling','whaledeck-container-recycle-restores','whaledeck-container-recycle-purges','whaledeck-container-recycle-archives'):
            path=self.root/'var/lib'/name;path.mkdir(parents=True,mode=0o700);(path/'original.json').write_bytes(b'preserve-original');self.originals.append(path/'original.json')
        (self.root/'var/lib/whaledeck-container-recycle-restores/claims').mkdir(mode=0o700)
        for module in MODULES:(self.root/module).write_bytes(('owned-install-fixture:'+module).encode())
        self.state=self.root/STATE.lstrip('/')

    def fragment(self,validate=False):
        source=(HOST/('validate.sh' if validate else 'install.sh')).read_text()
        if validate:
            part='for retirement_state in '+source.split('for retirement_state in ',1)[1].split('# End independent retirement validation.',1)[0]
            metadata=source[source.index('assert_metadata()'):source.index('\n}\n',source.index('assert_metadata()'))+3]
        else:part='# Provision only the independent retirement registry.'+source.split('# Provision only the independent retirement registry.',1)[1].split('# End independent retirement provisioning.',1)[0];metadata=''
        text='#!/bin/bash\nset -Eeuo pipefail\n'+metadata+'\n'+part
        return text.replace('/var/lib/',str(self.root/'var/lib')+'/').replace('/usr/local/libexec',str(self.lib))

    def run_fragment(self,validate=False):
        script=self.root/('check' if validate else 'provision');script.write_text(self.fragment(validate))
        return subprocess.run(['/bin/bash',str(script)],capture_output=True,timeout=5)

    def preserved(self):self.assertTrue(all(p.read_bytes()==b'preserve-original' for p in self.originals))

    def test_real_temp_install_metadata_hashes_and_validation_keep_originals(self):
        result=self.run_fragment();self.assertEqual(result.returncode,0,result.stderr)
        for directory in (self.state,self.state/'claims'):
            i=directory.stat();self.assertEqual((i.st_uid,i.st_gid,i.st_mode&0o777),(0,0,0o700))
        for module in MODULES:
            target=self.lib/module;i=target.stat();self.assertEqual((i.st_uid,i.st_gid,i.st_mode&0o777,i.st_nlink),(0,0,0o750,1));self.assertEqual(target.read_bytes(),(self.root/module).read_bytes())
        result=self.run_fragment(True);self.assertEqual(result.returncode,0,result.stderr);self.preserved()
        (self.lib/MODULES[0]).write_bytes(b'wrong installed bytes')
        self.assertNotEqual(self.run_fragment(True).returncode,0);self.preserved()

    def test_symlink_or_unknown_ownership_mode_refused_without_repairing(self):
        original=self.originals[0].parent;self.state.symlink_to(original,target_is_directory=True)
        self.assertNotEqual(self.run_fragment().returncode,0);self.assertTrue(self.state.is_symlink());self.preserved()
        self.state.unlink();self.state.mkdir(mode=0o755)
        self.assertNotEqual(self.run_fragment().returncode,0);self.assertEqual(self.state.stat().st_mode&0o777,0o755);self.preserved()

    def test_claim_symlink_and_installed_module_hardlink_are_rejected(self):
        self.state.mkdir(mode=0o700);(self.state/'claims').symlink_to(self.originals[0].parent,target_is_directory=True)
        self.assertNotEqual(self.run_fragment().returncode,0);self.preserved();(self.state/'claims').unlink()
        self.assertEqual(self.run_fragment().returncode,0);os.link(self.lib/MODULES[0],self.root/'extra-link')
        self.assertNotEqual(self.run_fragment(True).returncode,0)
        self.assertNotEqual(self.run_fragment().returncode,0)
        self.assertTrue(os.path.samefile(self.lib/MODULES[0],self.root/'extra-link'));self.preserved()

    def test_directory_or_fifo_cannot_be_installed_or_validated_as_module(self):
        self.assertEqual(self.run_fragment().returncode,0)
        target=self.lib/MODULES[0];target.unlink()
        target.mkdir(mode=0o750)
        self.assertNotEqual(self.run_fragment().returncode,0)
        self.assertNotEqual(self.run_fragment(True).returncode,0)
        self.assertEqual(list(target.iterdir()),[])
        target.rmdir();os.mkfifo(target,0o750)
        self.assertNotEqual(self.run_fragment().returncode,0)
        self.assertNotEqual(self.run_fragment(True).returncode,0);self.preserved()


class RetirementInstallationSourceTests(unittest.TestCase):
    def test_installer_and_validator_cover_six_root_owned_modules_and_own_states(self):
        install=(HOST/'install.sh').read_text();validate=(HOST/'validate.sh').read_text()
        block=install.split('# Provision only the independent retirement registry.',1)[1].split('done\n',3)[0]
        self.assertIn("'700:0:0'",block);self.assertIn(' -ef $retirement_original',block);self.assertNotIn('rm ',block)
        for name in MODULES:self.assertIn(name,install);self.assertIn(name,validate)
        self.assertIn('install -T -o root -g root -m 0750 "$(dirname "$0")/$retirement_module"',install)
        self.assertIn('$(stat -c %h "$retirement_path") == 1',validate);self.assertIn('sha256sum "$retirement_path"',validate)
        for path in (STATE,STATE+'/claims'):self.assertIn(path,install);self.assertIn(path,validate)

    def test_source_preparation_does_not_map_or_advertise_new_rpc(self):
        repo=HOST.parents[1];program=(repo/'src/WhaleDeck.Agent/Program.cs').read_text();caps=(repo/'src/WhaleDeck.Agent/Services/AgentGrpcService.cs').read_text()
        self.assertNotIn('MapGrpcService<ContainerRecycleArchiveRetirementGrpcService>',program)
        self.assertIn('ProtocolMinor = 59',caps);self.assertNotIn('"docker.files.recycle.archive.retire"',caps)


if __name__=='__main__':unittest.main()
