import { Buffer } from 'node:buffer'
import { installTerminalFixture } from './terminal.mjs'
import {
  relationshipContainers,
  relationshipInspect,
  relationshipResponse,
} from './relationships.mjs'

// Synthetic UI only. Real Cookie/BFF/UDS acceptance remains a separate gate.
export async function installContainerFilesFixture(page, options = {}) {
  const session = await installTerminalFixture(page, options)
  const target = {
    containerId: 'a'.repeat(64),
    instanceId: '777:2026-10-22T01:02:03.123456789Z',
    name: 'demo-web',
    imageId: 'sha256:' + 'b'.repeat(64),
    configuredUser: '1000:1000',
  }
  const file = {
    name: '中文.bin',
    kind: 'File',
    sizeBytes: 3,
    modifiedAtUnixMs: 0,
    version: 'A'.repeat(64),
    mode: '0640',
    isProtected: false,
  }
  const longName = 'a-' + '长文件名'.repeat(18) + '.bin'
  const state = {
    requests: [],
    targetReads: 0,
    entriesStatus: 200,
    moreChanged: false,
    target,
    file,
    longName,
    pauseEntries: false,
    pauseDownload: false,
    entriesWaiting: false,
    downloadWaiting: false,
    releaseEntries: () => {},
    releaseDownload: () => {},
  }
  await page.route('**/api/v1/containers**', async (route) => {
    const url = new URL(route.request().url()),
      path = url.pathname
    const fulfill = (json, status = 200) => route.fulfill({ status, json })
    if (path.includes('/files/')) {
      state.requests.push({
        path,
        query: Object.fromEntries(url.searchParams),
        method: route.request().method(),
      })
      if (path.endsWith('/target')) {
        state.targetReads++
        return fulfill(state.target)
      }
      if (path.endsWith('/entries')) {
        if (state.pauseEntries) {
          state.entriesWaiting = true
          await new Promise((resolve) => {
            state.releaseEntries = resolve
          })
          if (route.request().failure()) return
        }
        if (state.entriesStatus !== 200)
          return fulfill(
            { title: 'Fixture changed', code: 'AGENT_PRECONDITION_FAILED' },
            state.entriesStatus,
          )
        const current = url.searchParams.get('path'),
          more = !!url.searchParams.get('after')
        return fulfill({
          entries: current
            ? [file]
            : more
              ? [{ ...file, name: '附录.txt' }]
              : [
                  { ...file, name: '.env', isProtected: true },
                  { ...file, name: longName },
                  { ...file, name: 'link', kind: 'Symlink', isProtected: true },
                  { ...file, name: '资料', kind: 'Directory' },
                ],
          nextCursor: current || more ? '' : '资料',
          version: (more && state.moreChanged ? 'D' : 'C').repeat(64),
        })
      }
      if (path.endsWith('/download')) {
        if (state.pauseDownload) {
          state.downloadWaiting = true
          await new Promise((resolve) => {
            state.releaseDownload = resolve
          })
          if (route.request().failure()) return
        }
        return route.fulfill({
          contentType: 'application/octet-stream',
          body: Buffer.from([1, 2, 3]),
          headers: { 'Content-Length': '3' },
        })
      }
    }
    if (path.endsWith('/containers'))
      return fulfill(
        relationshipContainers.map((item, index) => ({
          ...item,
          id: (index ? 'c' : 'a').repeat(64),
        })),
      )
    if (path.endsWith('/inspect'))
      return fulfill({ ...relationshipInspect, id: target.containerId })
    return fulfill(relationshipResponse(path))
  })
  return { state, session }
}
