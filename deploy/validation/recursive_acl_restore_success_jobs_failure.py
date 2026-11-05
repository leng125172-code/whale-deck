"""Retain only a new owned QA target on failure; wait original work without replay."""
def defer_failed_fixture(process,evidence,baseline,fixture):
    """Keep owned target on any new-mode failure; revoke QA identities after drain.

    No retry/cancel/ACK, SQL edits or helper signals. A timed-out drain leaves
    the target retained for explicit reconciliation, never automatic deletion.
    """
    import json,re,subprocess,time,uuid
    from validate_metric_continuity import query
    from recursive_acl_restore_prewrite import stop_browser
    from validate_container_file_publishing import retain_unsettled_fixture
    assert baseline['containerId']==fixture['identifier'] and re.fullmatch('[a-f0-9]{64}',fixture['identifier'])
    fixture['retain_unsettled']=True;assert retain_unsettled_fixture(fixture);stop_browser(process)
    began=time.monotonic();samples=[]
    while True:
        rows=query("""SELECT COALESCE(json_agg(x),'[]') FROM (
          SELECT id,execution_job_id,state,completed_at_utc IS NOT NULL AS completed,lease_token IS NOT NULL AS leased,'acl' AS kind FROM container_recursive_acl_intents WHERE container_id=:'container' AND instance_id=:'instance'
          UNION ALL SELECT id,execution_job_id,state,completed_at_utc IS NOT NULL,lease_token IS NOT NULL,'restore' FROM container_recursive_acl_restore_intents WHERE container_id=:'container' AND instance_id=:'instance') x;""",container=baseline['containerId'],instance=baseline['instanceId'])
        quiet=all(r['completed'] is True and r['leased'] is False for r in rows)
        if quiet:
            for r in rows:
                if r['execution_job_id'] is None:continue
                op=uuid.UUID(r['execution_job_id']).hex;unit='whaledeck-container-recursive-acl-'+('restore-' if r['kind']=='restore' else '')+op+'.service'
                value=subprocess.run(['systemctl','show',unit,'-p','MainPID','--value'],capture_output=True,text=True,timeout=10)
                if value.stdout.strip()!='0':quiet=False
        samples.append(dict(elapsedMs=round((time.monotonic()-began)*1000),originalRows=rows,quiet=quiet,targetRetained=True))
        (evidence/'job-summary-failure-drain.json').write_text(json.dumps(samples))
        if quiet or time.monotonic()-began>=350:return
        time.sleep(1)
