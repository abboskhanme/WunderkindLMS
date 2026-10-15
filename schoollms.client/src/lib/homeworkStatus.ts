/**
 * Homework completion statuses (docs/modules/homework-completion.md, client 2026-10-15).
 *
 * The ONE place on the web side that knows what `JournalEntry.homework` means. The server twin is
 * `SchoolLms.Domain/HomeworkStatus.cs`; the Mini App keeps a JS copy in
 * `pages/miniapp/ui-tg/src/lib/homework.js` — change all three together.
 *
 * 1 and 2 predate this feature and production rows carry them (the old "Qildi" / "Qilmadi"), so their
 * meaning is frozen. Symbols follow the client's literal order "+ / - / *" = bajardi / chala /
 * umuman yo'q; K and Y are the initials of "kech qoldi" and "kelmadi". Screens always show the legend
 * with the words, so the meaning never depends on the symbol alone.
 */

export const HW_NONE = 0
export const HW_DONE = 1
export const HW_NOT_DONE = 2
export const HW_PARTIAL = 3
export const HW_LATE = 4
export const HW_ABSENT = 5

export interface HomeworkStatusMeta {
  value: number
  /** Short mark for a table cell */
  symbol: string
  /** The word — legend, tooltips, exports */
  label: string
  /** Text colour of the symbol */
  text: string
  /** Selected chip / filled cell */
  solid: string
  /** Light background of a filled cell */
  soft: string
  /** Keys that set this status in the table (lower case) */
  keys: readonly string[]
}

/** Legend order — the client's: + − * K Y. Tailwind classes are spelled out for the JIT. */
export const HOMEWORK_STATUSES: readonly HomeworkStatusMeta[] = [
  {
    value: HW_DONE,
    symbol: '+',
    label: 'Bajardi',
    text: 'text-emerald-600',
    solid: 'bg-emerald-500 text-white ring-emerald-500',
    soft: 'bg-emerald-50',
    keys: ['+', '='],
  },
  {
    value: HW_PARTIAL,
    symbol: '−',
    label: 'Chala',
    text: 'text-amber-600',
    solid: 'bg-amber-400 text-white ring-amber-400',
    soft: 'bg-amber-50',
    keys: ['-', '−'],
  },
  {
    value: HW_NOT_DONE,
    symbol: '*',
    label: "Umuman yo'q",
    text: 'text-red-600',
    solid: 'bg-red-500 text-white ring-red-500',
    soft: 'bg-red-50',
    keys: ['*'],
  },
  {
    value: HW_LATE,
    symbol: 'K',
    label: 'Kech qoldi',
    text: 'text-violet-600',
    solid: 'bg-violet-500 text-white ring-violet-500',
    soft: 'bg-violet-50',
    keys: ['k', 'к'],
  },
  {
    value: HW_ABSENT,
    symbol: 'Y',
    label: 'Kelmadi',
    text: 'text-slate-500',
    solid: 'bg-slate-500 text-white ring-slate-500',
    soft: 'bg-slate-100',
    keys: ['y', 'й'],
  },
]

/** Status of a stored value; null for 0 (not marked) or an unknown value. */
export function homeworkMeta(value: number | null | undefined): HomeworkStatusMeta | null {
  return HOMEWORK_STATUSES.find((s) => s.value === value) ?? null
}

/** The status a key press means in the table (`0`, Backspace and Delete clear). */
export function homeworkFromKey(key: string): number | null {
  if (key === '0' || key === 'Backspace' || key === 'Delete') return HW_NONE
  const k = key.toLowerCase()
  return HOMEWORK_STATUSES.find((s) => s.keys.includes(k))?.value ?? null
}

/** Counts and the completion rate — the server's `HomeworkTallyDto`. */
export interface HomeworkTally {
  done: number
  partial: number
  notDone: number
  late: number
  absent: number
  /** done + partial + not done + late (Kelmadi stays out) */
  marked: number
  /** (done + ½·partial + ½·late) ÷ marked × 100, one decimal; null when nothing is marked */
  percent: number | null
}

/** The rate's definition, for a tooltip under the legend. */
export const HOMEWORK_RATE_HINT =
  "Bajarilish = (bajardi + ½·chala + ½·kech qoldi) ÷ belgilanganlar. «Kelmadi» hisobga kirmaydi."

/** Same arithmetic as `HomeworkCompletionService.Tally` — used to refresh totals after a local edit. */
export function homeworkTally(statuses: readonly number[]): HomeworkTally {
  const t = { done: 0, partial: 0, notDone: 0, late: 0, absent: 0 }
  for (const s of statuses) {
    if (s === HW_DONE) t.done++
    else if (s === HW_PARTIAL) t.partial++
    else if (s === HW_NOT_DONE) t.notDone++
    else if (s === HW_LATE) t.late++
    else if (s === HW_ABSENT) t.absent++
  }
  return homeworkTallyFromCounts(t)
}

/** The same tally from ready counts (the student profile carries counts, not cells). */
export function homeworkTallyFromCounts(c: {
  done: number
  partial: number
  notDone: number
  late: number
  absent: number
}): HomeworkTally {
  const marked = c.done + c.partial + c.notDone + c.late
  const credit = c.done + 0.5 * c.partial + 0.5 * c.late
  return { ...c, marked, percent: marked === 0 ? null : Math.round((credit * 1000) / marked) / 10 }
}

/** Count of one status in a tally. */
export function tallyCount(t: HomeworkTally, value: number): number {
  switch (value) {
    case HW_DONE:
      return t.done
    case HW_PARTIAL:
      return t.partial
    case HW_NOT_DONE:
      return t.notDone
    case HW_LATE:
      return t.late
    case HW_ABSENT:
      return t.absent
    default:
      return 0
  }
}

/** Colour of a completion rate — the journal's average colours, on a 0–100 scale. */
export function percentColor(p: number | null): string {
  if (p == null) return 'text-slate-300'
  if (p >= 85) return 'text-emerald-600'
  if (p >= 65) return 'text-brand-600'
  if (p >= 45) return 'text-amber-600'
  return 'text-red-600'
}
