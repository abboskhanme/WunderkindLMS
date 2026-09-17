import { useEffect, useState } from 'react'
import { Send } from 'lucide-react'
import type { Student } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { sendBroadcast } from '@/api/services/messages'
import type { StudentListFilter } from '@/api/services/studentSearch'

/**
 * S-6 — joriy filtrga mos BARCHA o'quvchiga yuborish (tanlangan qatorlardan
 * mustaqil, EduSchool'dagi "barcha sahifalar"). Berilsa `recipients` e'tiborsiz
 * qoladi — oyna FILTR rejimida ochiladi.
 */
interface FilterScope {
  filter: StudentListFilter
  /** Filtrga mos jami o'quvchi soni — S-1 ro'yxati bilan AYNAN bir xil (`page.total`). */
  total: number
}

interface Props {
  open: boolean
  onClose: () => void
  recipients: Student[]
  filterScope?: FilterScope
}

/**
 * Tanlangan o'quvchilarning (yoki — S-6 — filtrga mos BARCHA o'quvchining)
 * ota-onalariga xabar yuborish.
 *
 * <b>KANAL — TELEGRAM, SMS EMAS.</b> Fayl nomi tarixiy sabab bilan `SmsModal`
 * bo'lib qolgan; endpoint esa `POST /admin/messages/broadcast` va u xabarni
 * botga ro'yxatdan o'tgan ota-onalarga yuboradi. SMS provayderi tizimga
 * ulanmagan (mijoz javobi, SPEC §8.1), shuning uchun bu yerda "SMS" deyish
 * foydalanuvchini chalg'itardi.
 *
 * <b>YETKAZILGAN SON KO'RSATILADI.</b> Ilgari bu oyna hech narsa yubormasdan
 * "SMS yuborildi" deb yozardi (`// TODO: API` va ustidan `alert`). Bu shunchaki
 * ulanmagan tugma emas, YOLG'ON edi: direktor xabar ketdi deb o'ylardi.
 * Endi javobdagi `sentCount` / `recipientCount` ko'rsatiladi — botga
 * ro'yxatdan o'tmagan ota-ona xabarni OLMAYDI, va buni ekran ochiq aytadi.
 *
 * <b>FILTR REJIMI — SANAB, TASDIQLANIB YUBORILADI (S-6).</b> Bu butun maktabga
 * yetishi mumkin bo'lgan amal, shuning uchun jo'natish tugmasi sonni ANIQ
 * ko'rsatgan holda alohida "tasdiqlayman" katagi bosilmaguncha o'chiq turadi.
 */
export function SmsModal({ open, onClose, recipients, filterScope }: Props) {
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<{ sent: number; total: number } | null>(null)
  const [confirmed, setConfirmed] = useState(false)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda holatni tozalaymiz (maqsadli)
    if (open) {
      setMessage('')
      setError(null)
      setResult(null)
      setConfirmed(false)
    }
  }, [open])

  const handleSend = async () => {
    const text = message.trim()
    if (!text) return
    if (filterScope && !confirmed) return
    setBusy(true)
    setError(null)
    try {
      const res = filterScope
        ? await sendBroadcast({ scope: 'filter', onlyDebtors: false, filter: filterScope.filter, text })
        : await sendBroadcast({
            scope: 'selected',
            onlyDebtors: false,
            studentIds: recipients.map((s) => s.id),
            text,
          })
      setResult({ sent: res.sentCount, total: res.recipientCount })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Xabar yuborilmadi.')
    } finally {
      setBusy(false)
    }
  }

  const canSend = filterScope ? confirmed && !!message.trim() : !!message.trim()

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={filterScope ? 'Filtrlangan ro\'yxatga xabar (Telegram)' : 'Ota-onalarga xabar (Telegram)'}
      footer={
        result ? (
          <Button onClick={onClose}>Yopish</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Bekor qilish
            </Button>
            <Button onClick={handleSend} disabled={busy || !canSend}>
              <Send className="h-4 w-4" /> {busy ? 'Yuborilmoqda…' : 'Yuborish'}
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
              {result.total - result.sent} tasi xabarni olmadi — ular botga hali
              ro'yxatdan o'tmagan.
            </p>
          )}
        </div>
      ) : (
        <div className="space-y-4">
          {filterScope ? (
            <div className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
              Qamrov: joriy filtrga mos <b>{filterScope.total} ta</b> o'quvchining ota-onasi —
              tanlangan qatorlardan MUSTAQIL. Bu butun maktabga yetishi mumkin bo'lgan amal.
            </div>
          ) : (
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
              Qabul qiluvchilar: <b>{recipients.length} ta</b> ota-ona. Xabar
              Telegram bot orqali boradi — botga ro'yxatdan o'tganlar oladi.
            </div>
          )}
          <Textarea
            label="Xabar matni"
            rows={5}
            placeholder="Hurmatli ota-onalar, ..."
            value={message}
            onChange={(e) => setMessage(e.target.value)}
          />
          {filterScope && (
            <label className="flex items-start gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                className="mt-0.5 h-4 w-4 accent-brand-600"
                checked={confirmed}
                onChange={(e) => setConfirmed(e.target.checked)}
              />
              Ha, ushbu <b>{filterScope.total} ta</b> o'quvchining barchasiga yuborishni
              tasdiqlayman.
            </label>
          )}
          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
