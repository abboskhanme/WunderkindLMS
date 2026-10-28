/**
 * XULQ — bir o'quvchiga xulq-atvor yozuvi, dars ekranidan (mijoz, 2026-10-15; docs/modules/behaviour.md).
 *
 * EduSchool'dagi tartib: avval turi (Ijobiy / Salbiy), keyin shu turdagi sabablar balli bilan,
 * ixtiyoriy izoh. "Qo'yish" DARHOL saqlaydi — darsning "Saqlash" tugmasiga bog'liq emas. Tepada
 * o'quvchining joriy bali (100 dan boshlanadi) va bugungi o'zgarishi; pastda oxirgi yozuvlar —
 * o'qituvchi faqat o'zi BUGUN qo'yganini o'chira oladi (server ham shuni tekshiradi).
 */
import { useMemo, useState } from 'react'
import { Send, Trash2 } from 'lucide-react'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { dateTime, fullDate } from '../../lib/format'
import { haptic, hapticNotify } from '../../lib/telegram'
import { BottomSheet, Btn, INPUT_CLASS } from '../hr/shared'

const TYPES = [
  { value: 'positive', label: 'Ijobiy' },
  { value: 'negative', label: 'Salbiy' },
]

/** "+3" / "−10" — the minus is a real minus sign, as on the admin screens. */
export function signedPoints(p) {
  if (p > 0) return `+${p}`
  if (p < 0) return `−${Math.abs(p)}`
  return '0'
}

export function BehaviourSheet({ student, reasons, onClose, onChanged }) {
  const q = useAsync(() => teacherApi.disciplinePoints(student.id), [student.id])
  // Most entries teachers make are violations — the sheet opens on "Salbiy".
  const [type, setType] = useState('negative')
  const [reasonId, setReasonId] = useState(null)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const [deleting, setDeleting] = useState(null)

  const ofType = useMemo(
    () => reasons.filter((r) => r.type === type).sort((a, b) => a.name.localeCompare(b.name)),
    [reasons, type],
  )
  // Muddatli sabab (haftasiga / oyiga bir marta) shu o'quvchiga shu muddatda qo'yilgan bo'lsa — tanlanmaydi.
  const taken = useMemo(() => new Set(q.data?.takenReasonIds ?? []), [q.data])
  const selected = ofType.find((r) => r.id === reasonId && !taken.has(r.id)) || null

  const pickType = (next) => {
    haptic()
    setType(next)
    setReasonId(null)
  }

  const save = async () => {
    if (!selected || saving) return
    setSaving(true)
    setError(null)
    try {
      await teacherApi.addDisciplinePoint({ studentId: student.id, reasonId: selected.id, note: note.trim() || null })
      hapticNotify('success')
      onChanged?.()
      onClose()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Saqlab bo'lmadi")
      setSaving(false)
    }
  }

  const remove = async (item) => {
    if (deleting) return
    setDeleting(item.id)
    setError(null)
    try {
      await teacherApi.deleteDisciplinePoint(item.id)
      haptic('medium')
      q.reload()
      onChanged?.()
    } catch (e) {
      setError(e?.message || "O'chirib bo'lmadi")
    } finally {
      setDeleting(null)
    }
  }

  const data = q.data

  return (
    <BottomSheet
      title={student.fullName}
      busy={saving}
      onClose={onClose}
      footer={
        <Btn variant="primary" block loading={saving} disabled={!selected} onClick={save}>
          {selected ? `Qo'yish · ${signedPoints(selected.points)}` : 'Sababni tanlang'}
        </Btn>
      }
    >
      <p className="text-[13px] text-slate-500">
        Joriy ball:{' '}
        <b className={data && data.remaining < 100 ? 'text-red-600' : 'text-brand-ink'}>
          {data ? data.remaining : '…'}
        </b>
        {data && data.today !== 0 && (
          <>
            {' · bugun '}
            <b className={data.today < 0 ? 'text-red-600' : 'text-emerald-700'}>{signedPoints(data.today)}</b>
          </>
        )}
      </p>

      <div className="mt-3 flex rounded-2xl bg-slate-100 p-1">
        {TYPES.map((t) => {
          const on = t.value === type
          return (
            <button
              key={t.value}
              type="button"
              onClick={() => pickType(t.value)}
              className={
                'flex-1 rounded-xl px-3 py-2 text-[15px] font-semibold transition ' +
                (on
                  ? t.value === 'negative'
                    ? 'bg-white text-red-600 shadow-sm'
                    : 'bg-white text-emerald-700 shadow-sm'
                  : 'text-slate-500')
              }
            >
              {t.label}
            </button>
          )
        })}
      </div>

      {ofType.length === 0 ? (
        <p className="mt-4 text-[13px] text-slate-400">
          Sizga biriktirilgan bu turdagi sabab yo'q. Maktab ma'muriyati «Ball sabablar» bo'limida mas'ul qilib
          biriktiradi.
        </p>
      ) : (
        <div className="mt-3 flex flex-wrap gap-2">
          {ofType.map((r) => {
            const on = r.id === reasonId
            const used = taken.has(r.id)
            return (
              <button
                key={r.id}
                type="button"
                disabled={used}
                onClick={() => {
                  haptic()
                  setReasonId(on ? null : r.id)
                }}
                title={r.description || r.name}
                className={
                  'rounded-xl px-3 py-2 text-left text-[14px] font-semibold disabled:opacity-50 ' +
                  (on ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-700')
                }
              >
                {r.name}{' '}
                <span className={on ? '' : r.points < 0 ? 'text-red-600' : 'text-emerald-700'}>
                  {signedPoints(r.points)}
                </span>
                {r.period && r.period !== 'none' && (
                  <span className="block text-[11px] font-medium text-slate-400">
                    {used
                      ? r.period === 'week' ? 'Shu hafta qo\'yilgan' : 'Shu oy qo\'yilgan'
                      : r.period === 'week' ? 'Haftasiga 1 marta' : 'Oyiga 1 marta'}
                  </span>
                )}
              </button>
            )
          })}
        </div>
      )}

      {selected?.notifyParent && (
        <p className="mt-2 flex items-center gap-1 text-[12px] text-sky-600">
          <Send className="h-3.5 w-3.5" /> Ota-onaga Telegram orqali xabar boradi.
        </p>
      )}

      <label className="mt-4 block">
        <span className="mb-1.5 block text-[13px] font-semibold text-slate-500">Izoh (ixtiyoriy)</span>
        <textarea
          value={note}
          maxLength={500}
          rows={2}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Masalan: tanaffusda sinfdoshi bilan urushdi"
          className={INPUT_CLASS + ' resize-none'}
        />
      </label>

      {error && <p className="mt-3 text-[13px] font-medium text-red-600">{error}</p>}

      <p className="mb-1 mt-5 text-[13px] font-semibold text-slate-500">Oxirgi yozuvlar</p>
      {q.loading && !data ? (
        <p className="py-3 text-[13px] text-slate-400">Yuklanmoqda…</p>
      ) : q.error ? (
        <p className="py-3 text-[13px] text-slate-400">{q.error}</p>
      ) : (data?.items ?? []).length === 0 ? (
        <p className="py-3 text-[13px] text-slate-400">Hali yozuv yo'q.</p>
      ) : (
        data.items.map((item) => (
          <div key={item.id} className="flex items-center gap-3 border-t border-slate-100 py-2.5 first:border-t-0">
            <span
              className={
                'w-11 shrink-0 rounded-lg py-1 text-center text-[13px] font-bold ' +
                (item.points < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-700')
              }
            >
              {signedPoints(item.points)}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-[14px] font-semibold">{item.reasonName}</p>
              <p className="truncate text-[12px] text-slate-400">
                {[
                  item.source === 'attendance' ? fullDate(item.createdAt) : dateTime(item.createdAt),
                  item.source === 'attendance' ? 'davomat' : item.createdBy,
                  item.note && item.source !== 'attendance' ? item.note : null,
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
            </div>
            {item.canDelete && (
              <button
                type="button"
                onClick={() => remove(item)}
                disabled={deleting === item.id}
                aria-label="O'chirish"
                className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-500 disabled:opacity-40"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            )}
          </div>
        ))
      )}
    </BottomSheet>
  )
}
