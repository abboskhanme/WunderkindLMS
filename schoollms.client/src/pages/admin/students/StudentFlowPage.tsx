import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  BadgeCheck,
  Download,
  Pencil,
  RotateCcw,
  TrendingUp,
  UserMinus,
  UserPlus,
  Users,
} from 'lucide-react'
import type { SchoolClass, StudentFlowReport, StudentFlowRow } from '@/types'
import { downloadStudentFlow, getStudentFlow, updateLeaveReason } from '@/api/services/studentFlow'
import { getArchiveReasons, type ArchiveReason } from '@/api/services/archiveReasons'
import { getClasses } from '@/api/services/classes'
import { useAuth } from '@/context/auth-context'
import { pageLevel } from '@/lib/access'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Select, Textarea } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { StatCard } from '@/components/ui/StatCard'
import { cn, formatDate } from '@/lib/utils'
import { displayDate, monthEnd, monthStart, presetRange, tashkentToday } from '@/pages/admin/dashboardPeriod'

/**
 * O'quv bo'limi → "O'quvchilar oqimi" (docs/modules/student-flow.md, client 2026-10-15): for a
 * period — how many came, started paying, left and came back, why they left (per catalog reason),
 * per class, and who exactly. A departure's reason can be set or corrected here later.
 *
 * "Faollashganlar" = the pupil's FIRST payment falls in the period (the client did not define the
 * word; ASSUMPTIONS.md 2026-10-15). Pupils brought back from the archive are "Qaytganlar".
 */

const PAGE_KEY = '/admin/students/oqim'

type Preset = 'week' | 'month' | 'lastMonth' | 'year' | 'custom'
type Tab = 'new' | 'activated' | 'left' | 'returned'

const TABS: { key: Tab; label: string }[] = [
  { key: 'new', label: 'Yangi kelganlar' },
  { key: 'activated', label: 'Faollashganlar' },
  { key: 'left', label: 'Ketganlar' },
  { key: 'returned', label: 'Qaytganlar' },
]

const SOURCE_LABELS: Record<string, string> = {
  manual: "Qo'lda",
  bulk: 'Ommaviy arxiv',
  class_archive: 'Sinf arxivi',
  graduation: 'Bitiruv',
  delete: "O'chirildi",
  import: 'Excel import',
  lead: 'Liddan',
  backfill: 'Eski maʼlumot',
  payment: "Birinchi to'lov",
}

function schoolYearStart(today: string): string {
  const year = Number(today.slice(0, 4))
  const month = Number(today.slice(5, 7))
  return `${month >= 9 ? year : year - 1}-09-01`
}

function rangeOf(preset: Exclude<Preset, 'custom'>, today: string): { from: string; to: string } {
  if (preset === 'week' || preset === 'month') return presetRange(preset, today)
  if (preset === 'lastMonth') {
    const first = monthStart(today, 1)
    return { from: first, to: monthEnd(first) }
  }
  return { from: schoolYearStart(today), to: today }
}

function errorText(e: unknown, fallback: string): string {
  return (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

export function StudentFlowPage() {
  const { user } = useAuth()
  const canEdit = !user?.permissions || (user.permissions.includes('students') && pageLevel(user, PAGE_KEY) === 'edit')
  const [params] = useSearchParams()
  const today = useMemo(() => tashkentToday(), [])
  const fromParam = params.get('from')
  const toParam = params.get('to')
  const [preset, setPreset] = useState<Preset>(fromParam && toParam ? 'custom' : 'month')
  const [from, setFrom] = useState(fromParam ?? rangeOf('month', today).from)
  const [to, setTo] = useState(toParam ?? rangeOf('month', today).to)
  const [classId, setClassId] = useState(params.get('classId') ?? '')
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [report, setReport] = useState<StudentFlowReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>(() => TABS.find((x) => x.key === params.get('tab'))?.key ?? 'new')
  const [exporting, setExporting] = useState(false)
  const [editing, setEditing] = useState<StudentFlowRow | null>(null)

  useEffect(() => {
    getClasses().then(setClasses).catch(() => setClasses([]))
  }, [])

  const load = useCallback(() => {
    if (!from || !to || from > to) return
    setLoading(true)
    setError(null)
    getStudentFlow(from, to, classId || undefined)
      .then(setReport)
      .catch((e) => setError(errorText(e, "Hisobotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [from, to, classId])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr yoki sinf o'zgarganda qayta yuklanadi (maqsadli)
    load()
  }, [load])

  const choose = (p: Exclude<Preset, 'custom'>) => {
    const r = rangeOf(p, today)
    setPreset(p)
    setFrom(r.from)
    setTo(r.to)
  }

  const exportExcel = () => {
    setExporting(true)
    downloadStudentFlow(from, to, classId || undefined)
      .catch((e) => alert(errorText(e, "Excel faylni tayyorlab bo'lmadi")))
      .finally(() => setExporting(false))
  }

  const t = report?.totals
  const rows: StudentFlowRow[] = report
    ? { new: report.newList, activated: report.activatedList, left: report.leftList, returned: report.returnedList }[tab]
    : []
  const topReason = Math.max(1, ...(report?.byReason ?? []).map((r) => r.count))

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">O'quvchilar oqimi</h1>
          <p className="text-sm text-slate-400">
            Kelgan, faollashgan, ketgan va qaytgan o'quvchilar · {displayDate(from)} – {displayDate(to)}
          </p>
        </div>
        <Button variant="secondary" onClick={exportExcel} disabled={exporting || !report}>
          <Download className="h-4 w-4" />
          {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
        </Button>
      </div>

      <Card className="flex flex-wrap items-center gap-2">
        <div className="flex flex-wrap rounded-xl bg-slate-100 p-1 text-sm">
          {(
            [
              ['week', 'Bu hafta'],
              ['month', 'Bu oy'],
              ['lastMonth', "O'tgan oy"],
              ['year', "O'quv yili"],
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              onClick={() => choose(key)}
              className={cn(
                'rounded-lg px-3 py-1.5 font-medium transition-colors',
                preset === key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {label}
            </button>
          ))}
        </div>
        <DatePicker
          value={from}
          max={to}
          onChange={(v) => {
            setPreset('custom')
            setFrom(v)
          }}
          ariaLabel="Davr boshi"
          className="w-36"
        />
        <span className="text-slate-300">—</span>
        <DatePicker
          value={to}
          min={from}
          onChange={(v) => {
            setPreset('custom')
            setTo(v)
          }}
          ariaLabel="Davr oxiri"
          className="w-36"
        />
        <Select value={classId} onChange={(e) => setClassId(e.target.value)} className="w-44" aria-label="Sinf">
          <option value="">Barcha sinflar</option>
          {classes.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </Select>
      </Card>

      {error ? (
        <Card>
          <p className="text-sm text-red-600">{error}</p>
        </Card>
      ) : !report || !t ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className={cn('space-y-6 transition-opacity', loading && 'pointer-events-none opacity-60')}>
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4 xl:grid-cols-7">
            <StatCard label="Davr boshida" value={t.headcountStart} icon={Users} iconBg="bg-slate-100" iconColor="text-slate-500" />
            <StatCard
              label="Yangi kelganlar"
              value={t.new}
              icon={UserPlus}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
              onClick={() => setTab('new')}
            />
            <StatCard
              label="Faollashganlar"
              value={t.activated}
              icon={BadgeCheck}
              iconBg="bg-violet-50"
              iconColor="text-violet-600"
              hint="birinchi to'lov"
              onClick={() => setTab('activated')}
            />
            <StatCard
              label="Qaytganlar"
              value={t.returned}
              icon={RotateCcw}
              iconBg="bg-sky-50"
              iconColor="text-sky-600"
              hint="arxivdan"
              onClick={() => setTab('returned')}
            />
            <StatCard
              label="Ketganlar"
              value={t.left}
              icon={UserMinus}
              iconBg="bg-rose-50"
              iconColor="text-rose-600"
              onClick={() => setTab('left')}
            />
            <StatCard
              label="Sof o'zgarish"
              value={t.net > 0 ? `+${t.net}` : t.net}
              icon={TrendingUp}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
            />
            <StatCard label="Davr oxirida" value={t.headcountEnd} icon={Users} />
          </div>

          <div className="grid gap-6 xl:grid-cols-2">
            <Card>
              <h2 className="font-semibold text-slate-800">Ketish sabablari</h2>
              <p className="text-xs text-slate-400">Sababi tanlanmaganlar — «Boshqa»</p>
              {report.byReason.length === 0 ? (
                <p className="py-6 text-center text-sm text-slate-400">Bu davrda ketgan o'quvchi yo'q.</p>
              ) : (
                <ul className="mt-3">
                  {report.byReason.map((r) => (
                    <li key={r.name} className="py-1.5">
                      <div className="flex items-baseline justify-between gap-3 text-sm">
                        <span className="truncate text-slate-600" title={r.name}>
                          {r.name}
                        </span>
                        <span className="shrink-0 font-medium tabular-nums text-slate-800">
                          {r.count} <span className="text-xs font-normal text-slate-400">· {r.percent}%</span>
                        </span>
                      </div>
                      <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
                        <div
                          className="h-full rounded-full bg-rose-400"
                          style={{ width: `${Math.round((r.count / topReason) * 100)}%` }}
                        />
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </Card>

            <Card className="p-0">
              <div className="px-5 pt-5">
                <h2 className="font-semibold text-slate-800">Sinflar kesimida</h2>
                <p className="text-xs text-slate-400">Hodisa paytidagi sinf</p>
              </div>
              {report.byClass.length === 0 ? (
                <p className="px-5 py-6 text-center text-sm text-slate-400">Bu davrda o'zgarish yo'q.</p>
              ) : (
                <div className="mt-3 overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-5 py-2 font-medium">Sinf</th>
                        <th className="px-3 py-2 text-right font-medium">Yangi</th>
                        <th className="px-3 py-2 text-right font-medium">Qaytgan</th>
                        <th className="px-3 py-2 text-right font-medium">Ketgan</th>
                        <th className="px-5 py-2 text-right font-medium">Sof</th>
                      </tr>
                    </thead>
                    <tbody>
                      {report.byClass.map((c) => (
                        <tr key={c.className} className="border-t border-slate-100">
                          <td className="px-5 py-2 font-medium text-slate-700">{c.className}</td>
                          <td className="px-3 py-2 text-right tabular-nums text-slate-600">{c.new}</td>
                          <td className="px-3 py-2 text-right tabular-nums text-slate-600">{c.returned}</td>
                          <td className="px-3 py-2 text-right tabular-nums text-slate-600">{c.left}</td>
                          <td
                            className={cn(
                              'px-5 py-2 text-right font-medium tabular-nums',
                              c.net > 0 ? 'text-emerald-600' : c.net < 0 ? 'text-rose-600' : 'text-slate-500',
                            )}
                          >
                            {c.net > 0 ? `+${c.net}` : c.net}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </div>

          <Card className="p-0">
            <div className="flex flex-wrap gap-2 border-b border-slate-100 px-5 py-3">
              {TABS.map((x) => {
                const count = { new: t.new, activated: t.activated, left: t.left, returned: t.returned }[x.key]
                return (
                  <button
                    key={x.key}
                    type="button"
                    onClick={() => setTab(x.key)}
                    className={cn(
                      'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                      tab === x.key ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-100',
                    )}
                  >
                    {x.label} <span className={tab === x.key ? 'text-white/80' : 'text-slate-400'}>{count}</span>
                  </button>
                )
              })}
            </div>
            {rows.length === 0 ? (
              <p className="px-5 py-10 text-center text-sm text-slate-400">Bu davrda yozuv yo'q.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-5 py-2 font-medium">O'quvchi</th>
                      <th className="px-3 py-2 font-medium">Sinf</th>
                      <th className="px-3 py-2 font-medium">Sana</th>
                      {tab === 'left' && <th className="px-3 py-2 font-medium">Sabab</th>}
                      <th className="px-3 py-2 font-medium">{tab === 'activated' ? 'Manba' : 'Kim'}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((r) => (
                      <tr key={r.eventId ?? `${r.studentId}-${r.date}`} className="border-t border-slate-100 align-top">
                        <td className="px-5 py-2">
                          {r.studentExists ? (
                            <Link to={`/admin/students/${r.studentId}`} className="font-medium text-slate-800 hover:text-brand-700">
                              {r.studentName}
                            </Link>
                          ) : (
                            <span className="font-medium text-slate-500" title="O'quvchi o'chirilgan">
                              {r.studentName}
                            </span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-slate-600">{r.className ?? '—'}</td>
                        <td className="whitespace-nowrap px-3 py-2 text-slate-600">{formatDate(r.date)}</td>
                        {tab === 'left' && (
                          <td className="px-3 py-2">
                            <div className="flex items-start gap-2">
                              <div className="min-w-0">
                                <p className="text-slate-700">{r.reason ?? 'Boshqa'}</p>
                                {r.reasonText && <p className="text-xs text-slate-400">{r.reasonText}</p>}
                              </div>
                              {canEdit && r.eventId && (
                                <button
                                  type="button"
                                  onClick={() => setEditing(r)}
                                  className="shrink-0 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                                  title="Sababni o'zgartirish"
                                >
                                  <Pencil className="h-3.5 w-3.5" />
                                </button>
                              )}
                            </div>
                          </td>
                        )}
                        <td className="px-3 py-2 text-slate-500">
                          {tab === 'activated'
                            ? SOURCE_LABELS.payment
                            : `${r.actorName ?? '—'}${SOURCE_LABELS[r.source] ? ` · ${SOURCE_LABELS[r.source]}` : ''}`}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </div>
      )}

      {editing && (
        <ReasonModal
          row={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}
    </div>
  )
}

/** Set or correct a departure's reason (catalog + note). */
function ReasonModal({ row, onClose, onSaved }: { row: StudentFlowRow; onClose: () => void; onSaved: () => void }) {
  const [reasons, setReasons] = useState<ArchiveReason[]>([])
  const [reasonId, setReasonId] = useState(row.reasonId ?? '')
  const [note, setNote] = useState(row.reasonText ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getArchiveReasons()
      .then(setReasons)
      .catch(() => setReasons([]))
  }, [])

  const save = () => {
    setBusy(true)
    setError(null)
    updateLeaveReason(row.eventId!, reasonId || null, note.trim())
      .then(onSaved)
      .catch((e) => setError(errorText(e, "Saqlab bo'lmadi")))
      .finally(() => setBusy(false))
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Ketish sababi"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <p className="text-slate-600">
          <span className="font-medium text-slate-800">{row.studentName}</span>
          {row.className ? ` · ${row.className}` : ''} · {formatDate(row.date)}
        </p>
        <Select label="Sabab turi" value={reasonId} onChange={(e) => setReasonId(e.target.value)}>
          <option value="">Boshqa / tanlanmagan</option>
          {reasons.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </Select>
        <Textarea label="Izoh" rows={3} value={note} onChange={(e) => setNote(e.target.value)} />
        {error && <p className="text-xs text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
