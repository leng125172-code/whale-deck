#!/usr/bin/env python3
"""Private parent stdin only; -B is checked before any project imports."""
import sys

if not sys.dont_write_bytecode:
    print('{"collection":"failed","code":"BYTECODE_MUST_BE_DISABLED"}')
    raise SystemExit(1)

import json
import os
from pathlib import Path
import signal


def main():
    assert os.geteuid()==0 and len(sys.argv)==1
    def timeout(_number,_frame):raise TimeoutError()
    signal.signal(signal.SIGALRM,timeout);signal.alarm(10)
    try:
        raw=sys.stdin.buffer.readline(147457)
        assert raw.endswith(b'\n') and len(raw)<=147456
        assert not sys.stdin.buffer.read(1)
        def unique(pairs):
            result={}
            for key,value in pairs:assert key not in result;result[key]=value
            return result
        packet=json.loads(raw,object_pairs_hook=unique);assert type(packet) is dict and set(packet)=={'nativeRequest','folder'}
        assert type(packet['nativeRequest']) is str and type(packet['folder']) is str
        # No caller-controlled module, state directory, clock or action.
        sys.path.insert(0,str(Path(__file__).resolve().parent))
        from container_recycle_archive_retirement_agent_io import capture_native
        signal.alarm(125)
        print(json.dumps(capture_native(packet['nativeRequest'].encode('utf-8'),Path(packet['folder']))))
    finally:signal.alarm(0)


if __name__=='__main__':
    try:main()
    except BaseException as error:
        print(json.dumps(dict(collection='failed',type=type(error).__name__)))
        raise SystemExit(1) from None
