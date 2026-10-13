#!/usr/bin/env python3
"""Isolated real PostgreSQL inbox sources, reads, scope and retention; no delivery."""
from validate_backup_capacity_postgres import main

if __name__ == '__main__':
    main('notifications')
