"""Reject incomplete installed maintenance UI evidence; no live dependencies."""

def prove_report(value, revision):
    assert value['revision'] == revision and value['actualUi'] is True
    results = value['results']
    assert len(results) == 2 and {v['role'] for v in results} == {'administrator', 'ordinary'}
    for result in results:
        assert result['executionRequests'] == result['otherWrites'] == 0
        assert result['anonymous'] == [401] * 3
        logout = result['logout']
        assert logout['normalUiLogout'] is True and logout['providerNavigationObserved'] is True
        assert logout['bffStatus'] == 401 and logout['identityStatus'] in (401, 403) and logout['finalLoginPage'] is True
        assert logout['postStatus'] in (302, 303)
        if result['role'] == 'administrator':
            assert result['planRequests'] == 4
            assert len(result['scenes']) == 4
            assert {(s['width'], s['colorScheme']) for s in result['scenes']} == {(w, c) for w in (320, 1440) for c in ('dark', 'light')}
            assert all(s['actualBuildRevision'] == revision and s['noHorizontalOverflow'] is True for s in result['scenes'])
            assert len(result['cancelled']) == 4
            assert {(s['action'], s['width'], s['colorScheme']) for s in result['cancelled']} == {(a, w, c) for a in ('apply-update', 'rollback') for w, c in ((320, 'dark'), (1440, 'light'))}
            assert all(s['cancelVisible'] is True and s['planStatus'] == 200 for s in result['cancelled'])
            assert result['forbidden'] == []
        else:
            assert result['planRequests'] == 0 and result['cancelled'] == []
            assert len(result['forbidden']) == 5 and all(s['status'] == 403 for s in result['forbidden'])
            assert len(result['scenes']) == 2 and {(s['width'], s['colorScheme']) for s in result['scenes']} == {(320, 'dark'), (1440, 'light')}
            assert all(s['forbiddenPage'] is True and s['buildCardAbsent'] is True for s in result['scenes'])
