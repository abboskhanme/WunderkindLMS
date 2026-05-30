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

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    if (open) setName(initial?.name ?? '')
  }, [open, initial])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    onSubmit({ name: name.trim() })
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
      <form id="subject-form" onSubmit={handleSubmit}>
        <Input
          label="Fan nomi"
          required
          placeholder="Masalan: Matematika"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
      </form>
    </Modal>
  )
}
