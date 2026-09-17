import { useEffect, useState } from 'react'
import type { SchoolClass, Teacher } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import { getHomeroomTeachers } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { languageOptions, gradeOptions } from '@/config/constants'

interface Props {
  open: boolean
  onClose: () => void
  /** `homeroomTeacherIds` — C-5: tanlangan sinf rahbari(lar)i, alohida endpoint bilan saqlanadi. */
  onSubmit: (values: ClassPayload, homeroomTeacherIds: string[]) => void
  initial?: SchoolClass | null
}

const empty: ClassPayload = {
  name: '',
  grade: 1,
  language: 'uz',
  monthlyFee: 0,
  room: '',
  capacity: null,
}

export function ClassFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<ClassPayload>(empty)
  /** C-5: barcha (faol) o'qituvchilar — tanlov ro'yxati uchun. */
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [homeroomIds, setHomeroomIds] = useState<string[]>([])

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setForm(
      initial
        ? {
            name: initial.name,
            grade: initial.grade,
            language: initial.language,
            monthlyFee: initial.monthlyFee,
            room: initial.room ?? '',
            capacity: initial.capacity ?? null,
          }
        : empty,
    )
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda tozalanadi, quyida haqiqiy qiymat bilan to'ldiriladi (maqsadli)
    setHomeroomIds([])
    getTeachers().then(setTeachers)
    if (initial) {
      getHomeroomTeachers(initial.id).then((rows) => setHomeroomIds(rows.map((r) => r.id)))
    }
  }, [open, initial])

  const update = <K extends keyof ClassPayload>(key: K, value: ClassPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const toggleHomeroom = (teacherId: string) =>
    setHomeroomIds((prev) =>
      prev.includes(teacherId) ? prev.filter((id) => id !== teacherId) : [...prev, teacherId],
    )

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.name.trim()) return
    onSubmit(form, homeroomIds)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Sinfni tahrirlash' : 'Yangi sinf'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="class-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="class-form" onSubmit={handleSubmit} className="space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Sinf nomi"
            required
            placeholder="3-A"
            value={form.name}
            onChange={(e) => update('name', e.target.value)}
          />
          <Select
            label="Sinf (daraja)"
            value={form.grade}
            onChange={(e) => update('grade', Number(e.target.value))}
          >
            {gradeOptions.map((g) => (
              <option key={g} value={g}>
                {g}-sinf
              </option>
            ))}
          </Select>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Xona"
            placeholder="301"
            value={form.room}
            onChange={(e) => update('room', e.target.value)}
          />
          <Select
            label="Til"
            value={form.language}
            onChange={(e) => update('language', e.target.value as ClassPayload['language'])}
          >
            {languageOptions.map((l) => (
              <option key={l.value} value={l.value}>
                {l.label}
              </option>
            ))}
          </Select>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Oylik to'lov (so'm)"
            type="number"
            min={0}
            step={50000}
            value={form.monthlyFee}
            onChange={(e) => update('monthlyFee', Number(e.target.value))}
          />
          {/* C-4: sig'im — OGOHLANTIRISH chegarasi, taqiq emas. Bo'sh = chek yo'q. */}
          <Input
            label="Sig'im (ixtiyoriy)"
            type="number"
            min={1}
            placeholder="Cheksiz"
            value={form.capacity ?? ''}
            onChange={(e) => update('capacity', e.target.value === '' ? null : Number(e.target.value))}
          />
        </div>

        {/* C-5: sinf rahbari(lari) — bugun faqat o'qituvchi kartochkasidan sozlanardi. */}
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Sinf rahbari(lari)</span>
          {teachers.length === 0 ? (
            <p className="text-sm text-slate-400">O'qituvchilar ro'yxati bo'sh</p>
          ) : (
            <div className="max-h-40 space-y-1 overflow-y-auto rounded-lg border border-slate-200 p-2">
              {teachers.map((t) => (
                <label
                  key={t.id}
                  className="flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm text-slate-700 hover:bg-slate-50"
                >
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                    checked={homeroomIds.includes(t.id)}
                    onChange={() => toggleHomeroom(t.id)}
                  />
                  <span className="truncate">{t.fullName}</span>
                  {t.homeroomClass && t.homeroomClass !== initial?.name && (
                    <span className="ml-auto shrink-0 text-xs text-amber-600">
                      hozir: {t.homeroomClass}
                    </span>
                  )}
                </label>
              ))}
            </div>
          )}
        </div>
      </form>
    </Modal>
  )
}
