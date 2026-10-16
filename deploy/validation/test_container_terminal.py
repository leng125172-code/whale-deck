"""Pure policy/identity tests. No Docker mutation, production state or signaling."""
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('container_terminal', Path(__file__).resolve().parents[1] / 'host/whaledeck-container-terminal.py')
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)
IDENTIFIER = 'a' * 64
RESOURCES = [{'type': 'Container', 'externalId': 'core-api'}, {'id': 'whaledeck.apps', 'type': 'ManagedDirectory', 'path': '/data/WhaleDeck/apps'}]


def container():
    return {'Id': IDENTIFIER, 'Name': '/ordinary-test', 'Image': 'sha256:' + 'b' * 64,
            'Config': {'User': '1000', 'Labels': {}, 'Env': ['PRIVATE_VALUE=not-a-response']}, 'HostConfig': {},
            'State': {'Pid': 777, 'StartedAt': '2026-10-16T01:02:03Z', 'Running': True}}


class PolicyTests(unittest.TestCase):
    def test_target_contains_only_public_metadata(self):
        value = driver.target(container(), IDENTIFIER, RESOURCES)
        self.assertEqual(value['instanceId'], '777:2026-10-16T01:02:03Z')
        self.assertEqual(value['user'], '1000')
        self.assertNotIn('PRIVATE_VALUE', json.dumps(value))

    def test_registry_and_labels_independently_protect(self):
        for name, labels in (('/core-api', {}), ('/ordinary-test', {'io.whaledeck.protected': 'TRUE'})):
            value = container(); value['Name'] = name; value['Config']['Labels'] = labels
            with self.subTest(name=name), self.assertRaises(PermissionError): driver.target(value, IDENTIFIER, RESOURCES)

    def test_canonical_immutable_id_required(self):
        for identifier in ('a' * 12, 'A' * 64, '../ordinary-test', 'ordinary-test'):
            with self.subTest(identifier=identifier), self.assertRaises(ValueError): driver.target(container(), identifier, RESOURCES)

    def test_unsafe_isolation(self):
        for key, value in (('Privileged', True), ('CapAdd', ['SYS_ADMIN']), ('Devices', [{}]), ('DeviceRequests', [{}]),
                           ('DeviceCgroupRules', ['a *:* rwm']), ('PidMode', 'host'), ('IpcMode', 'host'), ('NetworkMode', 'host'),
                           ('UsernsMode', 'host'), ('UTSMode', 'host'), ('CgroupnsMode', 'host'), ('PidMode', 'container:other'),
                           ('SecurityOpt', ['seccomp=unconfined']), ('SecurityOpt', ['apparmor=unconfined'])):
            item = container(); item['HostConfig'][key] = value
            with self.subTest(key=key), self.assertRaises(PermissionError): driver.target(item, IDENTIFIER, RESOURCES)

    def test_inactive_states_and_boolean_pid(self):
        for key, value in (('Running', False), ('Paused', True), ('Restarting', True), ('Dead', True), ('Pid', True)):
            item = container(); item['State'][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): driver.target(item, IDENTIFIER, RESOURCES)

    def test_host_mounts_and_unowned_volumes_rejected(self):
        for mount in ({'Type': 'bind', 'Source': '/var/run/docker.sock'}, {'Type': 'bind', 'Source': '/etc'},
                      {'Type': 'volume', 'Name': 'database-platform-postgres'}, {'Type': 'npipe', 'Source': 'invalid'}):
            item = container(); item['Mounts'] = [mount]
            with self.subTest(mount=mount), self.assertRaises(PermissionError): driver.target(item, IDENTIFIER, RESOURCES)

    def test_owned_volume_and_tmpfs_allowed(self):
        item = container(); item['Mounts'] = [{'Type': 'volume', 'Name': 'whaledeck-app-example-data'}, {'Type': 'tmpfs'}]
        self.assertEqual(driver.target(item, IDENTIFIER, RESOURCES)['containerId'], IDENTIFIER)

    def test_not_running_exec_does_not_open_pidfd(self):
        with patch.object(driver, 'api', return_value={'Running': False}), patch.object(driver.os, 'pidfd_open') as opening:
            driver.stop_exec({'execId': 'b' * 64, 'containerId': IDENTIFIER})
            opening.assert_not_called()

    def test_changed_pid_never_signaled(self):
        for row in ({'execPid': 999}, {'execPid': True}):
            row.update(execId='b' * 64, containerId=IDENTIFIER)
            with patch.object(driver, 'api', return_value={'Running': True, 'Pid': 777, 'ContainerID': IDENTIFIER}), patch.object(driver.os, 'pidfd_open') as opening:
                with self.assertRaises(PermissionError): driver.stop_exec(row)
                opening.assert_not_called()

    def test_recycled_pid_identity_cannot_receive_a_signal(self):
        row = {'execId': 'b' * 64, 'containerId': IDENTIFIER, 'execPid': 777, 'execStart': 'old', 'pidNamespace': 10}
        with patch.object(driver, 'api', return_value={'Running': True, 'Pid': 777, 'ContainerID': IDENTIFIER}), \
             patch.object(driver.os, 'pidfd_open', return_value=10), patch.object(driver.os, 'close'), \
             patch.object(driver, 'process_identity', return_value=('new', 10)), patch.object(driver.signal, 'pidfd_send_signal') as signaling:
            with self.assertRaises(PermissionError): driver.stop_exec(row)
            signaling.assert_not_called()


if __name__ == '__main__': unittest.main()
