import { useEffect, useState } from 'react'
import type { Subject } from '@/types'
import type { SubjectPayload } from '@/api/services/subjects'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: SubjectPayload) => void
  initial?: Subject | null
}

export function SubjectFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [name, setName] = useState('')
  const [isGroupable, setIsGroupable] = useState(false)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    if (open) {
      setName(initial?.name ?? '')
      setIsGroupable(initial?.isGroupable ?? false)
    }
  }, [open, initial])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    onSubmit({ name: name.trim(), isGroupable })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Fanni tahrirlash' : 'Yangi fan'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="subject-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="subject-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Fan nomi"
          required
          placeholder="Masalan: Matematika"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />

        {/*
          G-9: shu bayroq yoqilgan fanlargagina o'quv guruhi ochiladi
          ("Guruhlar" bo'limi). Bayroq YOQISH hech narsani o'zgartirmaydi —
          u faqat guruh formasidagi fan ro'yxatini ochadi.
        */}
        <label className="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200 px-3 py-2.5">
          <input
            type="checkbox"
            className="mt-0.5 h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
            checked={isGroupable}
            onChange={(e) => setIsGroupable(e.target.checked)}
          />
          <span>
            <span className="block text-sm font-medium text-slate-700">Guruhlarga bo'linadi</span>
            <span className="block text-xs text-slate-400">
              Bu fan bo'yicha bir nechta sinfdan yig'iladigan o'quv guruhi ochish mumkin
              bo'ladi. Faol guruhi bor fandan belgini olib tashlab bo'lmaydi.
            </span>
          </span>
        </label>
      </form>
    </Modal>
  )
}
