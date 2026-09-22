/**
 * The entrance-test link, shown ONCE — right after it is issued
 * (`docs/modules/admission-and-testing.md` §3.1 screen 2, §6.4, §7.2; unit C2).
 *
 * The server keeps only the token's hash, so this is the one moment the URL
 * exists outside it. The modal says so in the words §7.2 prescribes and
 * offers "Nusxalash"; closing it is final — "Havolani qayta chiqarish" is the
 * recovery path, and it kills the old link.
 *
 * Delivery to the family is by hand, over Telegram (CLAUDE.md: Telegram is the
 * only channel; there is no SMS). Nothing here sends anything.
 */
import { useState } from 'react'
import { AlertTriangle, Check, Copy } from 'lucide-react'
import type { IssuedInvitation } from '@/api/services/candidates'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { copyText } from '@/lib/utils'
import { formatWallClock } from '../exams/examLabels'
import { LINK_ONCE_WARNING } from './CandidateHelpers'

interface Props {
  /** The issue call's answer; `null` — closed. */
  issued: IssuedInvitation | null
  candidateName: string
  onClose: () => void
}

export function CandidateLinkModal({ issued, candidateName, onClose }: Props) {
  const [copied, setCopied] = useState<'ok' | 'failed' | null>(null)

  const copy = async () => {
    if (!issued) return
    setCopied((await copyText(issued.url)) ? 'ok' : 'failed')
  }

  const close = () => {
    setCopied(null)
    onClose()
  }

  return (
    <Modal
      open={issued !== null}
      onClose={close}
      title="Imtihon havolasi"
      footer={
        <Button variant="secondary" onClick={close}>
          Yopish
        </Button>
      }
    >
      {issued && (
        <div className="space-y-4">
          <p className="text-sm text-slate-600">
            <span className="font-medium text-slate-800">{candidateName}</span> uchun qabul testi
            havolasi tayyor. Uni ota-onaga Telegram orqali yuboring.
          </p>

          <div className="flex items-stretch gap-2">
            <input
              readOnly
              value={issued.url}
              aria-label="Havola"
              onFocus={(e) => e.currentTarget.select()}
              className="min-w-0 flex-1 rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 font-mono text-xs text-slate-700 outline-none focus:border-brand-400"
            />
            <Button onClick={() => void copy()} className="shrink-0">
              {copied === 'ok' ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
              {copied === 'ok' ? 'Nusxalandi' : 'Nusxalash'}
            </Button>
          </div>
          {copied === 'failed' && (
            <p className="text-sm text-red-600">
              Brauzer nusxalashga ruxsat bermadi — havolani belgilab, qo'lda nusxalang.
            </p>
          )}

          <dl className="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 text-sm">
            <dt className="text-slate-400">Amal qiladi</dt>
            <dd className="text-slate-700">
              {formatWallClock(issued.validFrom)} — {formatWallClock(issued.validUntil)}
            </dd>
            <dt className="text-slate-400">Belgisi</dt>
            <dd className="font-mono text-xs leading-5 text-slate-700">…{issued.tokenHint}</dd>
          </dl>

          <p className="flex items-start gap-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            {LINK_ONCE_WARNING}
          </p>
        </div>
      )}
    </Modal>
  )
}
