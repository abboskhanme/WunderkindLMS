import { useState } from 'react'
import { Eye, EyeOff, Inbox, Plus } from 'lucide-react'
import type { CashBox } from '@/api/services/cashBoxes'
import { Card } from '@/components/ui/Card'
import { CashBoxCard, type CashBoxActionMode } from './CashBoxCard'

interface Props {
  boxes: CashBox[]
  selectedId: string | null
  canManage: boolean
  onSelect: (id: string) => void
  onAdd: () => void
  onEdit: (box: CashBox) => void
  onAction: (box: CashBox, mode: CashBoxActionMode) => void
}

/**
 * Chap ustun — kassalar ro'yxati (EduSchool tuzilishi, mijoz sxemasi:
 * "+"  yangi kassa qo'shadi, ko'z belgisi nofaol kassalarni yashiradi).
 */
export function CashBoxPanel({ boxes, selectedId, canManage, onSelect, onAdd, onEdit, onAction }: Props) {
  const [showInactive, setShowInactive] = useState(false)

  const visible = boxes.filter((b) => b.isActive || showInactive)
  const hiddenCount = boxes.length - boxes.filter((b) => b.isActive).length

  return (
    <Card className="h-fit p-4">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="font-semibold text-slate-800">Kassalar</h2>
        <div className="flex items-center gap-1">
          {hiddenCount > 0 && (
            <button
              type="button"
              onClick={() => setShowInactive((v) => !v)}
              title={showInactive ? "Nofaol kassalarni yashirish" : `Nofaol kassalarni ko'rsatish (${hiddenCount})`}
              aria-label="Nofaol kassalarni ko'rsatish/yashirish"
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
            >
              {showInactive ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          )}
          {canManage && (
            <button
              type="button"
              onClick={onAdd}
              title="Yangi kassa"
              aria-label="Yangi kassa qo'shish"
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
            >
              <Plus className="h-4 w-4" />
            </button>
          )}
        </div>
      </div>

      {visible.length === 0 ? (
        <div className="flex flex-col items-center gap-2 py-10 text-center">
          <Inbox className="h-7 w-7 text-slate-300" />
          <p className="text-sm text-slate-500">Kassa yo'q.</p>
          {canManage && (
            <button type="button" onClick={onAdd} className="text-sm font-medium text-brand-600 hover:underline">
              Birinchi kassani qo'shish
            </button>
          )}
        </div>
      ) : (
        <div className="space-y-3">
          {visible.map((box) => (
            <CashBoxCard
              key={box.id}
              box={box}
              selected={box.id === selectedId}
              canManage={canManage}
              onSelect={() => onSelect(box.id)}
              onEdit={() => onEdit(box)}
              onAction={(mode) => onAction(box, mode)}
            />
          ))}
        </div>
      )}
    </Card>
  )
}
