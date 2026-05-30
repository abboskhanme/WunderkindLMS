import { useEffect, useState } from 'react'
import type { SchoolClass, Subject, Teacher } from '@/types'
import type { TeacherPayload } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { genderOptions, teacherPermissions } from '@/config/constants'
import { cn, randomPassword } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: TeacherPayload) => void
  initial?: Teacher | null
  subjects: Subject[]
  classes: SchoolClass[]
}

const empty: TeacherPayload = {
  fullName: '',
  birthDate: '',
  address: '',
  gender: 'male',
  phone: '',
  homeroomClass: '',
  subjectIds: [],
  salary: 0,
  salaryStartMonth: '',
  // Yangi o'qituvchiga standart — barcha bo'limlar ochiq.
  permissions: teacherPermissions.map((p) => p.key),
}

export function TeacherFormModal({ open, onClose, onSubmit, initial, subjects, classes }: Props) {
  const [form, setForm] = useState<TeacherPayload>(empty)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setForm(
      initial
        ? {
            fullName: initial.fullName,
            birthDate: initial.birthDate,
            address: initial.address,
            gender: initial.gender,
            phone: initial.phone ?? '',
            homeroomClass: initial.homeroomClass,
            subjectIds: [...initial.subjectIds],
            salary: initial.salary,
            salaryStartMonth: initial.salaryStartMonth ?? '',
            permissions: [...(initial.permissions ?? [])],
          }
        : empty,
    )
  }, [open, initial])

  const update = <K extends keyof TeacherPayload>(key: K, value: TeacherPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const toggleSubject = (id: string) =>
    setForm((f) => ({
      ...f,
      subjectIds: f.subjectIds.includes(id)
        ? f.subjectIds.filter((x) => x !== id)
        : [...f.subjectIds, id],
    }))

  const togglePermission = (key: string) =>
    setForm((f) => ({
      ...f,
      permissions: f.permissions.includes(key)
        ? f.permissions.filter((x) => x !== key)
        : [...f.permissions, key],
    }))

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim()) return
    onSubmit(form)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? "O'qituvchini tahrirlash" : "Yangi o'qituvchi"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="teacher-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="teacher-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="F.I.SH"
          required
          value={form.fullName}
          onChange={(e) => update('fullName', e.target.value)}
        />
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Tug'ilgan kun"
            type="date"
            value={form.birthDate}
            onChange={(e) => update('birthDate', e.target.value)}
          />
          <Select
            label="Jinsi"
            value={form.gender}
            onChange={(e) => update('gender', e.target.value as TeacherPayload['gender'])}
          >
            {genderOptions.map((g) => (
              <option key={g.value} value={g.value}>
                {g.label}
              </option>
            ))}
          </Select>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => update('address', e.target.value)}
          />
          <Input
            label="Telefon"
            type="tel"
            placeholder="+998 ..."
            value={form.phone ?? ''}
            onChange={(e) => update('phone', e.target.value)}
          />
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Select
            label="Sinf rahbarligi"
            value={form.homeroomClass}
            onChange={(e) => update('homeroomClass', e.target.value)}
          >
            <option value="">Yo'q</option>
            {classes.map((c) => (
              <option key={c.id} value={c.name}>
                {c.name}
              </option>
            ))}
          </Select>
          <Input
            label="Oylik ish haqi (so'm)"
            type="number"
            min={0}
            step={100000}
            value={form.salary}
            onChange={(e) => update('salary', Number(e.target.value))}
          />
        </div>
        <div>
          <Input
            label="Oylik qaysi oydan hisoblansin"
            type="month"
            value={form.salaryStartMonth}
            onChange={(e) => update('salaryStartMonth', e.target.value)}
          />
          <p className="mt-1 text-xs text-slate-400">
            Bo'sh qoldirilsa — hisobot davri boshidan. Ishga kirgan oydan oldin maktab qarzdor bo'lmaydi.
          </p>
        </div>

        <div>
          <span className="mb-2 block text-sm font-medium text-slate-600">Dars beradigan fanlar</span>
          <div className="flex flex-wrap gap-2">
            {subjects.map((s) => {
              const active = form.subjectIds.includes(s.id)
              return (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => toggleSubject(s.id)}
                  className={cn(
                    'rounded-full border px-3 py-1 text-sm transition-colors',
                    active
                      ? 'border-brand-500 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {s.name}
                </button>
              )
            })}
            {subjects.length === 0 && (
              <p className="text-sm text-slate-400">Avval fan qo'shing</p>
            )}
          </div>
        </div>

        <div className="border-t border-slate-100 pt-4">
          <span className="mb-1 block text-sm font-medium text-slate-600">
            Web panel bo'limlari (ruxsatlar)
          </span>
          <p className="mb-2 text-xs text-slate-400">
            O'qituvchi web panelida qaysi bo'limlardan foydalana olishini belgilang. "Bosh sahifa" har doim ochiq.
          </p>
          <div className="flex flex-wrap gap-2">
            {teacherPermissions.map((p) => {
              const active = form.permissions.includes(p.key)
              return (
                <button
                  key={p.key}
                  type="button"
                  onClick={() => togglePermission(p.key)}
                  className={cn(
                    'rounded-full border px-3 py-1 text-sm transition-colors',
                    active
                      ? 'border-brand-500 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {p.label}
                </button>
              )
            })}
          </div>
        </div>

        {initial && (
          <div className="border-t border-slate-100 pt-4">
            <span className="mb-1 block text-sm font-medium text-slate-600">Parolni almashtirish</span>
            <div className="flex items-start gap-2">
              <div className="flex-1">
                <Input
                  type="text"
                  autoComplete="new-password"
                  placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                  value={form.newPassword ?? ''}
                  onChange={(e) => update('newPassword', e.target.value)}
                />
              </div>
              <Button
                type="button"
                variant="secondary"
                onClick={() => update('newPassword', randomPassword())}
              >
                Generatsiya
              </Button>
            </div>
            <p className="mt-1 text-xs text-slate-400">
              Login (username) o'zgarmaydi. Yangi parolni kiriting yoki generatsiya qiling — saqlangach
              o'qituvchiga topshiring.
            </p>
          </div>
        )}
      </form>
    </Modal>
  )
}
