import { apiRequest, apiUrl } from '@/services/apiClient'
import { maximumFileBytes, readFileDownloadResponse } from '@/services/fileManagement'
import type { FileEntry, FilePage } from '@/services/fileManagement'

export interface ContainerFileTarget {
  containerId: string
  name: string
  instanceId: string
  imageId: string
  configuredUser: string
}

export function containerFileBase(id: string) {
  if (!/^[a-f0-9]{64}$/.test(id)) throw new Error('需要完整容器 ID')
  return `containers/${id}/files`
}
export function containerFileQuery(
  target: ContainerFileTarget,
  path: string,
  extra: Record<string, string> = {},
) {
  containerFileBase(target.containerId)
  if (
    !/^[1-9][0-9]{0,9}:[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,9})?Z$/.test(
      target.instanceId,
    )
  )
    throw new Error('容器实例已失效，请重新连接')
  return new URLSearchParams({ ...extra, instanceId: target.instanceId, path }).toString()
}
export function inspectContainerFileTarget(id: string, signal: AbortSignal) {
  return apiRequest<ContainerFileTarget>(`${containerFileBase(id)}/target`, { signal })
}
export function listContainerFiles(
  target: ContainerFileTarget,
  path: string,
  after: string,
  signal: AbortSignal,
) {
  return apiRequest<FilePage>(
    `${containerFileBase(target.containerId)}/entries?${containerFileQuery(target, path, { after, take: '100' })}`,
    { signal },
  )
}
export function canDownloadContainerFile(entry: FileEntry) {
  return (
    entry.kind === 'File' &&
    !entry.isProtected &&
    Number.isSafeInteger(entry.sizeBytes) &&
    entry.sizeBytes >= 0 &&
    entry.sizeBytes <= maximumFileBytes &&
    /^[A-F0-9]{64}$/.test(entry.version)
  )
}
export async function downloadContainerFile(
  target: ContainerFileTarget,
  path: string,
  entry: FileEntry,
  signal: AbortSignal,
  progress: (percent: number) => void,
) {
  if (!canDownloadContainerFile(entry)) throw new Error('该项目受保护或超过下载上限')
  const response = await fetch(
    apiUrl(
      `${containerFileBase(target.containerId)}/download?${containerFileQuery(target, path, { expectedVersion: entry.version })}`,
    ),
    { credentials: 'include', signal, headers: { Accept: 'application/octet-stream' } },
  )
  return readFileDownloadResponse(response, entry, progress)
}
