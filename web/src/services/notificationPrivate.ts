import { apiRequest, createIdempotencyKey } from '@/services/apiClient'
import {
  executeNotificationIntent,
  notificationIntent,
  type NotificationProvider,
  type NotificationWriteIntent,
} from '@/services/notificationManagement'

export interface PrivateMetadata {
  channelVersion?: number | null
  hasTarget: boolean
  hasCredential: boolean
  revision: string
}
export interface PrivateStatus {
  jobId: string
  state: string
  resultCode: string | null
  version: number
  canAcknowledgeUnknown: boolean
}
export interface PrivatePlan {
  planHash: string
  expiresAtUtc: string
  warning: string
}
export interface PrivateJob {
  id: string
  state: string
  phase: string
  errorCode: string | null
}
export interface PrivateForm {
  url: string
  smtpHost: string
  smtpPort: number
  tls: 'StartTls' | 'SslOnConnect'
  from: string
  to: string
  allowedAddresses: string
  allowPrivateNetwork: boolean
  allowHttp: boolean
  minimumIntervalSeconds: number
  credentialMode: 'keep' | 'replace' | 'remove'
  bearerToken: string
  signingSecret: string
  username: string
  password: string
}
export function emptyPrivateForm(): PrivateForm {
  return {
    url: '',
    smtpHost: '',
    smtpPort: 587,
    tls: 'StartTls',
    from: '',
    to: '',
    allowedAddresses: '',
    allowPrivateNetwork: false,
    allowHttp: false,
    minimumIntervalSeconds: 5,
    credentialMode: 'remove',
    bearerToken: '',
    signingSecret: '',
    username: '',
    password: '',
  }
}
const list = (value: string) =>
  value
    .split(/[,，\n]/)
    .map((item) => item.trim())
    .filter(Boolean)

/** No existing private value is returned or substituted into a replacement. */
export function privatePayload(provider: NotificationProvider, form: PrivateForm) {
  const addresses = list(form.allowedAddresses)
  if (
    !Number.isInteger(form.minimumIntervalSeconds) ||
    form.minimumIntervalSeconds < 5 ||
    form.minimumIntervalSeconds > 300 ||
    addresses.length > 8
  )
    throw new Error('间隔需为 5–300 秒，批准地址最多 8 个。')
  if (form.allowPrivateNetwork && !addresses.length)
    throw new Error('允许私网时必须显式填写批准 IP 地址。')
  const target: Record<string, unknown> = {
    provider,
    allowedAddresses: addresses,
    allowPrivateNetwork: form.allowPrivateNetwork,
    allowHttp: form.allowHttp,
    minimumIntervalSeconds: form.minimumIntervalSeconds,
  }
  if (provider === 'Email') {
    if (
      !form.smtpHost.trim() ||
      !form.from.trim() ||
      !list(form.to).length ||
      list(form.to).length > 10 ||
      !Number.isInteger(form.smtpPort) ||
      form.smtpPort < 1 ||
      form.smtpPort > 65535
    )
      throw new Error('请填写 SMTP 主机、有效端口、发件人及 1–10 个收件人。')
    if (form.allowHttp) throw new Error('邮件只允许 STARTTLS 或直接 TLS。')
    Object.assign(target, {
      smtpHost: form.smtpHost.trim(),
      smtpPort: form.smtpPort,
      tls: form.tls,
      from: form.from.trim(),
      to: list(form.to),
    })
  } else {
    let uri: URL
    try {
      uri = new URL(form.url.trim())
    } catch {
      throw new Error('请填写完整的 HTTP/HTTPS 地址。')
    }
    if (!['https:', 'http:'].includes(uri.protocol) || uri.username || uri.password || uri.hash)
      throw new Error('地址不能包含用户名、密码或片段。')
    if (uri.protocol === 'http:' && (!form.allowHttp || !form.allowPrivateNetwork))
      throw new Error('HTTP 仅用于显式批准的私网目标。')
    if (provider !== 'Webhook' && (uri.protocol !== 'https:' || (uri.port && uri.port !== '443')))
      throw new Error('机器人必须使用官方 HTTPS 端点。')
    target.url = form.url.trim()
  }
  let credential: Record<string, string> | undefined
  if (form.credentialMode === 'replace') {
    if (provider === 'Email') {
      if (!form.username || !form.password)
        throw new Error('替换 SMTP 凭据需重新填写用户名与密码。')
      credential = { username: form.username, password: form.password }
    } else if (provider === 'DingTalk' || provider === 'Feishu') {
      if (!form.signingSecret) throw new Error('替换机器人凭据需重新填写签名密钥。')
      credential = { signingSecret: form.signingSecret }
    } else if (provider === 'Webhook') {
      if (!form.bearerToken && !form.signingSecret)
        throw new Error('替换凭据需填写 Bearer Token 或签名密钥。')
      credential = {
        ...(form.bearerToken ? { bearerToken: form.bearerToken } : {}),
        ...(form.signingSecret ? { signingSecret: form.signingSecret } : {}),
      }
    } else throw new Error('企业微信的密钥位于私有目标地址中，不使用独立凭据。')
  }
  return { target, credentialMode: form.credentialMode, ...(credential ? { credential } : {}) }
}

export async function preparePrivate(
  channelId: string,
  version: number,
  action: 'apply' | 'remove',
  payload: object | null,
  signal: AbortSignal,
) {
  let ticket: string | null = null
  if (action === 'apply') {
    const staged = await apiRequest<{ token: string }>('secrets/stage', {
      method: 'POST',
      body: JSON.stringify({ value: JSON.stringify(payload), generate: false }),
      headers: { 'Idempotency-Key': createIdempotencyKey() },
      signal,
    })
    ticket = staged.token
  }
  const plan = await apiRequest<PrivatePlan>(
    `notifications/manage/channels/${channelId}/private/plan`,
    {
      method: 'POST',
      body: JSON.stringify({ expectedChannelVersion: version, action, inputTicket: ticket }),
      headers: { 'Idempotency-Key': createIdempotencyKey() },
      signal,
    },
  )
  return { plan, ticket }
}
export function privateSubmitIntent(
  channelId: string,
  plan: PrivatePlan,
  ticket: string | null,
): NotificationWriteIntent {
  return notificationIntent(`channels/${channelId}/private`, 'POST', {
    planHash: plan.planHash,
    inputTicket: ticket,
    confirmed: true,
  })
}
export function submitPrivate(intent: NotificationWriteIntent, signal: AbortSignal) {
  return executeNotificationIntent<PrivateJob>(intent, signal)
}
