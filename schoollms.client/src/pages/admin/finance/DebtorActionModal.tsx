/**
 * "IZOH QO'SHISH" — qarzdor bilan ishlash oynasi
 * (docs/modules/existing-module-gaps.md §3.5).
 *
 * FAQAT IZOH (mijoz, 2026-09-19: "shunchaki izoh yozilsa yetadi qolgan
 * qismlari kerakmas"). Ilgari bu yerda holat tanlash va "va'da qilingan
 * to'lov sanasi" ham bor edi — ikkalasi olib tashlandi, ro'yxatdagi "Va'da"
 * ustuni o'rnini ham izoh egalladi.
 *
 * SERVER KONTRAKTI TEGILMAGAN: `addDebtorAction` hamon `statusId` va
 * `promisedOn` ni qabul qiladi, bu yerdan shunchaki `null` ketadi. Sabab —
 * eski yozuvlar (holati va va'dasi bor amallar) o'z ma'nosini yo'qotmasin
 * va kerak bo'lsa maydonlar qaytarilsin.
 *
 * SAQLANGACH OYNA YOPILADI va chaqiruvchi yashil xabar ko'rsatadi
 * (`onSaved`). Ilgari oyna ochiq qolib, "saqlandimi yoki yo'qmi" degan savol
 * tug'dirardi.
 *
 * TARIX o'sha joyida qoladi: administrator "yana bir marta qo'ng'iroq
 * qilaymi" degan qarorni oldingi yozuvlarni ko'rib qabul qiladi. Amal
 * O'CHIRILMAYDI — "O'chirish" serverda `deleted_at` qo'yadi.
 *
 * Pul bu yerda HISOBLANMAYDI: qarz summasi chaqiruvchidan tayyor keladi.
 */
import { useState } from 'react'
import { AlertTriangle, CalendarClock, Trash2, User } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  addDebtorAction,
  deleteDebtorAction,
  getDebtorActions,
} from '@/api/services/debtorWorkflow'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { cn, formatMoney } from '@/lib/utils'
import { formatDateTime } from './reportLabels'

interface Props {
  studentId: string
  studentName: string
  className: string
  /** Serverdan kelgan joriy qarz (so'm) — faqat ko'rsatish uchun. */
  debt: number
  onClose: () => void
  /**
   * Izoh saqlandi: ro'yxat yangilanadi va chaqiruvchi yashil xabar
   * ko'rsatadi. Oynani ham CHAQIRUVCHI yopadi (`setActing(null)`).
   */
  onSaved: (studentName: string) => void
}

export function DebtorActionModal({
  studentId,
  studentName,
  className,
  debt,
  onClose,
  onSaved,
}: Props) {
  const history = useAsync(() => getDebtorActions(studentId), [studentId])

  const [comment, setComment] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSave = comment.trim().length > 0 && !saving

  const submit = async () => {
    if (!canSave) return
    setSaving(true)
    setError(null)
    try {
      await addDebtorAction(studentId, {
        comment: comment.trim(),
        // Holat va va'da ish oqimidan olib tashlandi (fayl boshidagi izoh).
        statusId: null,
        promisedOn: null,
      })
      setComment('')
      history.refetch()
      onSaved(studentName)
    } catch (e) {
      setError(e instanceof Error ? e.message : "Saqlab bo'lmadi.")
    } finally {
      setSaving(false)
    }
  }

  const remove = async (id: string) => {
    setError(null)
    try {
      await deleteDebtorAction(id)
      history.refetch()
      onSaved(studentName)
    } catch (e) {
      setError(e instanceof Error ? e.message : "O'chirib bo'lmadi.")
    }
  }

  const actions = history.data ?? []

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={`Izoh — ${studentName}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Yopish
          </Button>
          <Button onClick={submit} disabled={!canSave}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div className="flex flex-wrap items-center gap-3 rounded-xl bg-slate-50 p-3 text-sm">
          <span className="rounded-md bg-white px-2 py-0.5 text-xs font-medium text-slate-600">
            {className}
          </span>
          <span className="text-slate-500">Joriy qarz:</span>
          <span className="font-semibold text-red-600">{formatMoney(debt)}</span>
        </div>

        {/* ---- Yangi amal ---- */}
        <div className="space-y-3">
          <Textarea
            label="Izoh"
            required
            rows={3}
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            placeholder="Masalan: onasiga qo'ng'iroq qilindi, oylik olgach to'lashini aytdi"
          />

          {error && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
          )}
        </div>

        {/* ---- Tarix ---- */}
        <div>
          <h4 className="mb-2 text-sm font-semibold text-slate-700">Izohlar tarixi</h4>

          {history.loading && <Loader label="Yuklanmoqda..." />}
          {history.error && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{history.error}</p>
          )}

          {!history.loading && !history.error && actions.length === 0 && (
            <p className="rounded-lg bg-slate-50 px-3 py-3 text-sm text-slate-400">
              Hali birorta izoh yozilmagan. Birinchisini yuqorida qo'shing.
            </p>
          )}

          <ul className="divide-y divide-slate-100">
            {actions.map((a) => (
              <li key={a.id} className="flex items-start gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <p className="text-sm text-slate-800">{a.comment}</p>
                  <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-slate-400">
                    {a.statusName && (
                      <span
                        className="rounded-md px-2 py-0.5 font-medium"
                        style={
                          a.statusColor
                            ? { backgroundColor: `${a.statusColor}1a`, color: a.statusColor }
                            : undefined
                        }
                      >
                        {a.statusName}
                      </span>
                    )}
                    {a.promisedOn && (
                      <span
                        className={cn(
                          'inline-flex items-center gap-1',
                          a.promiseOverdue ? 'text-red-600' : 'text-amber-600',
                        )}
                      >
                        {a.promiseOverdue ? (
                          <AlertTriangle className="h-3.5 w-3.5" />
                        ) : (
                          <CalendarClock className="h-3.5 w-3.5" />
                        )}
                        Va'da: {a.promisedOn}
                      </span>
                    )}
                    <span className="inline-flex items-center gap-1">
                      <User className="h-3.5 w-3.5" />
                      {a.createdByName}
                    </span>
                    <span>{formatDateTime(a.createdAt)}</span>
                  </div>
                </div>
                <button
                  onClick={() => remove(a.id)}
                  title="Amalni o'chirish (yozuv bazada qoladi)"
                  className="rounded-lg p-1.5 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-500"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </li>
            ))}
          </ul>

          {actions.length > 0 && (
            <p className="mt-2 text-xs text-slate-400">
              O'chirilgan amal ro'yxatdan yo'qoladi, lekin bazada qoladi — nima va'da
              qilingani tarixi saqlanishi kerak.
            </p>
          )}
        </div>
      </div>
    </Modal>
  )
}
