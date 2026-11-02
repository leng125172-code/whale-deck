#!/usr/bin/env python3
"""Internal bounded recursive UID/GID engine, not a helper/RPC grant.

The existing recursive journal owns inventory, budgets and observe-only recovery;
the existing ownership journal owns every fchown. All hash/history domains differ
from chmod, with the same refusal of links, special modes and extended metadata.
"""
NAMESPACE = 'container-recursive-ownership-v1'


def recursive_ownership_history_type(recursive):
    class RecursiveOwnershipHistory(recursive.RecursivePermissionHistory):
        NAMESPACE = NAMESPACE
        RESOURCE_TYPE = 'ContainerRecursiveOwnershipMetadata'
        ERROR_PREFIX = 'CONTAINER_RECURSIVE_OWNERSHIP_'
        DOMAIN = b'whaledeck-container-recursive-ownership-history-v1\n'
    return RecursiveOwnershipHistory


def recursive_ownership_type(ownership_type, directory_type, ownership_history_type, recursive):
    return recursive.recursive_permissions_type(ownership_type, directory_type, ownership_history_type, ownership=True)
