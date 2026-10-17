import { sha256 } from '@noble/hashes/sha2.js'
import type { FileEntry } from '@/services/fileManagement'

export const maximumTextBytes = 1024 ** 2
export interface FileTextSnapshot {
  rootId: string
  path: string
  version: string
  sizeBytes: number
  sha256: string
  text: string
  utf8Bom: boolean
  lineEnding: 'LF' | 'CRLF' | 'CR' | 'Mixed' | 'None'
}
export type TextLineEnding = 'LF' | 'CRLF' | 'CR' | 'Keep'
export interface PreparedFileText {
  file: File
  rootId: string
  path: string
  expectedVersion: string
}
export function normalizedText(text: string) {
  return text.replace(/\r\n|\r/g, '\n')
}
export function textLineEnding(text: string): FileTextSnapshot['lineEnding'] {
  const rest = text.replace(/\r\n/g, '')
  const crlf = text.includes('\r\n'),
    cr = rest.includes('\r'),
    lf = rest.includes('\n')
  return Number(crlf) + Number(cr) + Number(lf) > 1
    ? 'Mixed'
    : crlf
      ? 'CRLF'
      : cr
        ? 'CR'
        : lf
          ? 'LF'
          : 'None'
}
function bytes(text: string) {
  for (let index = 0; index < text.length; index++) {
    const code = text.charCodeAt(index)
    if (code === 0) throw new Error('不支持包含 NUL 的二进制内容')
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = text.charCodeAt(++index)
      if (!(next >= 0xdc00 && next <= 0xdfff))
        throw new Error('文本含无效 Unicode 字符，未准备保存')
    } else if (code >= 0xdc00 && code <= 0xdfff)
      throw new Error('文本含无效 Unicode 字符，未准备保存')
  }
  const encoded = new TextEncoder().encode(text)
  if (encoded.byteLength > maximumTextBytes)
    throw new Error('文本编辑最多支持 1 MiB，请下载后在本地编辑')
  return encoded
}
export function validateTextSnapshot(
  value: FileTextSnapshot,
  rootId: string,
  path: string,
  version: string,
) {
  if (
    !value ||
    value.rootId !== rootId ||
    value.path !== path ||
    value.version !== version ||
    typeof value.text !== 'string' ||
    typeof value.utf8Bom !== 'boolean' ||
    !Number.isSafeInteger(value.sizeBytes) ||
    value.sizeBytes < 0 ||
    value.sizeBytes > maximumTextBytes ||
    !/^[A-F0-9]{64}$/.test(value.sha256) ||
    !['LF', 'CRLF', 'CR', 'Mixed', 'None'].includes(value.lineEnding) ||
    textLineEnding(value.text) !== value.lineEnding
  )
    throw new Error('文本预览元数据无效，请刷新后重试')
  const content = bytes((value.utf8Bom ? '\uFEFF' : '') + value.text)
  const digest = Array.from(sha256(content), (item) => item.toString(16).padStart(2, '0'))
    .join('')
    .toUpperCase()
  if (content.byteLength !== value.sizeBytes || digest !== value.sha256)
    throw new Error('文本预览校验不一致，未打开编辑器')
  return value
}
export function textBytes(
  snapshot: FileTextSnapshot,
  text: string,
  ending: TextLineEnding,
  bom: boolean,
) {
  if (!['Keep', 'LF', 'CRLF', 'CR'].includes(ending) || typeof bom !== 'boolean')
    throw new Error('文本格式无效')
  let content: string
  if (ending === 'Keep') {
    if (normalizedText(snapshot.text) !== text)
      throw new Error('原文件使用混合换行，编辑后请选择 LF、CRLF 或 CR')
    content = snapshot.text
  } else {
    const newline = ending === 'CRLF' ? '\r\n' : ending === 'CR' ? '\r' : '\n'
    content = normalizedText(text).replace(/\n/g, newline)
  }
  return bytes((bom ? '\uFEFF' : '') + content)
}
export function prepareText(
  snapshot: FileTextSnapshot,
  text: string,
  ending: TextLineEnding,
  bom: boolean,
): PreparedFileText {
  const content = textBytes(snapshot, text, ending, bom)
  const name = snapshot.path.split('/').pop()
  if (!name || name === '.' || name === '..') throw new Error('文本目标路径无效')
  return {
    file: new File([content], name, { type: 'text/plain;charset=utf-8' }),
    rootId: snapshot.rootId,
    path: snapshot.path,
    expectedVersion: snapshot.version,
  }
}
export function textEligible(entry: FileEntry) {
  return (
    entry.kind === 'File' &&
    !entry.isProtected &&
    Number.isSafeInteger(entry.sizeBytes) &&
    entry.sizeBytes >= 0 &&
    entry.sizeBytes <= maximumTextBytes
  )
}
