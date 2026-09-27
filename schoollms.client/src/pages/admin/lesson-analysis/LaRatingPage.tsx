import { useCallback, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronRight, Download, X } from 'lucide-react'
import { downloadRatingXlsx, getRatingBoard, laErrorMessage } from '@/api/services/lessonAnalysis'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { fmtDate } from '@/components/lesson-analysis/format'
import { DateRangeFields, EmptyState, Notice, Score, SortHeader } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { laPaths } from './paths'

type SortKey = 'rating' | 'teacherName' | 'observationsCount'
interface Sort {
  key: SortKey
  dir: 'asc' | 'desc'
}
const DEFAULT_SORT: Sort = { key: 'rating', dir: 'desc' }

const selectCls =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/** Reyting — eski `Ratings.tsx` staff ko'rinishi (reyting doskasi). */
export function LaRatingPage() {
  const navigate = useNavigate()
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [subject, setSubject] = useState('')
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const clearError = useCallback(() => setError(null), [])

  const board = useLoad(`rating|${dateFrom}|${dateTo}`, () => getRatingBoard({ dateFrom, dateTo }))
  const rows = board.data?.rows ?? null

  /**
   * Reyting bo'yicha o'rin — butun doska bo'yicha bir marta hisoblanadi. Saralash yoki fan
   * filtri uni o'zgartirmaydi: "#" doim "maktabda nechanchi" degan savolga javob beradi.
   */
  const rankByTeacher = useMemo(() => {
    const byRating = [...(rows ?? [])].sort(
      (a, b) => b.rating - a.rating || a.teacherName.localeCompare(b.teacherName),
    )
    const map = new Map<string, number>()
    byRating.forEach((row, index) => map.set(row.teacherId, index + 1))
    return map
  }, [rows])

  const subjectOptions = useMemo(() => {
    const names = new Set<string>()
    for (const row of rows ?? []) if (row.subject) names.add(row.subject)
    return [...names].sort((a, b) => a.localeCompare(b))
  }, [rows])

  const visible = useMemo(() => {
    if (!rows) return null
    const filtered = subject ? rows.filter((row) => row.subject === subject) : rows
    const factor = sort.dir === 'asc' ? 1 : -1
    return [...filtered].sort((a, b) => {
      if (sort.key === 'teacherName') return factor * a.teacherName.localeCompare(b.teacherName)
      if (sort.key === 'observationsCount') {
        return factor * (a.observationsCount - b.observationsCount) || b.rating - a.rating
      }
      return factor * (a.rating - b.rating) || a.teacherName.localeCompare(b.teacherName)
    })
  }, [rows, subject, sort])

  function toggleSort(key: SortKey) {
    setSort((prev) =>
      prev.key === key
        ? { key, dir: prev.dir === 'asc' ? 'desc' : 'asc' }
        : { key, dir: key === 'teacherName' ? 'asc' : 'desc' },
    )
  }

  function clearFilters() {
    setDateFrom('')
    setDateTo('')
    setSubject('')
  }
  const filtered = Boolean(dateFrom || dateTo || subject)

  async function exportExcel() {
    setBusy(true)
    setError(null)
    try {
      await downloadRatingXlsx({ dateFrom, dateTo })
    } catch (err) {
      setError(laErrorMessage(err, 'Excel yuklanmadi'))
    } finally {
      setBusy(false)
    }
  }

  const clearButton = (
    <Button variant="secondary" onClick={clearFilters} className="px-3 py-1.5">
      <X className="h-4 w-4" />
      Filtrlarni tozalash
    </Button>
  )
  const notEvaluated = board.data?.notEvaluated ?? []

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="font-semibold text-slate-800">O‘qituvchilar reytingi</h2>
          <p className="text-sm text-slate-400">Yakunlangan tahlillardagi bajarilgan mezonlar foizining o‘rtachasi</p>
        </div>
        <Button
          variant="secondary"
          onClick={exportExcel}
          disabled={busy}
          title="Tanlangan sana oralig‘idagi to‘liq reyting yuklanadi"
        >
          <Download className="h-4 w-4" />
          {busy ? 'Tayyorlanmoqda…' : 'Excel eksport'}
        </Button>
      </div>

      <Card className="space-y-3 p-4">
        <div className="grid gap-3 sm:grid-cols-3">
          <DateRangeFields dateFrom={dateFrom} dateTo={dateTo} onFrom={setDateFrom} onTo={setDateTo} />
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Fan</span>
            <select className={selectCls} value={subject} onChange={(e) => setSubject(e.target.value)}>
              <option value="">Barchasi</option>
              {subjectOptions.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
          </label>
        </div>
        {filtered && clearButton}
      </Card>

      {board.error && <Notice>{board.error}</Notice>}

      {!visible ? (
        board.error ? null : <Loader label="Yuklanmoqda..." />
      ) : rows && rows.length === 0 ? (
        <Card>
          <EmptyState
            title="Reyting uchun ma’lumot yo‘q"
            hint={
              filtered
                ? 'Tanlangan sana oralig‘ida yakunlangan tahlil topilmadi — oraliqni kengaytiring'
                : 'Avval kamida bitta tahlilni yakunlang, reyting o‘shanda hisoblanadi'
            }
            action={filtered ? clearButton : undefined}
          />
        </Card>
      ) : visible.length === 0 ? (
        <Card>
          <EmptyState
            title="Filtrga mos o‘qituvchi topilmadi"
            hint={`«${subject}» fani bo‘yicha reytingda o‘qituvchi yo‘q`}
            action={clearButton}
          />
        </Card>
      ) : (
        <Card className="overflow-hidden p-0">
          <div className="hidden overflow-x-auto sm:block">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-14 px-5 py-3 font-medium" title="Reyting bo‘yicha o‘rin — saralashdan o‘zgarmaydi">
                    #
                  </th>
                  <th className="px-3 py-3 font-medium">
                    <SortHeader
                      label="O‘qituvchi"
                      active={sort.key === 'teacherName'}
                      dir={sort.dir}
                      onClick={() => toggleSort('teacherName')}
                    />
                  </th>
                  <th className="px-3 py-3 font-medium">Fan</th>
                  <th className="px-3 py-3 font-medium">
                    <SortHeader
                      label="Tahlillar"
                      active={sort.key === 'observationsCount'}
                      dir={sort.dir}
                      onClick={() => toggleSort('observationsCount')}
                    />
                  </th>
                  <th className="px-3 py-3 font-medium">Oxirgi tahlil</th>
                  <th className="px-3 py-3 text-right font-medium">
                    <SortHeader
                      label="Reyting"
                      active={sort.key === 'rating'}
                      dir={sort.dir}
                      onClick={() => toggleSort('rating')}
                    />
                  </th>
                  <th className="w-10 px-3 py-3">
                    <span className="sr-only">Ochish</span>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {visible.map((row) => (
                  <tr
                    key={row.teacherId}
                    tabIndex={0}
                    onClick={() => navigate(laPaths.teacher(row.teacherId))}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') navigate(laPaths.teacher(row.teacherId))
                    }}
                    className="cursor-pointer transition-colors hover:bg-slate-50/70 focus-visible:bg-slate-50 focus-visible:outline-none"
                  >
                    <td className="px-5 py-3 font-semibold tabular-nums text-slate-400">
                      {rankByTeacher.get(row.teacherId) ?? '—'}
                    </td>
                    <td className="px-3 py-3 font-medium text-slate-800">{row.teacherName}</td>
                    <td className="px-3 py-3 text-slate-600">{row.subject || '—'}</td>
                    <td className="px-3 py-3 tabular-nums text-slate-600">{row.observationsCount}</td>
                    <td className="whitespace-nowrap px-3 py-3 tabular-nums text-slate-600">
                      {fmtDate(row.lastObservationDate)}
                    </td>
                    <td className="px-3 py-3 text-right">
                      <Score percent={row.rating} />
                    </td>
                    <td className="px-3 py-3 text-right">
                      <ChevronRight className="inline h-4 w-4 text-slate-300" />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <ul className="divide-y divide-slate-100 sm:hidden">
            {visible.map((row) => (
              <li key={row.teacherId}>
                <button
                  type="button"
                  onClick={() => navigate(laPaths.teacher(row.teacherId))}
                  className="flex w-full items-center gap-3 px-4 py-3.5 text-left transition-colors hover:bg-slate-50"
                >
                  <span className="w-5 shrink-0 text-sm font-semibold tabular-nums text-slate-400" title="Reyting bo‘yicha o‘rin">
                    {rankByTeacher.get(row.teacherId) ?? '—'}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium text-slate-800">{row.teacherName}</p>
                    <p className="truncate text-xs text-slate-400">
                      {row.subject || 'Fan ko‘rsatilmagan'} · {row.observationsCount} ta tahlil
                    </p>
                  </div>
                  <Score percent={row.rating} />
                </button>
              </li>
            ))}
          </ul>
        </Card>
      )}

      {board.data && notEvaluated.length > 0 && (
        <Card>
          <div className="flex items-center justify-between gap-3">
            <h2 className="font-semibold text-slate-800">Hali tahlil qilinmagan</h2>
            <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-700">
              {notEvaluated.length}
            </span>
          </div>
          <p className="mt-0.5 text-xs text-slate-400">
            {filtered ? 'Bu oraliqda tahlil qilinmagan faol o‘qituvchilar' : 'Bu o‘qituvchilar reytingda qatnashmaydi'}
          </p>
          <div className="mt-3.5 flex flex-wrap gap-2">
            {notEvaluated.map((t) => (
              <button
                key={t.id}
                type="button"
                onClick={() => navigate(laPaths.teacher(t.id))}
                className="rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-600 transition-colors hover:bg-brand-50 hover:text-brand-700"
                title={t.subject ?? undefined}
              >
                {t.fullName}
              </button>
            ))}
          </div>
        </Card>
      )}

      <Toast message={error} onClose={clearError} tone="error" duration={6000} />
    </div>
  )
}
