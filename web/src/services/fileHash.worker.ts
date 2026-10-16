import { sha256 } from '@noble/hashes/sha2.js'

// No SubtleCrypto requirement: deployed HTTP/IP is not a secure context.
// Incremental worker hashing avoids whole-file JS buffers and blocking the UI.
self.onmessage = async (event: MessageEvent<File>) => {
  const hash = sha256.create()
  try {
    const file = event.data
    let last = -1
    for (let offset = 0; offset < file.size; offset += 1024 * 1024) {
      hash.update(new Uint8Array(await file.slice(offset, offset + 1024 * 1024).arrayBuffer()))
      const percent = Math.floor((Math.min(file.size, offset + 1024 * 1024) / file.size) * 100)
      if (percent !== last) {
        self.postMessage({ progress: percent })
        last = percent
      }
    }
    const digest = Array.from(hash.digest(), (byte) => byte.toString(16).padStart(2, '0'))
      .join('')
      .toUpperCase()
    self.postMessage({ digest })
  } catch {
    self.postMessage({ failed: true })
  } finally {
    hash.destroy()
  }
}
