import { useEffect, useState } from 'react'
import { Check } from 'lucide-react'
import type { Stage, StageColor } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { stageColors, stageColorKeys } from '@/config/stageColors'
import { cn } from '@/lib/utils'
import type { StagePayload } from '@/api/services/stages'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: StagePayload) => void
  /** Tahrirlash uchun mavjud ustun, qo'shish uchun null */
  initial?: Stage | null
}

export function StageFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [title, setTitle] = useState('')
  const [color, setColor] = useState<StageColor>('blue')

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setTitle(initial?.title ?? '')
    setColor(initial?.color ?? 'blue')
  }, [open, initial])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!title.trim()) return
    onSubmit({ title: title.trim(), color })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Ustunni tahrirlash' : 'Yangi ustun'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="stage-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="stage-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Ustun nomi"
          required
          placeholder="Masalan: Kutilmoqda"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
        />
        <div>
          <span className="mb-2 block text-sm font-medium text-slate-600">Rang</span>
          <div className="flex flex-wrap gap-2">
            {stageColorKeys.map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => setColor(key)}
                className={cn(
                  'flex h-8 w-8 items-center justify-center rounded-full transition',
                  stageColors[key].swatch,
                  color === key && 'ring-2 ring-slate-800 ring-offset-2',
                )}
              >
                {color === key && <Check className="h-4 w-4 text-white" />}
              </button>
            ))}
          </div>
        </div>
      </form>
    </Modal>
  )
}
