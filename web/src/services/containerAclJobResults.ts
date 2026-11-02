interface AclJobResult {
  schemaVersion: number
  intentId: string
  stage: string
  state: string
  code: string
  aclType: string
  kind: string | null
  targetMode: number | null
  noChange: boolean | null
  aclConfirmed: boolean | null
  receiptState: string | null
  bindingCurrent: boolean | null
  historyMayExist: boolean
  historyVerified: boolean
  hasPublishedVersion: boolean
}
const keys = [
  'schemaVersion',
  'intentId',
  'stage',
  'state',
  'code',
  'aclType',
  'kind',
  'targetMode',
  'noChange',
  'aclConfirmed',
  'receiptState',
  'bindingCurrent',
  'historyMayExist',
  'historyVerified',
  'hasPublishedVersion',
]
export function readAclJobResult(
  json: string | null,
  jobType: string,
  state: string,
): AclJobResult | null {
  if (!json || json.length > 4096) return null
  try {
    const r = JSON.parse(json) as Record<string, unknown>
    if (
      !r ||
      Array.isArray(r) ||
      Object.keys(r).length !== keys.length ||
      !keys.every((k) => Object.prototype.hasOwnProperty.call(r, k))
    )
      return null
    if (
      r.schemaVersion !== 1 ||
      typeof r.intentId !== 'string' ||
      !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(r.intentId) ||
      r.intentId === '00000000-0000-0000-0000-000000000000'
    )
      return null
    if (
      r.stage !== (jobType === 'containers.files.acl' ? 'execution' : 'planning') ||
      ![
        'Ready',
        'Expired',
        'Succeeded',
        'Superseded',
        'Canceled',
        'Failed',
        'Unknown',
        'Acknowledged',
      ].includes(r.state as string)
    )
      return null
    if (
      state !==
      (['Ready', 'Succeeded'].includes(r.state as string)
        ? 'Succeeded'
        : r.state === 'Unknown'
          ? 'Running'
          : r.state === 'Canceled'
            ? 'Canceled'
            : 'Failed')
    )
      return null
    if (
      typeof r.code !== 'string' ||
      r.code.length > 64 ||
      !/^CONTAINER_ACL_[A-Z_]*$/.test(r.code) ||
      !['access', 'default'].includes(r.aclType as string)
    )
      return null
    if (
      ![null, 'File', 'Directory'].includes(r.kind as string | null) ||
      !(
        r.targetMode === null ||
        (typeof r.targetMode === 'number' &&
          Number.isInteger(r.targetMode) &&
          r.targetMode >= 0 &&
          r.targetMode <= 511)
      )
    )
      return null
    if (![null, 'Completed', 'Aborted', 'Unknown'].includes(r.receiptState as string | null))
      return null
    if (
      !['noChange', 'aclConfirmed', 'bindingCurrent'].every(
        (k) => r[k] === null || typeof r[k] === 'boolean',
      ) ||
      !['historyMayExist', 'historyVerified', 'hasPublishedVersion'].every(
        (k) => typeof r[k] === 'boolean',
      )
    )
      return null
    return r as unknown as AclJobResult
  } catch {
    return null
  }
}
