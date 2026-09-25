import { Fragment, useCallback, useEffect, useMemo, useState } from 'react'
import { Search, Users, CheckCircle2, Circle, ChevronDown, Download, Pencil } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import type { GuardianRelation, GuardianRow, SchoolClass } from '@/types'
import type { GuardianListFilter } from '@/api/services/parents'
import { exportGuardianRows, getGuardianRows } from '@/api/services/parents'
import { guardianRelations, relationLabel } from '@/api/services/studentGuardians'
import { getClasses } from '@/api/services/classes'
import { getGroups, type StudyGroupListItem } from '@/api/services/groups'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { GuardianEditModal } from './GuardianEditModal'
import { formatPhone } from '@/lib/phone'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type ConnectionFilter = 'all' | 'connected' | 'offline'

/** "YYYY-MM-DDThh:mm:ss" ni o'qiladigan ko'rinishga keltirish. */
function formatDateTime(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Hozirgi vaqtdan farqi (masalan "2 kun oldin"). */
function timeAgo(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const diff = Date.now() - d.getTime()
  const m = Math.floor(diff / 60000)
  if (m < 1) return 'hozir'
  if (m < 60) return `${m} daqiqa oldin`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h} soat oldin`
  const days = Math.floor(h / 24)
  if (days < 30) return `${days} kun oldin`
  const months = Math.floor(days / 30)
  if (months < 12) return `${months} oy oldin`
  const years = Math.floor(months / 12)
  return `${years} yil oldin`
}

/**
 * Admin "Ilova → Ota-onalar" sahifasi (docs/modules/students-parity.md §2.9).
 *
 * Qator = ODAM: ro'yxat `guardians` + `student_guardians` jadvalidan quriladi,
 * telefon raqamini guruhlashdan emas (P-1). Shu sababli bir ota-onaning
 * raqami bir farzandida yangilanib ikkinchisida eski qolsa ham u BITTA qator
 * bo'lib ko'rinadi, raqamsiz vasiy esa umuman yo'qolmaydi.
 *
 * "Ilova o'rnatilgan" o'rnini Telegram bog'lanishi egallaydi: bizda yagona
 * kanal — Telegram (CLAUDE.md).
 */
export function ParentsPage() {
  const [rows, setRows] = useState<GuardianRow[]>([])
  const [loading, setLoading] = useState(true)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [editing, setEditing] = useState<GuardianRow | null>(null)

  /* ---- Filtrlar (§2.9.1) ---- */
  // `?q=` — yuqori paneldagi umumiy qidiruvdan kelganda ro'yxat o'sha ism bilan ochiladi.
  const [searchParams] = useSearchParams()
  const [search, setSearch] = useState(() => searchParams.get('q') ?? '')
  // Sahifa ochiq turganda qidiruvdan boshqa natija tanlansa — URL o'zgaradi, komponent esa
  // qayta yaratilmaydi. React'ning "prop o'zgarsa holatni moslash" naqshi (effektsiz).
  const urlQ = searchParams.get('q') ?? ''
  const [seenQ, setSeenQ] = useState(urlQ)
  if (urlQ !== seenQ) {
    setSeenQ(urlQ)
    setSearch(urlQ)
  }
  const [className, setClassName] = useState('')
  const [groupId, setGroupId] = useState('')
  const [relation, setRelation] = useState<'' | GuardianRelation>('')
  const [connection, setConnection] = useState<ConnectionFilter>('all')
  const [state, setState] = useState<'active' | 'archived' | 'all'>('active')

  /* ---- Ma'lumotnomalar ---- */
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [groups, setGroups] = useState<StudyGroupListItem[]>([])

  useEffect(() => {
    getClasses().then(setClasses).catch(() => { /* ma'lumotnoma yuklanmadi */ })
    getGroups().then(setGroups).catch(() => { /* ma'lumotnoma yuklanmadi */ })
  }, [])

  const filter = useMemo<GuardianListFilter>(
    () => ({
      search: search.trim() || undefined,
      className: className || undefined,
      groupId: groupId || undefined,
      relation: relation || undefined,
      connected: connection === 'all' ? undefined : connection === 'connected',
      state,
    }),
    [search, className, groupId, relation, connection, state],
  )

  const reload = useCallback(() => {
    setLoading(true)
    return getGuardianRows(filter)
      .then(setRows)
      .catch(() => setRows([]))
      .finally(() => setLoading(false))
  }, [filter])

  useEffect(() => {
    // Qidiruv har bosishda so'rov yubormasin.
    const timer = setTimeout(() => { void reload() }, 250)
    return () => clearTimeout(timer)
  }, [reload])

  // Statistika: jami / Telegram bog'langan / bog'lanmagan
  const stats = useMemo(() => {
    const connected = rows.filter((r) => r.telegramLinked).length
    return { total: rows.length, connected, offline: rows.length - connected }
  }, [rows])

  const toggleExpand = (key: string) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  const handleExport = () => {
    exportGuardianRows(filter).catch(() => alert("Eksportni yuklab bo'lmadi"))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ota-onalar</h1>
          <p className="text-sm text-slate-400">
            Vasiylar ro'yxati — farzandlari, vasiylik turi va Telegram holati
          </p>
        </div>
        <Button variant="secondary" onClick={handleExport}>
          <Download className="h-4 w-4" /> Excel
        </Button>
      </div>

      {/* Statistik kartochkalar */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard label="Jami vasiylar" value={stats.total} icon={Users} color="slate" />
        <StatCard
          label="Telegram bog'langan"
          value={stats.connected}
          icon={CheckCircle2}
          color="emerald"
        />
        <StatCard
          label="Hali bog'lanmagan"
          value={stats.offline}
          icon={Circle}
          color="amber"
        />
      </div>

      <Card className="p-0">
        {/* Filtrlar */}
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative flex-1 min-w-[200px]">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Vasiy, telefon yoki farzand nomi..."
              className={cn(control, 'w-full pl-9')}
            />
          </div>
          <select
            value={className}
            onChange={(e) => setClassName(e.target.value)}
            className={control}
          >
            <option value="">Barcha sinflar</option>
            {classes.map((c) => (
              <option key={c.id} value={c.name}>
                {c.name}
              </option>
            ))}
          </select>
          <select value={groupId} onChange={(e) => setGroupId(e.target.value)} className={control}>
            <option value="">Barcha guruhlar</option>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </select>
          <select
            value={relation}
            onChange={(e) => setRelation(e.target.value as '' | GuardianRelation)}
            className={control}
          >
            <option value="">Barcha turlar</option>
            {guardianRelations.map((r) => (
              <option key={r.value} value={r.value}>
                {r.label}
              </option>
            ))}
          </select>
          <select
            value={state}
            onChange={(e) => setState(e.target.value as 'active' | 'archived' | 'all')}
            className={control}
          >
            <option value="active">Faol o'quvchilar</option>
            <option value="archived">Arxivdagilar</option>
            <option value="all">Hammasi</option>
          </select>
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            {(['all', 'connected', 'offline'] as ConnectionFilter[]).map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => setConnection(f)}
                className={cn(
                  'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                  f === connection
                    ? 'bg-white text-brand-700 shadow-sm'
                    : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {f === 'all' ? 'Hammasi' : f === 'connected' ? 'Telegram' : 'Bog\'lanmagan'}
              </button>
            ))}
          </div>
        </div>

        {/* Jadval */}
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            Filtrga mos vasiy topilmadi
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-8 px-2 py-3"></th>
                  <th className="px-4 py-3">Vasiy F.I.SH</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Farzandlar</th>
                  <th className="px-4 py-3">Vasiylik turi</th>
                  <th className="px-4 py-3">Telegram</th>
                  <th className="px-4 py-3">Oxirgi kirish</th>
                  <th className="w-10 px-2 py-3"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => {
                  const isOpen = expanded.has(r.guardianId)
                  const kinds = [
                    ...new Set(r.children.map((c) => relationLabel(c.relation, c.relationNote))),
                  ]
                  return (
                    <Fragment key={r.guardianId}>
                      <tr
                        className="cursor-pointer hover:bg-slate-50/60"
                        onClick={() => toggleExpand(r.guardianId)}
                      >
                        <td className="px-2 py-3 text-slate-400">
                          <ChevronDown
                            className={cn(
                              'h-4 w-4 transition-transform',
                              isOpen ? 'rotate-180' : '',
                            )}
                          />
                        </td>
                        <td className="px-4 py-3 font-medium text-slate-800">
                          {r.fullName || <span className="text-slate-400">—</span>}
                        </td>
                        <td className="px-4 py-3 text-slate-600">{formatPhone(r.phone) || '—'}</td>
                        <td className="px-4 py-3">
                          <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                            {r.childrenCount} farzand
                          </span>
                        </td>
                        <td className="px-4 py-3 text-slate-600">{kinds.join(', ') || '—'}</td>
                        <td className="px-4 py-3">
                          {r.telegramLinked ? (
                            <span className="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                              <CheckCircle2 className="h-3.5 w-3.5" /> Bog'langan
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                              <Circle className="h-3.5 w-3.5" /> Yo'q
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <div className="text-sm text-slate-700">{formatDateTime(r.lastSeenAt)}</div>
                          {r.lastSeenAt && (
                            <div className="text-[11px] text-slate-400">{timeAgo(r.lastSeenAt)}</div>
                          )}
                        </td>
                        <td className="px-2 py-3">
                          <button
                            type="button"
                            title="Tahrirlash"
                            className="rounded-md p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
                            onClick={(e) => {
                              e.stopPropagation()
                              setEditing(r)
                            }}
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                      {isOpen && (
                        <tr className="bg-slate-50/40">
                          <td colSpan={8} className="px-4 py-3">
                            <div className="rounded-lg border border-slate-200 bg-white">
                              <table className="w-full text-sm">
                                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                                  <tr>
                                    <th className="px-3 py-2 text-left">Farzand</th>
                                    <th className="px-3 py-2 text-left">Sinf</th>
                                    <th className="px-3 py-2 text-left">Vasiylik turi</th>
                                    <th className="px-3 py-2 text-left">O'quvchi telefoni</th>
                                    <th className="px-3 py-2 text-left">Holat</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {r.children.map((c) => (
                                    <tr key={c.studentId} className="border-t border-slate-100">
                                      <td className="px-3 py-2 font-medium text-slate-700">
                                        <Link
                                          to={`/admin/students/${c.studentId}`}
                                          className="hover:text-brand-600 hover:underline"
                                        >
                                          {c.fullName}
                                        </Link>
                                      </td>
                                      <td className="px-3 py-2 text-slate-600">{c.className}</td>
                                      <td className="px-3 py-2 text-slate-600">
                                        {relationLabel(c.relation, c.relationNote)}
                                        {c.isPrimary && (
                                          <span className="ml-1 text-[11px] text-amber-600">
                                            (asosiy)
                                          </span>
                                        )}
                                      </td>
                                      <td className="px-3 py-2 text-slate-600">{formatPhone(c.phone) || '—'}</td>
                                      <td className="px-3 py-2 text-slate-500">
                                        {c.isArchived ? 'Arxivda' : 'Faol'}
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <GuardianEditModal
        open={editing !== null}
        guardian={editing}
        onClose={() => setEditing(null)}
        onSaved={() => {
          setEditing(null)
          void reload()
        }}
      />
    </div>
  )
}

function StatCard({
  label,
  value,
  icon: Icon,
  color,
}: {
  label: string
  value: number
  icon: typeof Users
  color: 'slate' | 'emerald' | 'amber'
}) {
  const colors = {
    slate: 'bg-slate-100 text-slate-600',
    emerald: 'bg-emerald-100 text-emerald-600',
    amber: 'bg-amber-100 text-amber-600',
  }[color]
  return (
    <Card>
      <div className="flex items-center gap-3">
        <div className={cn('flex h-10 w-10 items-center justify-center rounded-lg', colors)}>
          <Icon className="h-5 w-5" />
        </div>
        <div>
          <p className="text-xs uppercase tracking-wide text-slate-400">{label}</p>
          <p className="text-2xl font-semibold text-slate-800">{value}</p>
        </div>
      </div>
    </Card>
  )
}
