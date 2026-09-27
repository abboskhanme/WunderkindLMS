import { useRef, useState } from 'react'
import { FileText, Paperclip, X } from 'lucide-react'
import { addMyProgress, completeMyTask, createMyTask, hrErrorCode, hrErrorMessage, hrErrorStatus, uploadMyProof, type HrMyTask } from '@/api/services/hrTasks'
import { HR_MAX_PROOFS, HR_PROOF_ACCEPT } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { DateTimeField, Field, inputCls } from '@/components/hr-tasks/ui'
import { hrFormatBytes, hrFormatDateTime, hrProgressText, hrProofFileError, hrToIso } from '@/components/hr-tasks/lib'

const footerCls = 'flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-end'

/** Uzbek text for a "Bajardim" error (the old `hrMyCompleteError`). */
function completeError(err: unknown): string {
  const code = hrErrorCode(err)
  // `/api/hr/me/*` drops the `hr_` prefix (`proof_required`), the panel keeps it.
  if (code === 'proof_required' || code === 'hr_proof_required') return 'Bu topshiriq uchun isbot talab qilinadi — fayl biriktiring yoki nima qilganingizni yozing.'
  if (code === 'validation_error' || hrErrorStatus(err) === 422) {
    return 'Izoh qabul qilinmadi — u bo‘sh yoki juda uzun. Nima qilganingizni qisqacha yozib, qayta yuboring.'
  }
  return hrErrorMessage(err)
}

/**
 * "Bajardim" — a proof FILE or a non-empty COMMENT (hr-tasks.md §5). Our JWT works on the
 * web too, so files can be uploaded from the panel (`/api/hr/me/proofs`).
 */
export function CompleteTaskModal({
  task,
  onClose,
  onDone,
}: {
  task: HrMyTask
  onClose: () => void
  onDone: (message: string) => void
}) {
  const [comment, setComment] = useState('')
  const [files, setFiles] = useState<File[]>([])
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [progress, setProgress] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement | null>(null)

  const commentTooLong = comment.trim().length > 2000
  const missingProof = comment.trim().length === 0 && files.length === 0
  const formError = submitted
    ? commentTooLong
      ? 'Izoh 2000 belgidan oshmasin'
      : missingProof
        ? 'Isbot kerak: fayl biriktiring yoki nima qilganingizni yozing'
        : null
    : null

  function addFiles(list: FileList | null): void {
    if (!list) return
    const next = [...files]
    for (const file of Array.from(list)) {
      const problem = hrProofFileError(file)
      if (problem) {
        setError(`${file.name}: ${problem}`)
        continue
      }
      if (next.length >= HR_MAX_PROOFS) {
        setError(`Ko‘pi bilan ${HR_MAX_PROOFS} ta fayl biriktiriladi`)
        break
      }
      next.push(file)
    }
    setFiles(next)
  }

  async function submit(): Promise<void> {
    setSubmitted(true)
    if (commentTooLong || missingProof) return
    setBusy(true)
    setError(null)
    try {
      const proofIds: string[] = []
      for (const [index, file] of files.entries()) {
        setProgress(`Fayl yuklanmoqda ${index + 1}/${files.length}…`)
        const uploaded = await uploadMyProof(task.id, { file })
        proofIds.push(uploaded.id)
      }
      setProgress(null)
      await completeMyTask(task.id, { comment: comment.trim() || null, proofIds })
      onDone('Topshiriq bajarildi deb belgilandi')
    } catch (err) {
      // The modal STAYS OPEN — the comment and files are kept.
      setError(completeError(err))
    } finally {
      setBusy(false)
      setProgress(null)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Topshiriqni bajardim"
      footer={
        <div className={footerCls}>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? (progress ?? 'Yuborilmoqda…') : 'Yuborish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <p className="break-words text-sm font-medium text-slate-800">{task.title}</p>
        <p className="text-sm text-slate-400">
          Muddat: <span className="tabular-nums">{hrFormatDateTime(task.dueAt)}</span>
        </p>
        <Field label="Izoh" hint="Isbot har doim talab qilinadi: fayl biriktiring yoki nima qilganingizni yozing.">
          <textarea
            className={`${inputCls} resize-none`}
            rows={4}
            autoFocus
            placeholder="Nima qilganingizni yozing — fayl bo‘lmasa, bu isbot o‘rnida qoladi"
            value={comment}
            onChange={(e) => setComment(e.target.value)}
          />
        </Field>

        <Field label="Isbot fayllari" hint={`Rasm, hujjat, ovoz yoki video — ${HR_MAX_PROOFS} tagacha, har biri 10 MB gacha`}>
          <div className="space-y-2">
            {files.length > 0 && (
              <ul className="space-y-1.5">
                {files.map((file, index) => (
                  <li key={`${file.name}-${index}`} className="flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm">
                    <FileText className="h-4 w-4 shrink-0 text-slate-400" />
                    <span className="min-w-0 flex-1 truncate text-slate-700">{file.name}</span>
                    <span className="shrink-0 text-xs text-slate-400">{hrFormatBytes(file.size)}</span>
                    <button
                      type="button"
                      aria-label={`${file.name} — olib tashlash`}
                      disabled={busy}
                      onClick={() => setFiles((list) => list.filter((_, i) => i !== index))}
                      className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                    >
                      <X className="h-4 w-4" />
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <Button
              type="button"
              variant="secondary"
              className="px-3"
              disabled={busy || files.length >= HR_MAX_PROOFS}
              onClick={() => inputRef.current?.click()}
            >
              <Paperclip className="h-4 w-4" />
              Fayl biriktirish
            </Button>
            <input
              ref={inputRef}
              type="file"
              multiple
              className="hidden"
              accept={HR_PROOF_ACCEPT || undefined}
              onChange={(e) => {
                addFiles(e.target.files)
                e.target.value = ''
              }}
            />
          </div>
        </Field>

        {(formError || error) && (
          <p className="text-sm text-red-600" role="alert">
            {formError ?? error}
          </p>
        )}
      </div>
    </Modal>
  )
}

/** KPI step (`numeric`) — the task completes itself at the target. */
export function ProgressModal({
  task,
  onClose,
  onDone,
}: {
  task: HrMyTask
  onClose: () => void
  onDone: (message: string) => void
}) {
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const normalized = amount.trim().replace(',', '.')
  const amountError = !/^-?\d+(\.\d{1,2})?$/.test(normalized) || Number(normalized) === 0 ? 'Noldan farqli son kiriting (masalan 12 yoki 2.5)' : null

  async function submit(): Promise<void> {
    setSubmitted(true)
    if (amountError) return
    setBusy(true)
    setError(null)
    try {
      await addMyProgress(task.id, { amount: normalized, note: note.trim() || null })
      onDone('Natija qo‘shildi')
    } catch (err) {
      setError(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Natija qo‘shish"
      size="sm"
      footer={
        <div className={footerCls}>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Qo‘shish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <p className="break-words text-sm font-medium text-slate-800">{task.title}</p>
        <p className="text-sm text-slate-400">Hozirgi holat: {hrProgressText(task) ?? '—'}</p>
        <Field label={`Miqdor${task.unit ? ` (${task.unit})` : ''}`} required hint="Xatoni tuzatish uchun manfiy son ham kiritish mumkin" error={submitted ? amountError : null}>
          <input className={inputCls} inputMode="decimal" autoFocus placeholder="12" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
        <Field label="Izoh">
          <input className={inputCls} placeholder="Ixtiyoriy" value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}

/** "+ O'zimga topshiriq" (`source = self`). */
export function SelfTaskModal({
  today,
  onClose,
  onDone,
}: {
  today: string
  onClose: () => void
  onDone: (message: string) => void
}) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [date, setDate] = useState(today)
  const [time, setTime] = useState('18:00')
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const titleError = title.trim().length < 3 ? 'Sarlavha kamida 3 ta belgi' : title.trim().length > 200 ? 'Sarlavha 200 belgidan oshmasin' : null
  const dueIso = hrToIso(date, time)
  const dueError = !dueIso ? 'Muddat noto‘g‘ri: sana va vaqtni tanlang' : null

  async function submit(): Promise<void> {
    setSubmitted(true)
    if (titleError || !dueIso) return
    setBusy(true)
    setError(null)
    try {
      await createMyTask({ title: title.trim(), description: description.trim() || null, dueAt: dueIso })
      onDone('Topshiriq qo‘shildi')
    } catch (err) {
      setError(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="O‘zimga topshiriq"
      footer={
        <div className={footerCls}>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Qo‘shish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <Field label="Sarlavha" required error={submitted ? titleError : null}>
          <input className={inputCls} autoFocus placeholder="Masalan: Hisobotni tekshirish" value={title} onChange={(e) => setTitle(e.target.value)} />
        </Field>
        <Field label="Tavsif">
          <textarea className={`${inputCls} resize-none`} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <DateTimeField
          label="Muddat"
          required
          hint="Toshkent vaqti"
          date={date}
          time={time}
          onDate={setDate}
          onTime={setTime}
          error={submitted ? dueError : null}
        />
        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}
