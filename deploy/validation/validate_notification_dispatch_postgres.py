#!/usr/bin/env python3
"""Isolated PostgreSQL dispatch, including test-only local HTTP/TLS SMTP collectors.

Never invokes production destinations or mounts the private recipient registry.
"""
from validate_backup_capacity_postgres import main

if __name__ == '__main__':
    main('dispatch')
