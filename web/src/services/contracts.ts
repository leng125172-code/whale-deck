export interface CurrentUser {
  subject: string
  name: string | null
  groups: string[]
  isAdministrator: boolean
  permissionExpiresAtUtc: string
}

export interface PortalItem {
  id: string
  scope: 'Personal' | 'Public'
  name: string
  description: string | null
  url: string
  iconKind: string
  iconValue: string
  color: string
  sortOrder: number
  isEnabled: boolean
  version: number
}

export interface AgentHealth {
  available: boolean
  dockerAvailable: boolean
  systemdAvailable: boolean
  status: string
  observedAtUtc: string
}

export interface HostInfo {
  hostName: string
  distribution: string
  kernelVersion: string
  architecture: string
  bootTimeUtc: string
  uptime: string
  logicalProcessorCount: number
  totalMemoryBytes: number
  addresses: Array<{ interfaceName: string; address: string }>
}

export interface JournalEntry {
  occurredAtUtc: string
  unit: string
  priority: string
  process: string
  processId: number
  message: string
}

export interface ManagedResource {
  id: string
  name: string
  type: string
  state: string
  version: string
  isProtected: boolean
  attributes: Record<string, string>
}

export interface RoleMapping {
  id: string | null
  authentikGroupId: string
  authentikGroupName: string
  role: 'Administrator'
  isEnabled: boolean
  version: number
  source: 'Deployment' | 'Database'
  isMutable: boolean
  updatedAtUtc: string | null
}

export interface ContainerLogs {
  lines: string[]
  truncated: boolean
}

export interface HostProcess {
  pid: number
  parentPid: number
  userId: number
  instanceId: string
  name: string
  state: string
  threadCount: number
  startedAtUtc: string
  cpuTimeSeconds: number
  cpuPercent: number | null
  memoryBytes: number
  virtualMemoryBytes: number
}

export interface HostProcessPage {
  items: HostProcess[]
  total: number
  page: number
  pageSize: number
  sampledAtUtc: string
  cpuWindowSeconds: number
  isStale: boolean
  isTruncated: boolean
  skippedCount: number
}

export interface ContainerStats {
  values: Record<string, string>
}

export interface ContainerUsage {
  containerId: string
  state: string
  sampledAtUtc: string | null
  cpuPercent: number | null
  cpuWindowSeconds: number
  memoryUsedBytes: number | null
  memoryLimitBytes: number | null
  memoryPercent: number | null
  networkReceiveBytes: number | null
  networkSendBytes: number | null
  blockReadBytes: number | null
  blockWriteBytes: number | null
  pids: number | null
  quality: string
}

export interface ContainerUsageBatch {
  items: ContainerUsage[]
  sampledAtUtc: string | null
  isStale: boolean
  isTruncated: boolean
  skippedCount: number
  totalCount: number
  intervalSeconds: number
}

export interface ContainerInspect {
  id: string
  name: string
  image: string
  environmentNames: string[]
  command: string[]
  entrypoint: string[]
  labels: Record<string, string>
  ports: string[]
  volumes: string[]
  networks: string[]
  restartPolicy: string
  cpus: number
  memoryMb: number
  healthCommand: string[]
  healthIntervalSeconds: number
  healthTimeoutSeconds: number
  healthRetries: number
  relationships?: ContainerRelationships | null
}

export interface ContainerRelationships {
  imageId: string
  composeProject: string
  composeService: string
  networkMode: string
  endpoints: {
    name: string
    networkId: string
    ipv4Address: string
    ipv4PrefixLength: number
    ipv4Gateway: string
    ipv6Address: string
    ipv6PrefixLength: number
    ipv6Gateway: string
    macAddress: string
  }[]
  mounts: { type: string; name: string; source: string; destination: string; readWrite: boolean }[]
  portBindings: {
    containerPort: string
    hostAddress: string
    hostPort: string
    published: boolean
  }[]
  isTruncated: boolean
}

export interface ContainerUpdateRun {
  id: string
  externalContainerId: string
  containerName: string
  image: string
  oldImageDigest: string | null
  newImageDigest: string | null
  versionPolicy: string | null
  jobId: string
  startedAtUtc: string
  completedAtUtc: string | null
  result: string
  wasRolledBack: boolean
  errorSummary: string | null
}

export interface DockerEvent {
  fingerprint: string
  occurredAtUtc: string
  eventType: string
  action: string
  resourceId: string
  resourceName: string
  image: string | null
  attributes: Record<string, string>
}

export interface GlobalSearchResult {
  id: string
  kind: string
  title: string
  subtitle: string
  targetUrl: string
  state: string | null
  external: boolean
}

export interface MetricValue {
  kind: string
  deviceId: string
  value: number
  unit: string
  quality: string
  sampledAtUtc: string
}

export interface MetricSeriesSnapshot {
  kind: string
  deviceId: string
  unit: string
  points: Array<{
    sampledAtUtc: string
    value: number | null
    quality: string
    aggregationWeight?: number | null
  }>
  windowStartUtc?: string | null
  windowEndUtc?: string | null
  resolutionSeconds?: number
}

export interface CatalogApplication {
  id: string
  name: string
  description: string
  image: string
  version: string
  iconUrl: string | null
  installed: boolean
  state: string | null
}

export interface ApplicationImageMetadata {
  image: string
  imageId: string
  environment: string[]
  exposedPorts: string[]
  volumes: string[]
  entrypoint: string[]
  command: string[]
  labels: Record<string, string>
}

export interface ApplicationConfigurationMount {
  type: string
  source: string
  target: string
  readOnly: boolean
}
export interface ApplicationServiceConfiguration {
  name: string
  image: string
  cpus: number
  memoryMb: number
  restartPolicy: string
  ports: string[]
  commandPresent: boolean
  entrypointPresent: boolean
  environmentNames: string[]
  mounts: ApplicationConfigurationMount[]
  networks: string[]
  networkMode: string
}
export interface ApplicationConfiguration {
  resourceId: string
  fingerprint: string
  services: ApplicationServiceConfiguration[]
  environmentNames: string[]
  autoUpdate: boolean
  versionPolicy: string
  maintenanceWindow: string
  dependencies: string[]
  approvedNetworks: string[]
  ownedDataDirectory: string
}

export interface OverviewResponse {
  scope: 'Public' | 'Administrator'
  platformStatus: string
  sampledAtUtc: string
  portals: PortalItem[]
  agent: AgentHealth | null
  host: HostInfo | null
  resources: ManagedResource[] | null
  metrics: MetricValue[] | null
  applications: CatalogApplication[] | null
  catalogIsStale: boolean
}

export interface Job {
  id: string
  jobType: string
  state: string
  phase: string
  progressPercent: number | null
  errorCode: string | null
  createdAtUtc: string
  completedAtUtc: string | null
  resultJson: string | null
}

export interface JobEvent {
  sequence: number
  state: string
  phase: string
  progressPercent: number | null
  messageCode: string
  occurredAtUtc: string
}

export interface ScheduledTask {
  id: string
  taskType: string
  name: string
  scheduleKind: string
  scheduleExpression: string
  timezone: string
  parametersJson: string
  concurrencyPolicy: string
  timeoutSeconds: number
  isEnabled: boolean
  nextRunAtUtc: string | null
  version: number
}

export interface ScheduledTaskRun {
  id: string
  scheduleId: string
  jobId: string
  scheduledForUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  result: string
}

export interface AlertRule {
  id: string
  ruleType: string
  resourceSelectorJson: string
  thresholdJson: string
  evaluationWindowSeconds: number
  severity: 'Info' | 'Warning' | 'Critical'
  isEnabled: boolean
  version: number
}

export interface AlertEvent {
  monitoring?: {
    triggered: boolean | null
    severity: string
    value: number | null
    coveragePercent: number | null
    observedAtUtc: string | null
    reason: string
  } | null
  capacity?: {
    policyId: string
    targetDirectoryId: string
    available: boolean
    usedPercent: number | null
    freeBytes: number | null
    warningPercent: number
    criticalPercent: number
    observedAtUtc: string | null
  } | null
  id: string
  ruleId: string
  resourceId: string | null
  state: string
  severity: 'Info' | 'Warning' | 'Critical'
  occurrenceCount: number
  firstOccurredAtUtc: string
  lastOccurredAtUtc: string
  recoveredAtUtc: string | null
  acknowledgedBySubject: string | null
  acknowledgedAtUtc: string | null
  silencedUntilUtc: string | null
  summaryCode: string
}

export interface AlertEventHistory {
  id: string
  alertEventId: string
  state: string
  actorSubject: string
  occurredAtUtc: string
}

export interface PlatformSetting {
  key: string
  valueJson: string
  version: number
  updatedAtUtc: string
}

export interface BackupPolicy {
  id: string
  instanceResourceId: string
  isEnabled: boolean
  scheduleExpression: string
  timezone: string
  retentionCount: number
  retentionDays: number
  targetDirectoryId: string
  compression: string
  verifyAfterBackup: boolean
  capacityWarningPercent: number
  capacityCriticalPercent: number
  version: number
  consistency?: 'live' | 'quiesced'
}

export interface BackupRecord {
  id: string
  instanceResourceId: string
  policyId: string | null
  jobId: string
  status: string
  relativePath: string | null
  sizeBytes: number | null
  checksumAlgorithm: string | null
  checksum: string | null
  startedAtUtc: string
  completedAtUtc: string | null
  verifiedAtUtc: string | null
  expiresAtUtc: string | null
  errorCode: string | null
}

export interface ApplicationInstallation {
  id: string
  catalogAppId: string
  templateId: string
  templateVersion: string
  displayName: string
  installedVersion: string
  desiredVersion: string | null
  state: string
  autoUpdateEnabled: boolean
  configSummaryJson: string
  installedBySubject: string
  installedAtUtc: string
  updatedAtUtc: string
  version: number
}

export interface ApplicationResource {
  role: string
  resourceId: string
  externalId: string
  name: string
  type: string
  state: string
  version: string
  isProtected: boolean
}

export interface ApplicationUpdateRun {
  id: string
  applicationId: string
  oldImageDigest: string | null
  newImageDigest: string
  versionPolicy: string | null
  planHash: string
  jobId: string
  startedAtUtc: string
  completedAtUtc: string | null
  result: string
  wasRolledBack: boolean
  errorSummary: string | null
}

export interface ApplicationDetail {
  installation: ApplicationInstallation
  resources: ApplicationResource[]
  updateHistory: ApplicationUpdateRun[]
}

export interface ServiceEntry {
  id: string
  name: string
  description: string
  url: string
  status: string
  latencyMilliseconds: number | null
  checkedAtUtc: string
}

export interface AgentCapabilities {
  agentVersion: string
  protocolMajor: number
  protocolMinor: number
  capabilities: string[]
}

export interface AgentHealth {
  available: boolean
  dockerAvailable: boolean
  systemdAvailable: boolean
  status: string
  observedAtUtc: string
}

export interface AgentStatus {
  capabilities: AgentCapabilities
  health: AgentHealth
  currentJobs: Job[]
}
