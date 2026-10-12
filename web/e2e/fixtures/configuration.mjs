export const configurationSnapshot = {
  resourceId: 'application:demo',
  fingerprint: 'a'.repeat(64),
  environmentNames: ['EXISTING'],
  autoUpdate: false,
  versionPolicy: '*',
  maintenanceWindow: 'Sun@20:00-23:59',
  dependencies: [],
  approvedNetworks: ['whaledeck-app-demo_default', 'team-net'],
  ownedDataDirectory: '/data/WhaleDeck/apps/demo/data',
  services: ['app', 'sidecar'].map((name) => ({
    name,
    image: 'nginxinc/nginx-unprivileged:alpine',
    cpus: 0.5,
    memoryMb: 64,
    restartPolicy: 'unless-stopped',
    ports: [],
    commandPresent: true,
    entrypointPresent: false,
    environmentNames: ['INLINE'],
    mounts: [],
    networks: ['whaledeck-app-demo_default'],
    networkMode: 'bridge',
  })),
}
export function configurationResponse(path) {
  const key = path.split('/api/v1/')[1]?.split('?')[0] || ''
  if (key === 'auth/csrf') return { headerName: 'X-CSRF-TOKEN', token: 'synthetic-csrf' }
  if (key === 'applications/popular') return { applications: [], isStale: false }
  if (key === 'applications')
    return [
      {
        id: 'application:demo',
        name: 'demo',
        type: 'ComposeApplication',
        state: 'Running',
        version: 'nginxinc/nginx-unprivileged:alpine',
        isProtected: false,
        attributes: { composeProject: 'whaledeck-app-demo' },
      },
      {
        id: 'compose:whaledeck',
        name: 'whaledeck',
        type: 'ComposeProject',
        state: 'Running',
        version: '',
        isProtected: true,
        attributes: {},
      },
    ]
  if (key === 'applications/configuration') return configurationSnapshot
  if (key === 'secrets/stage')
    return { token: 'b'.repeat(64), expiresAtUtc: new Date(Date.now() + 300000).toISOString() }
  if (key === 'applications/configuration/plan')
    return {
      planHash: 'c'.repeat(64),
      expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
      changes: ['Service app: approved overrides (values redacted)'],
      warnings: ['Data retained; no implicit rollback'],
    }
  if (key === 'applications/configure')
    return { id: 'c6f1fa10-cf97-43b3-a3aa-9e43d5bab7cb', state: 'Queued' }
  return []
}
