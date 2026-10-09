/**
 * Abonementlar analitikasining davr filtri (mijoz, 2026-10-09: "bunda vaqt filterlari ham
 * muammosiz ishlasin"): "Bu oy", "O'tgan oy", "O'quv yili" va qo'lda tanlanadigan sana oralig'i.
 *
 * Tugmalar BUTUN OYLARNI oladi: hisob-faktura oylik, ya'ni oy o'rtasida kelgan o'quvchi ham
 * shu oyning abonementchisi — davr bugun bilan kesilsa, uning fakturasi hisobda bo'lardi-yu,
 * o'zi o'quvchilar sonida bo'lmasdi.
 *
 * Davr URL'da (`?period=prev`, `?period=custom&from=…&to=…`) — sinf sahifasiga o'tganda ham,
 * orqaga qaytganda ham, havola yuborilganda ham o'sha davr qoladi. Standart — "Bu oy".
 * Qo'lda tanlangan davr serverdagi chegaradan (`SubscriptionAnalyticsQuery.MaxDays`) oshmaydi.
 */
import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { addDays, daysBetween, displayDate, monthEnd, monthStart, tashkentToday } from '../../dashboardPeriod'

export type SubscriptionPreset = 'month' | 'prev' | 'year'
export type SubscriptionPeriodKind = SubscriptionPreset | 'custom'

export const SUBSCRIPTION_PRESETS: { key: SubscriptionPreset; label: string }[] = [
  { key: 'month', label: 'Bu oy' },
  { key: 'prev', label: "O'tgan oy" },
  { key: 'year', label: "O'quv yili" },
]

/** Serverdagi `SubscriptionAnalyticsQuery.MaxDays` bilan bir xil. */
export const MAX_PERIOD_DAYS = 400

/** O'quv yili sentyabrda boshlanadi. */
function academicYearStart(today: string): string {
  const [y, m] = today.split('-').map(Number)
  return `${m >= 9 ? y : y - 1}-09-01`
}

export function subscriptionPresetRange(preset: SubscriptionPreset, today: string): { from: string; to: string } {
  if (preset === 'prev') {
    const from = monthStart(today, 1)
    return { from, to: monthEnd(from) }
  }
  if (preset === 'year') return { from: academicYearStart(today), to: monthEnd(today) }
  return { from: monthStart(today), to: monthEnd(today) }
}

const isIso = (v: string | null): v is string => !!v && /^\d{4}-\d{2}-\d{2}$/.test(v)

export interface SubscriptionPeriod {
  kind: SubscriptionPeriodKind
  from: string
  to: string
  /** "01.10.2026 – 31.10.2026" */
  label: string
}

export function useSubscriptionPeriod() {
  const [params, setParams] = useSearchParams()
  const [today] = useState(() => tashkentToday())

  const raw = params.get('period')
  const rawFrom = params.get('from')
  const rawTo = params.get('to')

  const period = useMemo<SubscriptionPeriod>(() => {
    let kind: SubscriptionPeriodKind = raw === 'prev' || raw === 'year' ? raw : 'month'
    let range = subscriptionPresetRange(kind as SubscriptionPreset, today)
    if (
      raw === 'custom' &&
      isIso(rawFrom) &&
      isIso(rawTo) &&
      rawFrom <= rawTo &&
      daysBetween(rawFrom, rawTo) <= MAX_PERIOD_DAYS
    ) {
      kind = 'custom'
      range = { from: rawFrom, to: rawTo }
    }
    const { from, to } = range
    const label = from === to ? displayDate(from) : `${displayDate(from)} – ${displayDate(to)}`
    return { kind, from, to, label }
  }, [raw, rawFrom, rawTo, today])

  const write = useCallback(
    (next: Record<string, string | null>) =>
      setParams(
        (prev) => {
          const p = new URLSearchParams(prev)
          for (const [k, v] of Object.entries(next)) {
            if (v) p.set(k, v)
            else p.delete(k)
          }
          return p
        },
        { replace: true },
      ),
    [setParams],
  )

  const setPreset = useCallback(
    (preset: SubscriptionPreset) => write({ period: preset === 'month' ? null : preset, from: null, to: null }),
    [write],
  )

  const setRange = useCallback(
    (from: string, to: string) => {
      if (!isIso(from) || !isIso(to) || from > to) return
      write({ period: 'custom', from, to })
    },
    [write],
  )

  // Boshlanish ≤ tugash va oraliq MAX_PERIOD_DAYS dan oshmaydi.
  const fromBounds = { min: addDays(period.to, -(MAX_PERIOD_DAYS - 1)), max: period.to }
  const toBounds = { min: period.from, max: addDays(period.from, MAX_PERIOD_DAYS - 1) }

  /** Sahifalar orasida davrni olib yuradigan query (`?period=…`). */
  const search = useMemo(() => {
    const p = new URLSearchParams()
    for (const k of ['period', 'from', 'to']) {
      const v = params.get(k)
      if (v) p.set(k, v)
    }
    const s = p.toString()
    return s ? `?${s}` : ''
  }, [params])

  return { period, setPreset, setRange, fromBounds, toBounds, search }
}
