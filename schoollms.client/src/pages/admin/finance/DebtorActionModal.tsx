/**
 * "AMAL QO'SHISH" — qarzdor bilan ishlash oynasi
 * (docs/modules/existing-module-gaps.md §3.5).
 *
 * Bitta oynada ikkita narsa: YANGI amal formasi va shu o'quvchining butun
 * TARIXI. Ular ataylab birga — administrator "yana bir marta qo'ng'iroq
 * qilaymi" degan qarorni oldingi suhbatlarni ko'rib qabul qiladi.
 *
 * UCHTA QOIDA EKRANDA HAM KO'RINADI:
 *   1. Izoh MAJBURIY — izohsiz amal "kimdir nimadir qildi" degani.
 *   2. Joriy holat SAQLANMAYDI — u eng oxirgi amalniki. Shuning uchun
 *      "holatni o'zgartirish" degan alohida tugma YO'Q: holat yangi amal
 *      bilan birga qo'yiladi.
 *   3. Amal O'CHIRILMAYDI — "O'chirish" tugmasi serverda `deleted_at`
 *      qo'yadi. Tugma yonidagi izoh ham shuni aytadi.
 *
 * Pul bu yerda HISOBLANMAYDI: qarz summasi chaqiruvchidan (qarzdorlar
 * ro'yxatidan) tayyor holda keladi va faqat ko'rsatiladi.
 */
import { useState } from 'react'
import { AlertTriangle, CalendarClock, Trash2, User } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  addDebtorAction,
  deleteDebtorAction,
  getDebtorActions,
  getDebtorStatuses,
} from '@/api/services/debtorWorkflow'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
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
  /** Ro'yxatni yangilash — yangi amal ro'yxat ustunlarini o'zgartiradi. */
  onSaved: () => void
}

export function DebtorActionModal({
  studentId,
  studentName,
  className,
  debt,
  onClose,
  onSaved,
}: Props) {
  const statuses = useAsync(() => getDebtorStatuses(), [])
  const history = useAsync(() => getDebtorActions(studentId), [studentId])

  const [comment, setComment] = useState('')
  const [statusId, setStatusId] = useState('')
  const [promisedOn, setPromisedOn] = useState('')
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
        statusId: statusId || null,
        promisedOn: promisedOn || null,
      })
      setComment('')
      setStatusId('')
      setPromisedOn('')
      history.refetch()
      onSaved()
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
      onSaved()
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
      title={`Amal qo'shish — ${studentName}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Yopish
          </Button>
          <Button onClick={submit} disabled={!canSave}>
            {saving ? 'Saqlanmoqda...' : 'Amalni saqlash'}
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
            label="Nima qilindi"
            required
            rows={3}
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            placeholder="Masalan: onasiga qo'ng'iroq qilindi, oylik olgach to'lashini aytdi"
          />

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Select
              label="Holat"
              value={statusId}
              onChange={(e) => setStatusId(e.target.value)}
              disabled={statuses.loading}
            >
              <option value="">O'zgartirilmasin</option>
              {(statuses.data ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>

            <Input
              label="Va'da qilingan to'lov sanasi"
              type="date"
              value={promisedOn}
              onChange={(e) => setPromisedOn(e.target.value)}
            />
          </div>

          {statusId && (
            <p className="text-xs text-slate-400">
              {(statuses.data ?? []).find((s) => s.id === statusId)?.hint ??
                "Bu amaldan keyin o'quvchining joriy holati shu bo'ladi."}
            </p>
          )}

          <p className="text-xs text-slate-400">
            Va'da sanasi o'tib ketsa va qarz hali yopilmagan bo'lsa — bu "buzilgan va'da"
            bo'lib direktor paneliga chiqadi.
          </p>

          {error && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
          )}
        </div>

        {/* ---- Tarix ---- */}
        <div>
          <h4 className="mb-2 text-sm font-semibold text-slate-700">Amallar tarixi</h4>

          {history.loading && <Loader label="Yuklanmoqda..." />}
          {history.error && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{history.error}</p>
          )}

          {!history.loading && !history.error && actions.length === 0 && (
            <p className="rounded-lg bg-slate-50 px-3 py-3 text-sm text-slate-400">
              Hali birorta amal yozilmagan. Birinchisini yuqorida qo'shing.
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
