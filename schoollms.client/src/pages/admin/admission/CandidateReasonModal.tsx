/**
 * "Why?" — the written reason three attempt actions require
 * (`docs/modules/admission-and-testing.md` §6.3, §6.4, §7.4 point 5, §8.3; unit C2).
 *
 * `unlock-device`, `force-finish` and `reset-attempt` all take `{ reason }`
 * and write it to the audit log. They do very different things, so the modal
 * leads with what will happen, in the wording §7.4 fixes, before it asks why:
 * nobody should reach for "Urinishni bekor qilish (natija o'chadi)" when they
 * meant "Qurilma qulfini ochish".
 */
import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'

export interface ReasonRequest {
  title: string
  /** What the action does, in one or two sentences. */
  description: string
  confirmLabel: string
  danger?: boolean
  /** Resolves → the modal closes; rejects with a sentence → it is shown and the modal stays. */
  run: (reason: string) => Promise<void>
}

interface Props {
  request: ReasonRequest | null
  onClose: () => void
}

/** Mounted with `key` per request, so every opening starts from an empty reason. */
export function CandidateReasonModal({ request, onClose }: Props) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const close = () => {
    if (!busy) onClose()
  }

  const submit = async () => {
    if (!request || busy || !reason.trim()) return
    setBusy(true)
    setError(null)
    try {
      await request.run(reason.trim())
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={request !== null}
      onClose={close}
      size="sm"
      title={request?.title}
      footer={
        <>
          <Button variant="secondary" onClick={close} disabled={busy}>
            Ortga
          </Button>
          <Button
            variant={request?.danger ? 'danger' : 'primary'}
            onClick={() => void submit()}
            disabled={busy || !reason.trim()}
          >
            {busy && <Loader2 className="h-4 w-4 animate-spin" />}
            {request?.confirmLabel}
          </Button>
        </>
      }
    >
      {request && (
        <div className="space-y-3">
          <p className="text-sm text-slate-600">{request.description}</p>
          <Textarea
            label="Sababi"
            required
            rows={3}
            maxLength={500}
            autoFocus
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Masalan: telefoni o'chib qoldi, boshqa qurilmadan davom etadi"
          />
          {error && (
            <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
              {error}
            </p>
          )}
        </div>
      )}
    </Modal>
  )
}
