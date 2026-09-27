import { useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ArrowLeft, Plus, Send } from 'lucide-react'
import {
  createTaskBatch,
  createTemplateBatch,
  deleteAttachment,
  getEmployees,
  getPositions,
  hrErrorMessage,
} from '@/api/services/hrTasks'
import type { HrBatchResult } from '@/types/hrTasks'
import { HR_MAX_BATCH_ITEMS, HR_MAX_PAGE_SIZE } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Notice, PageHeader, SectionTitle, Switch, secondaryLinkCls } from '@/components/hr-tasks/ui'
import { useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { hrBatchSkipReason, hrToday } from '@/components/hr-tasks/lib'
import { EmployeePicker } from './EmployeePicker'
import { TaskRowFields } from './TaskRowFields'
import {
  hrBatchLimitError,
  hrBatchSummary,
  hrEmptyTaskRow,
  hrHasRowError,
  hrTaskRowErrors,
  hrToBatchItem,
  hrToTemplateBatchItem,
  type HrTaskRow,
} from './taskBatchForm'
import { hrtPaths } from './paths'

/**
 * "Topshiriq berish" — several tasks for several employees (the old `HrTaskCreatePage`,
 * a separate PAGE, not a modal). Two paths:
 *   · one-off     → `POST /tasks/batch`
 *   · "Takroriy"  → `POST /templates/batch` (a SEPARATE template per employee).
 */
export function HrTaskCreatePage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const toast = useToast()
  const [today] = useState(() => hrToday())

  const [selected, setSelected] = useState<string[]>(() => {
    const preset = searchParams.get('employeeId')
    return preset ? [preset] : []
  })
  const [rows, setRows] = useState<HrTaskRow[]>(() => [hrEmptyTaskRow(today)])
  // `?takroriy=1` — from the recurring-tasks page: opens with the switch on.
  const [recurring, setRecurring] = useState(() => searchParams.get('takroriy') === '1')
  const [voiceDropped, setVoiceDropped] = useState(false)
  const [submitted, setSubmitted] = useState(false)
  const [pending, setPending] = useState(false)
  const [result, setResult] = useState<HrBatchResult | null>(null)

  // Inactive employees get no tasks — they are not listed either.
  const employees = useLoad('employees|active', () => getEmployees({ isActive: true, limit: HR_MAX_PAGE_SIZE }))
  const positions = useLoad('positions|active', () => getPositions({ isActive: true, limit: HR_MAX_PAGE_SIZE }))
  const employeeItems = useMemo(() => employees.data?.items ?? [], [employees.data])
  const positionItems = positions.data?.items ?? []

  const rowErrors = useMemo(() => rows.map((row) => hrTaskRowErrors(row, recurring)), [rows, recurring])
  const limitError = hrBatchLimitError(selected.length, rows.length)
  const hasRowError = rowErrors.some(hrHasRowError)

  const employeeName = useMemo(() => {
    const byId = new Map(employeeItems.map((e) => [e.id, e.fullName]))
    return (id: string): string => byId.get(id) ?? id
  }, [employeeItems])

  function patchRow(key: string, patch: Partial<HrTaskRow>): void {
    setRows((current) => current.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function addRow(): void {
    setRows((current) => (current.length >= HR_MAX_BATCH_ITEMS ? current : [...current, hrEmptyTaskRow(today)]))
  }

  function removeRow(key: string): void {
    setRows((current) => (current.length <= 1 ? current : current.filter((row) => row.key !== key)))
  }

  /**
   * The "Takroriy" switch. Voice notes are NOT sent in recurring mode — a recorded one is
   * removed with a visible notice (never silently).
   */
  function toggleRecurring(next: boolean): void {
    if (!next) {
      setVoiceDropped(false)
      setRecurring(false)
      return
    }
    const dropped = rows.filter((row) => row.attachment !== null)
    if (dropped.length > 0) {
      setRows((current) => current.map((row) => (row.attachment ? { ...row, attachment: null } : row)))
      setVoiceDropped(true)
      toast.error('Takroriy topshiriqda ovozli izoh yuborilmaydi — yozuv olib tashlandi')
      for (const row of dropped) {
        const id = row.attachment?.id
        if (id) void deleteAttachment(id).catch(() => undefined)
      }
    }
    setRecurring(true)
  }

  async function handleSubmit(): Promise<void> {
    setSubmitted(true)
    setResult(null)
    if (limitError) {
      toast.error(limitError)
      return
    }
    if (hasRowError) {
      toast.error('Qatorlarda xato bor — qizil yozuvlarni to‘g‘rilang')
      return
    }
    setPending(true)
    try {
      const batch = recurring
        ? await createTemplateBatch({ employeeIds: selected, items: rows.map(hrToTemplateBatchItem) })
        : await createTaskBatch({ employeeIds: selected, items: rows.map(hrToBatchItem) })
      const skipped = batch.skipped ?? []
      const noun = recurring ? 'takroriy topshiriq' : 'topshiriq'
      if (skipped.length === 0) {
        navigate(recurring ? hrtPaths.templates : hrtPaths.tasks, {
          state: { flash: `${batch.created} ta ${noun} yaratildi` },
        })
        return
      }
      // Partial success: stay on the page so the skipped employees are visible.
      setResult(batch)
      toast.success(`${batch.created} ta yaratildi, ${skipped.length} tasi o‘tkazib yuborildi`)
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setPending(false)
    }
  }

  const skipped = result?.skipped ?? []

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Topshiriq berish"
        description="Bir nechta xodimga bir nechta topshiriqni birdan bering"
        actions={
          <Link to={hrtPaths.tasks} className={secondaryLinkCls}>
            <ArrowLeft className="h-4 w-4" />
            Topshiriqlarga qaytish
          </Link>
        }
      />

      <Card className="p-4">
        <SectionTitle hint="Xodimlarni tanlang — lavozim bo‘yicha birdan belgilash ham mumkin">Kimga</SectionTitle>
        <EmployeePicker
          employees={employeeItems}
          positions={positionItems}
          loading={employees.loading}
          errored={Boolean(employees.error)}
          selected={selected}
          onChange={setSelected}
        />
      </Card>

      <Card className="space-y-4 p-4">
        <SectionTitle
          hint={
            recurring
              ? 'Har bir qator takroriy qoida bo‘ladi: nusxalar belgilangan kunlarda o‘zi yaratiladi'
              : 'Har bir qator tanlangan HAR BIR xodimga alohida topshiriq bo‘lib tushadi'
          }
        >
          Topshiriqlar
        </SectionTitle>
        <Switch
          checked={recurring}
          disabled={pending}
          onChange={toggleRecurring}
          label="Takroriy topshiriq"
          hint="Yoqilsa muddat o‘rniga takroriylik qoidasi (kunlik / haftalik / oylik) so‘raladi"
        />
        {voiceDropped && (
          <Notice kind="warning">Takroriy topshiriqda ovozli izoh yuborilmaydi — yozib olingan ovoz olib tashlandi.</Notice>
        )}
        <ul className="space-y-3">
          {rows.map((row, index) => (
            <TaskRowFields
              key={row.key}
              index={index}
              row={row}
              errors={submitted ? (rowErrors[index] ?? {}) : {}}
              recurring={recurring}
              canRemove={rows.length > 1}
              disabled={pending}
              onChange={(patch) => patchRow(row.key, patch)}
              onRemove={() => removeRow(row.key)}
            />
          ))}
        </ul>
        <Button type="button" variant="secondary" disabled={pending || rows.length >= HR_MAX_BATCH_ITEMS} onClick={addRow}>
          <Plus className="h-4 w-4" />
          Yana bitta topshiriq
        </Button>
        {rows.length >= HR_MAX_BATCH_ITEMS && (
          <p className="text-xs text-slate-400">Bir vaqtda ko‘pi bilan {HR_MAX_BATCH_ITEMS} ta qator yuboriladi.</p>
        )}
      </Card>

      <Card className="space-y-3 p-4">
        <SectionTitle hint="Yaratilishidan oldin raqamni tekshiring">Yuborish</SectionTitle>
        <p className="text-sm font-medium text-slate-800">{hrBatchSummary(selected.length, rows.length)}</p>
        {limitError && submitted && (
          <p className="text-sm text-red-600" role="alert">
            {limitError}
          </p>
        )}
        {submitted && hasRowError && (
          <p className="text-sm text-red-600" role="alert">
            Qatorlarda to‘ldirilmagan maydonlar bor.
          </p>
        )}
        <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
          <Button disabled={pending} onClick={() => void handleSubmit()}>
            <Send className="h-4 w-4" />
            {pending ? 'Yuborilmoqda…' : recurring ? 'Takroriy qilib yaratish' : 'Topshiriq berish'}
          </Button>
          <Button variant="ghost" disabled={pending} onClick={() => navigate(hrtPaths.tasks)}>
            Bekor qilish
          </Button>
        </div>
        {skipped.length > 0 && (
          <div className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2">
            <p className="text-sm font-medium text-amber-800">{skipped.length} ta xodim o‘tkazib yuborildi</p>
            <ul className="mt-1 space-y-0.5 text-xs text-amber-900">
              {skipped.map((item) => (
                <li key={item.employeeId}>
                  {employeeName(item.employeeId)} — {hrBatchSkipReason(item.reason)}
                </li>
              ))}
            </ul>
          </div>
        )}
      </Card>
      {toast.node}
    </div>
  )
}
