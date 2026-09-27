import { useState } from 'react'
import { Plus, X } from 'lucide-react'
import { createPosition, hrErrorMessage } from '@/api/services/hrTasks'
import type { HrPosition } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Field, inputCls } from '@/components/hr-tasks/ui'
import { cn } from '@/lib/utils'

/** Backend column is `varchar(120)`. */
export const POSITION_NAME_MAX = 120

/**
 * "+ Yangi lavozim" — a one-line add (in the positions modal and the template form).
 * No inner `<form>`: it sits inside the template form and nested forms are invalid HTML.
 */
export function NewPositionInline({
  onCreated,
  onCancel,
  onError,
  label = 'Yangi lavozim',
  autoFocus = false,
  className,
}: {
  onCreated?: (position: HrPosition) => void
  onCancel?: () => void
  onError?: (message: string) => void
  label?: string
  autoFocus?: boolean
  className?: string
}) {
  const [name, setName] = useState('')
  const [touched, setTouched] = useState(false)
  const [busy, setBusy] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)

  const trimmed = name.trim()
  const error =
    trimmed.length < 2
      ? 'Lavozim nomi kamida 2 ta belgidan iborat bo‘lsin'
      : trimmed.length > POSITION_NAME_MAX
        ? `Lavozim nomi ${POSITION_NAME_MAX} belgidan oshmasin`
        : null

  async function submit(): Promise<void> {
    setTouched(true)
    if (error) return
    setBusy(true)
    setServerError(null)
    try {
      const created = await createPosition({ name: trimmed })
      setName('')
      setTouched(false)
      onCreated?.(created)
    } catch (err) {
      // 409 — a position with this name exists; the text comes from the backend.
      const message = hrErrorMessage(err)
      setServerError(message)
      onError?.(message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={cn('flex flex-col gap-2 sm:flex-row sm:items-start', className)}>
      <Field label={label} className="min-w-0 flex-1" error={(touched ? error : null) ?? serverError}>
        <input
          className={inputCls}
          placeholder="Marketolog"
          autoFocus={autoFocus}
          maxLength={POSITION_NAME_MAX}
          value={name}
          onChange={(e) => {
            setName(e.target.value)
            setServerError(null)
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              void submit()
            }
            if (e.key === 'Escape' && onCancel) onCancel()
          }}
        />
      </Field>
      <div className="flex gap-2 sm:mt-6 sm:shrink-0">
        <Button type="button" variant="secondary" className="flex-1 sm:flex-none" disabled={busy} onClick={() => void submit()}>
          <Plus className="h-4 w-4" />
          {busy ? 'Qo‘shilmoqda…' : 'Qo‘shish'}
        </Button>
        {onCancel && (
          <Button type="button" variant="ghost" className="px-3" aria-label="Bekor qilish" onClick={onCancel}>
            <X className="h-4 w-4" />
          </Button>
        )}
      </div>
    </div>
  )
}
