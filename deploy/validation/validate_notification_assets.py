"""Read-only HTTP/Gateway artifact identity check, including the lazy notification page."""
import hashlib
import json
import os
import re
import subprocess

import requests


def main():
    base = os.environ.get('WHALEDECK_URL', 'http://192.168.100.13:8080').rstrip('/')
    session = requests.Session(); session.trust_env = False
    root = session.get(base + '/', timeout=15); root.raise_for_status()
    paths = sorted(set(re.findall(r'(?:src|href)="(/assets/[A-Za-z0-9._-]{1,160}\.js)"', root.text)))
    assert 1 <= len(paths) <= 32, 'Unexpected entry/preload asset inventory'
    verified = []
    lazy = set()
    for path in paths:
        content = verify(session, base, path)
        lazy.update('/assets/' + name for name in re.findall(r'NotificationSettingsView-[A-Za-z0-9_-]{1,80}\.js', content.decode('utf-8')))
        verified.append({'path': path, 'sha256': hashlib.sha256(content).hexdigest()})
    assert len(lazy) == 1, 'Notification management lazy artifact was not referenced'
    path = lazy.pop(); content = verify(session, base, path)
    text = content.decode('utf-8')
    assert all(token in text for token in ('notifications/manage/', 'Idempotency-Key', '外部发送需要私有配置', '重试原请求', '/attempts', '/test', '/retry-unknown', '接受风险并重试', '已有重试记录', '/private/plan', '/acknowledge-unknown', '关闭并清空', '准备配置计划', '提交响应可能丢失，原请求已冻结', 'PRIVATE_UNKNOWN_ACKNOWLEDGED', '已接受未知结果，发送仍被阻断')), 'New page contract was not served'
    verified.append({'path': path, 'sha256': hashlib.sha256(content).hexdigest()})
    print(json.dumps({'notificationAssets': 'passed', 'httpMatchesActiveGatewayBytes': True, 'verified': verified, 'productionIdentityBrowser': 'not-tested'}))


def verify(session, base, path):
    assert re.fullmatch(r'/assets/[A-Za-z0-9._-]{1,160}\.js', path)
    response = session.get(base + path, timeout=15); response.raise_for_status()
    assert 0 < len(response.content) < 2_000_000, 'Unexpected public script size'
    live = subprocess.run(['docker', 'exec', 'whaledeck-gateway', 'cat', '/usr/share/nginx/html' + path], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
    assert response.content == live.stdout, 'HTTP/Gateway artifact mismatch'
    return response.content


if __name__ == '__main__':
    main()
