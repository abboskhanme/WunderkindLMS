import { useEffect, useState } from 'react'
import type { Subject } from '@/types'
import type { SubjectPayload } from '@/api/services/subjects'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: SubjectPayload) => void
  initial?: Subject | null
}

/** Tayyor ranglar — StudentStatusesPage bilan bir xil iOS palitrasi (F-3). */
const PRESETS = ['#34C759', '#007AFF', '#FF9500', '#FF3B30', '#AF52DE', '#8E8E93']

export function SubjectFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [name, setName] = useState('')
  const [color, setColor] = useState('')
  const [isActive, setIsActive] = useState(true)

  useEffect(() => {
    if (open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani to'ldiramiz (maqsadli, loyihadagi mavjud naqsh)
      setName(initial?.name ?? '')
      setColor(initial?.color ?? '')
      setIsActive(initial?.isActive ?? true)
    }
  }, [open, initial])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    // `isGroupable` endi ma'nosiz (istalgan fanga guruh ochiladi) — borini o'zgartirmay yuboramiz.
    onSubmit({ name: name.trim(), isGroupable: initial?.isGroupable ?? false, color: color.trim(), isActive })
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

        {/* F-3: jadval katakchasini bo'yaydigan rang. */}
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Rang</span>
          <div className="flex flex-wrap items-center gap-2">
            {PRESETS.map((c) => (
              <button
                key={c}
                type="button"
                title={c}
                onClick={() => setColor(c)}
                style={{ backgroundColor: c }}
                className={cn(
                  'h-7 w-7 rounded-full border-2 transition-transform',
                  color.toUpperCase() === c ? 'border-slate-800 scale-110' : 'border-white',
                )}
              />
            ))}
            <input
              value={color}
              onChange={(e) => setColor(e.target.value)}
              placeholder="#34C759"
              className="w-28 rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
            {color && (
              <button
                type="button"
                onClick={() => setColor('')}
                className="text-sm text-slate-400 hover:text-slate-600"
              >
                Tozalash
              </button>
            )}
          </div>
          <p className="mt-1 text-xs text-slate-400">
            Bo'sh qoldirilsa — jadval katagi neytral rangda qoladi.
          </p>
        </div>

        {/*
          F-3: o'chirish o'rniga arxivlash. O'chirish (Trash) hamon bor —
          faqat ishlatilmagan fan uchun ishlaydi; faolsizlantirish esa
          ishlatilgan fanni ham ro'yxatdan yangi tanlovlarda olib tashlaydi.
        */}
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600"
          />
          Faol — jadval, jurnal va guruh tanlovida ko'rinadi
        </label>
      </form>
    </Modal>
  )
}
