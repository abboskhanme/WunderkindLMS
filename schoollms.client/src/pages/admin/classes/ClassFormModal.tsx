import { useEffect, useState } from 'react'
import type { SchoolClass } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { languageOptions, gradeOptions } from '@/config/constants'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: ClassPayload) => void
  initial?: SchoolClass | null
}

const empty: ClassPayload = {
  name: '',
  grade: 1,
  language: 'uz',
  monthlyFee: 0,
  room: '',
}

export function ClassFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<ClassPayload>(empty)

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
          }
        : empty,
    )
  }, [open, initial])

  const update = <K extends keyof ClassPayload>(key: K, value: ClassPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.name.trim()) return
    onSubmit(form)
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
        <Input
          label="Oylik to'lov (so'm)"
          type="number"
          min={0}
          step={50000}
          value={form.monthlyFee}
          onChange={(e) => update('monthlyFee', Number(e.target.value))}
        />
      </form>
    </Modal>
  )
}
