"""Security contract for the destructive, parent-paced native QA probe."""
import re
import unittest
from pathlib import Path
import container_recycle_purge_rpc_proof as proof


class PurgeProbeScopeTests(unittest.TestCase):
    def setUp(self):
        root=Path(__file__).resolve().parent/'TerminalProbe';self.s=(root/'ContainerRecyclePurgeProbe.cs').read_text();self.main=(root/'Program.cs').read_text()

    def test_new_mode_has_own_failure_observation_and_before_key_capability_gate(self):
        self.assertIn('args[0] == "container-recycle-purges"',self.main);self.assertIn('capabilityObservation = ContainerRecyclePurgeProbe.CapabilityObservation',self.main)
        gate=self.s.index('caps.ProtocolMinor >= 58');key=self.s.index('File.ReadAllBytesAsync("/etc/whaledeck/agent-capability.key"');self.assertLess(gate,key)
        self.assertIn('CONTAINER_RECYCLE_PURGE_CAPABILITY_UNAVAILABLE',self.s[:key]);self.assertNotIn('CONTAINER_RECYCLE_RESTORE_CAPABILITY_UNAVAILABLE',self.s);self.assertIn('CryptographicOperations.ZeroMemory(key)',self.s)

    def test_no_caller_path_and_no_new_apply_before_explicit_reserved_operation(self):
        self.assertIn('Path = "work/recycle-rpc.bin"',self.s);self.assertNotIn('Text(input, "path")',self.s);self.assertIn('id != core',self.s)
        self.assertIn('Require(action is "reserve" or "discard" or "reject")',self.s);self.assertIn('purgePlan.Equals(approvedPurge)',self.s);self.assertIn('operationId != purgePlan.Command.Source.OperationId',self.s)
        self.assertIn('count < 64',self.s);self.assertIn('key.Length == 32',self.s)

    def test_refusal_counts_cover_full_payload_and_cross_method_domains(self):
        fields=re.search(r'foreach \(var field in new\[\] \{ ([^}]+) \}\)\s*\{\s*var changed = operation.Clone',self.s).group(1);self.assertEqual(16,len(re.findall(r'"[^"]+"',fields)))
        self.assertEqual(proof.OPERATION_REFUSALS,16+4+1+2+1+1+1);self.assertEqual(proof.PLAN_REFUSALS,1+6+1+7+2+1)
        self.assertIn('StatusCode.FailedPrecondition',self.s);self.assertIn('"restore-domain" => ContainerRecycleRestoreBinding.Payload',self.s);self.assertIn('ContainerRecyclePurgeBinding.Hash(changed) != ContainerRecyclePurgeBinding.Hash(operation)',self.s)


if __name__=='__main__':unittest.main()
