#!/usr/bin/env python3
"""Real PostgreSQL alert lifecycle, without production metric or resource mutations."""
from validate_backup_capacity_postgres import main

if __name__ == '__main__':
    main('alerts')
