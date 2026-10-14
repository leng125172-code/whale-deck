#!/usr/bin/env python3
"""Isolated PostgreSQL dispatch foundation; never invokes an external transport."""
from validate_backup_capacity_postgres import main

if __name__ == '__main__':
    main('dispatch')
