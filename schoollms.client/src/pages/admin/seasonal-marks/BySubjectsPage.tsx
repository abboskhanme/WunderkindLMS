/**
 * FANLAR KESIMIDA — `/admin/seasonal-marks/by-subjects`, screen 12 of
 * `docs/modules/admission-and-testing.md` §3.3.
 *
 * Rows are pupils, columns are the chosen subjects, cells are the score of the
 * chosen period. The pivot is the server's (`GET /by-subjects`, §6.6): the
 * page draws the `columns` it is given and fills each cell from `scores`.
 *
 * Nothing is asked until tur, davr, at least one class and at least one
 * subject are all chosen — the same rule EduSchool's screen follows.
 * Read-only: data and the Excel button, nothing else (§3.7).
 */
import { useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { FileSpreadsheet, Grid3x3, ListFilter } from 'lucide-react'
import {
  downloadBySubjects,
  getBySubjects,
  getSeasonalScope,
  seasonalErrorMessage,
  type PivotQuery,
} from '@/api/services/seasonalMarks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { PeriodPicker } from './PeriodPicker'
import { EmptyState, ErrorState, InlineError, Pager } from './StateViews'
import {
  SCORE_COLUMN_LABEL,
  defaultPeriod,
  formatScore,
  periodCaption,
  periodKindLabels,
  toPeriodQuery,
  type PeriodSelection,
} from './periods'
import { classOptions, subjectOptions, type NamedOption } from './scopeOptions'
import { useRemote } from './useRemote'

const PAGE_SIZE = 50

export function BySubjectsPage() {
  const [period, setPeriod] = useState<PeriodSelection>(() => defaultPeriod())
  const [classIds, setClassIds] = useState<string[]>([])
  const [subjectIds, setSubjectIds] = useState<string[]>([])
  const [page, setPage] = useState(1)
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const scope = useRemote('scope', () => getSeasonalScope())
  const classes = useMemo(() => (scope.data ? classOptions(scope.data) : []), [scope.data])
  const subjects = useMemo(
    () => (scope.data && classIds.length > 0 ? subjectOptions(scope.data, classIds) : []),
    [scope.data, classIds],
  )

  // A subject no longer taught in any chosen class drops out of the query.
  const activeSubjectIds = subjectIds.filter((id) => subjects.some((s) => s.id === id))
  const ready = classIds.length > 0 && activeSubjectIds.length > 0

  // `null` until all four are chosen: no request is made (§3.3).
  const query: PivotQuery | null = ready
    ? { ...toPeriodQuery(period), classIds, subjectIds: activeSubjectIds }
    : null

  // `useRemote` never calls the loader while the key is null.
  const pivot = useRemote(query ? JSON.stringify({ ...query, page }) : null, () =>
    query
      ? getBySubjects({ ...query, page, limit: PAGE_SIZE })
      : Promise.reject(new Error('selection incomplete')),
  )

  const toggle = (list: string[], id: string) =>
    list.includes(id) ? list.filter((x) => x !== id) : [...list, id]

  const handleExport = async () => {
    if (!query) return
    setExporting(true)
    setExportError(null)
    try {
      await downloadBySubjects(query)
    } catch (err: unknown) {
      setExportError(seasonalErrorMessage(err))
    } finally {
      setExporting(false)
    }
  }

  const data = pivot.data
  const total = data?.total ?? 0

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Mavsumiy baholash — fanlar kesimida</h1>
          <p className="text-sm text-slate-400">
            Har bir o'quvchining tanlangan fanlardan olgan bali — {SCORE_COLUMN_LABEL}
          </p>
        </div>
        <Button
          variant="secondary"
          onClick={handleExport}
          disabled={!query || exporting || pivot.loading || total === 0}
          title="Butun tanlov bo'yicha Excel fayl (ko'rinib turgan sahifa emas)"
        >
          <FileSpreadsheet className="h-4 w-4" />
          {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
        </Button>
      </div>

      {exportError && <InlineError message={exportError} />}

      <Card className="space-y-4">
        <PeriodPicker
          value={period}
          onChange={(next) => {
            setPage(1)
            setPeriod(next)
          }}
        />

        {scope.loading ? (
          <Loader label="Sinflar yuklanmoqda..." className="py-6" />
        ) : scope.error ? (
          <ErrorState
            title="Sinflar ro'yxatini yuklab bo'lmadi"
            message={seasonalErrorMessage(scope.error)}
            onRetry={scope.reload}
          />
        ) : classes.length === 0 ? (
          <p className="py-4 text-center text-sm text-slate-400">
            Sinflar topilmadi — sinf va fanlar dars jadvalidan olinadi.
          </p>
        ) : (
          <>
            <ChipGroup
              title="Sinflar"
              options={classes}
              selected={classIds}
              onToggle={(id) => {
                setPage(1)
                setClassIds((prev) => toggle(prev, id))
              }}
              onAll={(all) => {
                setPage(1)
                setClassIds(all ? classes.map((c) => c.id) : [])
              }}
            />
            {classIds.length > 0 &&
              (subjects.length === 0 ? (
                <p className="text-sm text-slate-400">
                  Tanlangan sinflarda fanlar yo'q — avval dars jadvalini tuzing.
                </p>
              ) : (
                <ChipGroup
                  title="Fanlar"
                  options={subjects}
                  selected={activeSubjectIds}
                  onToggle={(id) => {
                    setPage(1)
                    setSubjectIds((prev) => toggle(prev, id))
                  }}
                  onAll={(all) => {
                    setPage(1)
                    setSubjectIds(all ? subjects.map((s) => s.id) : [])
                  }}
                />
              ))}
          </>
        )}
      </Card>

      <Card className="p-0">
        {!query ? (
          <EmptyState
            icon={ListFilter}
            title="Tur, davr, sinf va fanni tanlang"
            hint="Jadval kamida bitta sinf va bitta fan tanlangach ochiladi."
          />
        ) : pivot.loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : pivot.error ? (
          <ErrorState
            title="Jadvalni yuklab bo'lmadi"
            message={seasonalErrorMessage(pivot.error)}
            onRetry={pivot.reload}
          />
        ) : !data || data.items.length === 0 || data.columns.length === 0 ? (
          <EmptyState
            icon={Grid3x3}
            title="Tanlangan sinflarda o'quvchi topilmadi"
            hint="Boshqa sinf yoki davrni tanlab ko'ring."
          />
        ) : (
          <>
            <div className="border-b border-slate-100 px-4 py-3 text-sm text-slate-600">
              {periodKindLabels[period.kind]}, {periodCaption(period)}
            </div>
            <div className={cn('overflow-x-auto', pivot.refreshing && 'opacity-60')}>
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th
                      rowSpan={2}
                      className="sticky left-0 z-10 whitespace-nowrap bg-slate-50 px-4 py-3 align-bottom"
                    >
                      O'quvchi
                    </th>
                    <th rowSpan={2} className="whitespace-nowrap px-3 py-3 align-bottom">
                      Sinf
                    </th>
                    {/* §13 Q5: the scale is named over every score column. */}
                    <th
                      colSpan={data.columns.length}
                      className="border-b border-slate-100 px-3 pt-3 pb-1 text-center normal-case"
                    >
                      {SCORE_COLUMN_LABEL}
                    </th>
                  </tr>
                  <tr>
                    {data.columns.map((c) => (
                      <th key={c.subjectId} className="px-3 pt-1 pb-3 text-center">
                        {c.name}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 tabular-nums">
                  {data.items.map((row) => (
                    <tr key={row.studentId} className="hover:bg-slate-50/60">
                      <td className="sticky left-0 z-10 bg-white px-4 py-2.5 font-medium text-slate-800">
                        <span className="block max-w-[14rem] truncate" title={row.fullName}>
                          {row.fullName}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-3 py-2.5 text-slate-500">{row.className}</td>
                      {data.columns.map((c) => {
                        const score = row.scores[c.subjectId]
                        const shown = formatScore(score)
                        return (
                          <td
                            key={c.subjectId}
                            className={cn(
                              'px-3 py-2.5 text-center',
                              shown ? 'font-semibold text-slate-800' : 'text-slate-300',
                            )}
                          >
                            {shown || '—'}
                          </td>
                        )
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p className="border-t border-slate-100 px-4 py-2 text-xs text-slate-400">
              “—” — shu fandan baho qo'yilmagan (nol emas).
            </p>
          </>
        )}
        {query && (
          <Pager page={page} limit={PAGE_SIZE} total={total} disabled={pivot.loading} onPage={setPage} />
        )}
      </Card>
    </div>
  )
}

interface ChipGroupProps {
  title: ReactNode
  options: NamedOption[]
  selected: string[]
  onToggle: (id: string) => void
  onAll: (all: boolean) => void
}

/** The checkbox chips of the grade reports' "Sinflar" step. */
function ChipGroup({ title, options, selected, onToggle, onAll }: ChipGroupProps) {
  const allChecked = options.length > 0 && options.every((o) => selected.includes(o.id))
  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-sm font-semibold text-slate-700">{title}</h2>
        <button
          type="button"
          onClick={() => onAll(!allChecked)}
          className="text-sm font-medium text-brand-600 hover:text-brand-700"
        >
          {allChecked ? 'Tanlovni bekor qilish' : 'Barchasini tanlash'}
        </button>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-6">
        {options.map((o) => {
          const checked = selected.includes(o.id)
          return (
            <label
              key={o.id}
              className={cn(
                'flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors',
                checked
                  ? 'border-brand-300 bg-brand-50 text-brand-800'
                  : 'border-slate-200 text-slate-600 hover:bg-slate-50',
              )}
            >
              <input
                type="checkbox"
                checked={checked}
                onChange={() => onToggle(o.id)}
                className="h-4 w-4 accent-brand-600"
              />
              <span className="truncate" title={o.name}>
                {o.name}
              </span>
            </label>
          )
        })}
      </div>
    </div>
  )
}
