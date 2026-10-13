#!/usr/bin/env python3
"""Real partitioned PostgreSQL metric SQL/migrations in one owned, isolated, disposable database."""
from validate_backup_capacity_postgres import main

if __name__ == '__main__':
    main('metrics')
