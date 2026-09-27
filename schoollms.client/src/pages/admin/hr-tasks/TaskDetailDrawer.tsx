import type { ReactNode } from 'react'
import { AlertTriangle, BellRing, CheckCircle2, ExternalLink, FileText, Mic, Paperclip } from 'lucide-react'
import { getTask } from '@/api/services/hrTasks'
import type { HrTaskDetail, HrTaskProof } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Drawer } from '@/components/ui/Drawer'
import { Badge, EmptyState, ProgressBar, Skeleton } from '@/components/hr-tasks/ui'
import { useLoad } from '@/components/hr-tasks/useLoad'
import {
  hrAmount,
  hrFormatBytes,
  hrFormatDate,
  hrFormatDateTime,
  hrFormatNumber,
  hrIsFinal,
  hrIsImageProof,
  hrIsOpen,
  hrIsOverdue,
  hrMediaSrc,
  hrOverdueLabel,
  hrProgressPercent,
  hrProgressText,
  hrProofKindLabel,
  hrProofUrl,
  hrTaskKindLabel,
  hrTaskSourceLabel,
  hrTaskStatusLabel,
  hrTaskStatusTone,
  hrVoiceDuration,
} from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'

/**
 * Task detail — side drawer (the old `TaskDetailDrawer`). Action modals (edit, return,
 * cancel) open at page level; the drawer hides meanwhile.
 *
 * `version` — bumped by the page after an action to refetch.
 */
export function TaskDetailDrawer({
  open,
  taskId,
  version,
  onClose,
  onEdit,
  onReturn,
  onCancel,
}: {
  open: boolean
  taskId: string | null
  version: number
  onClose: () => void
  onEdit: (task: HrTaskDetail) => void
  onReturn: (task: HrTaskDetail) => void
  onCancel: (task: HrTaskDetail) => void
}) {
  const detail = useLoad(taskId && open ? `task|${taskId}|${version}` : null, () => getTask(taskId ?? ''))
  const task = detail.data

  const proofs = task?.proofs ?? []
  const progress = task?.progress ?? []
  const attachments = task?.attachments ?? []
  const voice = attachments.find((a) => a.kind === 'voice') ?? null
  const voiceUrl = hrMediaSrc(voice?.url ?? task?.voiceUrl)
  const files = attachments.filter((a) => a.kind !== 'voice')
  const overdue = task ? hrIsOverdue(task) : false

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size="lg"
      title="Topshiriq"
      footer={
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          {task && !hrIsFinal(task) && (
            <Button variant="secondary" onClick={() => onEdit(task)}>
              Tahrirlash
            </Button>
          )}
          {task && hrIsOpen(task) && (
            <Button variant="secondary" onClick={() => onCancel(task)}>
              Bekor qilish
            </Button>
          )}
          {task && task.status === 'done' && (
            <Button variant="danger" onClick={() => onReturn(task)}>
              Qaytarish
            </Button>
          )}
          <Button variant="ghost" onClick={onClose}>
            Yopish
          </Button>
        </div>
      }
    >
      {detail.loading ? (
        <div className="space-y-4">
          <Skeleton className="h-6 w-2/3" />
          <Skeleton className="h-4 w-1/3" />
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-24 w-full" />
        </div>
      ) : detail.error ? (
        <EmptyState
          title="Topshiriqni yuklab bo‘lmadi"
          hint={detail.error}
          action={
            <Button variant="secondary" onClick={detail.reload}>
              Qayta urinish
            </Button>
          }
        />
      ) : !task ? (
        <p className="text-sm text-slate-400">Topshiriq tanlanmagan.</p>
      ) : (
        <div className="space-y-6">
          <section className="space-y-2">
            <div className="flex flex-wrap items-center gap-1.5">
              <Badge tone={hrTaskStatusTone(task.status)}>{hrTaskStatusLabel(task.status)}</Badge>
              <Badge>{hrTaskSourceLabel(task.source)}</Badge>
              <Badge>{hrTaskKindLabel(task.kind)}</Badge>
              {task.isLate && <Badge tone="warning">Kechikib bajarilgan</Badge>}
              {overdue && (
                <Badge tone="danger" dot>
                  {hrOverdueLabel(task.dueAt)}
                </Badge>
              )}
              {task.acceptedAt ? (
                <Badge tone="success">Tasdiqlandi</Badge>
              ) : task.needsAcceptance === true ? (
                <Badge tone="warning" dot>
                  Tasdiq kutilmoqda
                </Badge>
              ) : null}
            </div>
            <h3 className="break-words text-lg font-semibold leading-snug text-slate-900">{task.title}</h3>
            {task.description ? (
              <p className="whitespace-pre-line break-words text-sm text-slate-600">{task.description}</p>
            ) : (
              <p className="text-sm text-slate-400">Tavsif berilmagan.</p>
            )}
          </section>

          <dl className="grid gap-x-4 gap-y-3 sm:grid-cols-2">
            <DetailRow label="Xodim" value={task.employeeName ?? '—'} />
            <DetailRow label="Lavozim" value={task.positionName ?? '—'} />
            <DetailRow label="Muddat" value={hrFormatDateTime(task.dueAt)} />
            <DetailRow label="Isbot" value={proofs.length === 0 ? 'Biriktirilmagan' : `${proofs.length} ta`} />
            <DetailRow
              label="Tasdiq"
              value={
                task.acceptedAt ? (
                  <span className="inline-flex items-center gap-1.5 text-emerald-600">
                    <CheckCircle2 className="h-3.5 w-3.5" />
                    {hrFormatDateTime(task.acceptedAt)}
                  </span>
                ) : task.needsAcceptance === true ? (
                  <span className="inline-flex items-center gap-1.5 text-amber-600">
                    <BellRing className="h-3.5 w-3.5" />
                    Tasdiq kutilmoqda
                  </span>
                ) : (
                  '—'
                )
              }
            />
          </dl>

          {voiceUrl && (
            <section className="space-y-2">
              <SectionHeading>
                <Mic className="h-4 w-4 text-slate-400" />
                Ovozli izoh
              </SectionHeading>
              <p className="text-sm text-slate-400">
                Bu — RAHBAR biriktirgan izoh (isbot emas): xodim topshiriqni ochganda shu yozuvni eshitadi.
              </p>
              <div className="flex flex-wrap items-center gap-2">
                <audio className="h-10 min-w-0 max-w-full flex-1" controls preload="metadata" src={voiceUrl} />
                {hrVoiceDuration(voice?.durationSeconds) && (
                  <span className="shrink-0 text-xs tabular-nums text-slate-400">{hrVoiceDuration(voice?.durationSeconds)}</span>
                )}
              </div>
            </section>
          )}

          {files.length > 0 && (
            <section className="space-y-2">
              <SectionHeading>Biriktirmalar</SectionHeading>
              <ul className="space-y-1.5">
                {files.map((file) => (
                  <li key={file.id}>
                    <a
                      href={hrMediaSrc(file.url) ?? undefined}
                      target="_blank"
                      rel="noreferrer"
                      className="inline-flex max-w-full items-center gap-1.5 text-sm text-brand-600 hover:underline"
                    >
                      <FileText className="h-3.5 w-3.5 shrink-0" />
                      <span className="truncate">{file.originalName ?? 'Faylni ochish'}</span>
                      <span className="shrink-0 text-xs text-slate-400">{hrFormatBytes(file.sizeBytes)}</span>
                    </a>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {task.kind === 'numeric' && (
            <section className="space-y-3">
              <SectionHeading>KPI qadamlari</SectionHeading>
              <ProgressBar percent={hrProgressPercent(task)} caption={hrProgressText(task) ?? undefined} showValue />
              {progress.length === 0 ? (
                <p className="text-sm text-slate-400">Hali birorta qadam qo‘shilmagan.</p>
              ) : (
                <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
                  {progress.map((step) => {
                    const amount = hrAmount(step.amount)
                    return (
                      <li key={step.id} className="flex items-start justify-between gap-3 px-3 py-2">
                        <div className="min-w-0">
                          <p className={cn('text-sm font-medium tabular-nums', amount < 0 ? 'text-red-600' : 'text-emerald-600')}>
                            {amount > 0 ? '+' : ''}
                            {hrFormatNumber(amount)}
                            {task.unit ? ` ${task.unit}` : ''}
                          </p>
                          {step.note && <p className="break-words text-xs text-slate-600">{step.note}</p>}
                        </div>
                        <p className="shrink-0 text-xs tabular-nums text-slate-400">{hrFormatDateTime(step.createdAt)}</p>
                      </li>
                    )
                  })}
                </ul>
              )}
            </section>
          )}

          <section className="space-y-3">
            <SectionHeading>Izohlar</SectionHeading>
            {task.employeeComment ? (
              <div className="rounded-xl border border-slate-200 px-3 py-2">
                <p className="text-xs text-slate-400">Xodim izohi</p>
                <p className="mt-1 whitespace-pre-line break-words text-sm text-slate-800">{task.employeeComment}</p>
              </div>
            ) : (
              <p className="text-sm text-slate-400">Xodim izoh qoldirmagan.</p>
            )}
            {task.directorComment && (
              <div className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2">
                <p className="flex items-center gap-1.5 text-xs font-medium text-amber-700">
                  <AlertTriangle className="h-3.5 w-3.5" />
                  Qaytarish izohi
                </p>
                <p className="mt-1 whitespace-pre-line break-words text-sm text-amber-900">{task.directorComment}</p>
              </div>
            )}
          </section>

          <section className="space-y-3">
            <SectionHeading>Isbotlar ({proofs.length})</SectionHeading>
            <p className="text-sm text-slate-400">
              Isbot fayl ham, izoh matni ham bo‘lishi mumkin — fayl bilan isbotlab bo‘lmaydigan ishlar uchun izohning o‘zi
              yetadi.
            </p>
            <ProofGallery proofs={proofs} />
          </section>

          <section className="space-y-3">
            <SectionHeading>Tarix</SectionHeading>
            <dl className="grid gap-x-4 gap-y-3 sm:grid-cols-2">
              <DetailRow label="Yaratilgan" value={hrFormatDateTime(task.createdAt)} />
              <DetailRow label="Davr boshi" value={hrFormatDate(task.periodStart)} />
              <DetailRow label="Bajarilgan" value={hrFormatDateTime(task.completedAt)} />
              <DetailRow label="Qaytarilgan" value={hrFormatDateTime(task.returnedAt)} />
            </dl>
          </section>
        </div>
      )}
    </Drawer>
  )
}

/** Images preview inline; other files are links, text proofs read in place; audio gets a player. */
function ProofGallery({ proofs }: { proofs: HrTaskProof[] }) {
  if (proofs.length === 0) return <p className="text-sm text-slate-400">Isbot biriktirilmagan.</p>
  return (
    <ul className="grid gap-3 sm:grid-cols-2">
      {proofs.map((proof) => {
        const url = hrProofUrl(proof)
        const isAudio = Boolean(proof.contentType?.startsWith('audio/'))
        return (
          <li key={proof.id} className="overflow-hidden rounded-xl border border-slate-200 bg-white">
            {hrIsImageProof(proof) && url && (
              <a href={url} target="_blank" rel="noreferrer" className="block">
                <img
                  src={url}
                  alt={proof.originalName ?? 'Isbot rasmi'}
                  loading="lazy"
                  className="h-40 w-full bg-slate-100 object-cover"
                />
              </a>
            )}
            <div className="space-y-1 px-3 py-2">
              <div className="flex items-center gap-1.5 text-xs font-medium text-slate-700">
                <Paperclip className="h-3.5 w-3.5 shrink-0 text-slate-400" />
                <span className="truncate">{hrProofKindLabel(proof.kind)}</span>
              </div>
              {proof.kind === 'text' ? (
                <p className="whitespace-pre-line break-words text-sm text-slate-700">{proof.text || '—'}</p>
              ) : url ? (
                <>
                  {isAudio && <audio className="h-10 w-full" controls preload="metadata" src={url} />}
                  <a
                    href={url}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex max-w-full items-center gap-1.5 text-sm text-brand-600 hover:underline"
                  >
                    {proof.kind === 'link' ? (
                      <ExternalLink className="h-3.5 w-3.5 shrink-0" />
                    ) : (
                      <FileText className="h-3.5 w-3.5 shrink-0" />
                    )}
                    <span className="truncate">{proof.originalName ?? proof.url ?? 'Faylni ochish'}</span>
                  </a>
                </>
              ) : (
                <p className="text-sm text-slate-400">Fayl manzili yo‘q.</p>
              )}
              <p className="text-xs tabular-nums text-slate-400">
                {hrFormatDateTime(proof.createdAt)}
                {proof.sizeBytes ? ` · ${hrFormatBytes(proof.sizeBytes)}` : ''}
              </p>
            </div>
          </li>
        )
      })}
    </ul>
  )
}

function SectionHeading({ children }: { children: ReactNode }) {
  return <h4 className="flex items-center gap-1.5 text-sm font-semibold text-slate-800">{children}</h4>
}

function DetailRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs text-slate-400">{label}</dt>
      <dd className="break-words text-sm text-slate-800">{value}</dd>
    </div>
  )
}
