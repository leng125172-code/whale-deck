#!/usr/bin/env python3
"""Read-only ABI/sandbox diagnostic; reports fixed phase/errno, not paths/data."""
import importlib.util
import json
import os
import platform

spec = importlib.util.spec_from_file_location('files_driver', '/usr/local/libexec/whaledeck-files.py')
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)
phase = 'machine'
opened = []
try:
    machine = platform.machine()
    print(json.dumps(dict(machine=machine, supported=machine in ('x86_64', 'aarch64'))), flush=True)
    phase = 'registered-root'
    slash = os.open('/', os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC); opened.append(slash)
    root = driver.open_at(slash, 'data/WhaleDeck/files', os.O_RDONLY | os.O_DIRECTORY, cross_mount=True); opened.append(root)
    phase = 'relative-root'
    folder = driver.open_at(root, '.', os.O_RDONLY | os.O_DIRECTORY); opened.append(folder)
    print(json.dumps(dict(nativeFileKernel='passed')), flush=True)
except Exception as error:
    code = str(error) if isinstance(error, driver.Rejected) else None
    print(json.dumps(dict(nativeFileKernel='failed', phase=phase, ownCode=code, errno=getattr(error, 'errno', None))), flush=True)
    raise SystemExit(1) from None
finally:
    for descriptor in reversed(opened): os.close(descriptor)
