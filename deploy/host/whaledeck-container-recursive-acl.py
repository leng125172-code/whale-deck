#!/usr/bin/env python3
"""Internal bounded recursive ACL engine; no CLI, installed helper or RPC grant.

Access ACLs explicitly target files and directories separately. Default ACLs only
change directories; files remain in the sealed inventory with their original
access ACL and a no-change child result. Every entry uses the original FD ACL
journal, with separate aggregate/child history, plan and outcome hash domains.
"""
NAMESPACE='container-recursive-acl-v1'


def recursive_acl_history_type(recursive):
    class RecursiveAclHistory(recursive.RecursivePermissionHistory):
        NAMESPACE=NAMESPACE
        RESOURCE_TYPE='ContainerRecursiveAclMetadata'
        ERROR_PREFIX='CONTAINER_RECURSIVE_ACL_'
        DOMAIN=b'whaledeck-container-recursive-acl-history-v1\n'
    return RecursiveAclHistory


def recursive_acl_type(acl_module,permission_module,directory_type,recursive):
    class AclEntry(acl_module.container_acl_type(directory_type)):
        validate_entries=staticmethod(acl_module.validate_entries)
    return recursive.recursive_permissions_type(AclEntry,directory_type,acl_module.acl_history_type(permission_module),acl=True)
