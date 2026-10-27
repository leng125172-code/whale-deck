import { Buffer } from 'node:buffer'
import { createHash } from 'node:crypto'
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
  const downloadBody =
    options.textContent === undefined
      ? Buffer.from([1, 2, 3])
      : Buffer.from(options.textContent, 'utf8')
  file.sizeBytes = downloadBody.length
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
    uploadWrites: [],
    uploadReads: 0,
    contentBodies: [],
    uploadMetadata: null,
    uploadAccepted: false,
    uploadCreateLost: false,
    uploadContentWaiting: false,
    releaseUploadContent: () => {},
  }
  const uploadId = '7d4fec0a-01b7-494a-9b55-e96e265503a1'
  const upload = {
    id: uploadId,
    jobId: uploadId,
    location: { containerId: target.containerId, instanceId: target.instanceId, path: '' },
    sizeBytes: 0,
    sha256: '',
    state: 'AwaitingUpload',
    expiresAtUtc: new Date(Date.now() + 900000).toISOString(),
    resultCode: null,
    version: 0,
    publishedVersion: null,
    previousRetained: false,
  }
  await page.route('**/api/v1/containers**', async (route) => {
    const url = new URL(route.request().url()),
      path = url.pathname
    const fulfill = (json, status = 200) => route.fulfill({ status, json })
    if (path.includes('/files/')) {
      const request = route.request()
      state.requests.push({
        path,
        query: Object.fromEntries(url.searchParams),
        method: request.method(),
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
                  ...(upload.state === 'Succeeded'
                    ? [
                        {
                          ...file,
                          name: upload.location.path.split('/').at(-1),
                          sizeBytes: upload.sizeBytes,
                          version: upload.publishedVersion,
                        },
                      ]
                    : []),
                ],
          nextCursor: current || more ? '' : '资料',
          version: (more && state.moreChanged ? 'D' : 'C').repeat(64),
        })
      }
      if (path.endsWith('/files/entry')) {
        const requested = url.searchParams.get('path')
        if (requested === file.name || options.overwrite) return fulfill(file)
        return fulfill({ status: 404, title: 'Not found' }, 404)
      }
      if (path.endsWith('/files/uploads/plan') && request.method() === 'POST') {
        const body = request.postDataJSON()
        state.uploadWrites.push({ path: 'plan', body, headers: request.headers() })
        state.uploadMetadata = body
        if (
          Object.keys(body).sort().join(',') !==
            'expectedVersion,instanceId,path,sha256,sizeBytes' ||
          body.instanceId !== target.instanceId
        )
          return fulfill({ status: 400 }, 400)
        return fulfill({
          planHash: 'container-upload-fixture-plan',
          expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
          overwrites: body.expectedVersion !== 'ABSENT',
          warning:
            body.expectedVersion === 'ABSENT'
              ? '新文件会先暂存校验，再由任务发布。'
              : '覆盖前必须完成 HDD 备份或受控同盘留存并校验。',
        })
      }
      if (path.endsWith('/files/uploads') && request.method() === 'POST') {
        const body = request.postDataJSON()
        state.uploadWrites.push({ path: 'create', body, headers: request.headers() })
        if (
          Object.keys(body).sort().join(',') !== 'confirmed,idempotencyKey,planHash' ||
          body.confirmed !== true ||
          body.planHash !== 'container-upload-fixture-plan' ||
          body.idempotencyKey !== request.headers()['idempotency-key']
        )
          return fulfill({ status: 400 }, 400)
        Object.assign(upload, {
          location: {
            containerId: target.containerId,
            instanceId: target.instanceId,
            path: state.uploadMetadata.path,
          },
          sizeBytes: state.uploadMetadata.sizeBytes,
          sha256: state.uploadMetadata.sha256,
        })
        state.uploadAccepted = true
        if (options.loseUploadCreateResponse && !state.uploadCreateLost) {
          state.uploadCreateLost = true
          return route.abort('failed')
        }
        return fulfill(upload, 202)
      }
      if (path.endsWith(`/files/uploads/${uploadId}/content`) && request.method() === 'PUT') {
        const body = request.postDataBuffer()
        state.uploadWrites.push({ path: 'content', body: null, headers: request.headers() })
        state.contentBodies.push(body)
        if (options.pauseUploadContent) {
          state.uploadContentWaiting = true
          await new Promise((resolve) => {
            state.releaseUploadContent = resolve
          })
          if (request.failure()) return
        }
        if (
          !body ||
          body.length !== upload.sizeBytes ||
          createHash('sha256').update(body).digest('hex').toUpperCase() !== upload.sha256 ||
          request.headers()['content-type'] !== 'application/octet-stream' ||
          request.headers()['idempotency-key'] !== upload.id
        )
          return fulfill({ status: 400 }, 400)
        upload.state = 'Ready'
        upload.version++
        if (options.loseUploadContentResponse) return route.abort('failed')
        return fulfill(upload, 202)
      }
      if (path.endsWith(`/files/uploads/${uploadId}/cancel`) && request.method() === 'POST') {
        state.uploadWrites.push({ path: 'cancel', body: null, headers: request.headers() })
        upload.state = 'Canceled'
        upload.resultCode = 'CONTAINER_UPLOAD_CANCELED'
        upload.version++
        return fulfill({ id: upload.id, state: 'CancelRequested' }, 202)
      }
      if (path.endsWith(`/files/uploads/${uploadId}`) && request.method() === 'GET') {
        state.uploadReads++
        if (upload.state === 'Ready') {
          upload.state = options.unknownUpload ? 'Unknown' : 'Publishing'
          upload.resultCode = options.unknownUpload ? 'CONTAINER_UPLOAD_RESULT_UNKNOWN' : null
          upload.version++
        } else if (upload.state === 'Publishing' && !options.holdUpload) {
          upload.state = 'Succeeded'
          upload.publishedVersion = 'E'.repeat(64)
          upload.previousRetained = state.uploadMetadata.expectedVersion !== 'ABSENT'
          upload.version++
        }
        return fulfill(upload)
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
          body: downloadBody,
          headers: { 'Content-Length': String(downloadBody.length) },
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
  return { state, session, upload }
}
