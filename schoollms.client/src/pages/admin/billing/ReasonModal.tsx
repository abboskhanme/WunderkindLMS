/**
 * Sabab talab qiladigan amal uchun oyna: chegirmani rad etish, chiqimni
 * storno qilish.
 *
 * Sabab MAJBURIY va bo'sh bo'lolmaydi — SPEC §4.3: "kim, nega" savoliga
 * javobsiz moliyaviy qaror qabul qilinmaydi. Shuning uchun tugma sabab
 * yozilmaguncha o'chiq turadi (`disabled`), va bu holat serverning
 * `reason_required` xatosini kutib o'tirmaydi.
 */
import { useEffect, useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Notice } from './BillingUi'

interface Props {
  open: boolean
  title: string
  /** Amal nima qilishini bir jumlada tushuntiradi. */
  description: string
  /** Tasdiqlash tugmasidagi matn. */
  confirmLabel: string
  /** Xavfli amal (storno, rad etish) — qizil tugma. */
  danger?: boolean
  busy?: boolean
  error?: string | null
  onClose: () => void
  onConfirm: (reason: string) => void
}

const MIN_REASON = 3

export function ReasonModal({
  open,
  title,
  description,
  confirmLabel,
  danger = true,
  busy = false,
  error = null,
  onClose,
  onConfirm,
}: Props) {
  const [reason, setReason] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli)
    if (open) setReason('')
  }, [open])

  const trimmed = reason.trim()
  const valid = trimmed.length >= MIN_REASON

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button
            variant={danger ? 'danger' : 'primary'}
            disabled={!valid || busy}
            onClick={() => onConfirm(trimmed)}
          >
            {busy ? 'Bajarilmoqda...' : confirmLabel}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-slate-600">{description}</p>
        <Textarea
          label="Sabab"
          required
          rows={3}
          placeholder="Masalan: summa noto'g'ri kiritilgan"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        {!valid && trimmed.length > 0 && (
          <p className="text-xs text-slate-400">Sabab kamida {MIN_REASON} ta belgidan iborat bo'lsin.</p>
        )}
        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
