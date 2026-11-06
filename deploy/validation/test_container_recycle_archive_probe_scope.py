"""Security contract of the destructive, parent-paced archive native probe."""
import re
import unittest
from pathlib import Path
import container_recycle_archive_rpc_proof as proof


class ArchiveProbeScopeTests(unittest.TestCase):
    def setUp(self):
        p=Path(__file__).resolve().parent/'TerminalProbe';self.s=(p/'ContainerRecycleArchiveProbe.cs').read_text();self.main=(p/'Program.cs').read_text()

    def test_capability_gate_and_own_failure_shape_precede_key_or_any_operation(self):
        gate=self.s.index('caps.ProtocolMinor >= 59');key=self.s.index('File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key"');self.assertLess(gate,key);self.assertIn('CONTAINER_RECYCLE_ARCHIVE_CAPABILITY_UNAVAILABLE',self.s[:key]);self.assertIn('caps.Capabilities.Contains("docker.files.recycle.archive")',self.s[:key]);self.assertIn('CryptographicOperations.ZeroMemory(key)',self.s)
        self.assertIn('args[0] == "container-recycle-archives"',self.main);self.assertIn('capabilityObservation = ContainerRecycleArchiveProbe.CapabilityObservation',self.main)

    def test_fixed_source_path_and_approved_plans_prevent_unreserved_new_apply(self):
        self.assertIn('Path = "work/recycle-rpc.bin"',self.s);self.assertNotIn('Text(input, "path")',self.s);self.assertIn('id != core',self.s);self.assertIn('count < 64',self.s)
        self.assertIn('Require(action is "archive-reserve" or "archive-discard" or "archive-reject")',self.s);self.assertIn('supplied.Equals(approvedArchive)',self.s);self.assertIn('archiveStates[archiveId] is "Reserved" or "Completed" or "Aborted" or "Unknown"',self.s)

    def test_third_domain_and_three_authorities_are_not_purge_aliases(self):
        self.assertIn('archive ? ContainerRecycleArchiveBinding.Hash(request)',self.s);self.assertIn('archive ? ContainerRecycleArchiveBinding.Service',self.s);self.assertIn('archive ? ContainerRecycleArchiveBinding.Payload',self.s)
        self.assertIn('observedPurge.State=="Completed"',self.s);self.assertIn('observed.State=="Purged"',self.s);self.assertIn('archiveId!=supplied.Command.Purge.OperationId',self.s);self.assertIn('archiveId!=supplied.Command.Purge.Plan.Command.Source.OperationId',self.s)

    def test_refusals_count_structural_binding_mutations_and_no_silent_bad_cache(self):
        block=self.s.split('if(action=="archive-reject")',1)[1].split('Phase=action+"-value"',1)[0]
        mutations=re.search(r'foreach\(var field in new\[\]\{([^}]+)\}',block).group(1);self.assertEqual(len(re.findall('"[^"]+"',mutations)),20)
        self.assertEqual(proof.OPERATION_REFUSALS,20+4+1+2+1+1+1+1);self.assertEqual(proof.PLAN_REFUSALS,1+6+1+10+3+1)
        self.assertIn('old.Fingerprint=ContainerRecyclePurgeWire.Fingerprint(old)',block);self.assertIn('changed.Plan.Fingerprint=ContainerRecycleArchiveWire.Fingerprint(changed.Plan)',block);self.assertIn('ContainerRecycleArchiveBinding.Hash(changed)!=ContainerRecycleArchiveBinding.Hash(archiveOperation)',block)


if __name__=='__main__':unittest.main()
