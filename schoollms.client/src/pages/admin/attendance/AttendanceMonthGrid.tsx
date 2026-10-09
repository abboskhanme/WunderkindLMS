/**
 * Sinfning oylik davomat jadvali — o'quvchi × kun (mijoz, 2026-10-09: "sinf bo'yicha ... butun oy
 * bo'yicha jadval ko'rinishida"). Faqat bitta sinf tanlanganda chiziladi.
 *
 * Hech narsa qayta hisoblanmaydi: server har o'quvchining darsi bo'lgan kunlarini (`dates`) va oddiy
 * "keldi" bo'lmagan kunlarini (`events`) beradi. Ro'yxatda bor kun — o'sha holat; darsi bo'lgan,
 * lekin ro'yxatda yo'q kun — keldi; darsi bo'lmagan kun — bo'sh katak.
 */
import type {
  AttendanceEvent,
  AttendanceReasonRow,
  AttendanceStudentRow,
} from '@/api/services/attendanceAnalytics'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'

const WEEKDAYS = ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh']

const pct = (v: number | null) => (v == null ? '—' : `${Math.round(v)}%`)

function weekday(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return WEEKDAYS[new Date(Date.UTC(y, m - 1, d)).getUTCDay()]
}

export function AttendanceMonthGrid({
  students,
  events,
  reasons,
  days,
  onPickStudent,
}: {
  students: AttendanceStudentRow[]
  events: AttendanceEvent[]
  reasons: AttendanceReasonRow[]
  /** Davrdagi darsi bo'lgan kunlar (trend sanalari), eskisidan yangisiga. */
  days: string[]
  onPickStudent: (studentId: string) => void
}) {
  const reasonById = new Map(reasons.map((r) => [r.reasonId, r]))
  const eventAt = new Map(events.map((e) => [`${e.studentId}|${e.date}`, e]))

  if (days.length === 0 || students.length === 0) return null

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
        <div>
          <h2 className="font-semibold text-slate-800">Oylik jadval</h2>
          <p className="text-xs text-slate-400">
            Har bir o'quvchi har kuni. Katakni bosing — kunning sababi va darslari; ismni bosing — shu o'quvchining kunlari.
          </p>
        </div>
        <Legend />
      </div>
      <div className="mt-3 overflow-x-auto">
        <table className="w-max min-w-full border-separate border-spacing-0 text-center text-xs tabular-nums">
          <thead className="text-slate-400">
            <tr>
              <th className="sticky left-0 z-10 border-b border-slate-100 bg-slate-50 px-3 py-2 text-left font-medium">#</th>
              <th className="sticky left-8 z-10 min-w-[14rem] border-b border-slate-100 bg-slate-50 px-3 py-2 text-left font-medium">
                F.I.SH
              </th>
              {days.map((d) => (
                <th key={d} className="min-w-[2.25rem] border-b border-slate-100 bg-slate-50 px-1 py-2 font-medium">
                  <div className="text-slate-600">{d.slice(8)}</div>
                  <div className="text-[10px]">{weekday(d)}</div>
                </th>
              ))}
              <th className="border-b border-l border-slate-100 bg-slate-50 px-2 py-2 font-medium">Keldi</th>
              <th className="border-b border-slate-100 bg-slate-50 px-2 py-2 font-medium">Kelmadi</th>
              <th className="border-b border-slate-100 bg-slate-50 px-2 py-2 font-medium">Sababsiz</th>
            </tr>
          </thead>
          <tbody>
            {students.map((s, i) => {
              const had = new Set(s.dates ?? [])
              return (
                <tr key={s.studentId} className="hover:bg-slate-50/60">
                  <td className="sticky left-0 z-10 border-b border-slate-50 bg-white px-3 py-1.5 text-left text-slate-400">{i + 1}</td>
                  <td className="sticky left-8 z-10 max-w-[16rem] border-b border-slate-50 bg-white px-3 py-1.5 text-left">
                    <button
                      type="button"
                      onClick={() => onPickStudent(s.studentId)}
                      className="block max-w-full truncate text-sm font-medium text-slate-800 hover:text-brand-600 hover:underline"
                      title={s.fullName}
                    >
                      {s.fullName}
                    </button>
                  </td>
                  {days.map((d) => (
                    <td key={d} className="border-b border-slate-50 px-0.5 py-1">
                      <Cell
                        had={had.has(d)}
                        event={eventAt.get(`${s.studentId}|${d}`)}
                        reasonById={reasonById}
                        onPick={() => onPickStudent(s.studentId)}
                      />
                    </td>
                  ))}
                  <td className="border-b border-l border-slate-50 px-2 font-semibold text-emerald-600">{pct(s.tally.presentPct)}</td>
                  <td className="border-b border-slate-50 px-2 text-slate-600">{s.tally.absent || '—'}</td>
                  <td className="border-b border-slate-50 px-2 font-semibold text-red-600">{s.tally.unexcused || '—'}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

function Cell({
  had,
  event,
  reasonById,
  onPick,
}: {
  had: boolean
  event: AttendanceEvent | undefined
  reasonById: Map<string, AttendanceReasonRow>
  onPick: () => void
}) {
  if (!had && !event) return <span className="block h-7 rounded-md bg-slate-50" title="Dars yo'q" />

  if (!event)
    return (
      <span className="flex h-7 items-center justify-center rounded-md bg-emerald-50 text-emerald-600" title="Keldi">
        ✓
      </span>
    )

  const reason = event.reasonIds[0] ? reasonById.get(event.reasonIds[0]) : undefined
  const reasonNames = event.reasonIds.map((id) => reasonById.get(id)?.name ?? "O'chirilgan sabab").join(', ')
  const lessons = event.periods.length ? ` · ${event.periods.join(', ')}-dars` : ''
  const { text, tone, title } =
    event.status === 'absent'
      ? {
          text: reason?.short || 'Y',
          tone: event.unexcused ? 'bg-red-500 text-white' : 'bg-red-100 text-red-700',
          title: `Kelmadi — ${reasonNames || 'sabab yo\'q'} (${event.heldLessons} dars)`,
        }
      : event.status === 'partial'
        ? {
            text: '½',
            tone: 'bg-orange-100 text-orange-700',
            title: `Ayrim darslarga kelmadi — ${reasonNames}${lessons}`,
          }
        : event.status === 'late'
          ? { text: reason?.short || 'K', tone: 'bg-sky-100 text-sky-700', title: `Kech keldi${lessons}` }
          : { text: '?', tone: 'bg-amber-100 text-amber-700', title: 'Davomat belgilanmagan' }

  return (
    <button
      type="button"
      onClick={onPick}
      title={title}
      className={cn('flex h-7 w-full items-center justify-center rounded-md text-[11px] font-semibold', tone)}
    >
      {text}
    </button>
  )
}

function Legend() {
  const items: [string, string, string][] = [
    ['✓', 'bg-emerald-50 text-emerald-600', 'Keldi'],
    ['S', 'bg-red-500 text-white', 'Sababsiz'],
    ['K', 'bg-red-100 text-red-700', 'Sababli (sabab qisqartmasi)'],
    ['½', 'bg-orange-100 text-orange-700', 'Ayrim darslarga'],
    ['KQ', 'bg-sky-100 text-sky-700', 'Kech keldi'],
    ['?', 'bg-amber-100 text-amber-700', 'Belgilanmagan'],
  ]
  return (
    <div className="flex flex-wrap items-center gap-3 text-xs text-slate-500">
      {items.map(([sym, tone, label]) => (
        <span key={label} className="flex items-center gap-1.5">
          <span className={cn('flex h-5 min-w-[1.25rem] items-center justify-center rounded px-1 text-[10px] font-semibold', tone)}>
            {sym}
          </span>
          {label}
        </span>
      ))}
    </div>
  )
}
