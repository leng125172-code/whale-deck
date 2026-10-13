import { describe, expect, it } from 'vitest'
import {
  formatBytes,
  formatByteRate,
  formatBitRate,
  formatDuration,
  formatQuantity,
} from '@/services/displayUnits'

describe('Display units', () => {
  it('handles invalid percentages and durations without displaying NaN or a fabricated zero', () => {
    expect(formatQuantity(NaN, '%')).toBe('—')
    expect(formatQuantity(-1, '%')).toBe('—')
    expect(formatQuantity(125.3, '%')).toBe('125.30%')
    expect(formatQuantity(null, 'ms')).toBe('—')
    expect(formatQuantity(0.001, 'ms')).toBe('<0.01 ms')
  })
  it.each([
    [0, '0 B'],
    [1023, '1023 B'],
    [1024, '1.0 KiB'],
    [1048576, '1.0 MiB'],
    [1073741824, '1.0 GiB'],
    [16131004906, '15.02 GiB'],
    ['16131004906', '15.02 GiB'],
    [1024 ** 4, '1.0 TiB'],
    [1024 ** 5, '1.0 PiB'],
  ])('formats %s as %s', (input, expected) => expect(formatBytes(input)).toBe(expected))

  it.each([null, undefined, '', ' ', 'unknown', '-1', -1, NaN, Infinity, '0x100'])(
    'keeps %s unknown',
    (input) => {
      expect(formatBytes(input)).toBe('—')
      expect(formatBytes(input, '容量未知')).toBe('容量未知')
      expect(formatByteRate(input)).toBe('—')
      expect(formatBitRate(input)).toBe('—')
      expect(formatDuration(input)).toBe('—')
    },
  )

  it('distinguishes binary byte rates from decimal bit rates without rounding tiny rates to zero', () => {
    expect(formatByteRate(1024)).toBe('1.0 KiB/s')
    expect(formatByteRate(1024 ** 2)).toBe('1.0 MiB/s')
    expect(formatByteRate(1)).toBe('1 B/s')
    expect(formatByteRate(0)).toBe('0 B/s')
    expect(formatByteRate(0.001)).toBe('<0.01 B/s')
    expect(formatBitRate(1_000_000)).toBe('1.0 Mbps')
    expect(formatBitRate(2_500_000_000)).toBe('2.5 Gbps')
  })

  it.each([
    [0, '0 秒'],
    [0.25, '250 ms'],
    [6, '6 秒'],
    [65, '1 分 5 秒'],
    [3600, '1 小时'],
    [90061, '1 天 1 小时'],
  ])('formats durations %s as %s', (input, expected) =>
    expect(formatDuration(input)).toBe(expected),
  )
})
