import { useEffect, useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'

interface Props {
  open: boolean
  /** Tahrirlashda joriy nom, yangida bo'sh */
  initialName?: string
  /**
   * YANGI jadval uchun oldindan to'ldirib qo'yiladigan nom. Birinchi
   * jadvalda "Asosiy jadval" turadi: nom o'ylab topish sahifadan
   * jadvalgacha bo'lgan yo'lda ortiqcha to'siq edi (mijoz, 2026-09-19:
   * "hozirgi holati menga yoqmadi tushunarsiz ekan"). Maydon baribir
   * tanlangan holda ochiladi — boshqacha nom yozmoqchi bo'lsa, yozadi.
   */
  defaultName?: string
  onClose: () => void
  onSubmit: (name: string) => void
}

export function TemplateNameModal({
  open,
  initialName,
  defaultName,
  onClose,
  onSubmit,
}: Props) {
  const [name, setName] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda nom maydonini sinxronlash (maqsadli)
    if (open) setName(initialName ?? defaultName ?? '')
  }, [open, initialName, defaultName])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    onSubmit(name.trim())
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={initialName ? 'Jadval nomini o\'zgartirish' : 'Yangi jadval'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="template-name-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="template-name-form" onSubmit={handleSubmit}>
        <Input
          label="Jadval nomi"
          required
          autoFocus
          placeholder="Masalan: Asosiy jadval"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
      </form>
    </Modal>
  )
}
