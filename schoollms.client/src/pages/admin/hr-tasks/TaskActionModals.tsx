import { useState } from 'react'
import { hrErrorMessage, returnTask, updateTask } from '@/api/services/hrTasks'
import type { HrTask } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { DateTimeField, Field, inputCls } from '@/components/hr-tasks/ui'
import { hrTashkentParts, hrToIso } from '@/components/hr-tasks/lib'

/**
 * "Tahrirlash" — title, description and due date only (`PATCH /tasks/{id}`). Employee and
 * kind are a creation-time snapshot and never change. Mounted with a `key` per task.
 */
export function TaskEditModal({
  task,
  onClose,
  onSaved,
}: {
  task: HrTask
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const due = hrTashkentParts(task.dueAt)
  const [title, setTitle] = useState(task.title)
  const [description, setDescription] = useState(task.description ?? '')
  const [date, setDate] = useState(due?.date ?? '')
  const [time, setTime] = useState(due?.time ?? '18:00')
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const titleError =
    title.trim().length < 3 ? 'Sarlavha kamida 3 ta belgi' : title.trim().length > 200 ? 'Sarlavha 200 belgidan oshmasin' : null
  const descriptionError = description.trim().length > 2000 ? 'Tavsif 2000 belgidan oshmasin' : null
  const dueIso = hrToIso(date, time)
  const dueError = !date ? 'Muddatni belgilang' : !dueIso ? 'Muddat noto‘g‘ri: sana va vaqtni tanlang' : null

  async function save(): Promise<void> {
    setSubmitted(true)
    if (titleError || descriptionError || dueError || !dueIso) return
    setBusy(true)
    setError(null)
    try {
      await updateTask(task.id, { title: title.trim(), description: description.trim() || null, dueAt: dueIso })
      onSaved('Topshiriq yangilandi')
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
      title="Topshiriqni tahrirlash"
      footer={
        <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void save()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      }
    >
      <form
        className="space-y-4"
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <p className="text-sm text-slate-400">Sarlavha, tavsif va muddat o‘zgartiriladi</p>
        <div>
          <p className="text-sm font-medium text-slate-600">Xodim</p>
          <p className="mt-0.5 text-sm text-slate-800">{task.employeeName ?? '—'}</p>
        </div>
        <Field label="Sarlavha" required error={submitted ? titleError : null}>
          <input className={inputCls} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Masalan: Sentabr hisobotini tayyorlash" />
        </Field>
        <Field label="Tavsif" error={submitted ? descriptionError : null}>
          <textarea
            className={`${inputCls} resize-none`}
            rows={3}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Nima qilinishi kerakligi qisqacha"
          />
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
        {error && <p className="text-sm text-red-600" role="alert">{error}</p>}
      </form>
    </Modal>
  )
}

/**
 * "Qaytarish" — send a completed task back with a comment. The comment is REQUIRED: it
 * reaches the employee immediately.
 */
export function ReturnTaskModal({
  task,
  onClose,
  onSaved,
}: {
  task: HrTask
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const [comment, setComment] = useState('')
  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const commentError =
    comment.trim().length < 3
      ? 'Sababni yozing — xodim nimani tuzatishini bilishi kerak'
      : comment.trim().length > 1000
        ? 'Izoh 1000 belgidan oshmasin'
        : null

  async function submit(): Promise<void> {
    setSubmitted(true)
    if (commentError) return
    setBusy(true)
    setError(null)
    try {
      await returnTask(task.id, { comment: comment.trim() })
      onSaved('Topshiriq qaytarildi — xodimga xabar yuborildi')
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
      title="Topshiriqni qaytarish"
      size="sm"
      footer={
        <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={() => void submit()} disabled={busy}>
            {busy ? 'Yuborilmoqda…' : 'Qaytarish'}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <p className="break-words text-sm text-slate-500">{task.title}</p>
        <Field label="Izoh" required hint="Izoh xodimga darhol yuboriladi" error={submitted ? commentError : null}>
          <textarea
            className={`${inputCls} resize-none`}
            rows={4}
            autoFocus
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            placeholder="Masalan: hisobotda sentabr raqamlari yo‘q, qayta yuboring"
          />
        </Field>
        {error && <p className="text-sm text-red-600" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
