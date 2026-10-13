/** Display only: API values and calculations remain in bytes, bits/s and seconds. */
type Amount = number | string | null | undefined

function amount(value: Amount): number | null {
  if (
    value == null ||
    (typeof value === 'string' && !/^\s*\+?\d+(?:\.\d+)?(?:e[+-]?\d+)?\s*$/i.test(value))
  )
    return null
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null
}

function scaled(value: number, base: number, units: readonly string[]) {
  let index = 0
  while (value >= base && index < units.length - 1) {
    value /= base
    index++
  }
  // Preserve small nonzero measurements and one decimal for scaled capacities.
  const text =
    value > 0 && value < 0.01
      ? '<0.01'
      : index === 0 && Number.isInteger(value)
        ? String(value)
        : value.toFixed(2).replace(index === 0 ? /\.?0+$/ : /0$/, '')
  return `${text} ${units[index]}`
}

export function formatBytes(value: Amount, unavailable = '—'): string {
  const parsed = amount(value)
  return parsed === null
    ? unavailable
    : scaled(parsed, 1024, ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB', 'EiB'])
}

export function formatByteRate(value: Amount, unavailable = '—'): string {
  const capacity = formatBytes(value, unavailable)
  return capacity === unavailable ? unavailable : `${capacity}/s`
}

/** Link speed is bits/s, not bytes/s; use decimal SI units. */
export function formatBitRate(value: Amount, unavailable = '—'): string {
  const parsed = amount(value)
  return parsed === null
    ? unavailable
    : scaled(parsed, 1000, ['bps', 'Kbps', 'Mbps', 'Gbps', 'Tbps'])
}

export function formatDuration(seconds: Amount, unavailable = '—'): string {
  const value = amount(seconds)
  if (value === null) return unavailable
  if (value > 0 && value < 1) return `${scaled(value * 1000, 1000, ['ms'])}`
  if (value < 60) return `${Number(value.toFixed(2))} 秒`
  const parts: string[] = []
  let remaining = Math.floor(value)
  for (const [size, unit] of [
    [86400, '天'],
    [3600, '小时'],
    [60, '分'],
    [1, '秒'],
  ] as const) {
    const count = Math.floor(remaining / size)
    if (count > 0) parts.push(`${count} ${unit}`)
    remaining %= size
  }
  return parts.slice(0, 2).join(' ')
}

export function formatQuantity(value: Amount, unit: string, digits = 2, unavailable = '—'): string {
  const parsed = amount(value)
  if (parsed === null) return unavailable
  const minimum = 10 ** -digits
  const text = parsed > 0 && parsed < minimum ? `<${minimum}` : parsed.toFixed(digits)
  return `${text}${unit === '%' ? '' : ' '}${unit}`
}
