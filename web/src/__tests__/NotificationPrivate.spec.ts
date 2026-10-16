import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  emptyPrivateForm,
  privatePayload,
  preparePrivate,
  privateSubmitIntent,
  submitPrivate,
} from '@/services/notificationPrivate'

const { request } = vi.hoisted(() => ({
  request: vi.fn<(path: string, init?: RequestInit) => Promise<unknown>>(),
}))
vi.mock('@/services/apiClient', async (original) => ({
  ...(await original<typeof import('@/services/apiClient')>()),
  apiRequest: request,
}))
beforeEach(() => vi.clearAllMocks())
describe('Private notification input and intent', () => {
  it('does not substitute empty values into credential keep/remove', () => {
    const form = {
      ...emptyPrivateForm(),
      url: 'https://example.invalid/events',
      bearerToken: 'fixture-not-a-real-token',
      credentialMode: 'keep' as const,
    }
    expect(privatePayload('Webhook', form)).toMatchObject({ credentialMode: 'keep' })
    expect(privatePayload('Webhook', form)).not.toHaveProperty('credential')
    form.bearerToken = ''
    expect(() => privatePayload('Webhook', { ...form, credentialMode: 'replace' })).toThrow(
      '替换凭据',
    )
    expect(privatePayload('Webhook', { ...form, credentialMode: 'remove' })).not.toHaveProperty(
      'credential',
    )
  })
  it('supports complete email configuration and explicit paired replacement', () => {
    const form = {
      ...emptyPrivateForm(),
      smtpHost: 'smtp.example.invalid',
      from: 'sender@example.invalid',
      to: 'one@example.invalid\ntwo@example.invalid',
      username: 'fixture',
      password: 'fixture-only',
      credentialMode: 'replace' as const,
    }
    expect(privatePayload('Email', form)).toMatchObject({
      target: {
        smtpHost: 'smtp.example.invalid',
        smtpPort: 587,
        tls: 'StartTls',
        to: ['one@example.invalid', 'two@example.invalid'],
      },
      credential: { username: 'fixture', password: 'fixture-only' },
    })
    expect(() => privatePayload('Email', { ...form, password: '' })).toThrow('用户名与密码')
  })
  it.each(['DingTalk', 'Feishu'] as const)(
    'requires explicit %s signing replacement',
    (provider) => {
      const form = {
        ...emptyPrivateForm(),
        url: 'https://example.invalid/',
        credentialMode: 'replace' as const,
      }
      expect(() => privatePayload(provider, form)).toThrow('签名密钥')
      expect(privatePayload(provider, { ...form, signingSecret: 'fixture-only' })).toHaveProperty(
        'credential.signingSecret',
        'fixture-only',
      )
    },
  )
  it('does not allow arbitrary private HTTP without approved address and flags', () => {
    const form = { ...emptyPrivateForm(), url: 'http://192.168.1.20/events' }
    expect(() => privatePayload('Webhook', form)).toThrow('HTTP')
    expect(() => privatePayload('Webhook', { ...form, allowPrivateNetwork: true })).toThrow(
      '批准 IP',
    )
    expect(
      privatePayload('Webhook', {
        ...form,
        allowPrivateNetwork: true,
        allowHttp: true,
        allowedAddresses: '192.168.1.20',
      }),
    ).toHaveProperty('target.allowedAddresses', ['192.168.1.20'])
  })
  it('freezes opaque submit body and key with no automatic write retry', async () => {
    const plan = {
      planHash: 'opaque-plan',
      expiresAtUtc: new Date(Date.now() + 300000).toISOString(),
      warning: 'pause',
    }
    const intent = privateSubmitIntent('channel-id', plan, 'fixture-ticket')
    plan.planHash = 'mutated'
    request
      .mockRejectedValueOnce(new Error('fixture reply lost'))
      .mockResolvedValueOnce({ id: 'job-id' })
    await expect(submitPrivate(intent, new AbortController().signal)).rejects.toThrow(
      'fixture reply lost',
    )
    expect(request).toHaveBeenCalledTimes(1)
    await submitPrivate(intent, new AbortController().signal)
    expect(request.mock.calls[0]![1]!.body).toEqual(request.mock.calls[1]![1]!.body)
    expect(request.mock.calls[0]![1]!.headers).toEqual(request.mock.calls[1]![1]!.headers)
    expect(JSON.parse(intent.body)).toEqual({
      planHash: 'opaque-plan',
      inputTicket: 'fixture-ticket',
      confirmed: true,
    })
  })
  it('removal does not stage any private input and preparation does not submit a job', async () => {
    request.mockResolvedValue({ planHash: 'opaque', warning: 'pause', expiresAtUtc: '2026-10-15' })
    const result = await preparePrivate('channel', 7, 'remove', null, new AbortController().signal)
    expect(result.ticket).toBeNull()
    expect(request).toHaveBeenCalledTimes(1)
    expect(request.mock.calls[0]![0]).toContain('/private/plan')
    expect(JSON.parse(request.mock.calls[0]![1]!.body as string)).toEqual({
      expectedChannelVersion: 7,
      action: 'remove',
      inputTicket: null,
    })
  })
})
