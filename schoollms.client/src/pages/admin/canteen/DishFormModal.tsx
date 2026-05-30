import { useEffect, useRef, useState } from 'react'
import { ImagePlus, X } from 'lucide-react'
import type { Dish } from '@/types'
import type { DishPayload } from '@/api/services/canteen'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Textarea } from '@/components/ui/Input'

interface Props {
  open: boolean
  title: string
  initial?: Dish | null
  onClose: () => void
  onSubmit: (payload: DishPayload) => void
}

export function DishFormModal({ open, title, initial, onClose, onSubmit }: Props) {
  const [name, setName] = useState('')
  const [ingredients, setIngredients] = useState('')
  const [imageUrl, setImageUrl] = useState<string | undefined>(undefined)
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setName(initial?.name ?? '')
    setIngredients(initial?.ingredients ?? '')
    setImageUrl(initial?.imageUrl)
  }, [open, initial])

  const onFile = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    const reader = new FileReader()
    reader.onload = () => setImageUrl(reader.result as string)
    reader.readAsDataURL(file)
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    onSubmit({ name: name.trim(), ingredients: ingredients.trim(), imageUrl })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={title}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="dish-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="dish-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Ovqat nomi"
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <Textarea
          label="Tarkibi"
          rows={3}
          placeholder="Masalan: Guruch, go'sht, sabzi..."
          value={ingredients}
          onChange={(e) => setIngredients(e.target.value)}
        />

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Rasm</span>
          {imageUrl ? (
            <div className="relative w-fit">
              <img
                src={imageUrl}
                alt=""
                className="h-32 w-full max-w-xs rounded-lg object-cover"
              />
              <button
                type="button"
                onClick={() => setImageUrl(undefined)}
                className="absolute right-2 top-2 rounded-full bg-slate-900/60 p-1 text-white hover:bg-slate-900"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          ) : (
            <button
              type="button"
              onClick={() => fileRef.current?.click()}
              className="flex h-32 w-full max-w-xs flex-col items-center justify-center gap-1 rounded-lg border-2 border-dashed border-slate-200 text-slate-400 transition-colors hover:border-brand-300 hover:text-brand-600"
            >
              <ImagePlus className="h-6 w-6" />
              <span className="text-sm">Rasm yuklash</span>
            </button>
          )}
          <input
            ref={fileRef}
            type="file"
            accept="image/*"
            onChange={onFile}
            className="hidden"
          />
        </div>
      </form>
    </Modal>
  )
}
