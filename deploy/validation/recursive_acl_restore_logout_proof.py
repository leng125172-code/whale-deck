"""Pure evidence checks for normal logout during the original restore execution."""

def authority_proof(ui):
    assert ui['logoutDuringExecution'] is True and ui['prewriteCancellation'] is False
    assert ui['registrations']==ui['confirmations']==1 and ui['cancellations']==ui['uploads']==0
    assert ui['hintsCleared'] is ui['normalDualLogout'] is True and ui['anonymous']==[401]*6
    proof=ui['logoutIsolation']
    assert proof['reloginSubject']==ui['subject'] and proof['newLoginLookups']==[404,404]
    assert proof['hintsAbsentAfterRelogin'] is True and proof['oldTaskButtons']==0
    assert proof['jobId']==ui['pending']['executionJobId']==ui['terminal']['executionJobId'] and proof['jobState']=='Canceled'
    assert proof['adminAnonymous']==[401]*6
    for logout in (ui['logout'],ui['adminReloginLogout']):
        assert logout['normalUiLogout'] is logout['providerNavigationObserved'] is logout['finalLoginPage'] is True
        assert logout['postStatus'] in (302,303) and logout['bffStatus']==401 and logout['identityStatus'] in (401,403)
