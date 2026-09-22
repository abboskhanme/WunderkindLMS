import { useCallback, useMemo, useState } from 'react'
import { CalendarRange, GraduationCap, Loader2, Users } from 'lucide-react'
import {
  seasonalErrorMessage,
  type SeasonalBulkResult,
  type SeasonalBulkRow,
  type SeasonalEntryApi,
} from '@/api/services/seasonalMarks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { PeriodPicker } from './PeriodPicker'
import { SeasonalEntryGrid } from './SeasonalEntryGrid'
import { EmptyState, ErrorState } from './StateViews'
import {
  controlClass,
  defaultPeriod,
  periodCaption,
  periodKey,
  periodKindLabels,
  toPeriodQuery,
  type PeriodSelection,
} from './periods'
import { classOptions, subjectOptions, teachersOfPair } from './scopeOptions'
import { useRemote } from './useRemote'

export interface SeasonalEntryInitial {
  period?: PeriodSelection
  classId?: string
  subjectId?: string
}

export interface SeasonalEntryWorkspaceProps {
  /** Which endpoints to use — `adminSeasonalEntryApi` or `teacherSeasonalEntryApi`. */
  api: SeasonalEntryApi
  /** false → the grid is read-only and there is no Saqlash. */
  canEdit: boolean
  /** Preselection, e.g. from the list screen's filters. */
  initial?: SeasonalEntryInitial
  /** Explains an empty scope in the caller's words (admin vs teacher). */
  emptyScopeHint?: string
}

function saveSummary(r: SeasonalBulkResult): string {
  const parts = [
    r.created > 0 ? `${r.created} ta yangi` : '',
    r.updated > 0 ? `${r.updated} ta yangilandi` : '',
    r.deleted > 0 ? `${r.deleted} ta o'chirildi` : '',
  ].filter(Boolean)
  return parts.length > 0 ? `Saqlandi: ${parts.join(', ')}` : "Saqlandi — o'zgarish yo'q"
}

/**
 * Screen 11 (and, through C5, screen 14): tur → davr → sinf → fan, then the
 * pupils load with any stored score prefilled, and one Saqlash writes them all.
 *
 * The subject list comes from `/scope` — the (class, subject) pairs of the
 * schedule — because nothing else in the system answers "which subjects are
 * taught in this class" (§6.6).
 */
export function SeasonalEntryWorkspace({
  api,
  canEdit,
  initial,
  emptyScopeHint = "Sinflar va fanlar dars jadvalidan olinadi — avval jadval tuzilgan bo'lishi kerak.",
}: SeasonalEntryWorkspaceProps) {
  const [period, setPeriod] = useState<PeriodSelection>(() => initial?.period ?? defaultPeriod())
  const [classId, setClassId] = useState(initial?.classId ?? '')
  const [subjectId, setSubjectId] = useState(initial?.subjectId ?? '')

  const [dirty, setDirty] = useState(false)
  /** A selection change waiting for "discard unsaved scores?" to be answered. */
  const [pending, setPending] = useState<(() => void) | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const scope = useRemote('scope', api.scope)
  const classes = useMemo(() => (scope.data ? classOptions(scope.data) : []), [scope.data])

  // A preselected id that is not in scope (another year, not my class) is ignored;
  // a single choice is taken automatically — a teacher with one class skips a click.
  const effectiveClassId = classes.some((c) => c.id === classId)
    ? classId
    : classes.length === 1
      ? (classes[0]?.id ?? '')
      : ''
  const subjects = useMemo(
    () => (scope.data && effectiveClassId ? subjectOptions(scope.data, [effectiveClassId]) : []),
    [scope.data, effectiveClassId],
  )
  const effectiveSubjectId = subjects.some((s) => s.id === subjectId)
    ? subjectId
    : subjects.length === 1
      ? (subjects[0]?.id ?? '')
      : ''

  const studentsKey =
    effectiveClassId && effectiveSubjectId
      ? `${effectiveClassId}|${effectiveSubjectId}|${periodKey(period)}`
      : null
  const students = useRemote(studentsKey, () =>
    api.students({ classId: effectiveClassId, subjectId: effectiveSubjectId, ...toPeriodQuery(period) }),
  )

  /** Every selection change passes through here: unsaved scores are never lost silently. */
  const guard = (apply: () => void) => {
    if (dirty && canEdit) setPending(() => apply)
    else apply()
  }

  const onSave = async (rows: SeasonalBulkRow[]) => {
    const result = await api.bulk({
      classId: effectiveClassId,
      subjectId: effectiveSubjectId,
      ...toPeriodQuery(period),
      rows,
    })
    setToast(saveSummary(result))
    students.reload()
  }

  const className = classes.find((c) => c.id === effectiveClassId)?.name ?? ''
  const subjectName = subjects.find((s) => s.id === effectiveSubjectId)?.name ?? ''
  const teachers =
    scope.data && effectiveClassId && effectiveSubjectId
      ? teachersOfPair(scope.data, effectiveClassId, effectiveSubjectId)
      : []

  const scopeEmpty = scope.data !== null && (scope.data.classes.length === 0 || scope.data.pairs.length === 0)

  return (
    <div className="space-y-4">
      <Card className="space-y-3">
        <PeriodPicker value={period} onChange={(next) => guard(() => setPeriod(next))} />
        <div className="flex flex-wrap items-center gap-3">
          <select
            value={effectiveClassId}
            onChange={(e) => {
              const next = e.target.value
              guard(() => {
                setClassId(next)
                setSubjectId('')
              })
            }}
            disabled={!scope.data || classes.length === 0}
            aria-label="Sinf"
            className={controlClass}
          >
            <option value="">{scope.loading ? 'Yuklanmoqda...' : 'Sinfni tanlang'}</option>
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          <select
            value={effectiveSubjectId}
            onChange={(e) => {
              const next = e.target.value
              guard(() => setSubjectId(next))
            }}
            disabled={!effectiveClassId || subjects.length === 0}
            aria-label="Fan"
            className={controlClass}
          >
            <option value="">
              {effectiveClassId && subjects.length === 0 ? "Fan yo'q" : 'Fanni tanlang'}
            </option>
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
          {teachers.length > 0 && (
            <span className="truncate text-sm text-slate-400" title={teachers.join(', ')}>
              O'qituvchi: <span className="text-slate-600">{teachers.join(', ')}</span>
            </span>
          )}
        </div>
      </Card>

      <Card className="p-0">
        {scope.loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : scope.error ? (
          <ErrorState
            title="Sinflar ro'yxatini yuklab bo'lmadi"
            message={seasonalErrorMessage(scope.error)}
            onRetry={scope.reload}
          />
        ) : scopeEmpty ? (
          <EmptyState icon={CalendarRange} title="Baholanadigan sinf va fan topilmadi" hint={emptyScopeHint} />
        ) : !effectiveClassId ? (
          <EmptyState
            icon={GraduationCap}
            title="Sinfni tanlang"
            hint="Tur va davrni belgilang, keyin sinf va fanni tanlang — o'quvchilar ro'yxati shu yerda ochiladi."
          />
        ) : subjects.length === 0 ? (
          <EmptyState
            icon={CalendarRange}
            title="Bu sinfda fanlar yo'q"
            hint="Fanlar dars jadvalidan olinadi — avval shu sinf uchun jadval tuzing."
          />
        ) : !effectiveSubjectId ? (
          <EmptyState icon={GraduationCap} title="Fanni tanlang" />
        ) : students.loading ? (
          <Loader label="O'quvchilar yuklanmoqda..." />
        ) : students.error ? (
          <ErrorState
            title="O'quvchilar ro'yxatini yuklab bo'lmadi"
            message={seasonalErrorMessage(students.error)}
            onRetry={students.reload}
          />
        ) : !students.data || students.data.length === 0 ? (
          <EmptyState icon={Users} title="Bu sinfda o'quvchilar yo'q" />
        ) : (
          <>
            <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-4 py-3">
              <p className="text-sm text-slate-600">
                <span className="font-semibold text-slate-800">{className}</span>
                {' · '}
                {subjectName}
                {' · '}
                {periodKindLabels[period.kind]}, {periodCaption(period)}
              </p>
              <span className="flex items-center gap-2 text-xs text-slate-400">
                {students.refreshing && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                {students.data.length} ta o'quvchi
              </span>
            </div>
            <SeasonalEntryGrid
              rows={students.data}
              canEdit={canEdit}
              onSave={onSave}
              onDirtyChange={setDirty}
              busy={students.refreshing}
            />
          </>
        )}
      </Card>

      <Modal
        open={pending !== null}
        onClose={() => setPending(null)}
        size="sm"
        title="Saqlanmagan o'zgarishlar"
        footer={
          <>
            <Button variant="secondary" onClick={() => setPending(null)}>
              Qolish
            </Button>
            <Button
              variant="danger"
              onClick={() => {
                pending?.()
                setPending(null)
              }}
            >
              Tashlab ketish
            </Button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          Kiritilgan ballar hali saqlanmagan. Boshqa sinf, fan yoki davrga o'tsangiz, ular
          yo'qoladi.
        </p>
      </Modal>

      <Toast message={toast} onClose={closeToast} />
    </div>
  )
}
