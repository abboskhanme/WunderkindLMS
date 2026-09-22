import { Clock } from 'lucide-react'
import type { AuditAction, AuditLog } from '@/types'
import { getAuditLogs } from '@/api/services/audit'
import { seasonalError, type SeasonalMarkRowDto } from '@/api/services/seasonalMarks'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { EmptyState, ErrorState } from './StateViews'
import { SCORE_COLUMN_LABEL, formatScore, formatStamp } from './periods'
import { useRemote } from './useRemote'

/**
 * "Tarix" of one mark. There is no history column (§5.12): every create,
 * update and delete writes an `AuditLog` row with `EntityType = "SeasonalMark"`,
 * read back through the existing `GET /api/admin/audit`.
 *
 * The shared `AuditHistoryList` only knows finance field names, so the score
 * and comment diff is read here — case-insensitively, because the snapshot is
 * whatever the server serialised.
 */
interface MarkHistoryModalProps {
  mark: SeasonalMarkRowDto | null
  onClose: () => void
}

export function MarkHistoryModal({ mark, onClose }: MarkHistoryModalProps) {
  return (
    <Modal open={mark !== null} onClose={onClose} title="Baho tarixi">
      {mark && <HistoryBody key={mark.id} mark={mark} />}
    </Modal>
  )
}

const actionConfig: Record<AuditAction, { label: string; cls: string }> = {
  create: { label: "Qo'yildi", cls: 'bg-emerald-50 text-emerald-700' },
  update: { label: "O'zgartirildi", cls: 'bg-amber-50 text-amber-700' },
  delete: { label: "O'chirildi", cls: 'bg-red-50 text-red-700' },
}

type Snapshot = Record<string, unknown>

function parse(json: string | undefined): Snapshot | null {
  if (!json) return null
  try {
    const value: unknown = JSON.parse(json)
    return typeof value === 'object' && value !== null ? (value as Snapshot) : null
  } catch {
    return null
  }
}

/** `score` or `Score` — whichever casing the server used. */
function field(snapshot: Snapshot | null, name: string): unknown {
  if (!snapshot) return undefined
  const key = Object.keys(snapshot).find((k) => k.toLowerCase() === name)
  return key === undefined ? undefined : snapshot[key]
}

function showScore(v: unknown): string {
  if (typeof v === 'number') return formatScore(v)
  if (typeof v === 'string' && v.trim() !== '' && !Number.isNaN(Number(v))) return formatScore(Number(v))
  return '—'
}

function showComment(v: unknown): string {
  return typeof v === 'string' && v.trim() !== '' ? v : '—'
}

function Diff({ log }: { log: AuditLog }) {
  const before = parse(log.before)
  const after = parse(log.after)
  if (!before && !after) return null
  const lines = [
    { label: SCORE_COLUMN_LABEL, from: showScore(field(before, 'score')), to: showScore(field(after, 'score')) },
    { label: 'Izoh', from: showComment(field(before, 'comment')), to: showComment(field(after, 'comment')) },
  ]
  return (
    <dl className="mt-2 space-y-1 rounded-lg bg-slate-50 px-3 py-2 text-xs">
      {lines.map((l) => (
        <div key={l.label} className="flex justify-between gap-3">
          <dt className="shrink-0 text-slate-400">{l.label}</dt>
          <dd className="min-w-0 text-right text-slate-600">
            {before && after ? (
              l.from === l.to ? (
                <span className="break-words">{l.to}</span>
              ) : (
                <>
                  <span className="break-words text-slate-400 line-through">{l.from}</span>
                  {' → '}
                  <span className="break-words font-medium text-slate-700">{l.to}</span>
                </>
              )
            ) : (
              <span className="break-words">{after ? l.to : l.from}</span>
            )}
          </dd>
        </div>
      ))}
    </dl>
  )
}

function HistoryBody({ mark }: { mark: SeasonalMarkRowDto }) {
  const history = useRemote(`history:${mark.id}`, () =>
    getAuditLogs({ entityType: 'SeasonalMark', entityId: mark.id }),
  )

  const errorInfo = history.error ? seasonalError(history.error) : null

  return (
    <div className="space-y-3">
      <p className="text-sm text-slate-500">
        <span className="font-medium text-slate-800">{mark.student.fullName}</span>
        {' · '}
        {mark.subject.name}
        {' · '}
        {mark.periodLabel}
      </p>

      {history.loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : errorInfo ? (
        <ErrorState
          title="Tarixni yuklab bo'lmadi"
          message={
            errorInfo.kind === 'forbidden'
              ? "O'zgarishlar tarixini faqat administrator ko'ra oladi."
              : errorInfo.message
          }
          onRetry={errorInfo.kind === 'forbidden' ? undefined : history.reload}
        />
      ) : !history.data || history.data.length === 0 ? (
        <EmptyState icon={Clock} title="Tarix yozuvi yo'q" hint="Bu baho kiritilganidan beri o'zgarmagan." />
      ) : (
        <ul className="space-y-2">
          {history.data.map((log) => {
            const cfg = actionConfig[log.action]
            return (
              <li key={log.id} className="rounded-lg border border-slate-100 px-3 py-2">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', cfg.cls)}>
                    {cfg.label}
                  </span>
                  <span className="text-sm text-slate-700">{log.summary}</span>
                </div>
                <p className="mt-0.5 text-xs text-slate-400">
                  {formatStamp(log.timestamp)} · {log.actorName || 'Tizim'}
                </p>
                <Diff log={log} />
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
