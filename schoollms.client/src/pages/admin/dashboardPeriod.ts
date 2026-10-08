/**
 * Bosh sahifaning davr filtri (mijoz, 2026-10-08): "Bugun", "Bu hafta", "Bu oy" tugmalari va
 * qo'lda tanlanadigan sana oralig'i — butun sahifa uchun bitta.
 *
 * Davr URL'da turadi (`?period=week`, `?period=custom&from=…&to=…`): sahifa yangilansa ham,
 * havola yuborilsa ham o'sha davr ochiladi. Tugma nomi saqlanadi, sanalar emas — ertaga
 * ochilgan "Bu hafta" havolasi ertangi haftani ko'rsatadi. Standart holat — "Bugun".
 *
 * Qo'lda tanlangan davr 12 oydan oshmaydi: moliya hisobotlari (kirim/chiqim vidjetlari) bundan
 * uzun davrni rad etadi, sahifaning bir qismi xato ko'rsatmasin.
 */
import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

export type DashboardPreset = 'today' | 'week' | 'month'
export type DashboardPeriodKind = DashboardPreset | 'custom'

export const DASHBOARD_PRESETS: { key: DashboardPreset; label: string }[] = [
  { key: 'today', label: 'Bugun' },
  { key: 'week', label: 'Bu hafta' },
  { key: 'month', label: 'Bu oy' },
]

/** Qo'lda tanlangan davrning eng ko'p oylari (kalendar oylari, ikkala chekka ham kiradi). */
export const MAX_PERIOD_MONTHS = 12

const tashkentDate = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Asia/Tashkent',
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

/** Bugun Toshkentda (`yyyy-MM-dd`) — brauzer mintaqasidan qat'i nazar. */
export function tashkentToday(now: Date = new Date()): string {
  return tashkentDate.format(now)
}

/* ---------- "yyyy-MM-dd" arifmetikasi (UTC'da — kun surilmasin) ---------- */

function parse(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(Date.UTC(y ?? 1970, (m ?? 1) - 1, d ?? 1))
}

function format(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`
}

export function addDays(iso: string, days: number): string {
  const d = parse(iso)
  d.setUTCDate(d.getUTCDate() + days)
  return format(d)
}

/** `back` oy oldingi oyning birinchi kuni (`back` manfiy bo'lsa — keyingi oylar). */
export function monthStart(iso: string, back = 0): string {
  const d = parse(iso)
  return format(new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() - back, 1)))
}

/** Oyning oxirgi kuni. */
export function monthEnd(iso: string): string {
  const d = parse(iso)
  return format(new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0)))
}

/** Kalendar oylari soni, ikkala chekka ham kiradi (moliyadagi `MonthsBetween` bilan bir xil). */
export function monthsBetween(from: string, to: string): number {
  const a = parse(from)
  const b = parse(to)
  return (b.getUTCFullYear() - a.getUTCFullYear()) * 12 + b.getUTCMonth() - a.getUTCMonth() + 1
}

/** Kunlar soni, ikkala chekka ham kiradi. */
export function daysBetween(from: string, to: string): number {
  return Math.round((parse(to).getTime() - parse(from).getTime()) / 86_400_000) + 1
}

export function presetRange(preset: DashboardPreset, today: string): { from: string; to: string } {
  if (preset === 'today') return { from: today, to: today }
  if (preset === 'week') {
    // Hafta dushanbadan boshlanadi — maktab jadvali ham shunday.
    const weekday = (parse(today).getUTCDay() + 6) % 7
    return { from: addDays(today, -weekday), to: today }
  }
  return { from: monthStart(today), to: today }
}

/** "08.10.2026" */
export function displayDate(iso: string): string {
  const [y, m, d] = iso.split('-')
  return `${d}.${m}.${y}`
}

const isIso = (v: string | null): v is string => !!v && /^\d{4}-\d{2}-\d{2}$/.test(v)

export interface DashboardPeriod {
  kind: DashboardPeriodKind
  from: string
  to: string
  today: string
  /** Bitta kun tanlangan. */
  singleDay: boolean
  /** Davr kunlari soni. */
  days: number
  /** Kartalar izohi uchun: "bugun", "shu hafta", "shu oy", "01.10–08.10.2026". */
  label: string
}

export function useDashboardPeriod() {
  const [params, setParams] = useSearchParams()
  // Bir marta o'qiladi — yarim tunda davr o'zgarib, so'rovlar qayta-qayta ketmasin.
  const [today] = useState(() => tashkentToday())

  const raw = params.get('period')
  const rawFrom = params.get('from')
  const rawTo = params.get('to')

  const period = useMemo<DashboardPeriod>(() => {
    let kind: DashboardPeriodKind = raw === 'week' || raw === 'month' ? raw : 'today'
    let range = presetRange(kind as DashboardPreset, today)
    if (raw === 'custom' && isIso(rawFrom) && isIso(rawTo) && rawFrom <= rawTo) {
      kind = 'custom'
      range = { from: rawFrom, to: rawTo }
    }
    const { from, to } = range
    const label =
      kind === 'today'
        ? 'bugun'
        : kind === 'week'
          ? 'shu hafta'
          : kind === 'month'
            ? 'shu oy'
            : from === to
              ? displayDate(from)
              : `${displayDate(from).slice(0, 5)}–${displayDate(to)}`
    return { kind, from, to, today, singleDay: from === to, days: daysBetween(from, to), label }
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
    (preset: DashboardPreset) => write({ period: preset === 'today' ? null : preset, from: null, to: null }),
    [write],
  )

  /** Qo'lda tanlangan oraliq — chegaralari picker'larda (`fromBounds`/`toBounds`) ushlab turiladi. */
  const setRange = useCallback(
    (from: string, to: string) => {
      if (!isIso(from) || !isIso(to)) return
      write({ period: 'custom', from, to })
    },
    [write],
  )

  // Sana maydonlarining chegaralari: kelajak yo'q, boshlanish ≤ tugash, 12 oydan oshmaydi.
  const fromBounds = {
    min: monthStart(period.to, MAX_PERIOD_MONTHS - 1),
    max: period.to,
  }
  const toBounds = {
    min: period.from,
    max: [today, monthEnd(monthStart(period.from, -(MAX_PERIOD_MONTHS - 1)))].sort()[0],
  }

  return { period, setPreset, setRange, fromBounds, toBounds }
}
