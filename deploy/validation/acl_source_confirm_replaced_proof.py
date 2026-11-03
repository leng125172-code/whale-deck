"""One valid timestamp/digest replacement, rejected against ORIGINAL reference."""
import copy
import re
from acl_source_register_missing_proof import SEEDS as PREVIOUS
from acl_source_apply_missing_proof import source_closure
from acl_history_browser_proof import typed_equal

KIND='source-confirm-replaced'
SEEDS=(*PREVIOUS,'owned_acl_source_replacement.py','test_owned_acl_source_replacement.py',
       'acl_source_confirm_replaced_proof.py','test_acl_source_confirm_replaced_proof.py',
       'validate_container_acl_source_confirm_replaced.py','validate_container_acl_source_confirm_replaced.mjs',
       'verify_container_acl_source_confirm_replaced.py')


def manifest(repo,revision):return source_closure(repo,revision,seeds=SEEDS)


def replacement_snapshot(source,proof):
    assert proof['captureId']==source['entry']['captureId'] and proof['originalDigest']==source['entry']['historyDigest']
    assert proof['replacementDigest']!=proof['originalDigest'] and proof['originalPreserved'] is proof['sameCaptureId'] is proof['canonicalSourceExists'] is True
    assert all(type(proof[k]) is str and re.fullmatch('[A-F0-9]{64}',proof[k]) for k in ('originalDigest','replacementDigest'))
    value=copy.deepcopy(source);value['entry']['createdAtUnixMs']+=1;value['entry']['historyDigest']=proof['replacementDigest']
    return value


def verify_changed_snapshot(source,proof,actual):typed_equal(actual,replacement_snapshot(source,proof))
