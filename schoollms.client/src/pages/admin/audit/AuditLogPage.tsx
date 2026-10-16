import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ChevronDown, Download, History, Search } from 'lucide-react'
import type { AuditActor, AuditEntry, AuditSectionOption, Student } from '@/types'
import {
  downloadAuditLog,
  getAuditActors,
  getAuditPage,
  getAuditSections,
  type AuditLogQuery,
} from '@/api/services/audit'
import { getArchivedStudents, getStudents } from '@/api/services/students'
import { KIND_BADGE } from '@/components/audit/auditActions'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { addDays, tashkentToday } from '@/pages/admin/dashboardPeriod'

/**
 * Boshqaruv → "O'zgarishlar tarixi" (docs/modules/audit-log.md, client 2026-10-15): who changed
 * what, when and in which section — newest first, filtered by date, person, action, section,
 * pupil and text, with the before/after of every row and the filtered view as Excel.
 *
 * Finance and salary rows come only to users with the finance right, and amounts are stripped
 * from the rest — on the SERVER (a filter here would not protect anything).
 */

const PAGE_SIZE = 50

const KIND_OPTIONS = [
  { value: '', label: 'Barcha amallar' },
  { value: 'create', label: 'Yaratdi' },
  { value: 'update', label: 'Tahrirladi' },
  { value: 'delete', label: "O'chirdi" },
]

/** Field names written in English by older rows — shown in Uzbek. New rows already use Uzbek keys. */
const FIELD_LABELS: Record<string, string> = {
  amount: 'Summa',
  date: 'Sana',
  category: 'Toifa',
  note: 'Izoh',
  month: 'Oy',
  salary: 'Oylik',
  salarystartdate: 'Maosh boshlanishi',
  salarystartmonth: 'Maosh boshlanish oyi',
  monthlyfee: "Oylik to'lov",
  name: 'Nomi',
  fullname: 'F.I.SH',
  class: 'Sinf',
  classname: 'Sinf',
  reason: 'Sabab',
  status: 'Holat',
  isactive: 'Faol',
  isarchived: 'Arxivda',
  address: 'Manzil',
  latitude: 'Kenglik',
  longitude: 'Uzunlik',
  method: "To'lov usuli",
  discountpct: 'Chegirma foizi',
  discountamount: 'Chegirma summasi',
  removed: 'Olindi',
  added: "Qo'shildi",
}

const labelOf = (key: string) => FIELD_LABELS[key.toLowerCase()] ?? key

function parse(json: string | null): Record<string, unknown> | null {
  if (!json) return null
  try {
    const value: unknown = JSON.parse(json)
    return value && typeof value === 'object' && !Array.isArray(value) ? (value as Record<string, unknown>) : null
  } catch {
    return null
  }
}

function show(value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (typeof value === 'boolean') return value ? 'Ha' : "Yo'q"
  if (Array.isArray(value)) return value.length === 0 ? '—' : value.map((v) => show(v)).join(', ')
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}

/** "2026-10-15T09:57:03" → "15.10.2026 09:57" */
function stamp(ts: string): string {
  const [d, t] = ts.split('T')
  return `${formatDate(d)}${t ? ` ${t.slice(0, 5)}` : ''}`
}

/** Before → after, field by field. A create shows only "after", a delete only "before". */
function Diff({ before, after }: { before: string | null; after: string | null }) {
  const b = parse(before)
  const a = parse(after)
  const keys = [...new Set([...Object.keys(b ?? {}), ...Object.keys(a ?? {})])]
  if (keys.length === 0) return <p className="text-xs text-slate-400">Tafsilot yozilmagan.</p>
  return (
    <dl className="grid gap-x-6 gap-y-1 text-xs sm:grid-cols-[minmax(8rem,auto)_1fr]">
      {keys.map((k) => {
        const ov = b?.[k]
        const nv = a?.[k]
        const both = b !== null && a !== null && k in b && k in a
        return (
          <div key={k} className="contents">
            <dt className="text-slate-400">{labelOf(k)}</dt>
            <dd className="break-words text-slate-700">
              {both ? (
                <>
                  <span className="text-slate-400 line-through">{show(ov)}</span>
                  {' → '}
                  <span className="font-medium">{show(nv)}</span>
                </>
              ) : (
                show(a && k in a ? nv : ov)
              )}
            </dd>
          </div>
        )
      })}
    </dl>
  )
}

function errorText(e: unknown, fallback: string): string {
  return (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

export function AuditLogPage() {
  const [params] = useSearchParams()
  const today = useMemo(() => tashkentToday(), [])
  const [from, setFrom] = useState(() => params.get('from') ?? addDays(today, -29))
  const [to, setTo] = useState(() => params.get('to') ?? today)
  const [actorId, setActorId] = useState(() => params.get('actorId') ?? '')
  const [kind, setKind] = useState(() => params.get('kind') ?? '')
  const [section, setSection] = useState(() => params.get('section') ?? '')
  const [studentId, setStudentId] = useState(() => params.get('studentId') ?? '')
  const [studentQuery, setStudentQuery] = useState('')
  const [text, setText] = useState(() => params.get('q') ?? '')
  const [q, setQ] = useState(text)

  const [actors, setActors] = useState<AuditActor[]>([])
  const [sections, setSections] = useState<AuditSectionOption[]>([])
  const [students, setStudents] = useState<Student[]>([])

  const [rows, setRows] = useState<AuditEntry[]>([])
  const [total, setTotal] = useState(0)
  const [cursor, setCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [more, setMore] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState<string | null>(null)

  // Typing in the search box waits a moment before asking the server.
  useEffect(() => {
    const t = setTimeout(() => setQ(text.trim()), 400)
    return () => clearTimeout(t)
  }, [text])

  useEffect(() => {
    getAuditActors().then(setActors).catch(() => setActors([]))
    getAuditSections().then(setSections).catch(() => setSections([]))
    // Archived pupils too — their history is still there.
    Promise.all([getStudents(), getArchivedStudents()])
      .then(([active, archived]) => setStudents([...active, ...archived]))
      .catch(() => setStudents([]))
  }, [])

  const query: AuditLogQuery = useMemo(
    () => ({ from, to, actorId, kind, section, studentId, q }),
    [from, to, actorId, kind, section, studentId, q],
  )

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    setOpen(null)
    getAuditPage(query, null, PAGE_SIZE)
      .then((page) => {
        setRows(page.items)
        setTotal(page.total)
        setCursor(page.nextCursor)
      })
      .catch((e) => setError(errorText(e, "Tarixni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [query])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda birinchi sahifa qayta yuklanadi (maqsadli)
    load()
  }, [load])

  const loadMore = () => {
    if (!cursor) return
    setMore(true)
    getAuditPage(query, cursor, PAGE_SIZE)
      .then((page) => {
        setRows((prev) => [...prev, ...page.items])
        setTotal(page.total)
        setCursor(page.nextCursor)
      })
      .catch((e) => setError(errorText(e, "Tarixni yuklab bo'lmadi")))
      .finally(() => setMore(false))
  }

  const exportExcel = () => {
    setExporting(true)
    downloadAuditLog(query)
      .catch((e) => alert(errorText(e, "Excel faylni tayyorlab bo'lmadi")))
      .finally(() => setExporting(false))
  }

  const studentOptions = useMemo(() => {
    const needle = studentQuery.trim().toLocaleLowerCase('uz-Latn-UZ')
    const list = [...students].sort((x, y) => x.fullName.localeCompare(y.fullName, 'uz'))
    const filtered = needle
      ? list.filter((s) => `${s.fullName} ${s.className}`.toLocaleLowerCase('uz-Latn-UZ').includes(needle))
      : list
    // The chosen pupil stays in the list while the search changes.
    const chosen = list.find((s) => s.id === studentId)
    return chosen && !filtered.some((s) => s.id === chosen.id) ? [chosen, ...filtered] : filtered
  }, [students, studentQuery, studentId])

  const filtered = Boolean(actorId || kind || section || studentId || q)
  const resetFilters = () => {
    setActorId('')
    setKind('')
    setSection('')
    setStudentId('')
    setStudentQuery('')
    setText('')
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">O'zgarishlar tarixi</h1>
          <p className="text-sm text-slate-400">
            Kim, qachon, qaysi bo'limda nimani o'zgartirdi — eng so'nggisi yuqorida
          </p>
        </div>
        <Button variant="secondary" onClick={exportExcel} disabled={exporting || total === 0}>
          <Download className="h-4 w-4" />
          {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
        </Button>
      </div>

      <Card className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div className="flex items-center gap-2">
            <DatePicker value={from} onChange={setFrom} max={to} ariaLabel="Davr boshi" className="w-full" />
            <span className="text-slate-300">—</span>
            <DatePicker value={to} onChange={setTo} min={from} ariaLabel="Davr oxiri" className="w-full" />
          </div>
          <Select value={actorId} onChange={(e) => setActorId(e.target.value)} aria-label="Xodim">
            <option value="">Barcha xodimlar</option>
            {actors.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name} ({a.count})
              </option>
            ))}
          </Select>
          <Select value={kind} onChange={(e) => setKind(e.target.value)} aria-label="Amal">
            {KIND_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </Select>
          <Select value={section} onChange={(e) => setSection(e.target.value)} aria-label="Bo'lim">
            <option value="">Barcha bo'limlar</option>
            {sections.map((s) => (
              <option key={s.key} value={s.key}>
                {s.label}
              </option>
            ))}
          </Select>
        </div>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div className="relative lg:col-span-2">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              type="search"
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="Yozuv bo'yicha qidirish (ism, sinf, summa…)"
              className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            />
          </div>
          <input
            type="search"
            value={studentQuery}
            onChange={(e) => setStudentQuery(e.target.value)}
            placeholder="O'quvchini qidirish"
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
          <Select value={studentId} onChange={(e) => setStudentId(e.target.value)} aria-label="O'quvchi">
            <option value="">Barcha o'quvchilar</option>
            {studentOptions.map((s) => (
              <option key={s.id} value={s.id}>
                {s.fullName}
                {s.className ? ` (${s.className})` : s.isArchived ? ' (arxiv)' : ''}
              </option>
            ))}
          </Select>
        </div>
        {filtered && (
          <button type="button" onClick={resetFilters} className="text-xs font-medium text-brand-700 hover:underline">
            Filtrlarni tozalash
          </button>
        )}
      </Card>

      <Card className="p-0">
        {error ? (
          <p className="px-5 py-6 text-sm text-red-600">{error}</p>
        ) : loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <div className="flex flex-col items-center gap-2 px-5 py-12 text-sm text-slate-400">
            <History className="h-6 w-6" />
            Bu filtrlar bo'yicha o'zgarish yo'q.
          </div>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-5 py-2 font-medium">Vaqt</th>
                    <th className="px-3 py-2 font-medium">Xodim</th>
                    <th className="px-3 py-2 font-medium">Bo'lim</th>
                    <th className="px-3 py-2 font-medium">Yozuv</th>
                    <th className="px-3 py-2 font-medium">Amal</th>
                    <th className="w-10 px-3 py-2" />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => {
                    const badge = KIND_BADGE[r.kind] ?? KIND_BADGE.update
                    const expanded = open === r.id
                    return (
                      <tr key={r.id} className="border-t border-slate-100 align-top">
                        <td className="whitespace-nowrap px-5 py-2.5 text-slate-500">{stamp(r.timestamp)}</td>
                        <td className="px-3 py-2.5 text-slate-700">{r.actorName || 'Tizim'}</td>
                        <td className="whitespace-nowrap px-3 py-2.5 text-slate-500">{r.sectionLabel}</td>
                        <td className="px-3 py-2.5">
                          <p className="text-slate-800">{r.summary}</p>
                          {r.studentId && r.studentName && (
                            <Link
                              to={`/admin/students/${r.studentId}`}
                              className="mt-0.5 inline-block text-xs font-medium text-brand-700 hover:underline"
                            >
                              O'quvchi: {r.studentName}
                            </Link>
                          )}
                          {r.teacherId && r.teacherName && (
                            <Link
                              to={`/admin/boshqaruv/staff?position=teacher&q=${encodeURIComponent(r.teacherName)}`}
                              className="mt-0.5 block text-xs font-medium text-brand-700 hover:underline"
                            >
                              O'qituvchi: {r.teacherName}
                            </Link>
                          )}
                          {expanded && (
                            <div className="mt-2 rounded-lg bg-slate-50 px-3 py-2">
                              <Diff before={r.before} after={r.after} />
                            </div>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2.5">
                          <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', badge.cls)}>{badge.label}</span>
                          {r.actionLabel && <span className="ml-1.5 text-xs text-slate-500">{r.actionLabel}</span>}
                        </td>
                        <td className="px-3 py-2.5">
                          <button
                            type="button"
                            onClick={() => setOpen(expanded ? null : r.id)}
                            className="rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                            title="Oldin / keyin"
                            aria-expanded={expanded}
                          >
                            <ChevronDown className={cn('h-4 w-4 transition-transform', expanded && 'rotate-180')} />
                          </button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 px-5 py-3 text-xs text-slate-400">
              <span>
                {rows.length} / {total} ta yozuv
              </span>
              {cursor && (
                <Button variant="secondary" onClick={loadMore} disabled={more}>
                  {more ? 'Yuklanmoqda…' : 'Yana yuklash'}
                </Button>
              )}
            </div>
          </>
        )}
      </Card>
    </div>
  )
}
