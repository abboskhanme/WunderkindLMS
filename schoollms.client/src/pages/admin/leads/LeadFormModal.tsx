import { useEffect, useState } from 'react'
import type { Lead } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { genderOptions, gradeOptions } from '@/config/constants'

export type LeadFormValues = Omit<Lead, 'id' | 'stage'>

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: LeadFormValues) => void
  /** Tahrirlash uchun mavjud lid, qo'shish uchun null */
  initial?: Lead | null
}

const empty: LeadFormValues = {
  fullName: '',
  gender: 'male',
  birthDate: '',
  parentFullName: '',
  parentPhone: '',
  targetGrade: 1,
  note: '',
}

export function LeadFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<LeadFormValues>(empty)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setForm(
      initial
        ? {
            fullName: initial.fullName,
            gender: initial.gender,
            birthDate: initial.birthDate,
            parentFullName: initial.parentFullName,
            parentPhone: initial.parentPhone,
            targetGrade: initial.targetGrade,
            note: initial.note ?? '',
          }
        : empty,
    )
  }, [open, initial])

  const update = <K extends keyof LeadFormValues>(key: K, value: LeadFormValues[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim()) return
    onSubmit(form)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Lidni tahrirlash' : 'Yangi lid'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="lead-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="lead-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="F.I.SH"
          required
          value={form.fullName}
          onChange={(e) => update('fullName', e.target.value)}
        />
        <div className="grid grid-cols-2 gap-4">
          <Select
            label="Jinsi"
            value={form.gender}
            onChange={(e) => update('gender', e.target.value as LeadFormValues['gender'])}
          >
            {genderOptions.map((g) => (
              <option key={g.value} value={g.value}>
                {g.label}
              </option>
            ))}
          </Select>
          <Input
            label="Tug'ilgan kun"
            type="date"
            value={form.birthDate}
            onChange={(e) => update('birthDate', e.target.value)}
          />
        </div>
        <Input
          label="Ota-onasi F.I.SH"
          value={form.parentFullName}
          onChange={(e) => update('parentFullName', e.target.value)}
        />
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Ota-onasi raqami"
            placeholder="+998 90 123 45 67"
            value={form.parentPhone}
            onChange={(e) => update('parentPhone', e.target.value)}
          />
          <Select
            label="Nechinchi sinfga"
            value={form.targetGrade}
            onChange={(e) => update('targetGrade', Number(e.target.value))}
          >
            {gradeOptions.map((g) => (
              <option key={g} value={g}>
                {g}-sinf
              </option>
            ))}
          </Select>
        </div>
        <Textarea
          label="Izoh"
          rows={3}
          value={form.note}
          onChange={(e) => update('note', e.target.value)}
        />
      </form>
    </Modal>
  )
}
