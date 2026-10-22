import { EventEmitter } from 'node:events'
import assert from 'node:assert/strict'
import { test } from 'node:test'
import { observeDownload } from './browser_download_probe.mjs'

const path = `/api/v1/containers/${'a'.repeat(64)}/files/download`
function fixture() {
  const session = new EventEmitter()
  const observer = observeDownload(session, path)
  const start = (requestId = 'owned', pathname = path) => session.emit(
    'Network.requestWillBeSent', { requestId, request: { url: 'http://example.invalid' + pathname } })
  const response = (length = '67108871') => session.emit('Network.responseReceived', {
    requestId: 'owned', response: { status: 200, headers: { 'Content-Length': length } },
  })
  return { session, observer, start, response }
}
test('no event, snapshots and disposal never synthesize a terminal result', () => {
  const { observer } = fixture()
  assert.equal(observer.snapshot().seen, false)
  observer.dispose()
  assert.equal(observer.snapshot().terminal, null)
})
test('only exact path selects a request and no private URL data is retained', () => {
  const { session, observer, start, response } = fixture()
  start('other', path + '/different')
  assert.equal(observer.snapshot().seen, false)
  start('owned', path + '?path=private-name&secret=never-retain')
  response()
  session.emit('Network.dataReceived', { requestId: 'other', dataLength: 500 })
  session.emit('Network.loadingFailed', { requestId: 'other', errorText: 'net::ERR_ABORTED' })
  assert.equal(observer.snapshot().declaredBytes, 67108871)
  assert.equal(observer.snapshot().receivedBodyBytes, 0)
  assert.equal(observer.snapshot().terminal, null)
  assert.doesNotMatch(JSON.stringify(observer.snapshot()), /secret|private-name|http|owned/)
})
test('real loadingFailed is distinct from complete reception, including partial byte count', () => {
  const { session, observer, start, response } = fixture()
  start(); response()
  session.emit('Network.dataReceived', { requestId: 'owned', dataLength: 12345 })
  session.emit('Network.loadingFailed', { requestId: 'owned', errorText: 'net::ERR_ABORTED', canceled: true })
  assert.deepEqual(observer.snapshot(), { seen: true, responseStatus: 200,
    declaredBytes: 67108871, receivedBodyBytes: 12345, encodedBytes: null,
    terminal: 'failed', networkError: 'net::ERR_ABORTED', canceled: true, invalid: false })
  session.emit('Network.loadingFinished', { requestId: 'owned', encodedDataLength: 67108871 })
  session.emit('Network.dataReceived', { requestId: 'owned', dataLength: 67108871 })
  assert.equal(observer.snapshot().receivedBodyBytes, 12345)
  assert.equal(observer.snapshot().terminal, 'failed')
})
test('loadingFinished records actual receipt without claiming content digest validation', () => {
  const { session, observer, start, response } = fixture()
  start(); response('3')
  session.emit('Network.dataReceived', { requestId: 'owned', dataLength: 3 })
  session.emit('Network.loadingFinished', { requestId: 'owned', encodedDataLength: 440 })
  assert.equal(observer.snapshot().terminal, 'finished')
  assert.equal(observer.snapshot().receivedBodyBytes, 3)
  assert.equal(observer.snapshot().encodedBytes, 440)
  assert.equal(observer.snapshot().invalid, false)
})
test('duplicate matching requests cannot replace the original request identity', () => {
  const { session, observer, start, response } = fixture()
  start(); start('replacement'); response()
  session.emit('Network.loadingFailed', { requestId: 'replacement', errorText: 'net::ERR_ABORTED' })
  assert.equal(observer.snapshot().terminal, null)
})
test('invalid paths are rejected before any listener is added', () => {
  const session = new EventEmitter()
  for (const invalid of ['/arbitrary', path + '?query=1', '/api/v1/containers/short/files/download'])
    assert.throws(() => observeDownload(session, invalid), /invalid-download-observer-path/)
  assert.equal(session.eventNames().length, 0)
})
test('malformed or over-budget lengths mark evidence invalid without crashing the event handler', () => {
  for (const bad of ['-1', '1.5', '1073741825', 'private-content']) {
    const { observer, start, response } = fixture()
    start(); response(bad)
    assert.equal(observer.snapshot().invalid, true)
    assert.equal(observer.snapshot().declaredBytes, null)
  }
  const missing = fixture()
  missing.start()
  missing.session.emit('Network.responseReceived', {
    requestId: 'owned', response: { status: 200, headers: {} },
  })
  assert.equal(missing.observer.snapshot().invalid, true)
  for (const bad of [-1, 0.5, 1073741825, NaN]) {
    const { session, observer, start } = fixture()
    start(); session.emit('Network.dataReceived', { requestId: 'owned', dataLength: bad })
    assert.equal(observer.snapshot().invalid, true)
    assert.equal(observer.snapshot().receivedBodyBytes, 0)
  }
})
test('untrusted network error text is never exposed, disposal removes all own listeners', () => {
  const { session, observer, start } = fixture()
  start()
  session.emit('Network.loadingFailed', { requestId: 'owned', errorText: 'private URL cookie body' })
  assert.equal(observer.snapshot().networkError, 'unclassified-network-error')
  const snapshot = observer.snapshot(); snapshot.terminal = 'forged'
  assert.equal(observer.snapshot().terminal, 'failed')
  observer.dispose(); observer.dispose()
  assert.equal(session.eventNames().length, 0)
})
