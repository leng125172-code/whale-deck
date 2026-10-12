export const relationshipInspect = {
  id: 'demo',
  name: 'demo-web',
  image: 'nginx:alpine',
  environmentNames: ['PUBLIC_URL'],
  command: [],
  entrypoint: [],
  labels: {},
  ports: ['127.0.0.1:18088:80/tcp'],
  volumes: [],
  networks: ['team-net'],
  restartPolicy: 'no',
  cpus: 0.25,
  memoryMb: 64,
  healthCommand: [],
  healthIntervalSeconds: 0,
  healthTimeoutSeconds: 0,
  healthRetries: 0,
  relationships: {
    imageId: 'sha256:' + 'a'.repeat(64),
    composeProject: 'team',
    composeService: 'web',
    networkMode: 'team-net',
    endpoints: [
      {
        name: 'team-net',
        networkId: 'b'.repeat(64),
        ipv4Address: '172.19.0.2',
        ipv4PrefixLength: 24,
        ipv4Gateway: '172.19.0.1',
        ipv6Address: 'fd00::2',
        ipv6PrefixLength: 64,
        ipv6Gateway: 'fd00::1',
        macAddress: '02:42:ac:13:00:02',
      },
    ],
    mounts: [
      {
        type: 'volume',
        name: 'team-data',
        source: '/var/lib/docker/volumes/team-data/_data',
        destination: '/data',
        readWrite: true,
      },
      {
        type: 'bind',
        name: '',
        source: '/data/' + 'long-path-segment/'.repeat(16) + 'settings.json',
        destination: '/config/settings.json',
        readWrite: false,
      },
    ],
    portBindings: [
      { containerPort: '80/tcp', hostAddress: '127.0.0.1', hostPort: '18088', published: true },
      { containerPort: '443/tcp', hostAddress: '', hostPort: '', published: false },
    ],
    isTruncated: false,
  },
}
export const relationshipContainers = [
  {
    id: 'demo',
    name: 'demo-web',
    image: 'nginx:alpine',
    state: 'running',
    status: 'Up 1 hour',
    isProtected: false,
    protectionLevel: 'Managed',
    labels: { 'com.docker.compose.project': 'team' },
    imageId: relationshipInspect.relationships.imageId,
    networkIds: ['b'.repeat(64)],
    volumeNames: ['team-data'],
  },
  {
    id: 'other',
    name: 'unrelated',
    image: 'nginx:alpine',
    state: 'running',
    status: 'Up 1 hour',
    isProtected: true,
    protectionLevel: 'ControlPlane',
    labels: {},
    imageId: 'sha256:other',
    volumeNames: [],
    networkIds: [],
  },
]
export const relationshipResources = {
  images: [
    {
      id: 'image:' + relationshipInspect.relationships.imageId,
      name: 'nginx:alpine',
      type: 'Image',
      state: 'InUse',
      version: relationshipInspect.relationships.imageId,
      isProtected: false,
      attributes: { containerCount: '1', sizeBytes: '1000' },
    },
  ],
  networks: [
    {
      id: 'network:' + 'b'.repeat(64),
      name: 'team-net',
      type: 'DockerNetwork',
      state: 'External',
      version: 'bridge',
      isProtected: false,
      attributes: { containerCount: '1', internal: 'false' },
    },
  ],
  volumes: [
    {
      id: 'volume:team-data',
      name: 'team-data',
      type: 'DockerVolume',
      state: 'InUse',
      version: 'local',
      isProtected: false,
      attributes: { referenceCount: '1', sizeBytes: '-1' },
    },
  ],
  'compose-projects': [
    {
      id: 'compose:team',
      name: 'team',
      type: 'ComposeProject',
      state: 'Running',
      version: '',
      isProtected: false,
      attributes: {
        composeProject: 'team',
        services: 'web',
        containerCount: '1',
        allowedActions: '',
      },
    },
  ],
}
export function relationshipResponse(path) {
  const key = path.split('/api/v1/')[1]?.split('?')[0] || ''
  if (key === 'containers') return relationshipContainers
  if (key === 'containers/usage')
    return { items: [], sampledAtUtc: new Date().toISOString(), intervalSeconds: 6 }
  if (key.endsWith('/inspect')) return relationshipInspect
  if (key.endsWith('/stats')) return { values: {} }
  if (key.endsWith('/logs')) return { lines: [], truncated: false }
  if (key in relationshipResources) return relationshipResources[key]
  return []
}
