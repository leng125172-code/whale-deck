// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { packWorkspaceSources } from '../workspaceSourceTar'
import { identifySource, sourceArchiveLimit } from '../workspaceSourcePayload'

function file(name: string, relative = '', data = 'content', lastModified = 0) {
  const result = new File([data], name, { lastModified })
  Object.defineProperty(result, 'webkitRelativePath', { value: relative })
  return result
}

describe('workspace source file packaging', () => {
  it('produces identical bytes for a reselected tree with different picker order and timestamps', async () => {
    const first = packWorkspaceSources(
      [file('index.js', 'project/src/index.js'), file('index.js', 'project/test/index.js', 'test')],
      true,
    )
    const second = packWorkspaceSources(
      [
        file('index.js', 'renamed/test/index.js', 'test', 42),
        file('index.js', 'renamed/src/index.js', 'content', 43),
      ],
      true,
    )
    expect(await first.arrayBuffer()).toEqual(await second.arrayBuffer())
    const signal = new AbortController().signal
    expect(await identifySource(first, 'tar', signal)).toEqual(
      await identifySource(second, 'tar', signal),
    )
    const bytes = new Uint8Array(await first.arrayBuffer())
    const path = (offset: number) =>
      new TextDecoder().decode(bytes.subarray(offset, offset + 100)).split('\0')[0]
    expect(path(0)).toBe('src/index.js')
    expect(path(1024)).toBe('test/index.js')
  })

  it('writes a UTF-8 PAX path and preserves empty files', async () => {
    const leaf = '目录'.repeat(19) + '.txt'
    const name = 'sub/' + leaf
    const tar = packWorkspaceSources([file(leaf, 'project/' + name, '')], true)
    const bytes = new Uint8Array(await tar.arrayBuffer())
    expect(bytes[156]).toBe('x'.charCodeAt(0))
    const size = Number.parseInt(new TextDecoder().decode(bytes.subarray(124, 135)), 8)
    const record = new TextDecoder().decode(bytes.subarray(512, 512 + size))
    expect(record).toBe(`${size} path=${name}\n`)
    expect(bytes[1024 + 156]).toBe('0'.charCodeAt(0))
    expect(bytes.subarray(1536)).toEqual(new Uint8Array(1024))
  })

  it('rejects duplicates, ambiguous directories and protected paths before sending any bytes', () => {
    expect(() => packWorkspaceSources([file('main.js'), file('main.js')], false)).toThrow(/同名/)
    expect(() =>
      packWorkspaceSources([file('src', 'p/src'), file('main.js', 'p/src/main.js')], true),
    ).toThrow(/冲突/)
    expect(() =>
      packWorkspaceSources([file('main.js', 'p/src/main.js'), file('src', 'p/src')], true),
    ).toThrow(/冲突/)
    expect(() => packWorkspaceSources([file('a', 'p/a'), file('b', 'q/b')], true)).toThrow(
      /单个目录/,
    )
    expect(() => packWorkspaceSources([file('a', 'p/../a')], true)).toThrow(/无效/)
    expect(() => packWorkspaceSources([file('config', '.git/config')], true)).toThrow(/受保护/)
    expect(() => packWorkspaceSources([file('.env')], false)).toThrow(/受保护/)
    expect(packWorkspaceSources([file('.env.example')], false).size).toBe(2048)
  })

  it('accounts for TAR headers and padding in the archive byte limit', () => {
    const oversized = file('large.bin')
    Object.defineProperty(oversized, 'size', { value: sourceArchiveLimit })
    expect(() => packWorkspaceSources([oversized], false)).toThrow(/超过导入大小/)
  })
})
