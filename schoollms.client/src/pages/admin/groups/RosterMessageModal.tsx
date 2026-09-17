import { useEffect, useState } from 'react'
import { Send } from 'lucide-react'
import { sendBroadcast } from '@/api/services/messages'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'

interface Props {
  open: boolean
  onClose: () => void
  /** Tanlangan o'quvchilar id'lari — xabar ularning ota-onasiga boradi. */
  studentIds: string[]
}

/**
 * Ro'yxatdan tanlangan o'quvchilarning ota-onalariga xabar (§2.1.1, §2.2.1).
 *
 * <b>KANAL — TELEGRAM.</b> EduSchool bu yerda SMS yuboradi; bizda SMS
 * provayderi ulanmagan (mijoz javobi, SPEC §8.1) va yagona kanal — bot.
 * Shuning uchun matn ham "SMS" demaydi: botga ro'yxatdan o'tmagan ota-ona
 * xabarni OLMAYDI va ekran buni ochiq aytadi (yetkazilgan sonni ko'rsatadi).
 */
export function RosterMessageModal({ open, onClose, studentIds }: Props) {
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<{ sent: number; total: number } | null>(null)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda holat tozalanadi (maqsadli)
    if (open) {
      setMessage('')
      setError(null)
      setResult(null)
    }
  }, [open])

  const handleSend = async () => {
    const text = message.trim()
    if (!text) return
    setBusy(true)
    setError(null)
    try {
      const res = await sendBroadcast({
        scope: 'selected',
        onlyDebtors: false,
        studentIds,
        text,
      })
      setResult({ sent: res.sentCount, total: res.recipientCount })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Xabar yuborilmadi.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Ota-onalarga xabar (Telegram)"
      footer={
        result ? (
          <Button onClick={onClose}>Yopish</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Bekor qilish
            </Button>
            <Button onClick={handleSend} disabled={busy || !message.trim()}>
              <Send className="h-4 w-4" /> {busy ? 'Yuborilmoqda...' : 'Yuborish'}
            </Button>
          </>
        )
      }
    >
      {result ? (
        <div className="space-y-2">
          <p className="text-sm font-medium text-slate-800">
            {result.sent} ta ota-onaga yetkazildi.
          </p>
          {result.sent < result.total && (
            <p className="text-sm text-amber-700">
              {result.total - result.sent} tasi xabarni olmadi — ular botga hali ro'yxatdan
              o'tmagan.
            </p>
          )}
        </div>
      ) : (
        <div className="space-y-4">
          <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
            Qabul qiluvchilar: <b>{studentIds.length} ta</b> o'quvchining ota-onasi. Xabar
            Telegram bot orqali boradi.
          </div>
          <Textarea
            label="Xabar matni"
            rows={5}
            placeholder="Hurmatli ota-onalar, ..."
            value={message}
            onChange={(e) => setMessage(e.target.value)}
          />
          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
