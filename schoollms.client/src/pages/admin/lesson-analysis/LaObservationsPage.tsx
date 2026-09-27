import { useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { Plus, Search, X } from 'lucide-react'
import { getObservations, getTeachers } from '@/api/services/lessonAnalysis'
import type { LaObservationListItem } from '@/types/lessonAnalysis'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { DateRangeFields, EmptyState, Notice, ObservationTable, SortHeader } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { cn } from '@/lib/utils'
import { laPaths } from './paths'

type Filter = 'all' | 'completed' | 'draft'
type SortKey = 'lessonDate' | 'scorePercent'
interface Sort {
  key: SortKey
  dir: 'asc' | 'desc'
}

/** Qoralama tahrirlashda ochiladi, yakunlangani — o'qish uchun. */
const linkTo = (item: LaObservationListItem) =>
  item.status === 'draft' ? laPaths.edit(item.id) : laPaths.detail(item.id)

/** Tahlillarda uchragan qiymatlar (fan/sinf matn sifatida saqlanadi — qo'lda kiritilgani ham). */
function usedOptions(values: (string | null)[]): string[] {
  return [...new Set(values.filter((v): v is string => Boolean(v)))].sort((a, b) => a.localeCompare(b))
}

const selectCls =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/** Tahlillar — eski `EvaluationList.tsx` (staff ko'rinishi). */
export function LaObservationsPage() {
  const [params, setParams] = useSearchParams()
  const [teacherId, setTeacherIdState] = useState(params.get('teacherId') ?? '')
  const [subject, setSubject] = useState('')
  const [className, setClassName] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<Sort | null>(null)

  const teachers = useLoad('teachers', () => getTeachers())
  const list = useLoad(`obs|${teacherId}|${dateFrom}|${dateTo}`, () =>
    getObservations({ teacherId, dateFrom, dateTo }),
  )
  const items = list.data

  function setTeacherId(value: string) {
    setTeacherIdState(value)
    setParams(value ? { teacherId: value } : {}, { replace: true })
  }

  function toggleSort(key: SortKey) {
    setSort((prev) =>
      prev?.key === key ? { key, dir: prev.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'desc' },
    )
  }

  const visible = useMemo(() => {
    if (!items) return null
    const needle = query.trim().toLowerCase()
    const filtered = items.filter(
      (item) =>
        (filter === 'all' || item.status === filter) &&
        (!subject || item.subject === subject) &&
        (!className || item.className === className) &&
        (!needle ||
          item.teacherName.toLowerCase().includes(needle) ||
          (item.subject ?? '').toLowerCase().includes(needle) ||
          (item.className ?? '').toLowerCase().includes(needle)),
    )
    if (!sort) return filtered
    const factor = sort.dir === 'asc' ? 1 : -1
    return [...filtered].sort((a, b) =>
      sort.key === 'lessonDate'
        ? factor * a.lessonDate.localeCompare(b.lessonDate)
        : factor * (a.scorePercent - b.scorePercent),
    )
  }, [items, filter, query, subject, className, sort])

  const subjectOptions = useMemo(() => usedOptions((items ?? []).map((i) => i.subject)), [items])
  const classOptions = useMemo(() => usedOptions((items ?? []).map((i) => i.className)), [items])

  const filtersActive = Boolean(query || teacherId || dateFrom || dateTo || subject || className)
  /** Tab — ko'rinish, filtr emas: tozalashda joyida qoladi. */
  function clearFilters() {
    setQuery('')
    setTeacherId('')
    setSubject('')
    setClassName('')
    setDateFrom('')
    setDateTo('')
  }

  const clearButton = (
    <Button variant="secondary" onClick={clearFilters} className="px-3 py-1.5">
      <X className="h-4 w-4" />
      Filtrlarni tozalash
    </Button>
  )
  const newButton = (
    <Link
      to={laPaths.create(teacherId || undefined)}
      className="inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
    >
      <Plus className="h-4 w-4" />
      Yangi dars tahlili
    </Link>
  )

  const counts = {
    all: items?.length,
    completed: items?.filter((i) => i.status === 'completed').length,
    draft: items?.filter((i) => i.status === 'draft').length,
  }
  const tabs: { value: Filter; label: string }[] = [
    { value: 'all', label: 'Hammasi' },
    { value: 'completed', label: 'Yakunlangan' },
    { value: 'draft', label: 'Qoralama' },
  ]

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="font-semibold text-slate-800">Dars tahlillari</h2>
          <p className="text-sm text-slate-400">Barcha o‘tkazilgan kuzatuvlar va ularning natijalari</p>
        </div>
        {newButton}
      </div>

      <div className="flex gap-1 overflow-x-auto border-b border-slate-200">
        {tabs.map((t) => {
          const active = filter === t.value
          return (
            <button
              key={t.value}
              type="button"
              onClick={() => setFilter(t.value)}
              className={cn(
                '-mb-px flex shrink-0 items-center gap-2 border-b-2 px-3 pb-2.5 pt-1 text-sm font-medium transition-colors',
                active ? 'border-brand-600 text-brand-700' : 'border-transparent text-slate-500 hover:text-slate-800',
              )}
            >
              {t.label}
              {counts[t.value] !== undefined && (
                <span
                  className={cn(
                    'rounded-md px-1.5 py-0.5 text-xs tabular-nums',
                    active ? 'bg-brand-50 text-brand-700' : 'bg-slate-100 text-slate-500',
                  )}
                >
                  {counts[t.value]}
                </span>
              )}
            </button>
          )
        })}
      </div>

      <Card className="space-y-3 p-4">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            placeholder="O‘qituvchi, fan yoki sinf bo‘yicha qidiruv"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </div>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">O‘qituvchi</span>
            <select className={selectCls} value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
              <option value="">Barchasi</option>
              {(teachers.data ?? []).map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
            </select>
          </label>
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
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Sinf</span>
            <select className={selectCls} value={className} onChange={(e) => setClassName(e.target.value)}>
              <option value="">Barchasi</option>
              {classOptions.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
          </label>
          <DateRangeFields dateFrom={dateFrom} dateTo={dateTo} onFrom={setDateFrom} onTo={setDateTo} />
        </div>

        {items && items.length > 0 && (filtersActive || filter !== 'all' || sort) && (
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-xs text-slate-400">
              {items.length} tadan {visible?.length ?? 0} tasi ko‘rsatilmoqda
            </p>
            {filtersActive && clearButton}
          </div>
        )}
        <div className="flex flex-wrap items-center gap-4 text-xs font-medium text-slate-400">
          <span>Saralash:</span>
          <SortHeader
            label="Sana"
            active={sort?.key === 'lessonDate'}
            dir={sort?.dir ?? 'desc'}
            onClick={() => toggleSort('lessonDate')}
          />
          <SortHeader
            label="Foiz"
            active={sort?.key === 'scorePercent'}
            dir={sort?.dir ?? 'desc'}
            onClick={() => toggleSort('scorePercent')}
          />
        </div>
      </Card>

      {list.error && <Notice>{list.error}</Notice>}

      {!visible ? (
        list.error ? null : <Loader label="Yuklanmoqda..." />
      ) : visible.length === 0 ? (
        <Card>
          <EmptyState
            title="Tahlil topilmadi"
            hint={
              filtersActive || filter !== 'all'
                ? 'Filtrlar yoqilgan — shartlarga mos tahlil topilmadi'
                : 'Hozircha bironta dars tahlili yo‘q — birinchisini boshlang'
            }
            action={filtersActive ? clearButton : newButton}
          />
        </Card>
      ) : (
        <ObservationTable items={visible} linkTo={linkTo} showTeacher />
      )}
    </div>
  )
}
