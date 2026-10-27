import { sha256 } from '@noble/hashes/sha2.js'
import { downloadContainerFile } from '@/services/containerFiles'
import type { ContainerFileTarget } from '@/services/containerFiles'
import { validContainerUploadLocation } from '@/services/containerUploads'
import type { ContainerUploadLocation } from '@/services/containerUploads'
import type { FileEntry } from '@/services/fileManagement'
import {
  maximumTextBytes,
  prepareTextDocument,
  textEligible,
  textLineEnding,
  validateTextDocument,
} from '@/services/fileText'
import type { TextDocumentSnapshot, TextLineEnding } from '@/services/fileText'

export interface ContainerTextSnapshot extends TextDocumentSnapshot {
  location: ContainerUploadLocation
}
export interface PreparedContainerText {
  file: File
  location: ContainerUploadLocation
  expectedVersion: string
  draft: { snapshot: ContainerTextSnapshot; text: string; ending: TextLineEnding; bom: boolean }
}

export async function readContainerText(
  target: ContainerFileTarget,
  path: string,
  entry: FileEntry,
  signal: AbortSignal,
): Promise<ContainerTextSnapshot> {
  const location = { containerId: target.containerId, instanceId: target.instanceId, path }
  if (
    !validContainerUploadLocation(location) ||
    !textEligible(entry) ||
    typeof entry.version !== 'string' ||
    entry.version.length !== 64 ||
    !/^[A-F0-9]{64}$/.test(entry.version)
  )
    throw new Error('只支持未受保护的普通文件，UTF-8 文本上限 1 MiB')
  const frozenVersion = entry.version
  const frozenEntry = { ...entry }
  const blob = await downloadContainerFile(target, path, frozenEntry, signal, () => {})
  signal.throwIfAborted()
  if (blob.size > maximumTextBytes || blob.size !== frozenEntry.sizeBytes)
    throw new Error('文本大小已变化，请重新打开')
  const bytes = new Uint8Array(await blob.arrayBuffer())
  const utf8Bom = bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf
  let text: string
  try {
    text = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(
      utf8Bom ? bytes.subarray(3) : bytes,
    )
  } catch {
    throw new Error('文件不是完整有效的 UTF-8 文本，请下载后在本地编辑')
  }
  const snapshot: ContainerTextSnapshot = {
    location,
    path,
    version: frozenVersion,
    sizeBytes: bytes.length,
    sha256: Array.from(sha256(bytes), (byte) => byte.toString(16).padStart(2, '0'))
      .join('')
      .toUpperCase(),
    text,
    utf8Bom,
    lineEnding: textLineEnding(text),
  }
  signal.throwIfAborted()
  return validateTextDocument(snapshot, path, frozenVersion)
}

export function prepareContainerText(
  snapshot: ContainerTextSnapshot,
  text: string,
  ending: TextLineEnding,
  bom: boolean,
): PreparedContainerText {
  if (!validContainerUploadLocation(snapshot.location) || snapshot.location.path !== snapshot.path)
    throw new Error('容器文本目标已变化，请重新打开')
  validateTextDocument(snapshot, snapshot.path, snapshot.version)
  const result = prepareTextDocument(snapshot, text, ending, bom)
  return {
    file: result.file,
    location: { ...snapshot.location },
    expectedVersion: result.expectedVersion,
    draft: { snapshot: { ...snapshot, location: { ...snapshot.location } }, text, ending, bom },
  }
}
