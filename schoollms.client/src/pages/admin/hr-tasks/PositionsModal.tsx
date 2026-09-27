import { useState } from 'react'
import { Check, Pencil, RotateCcw, Trash2, X } from 'lucide-react'
import { deactivatePosition, getPositions, hrErrorMessage, updatePosition } from '@/api/services/hrTasks'
import type { HrPosition } from '@/types/hrTasks'
import { HR_MAX_PAGE_SIZE } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Badge, EmptyState, Notice, Skeleton, inputCls } from '@/components/hr-tasks/ui'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { NewPositionInline, POSITION_NAME_MAX } from './NewPositionInline'

const iconBtn = 'rounded-lg p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:opacity-50'

/**
 * "Lavozimlar" — list, add, rename, deactivate (the old `PositionsDialog`). Directors only.
 * No DELETE — deactivation keeps the name in template history.
 */
export function PositionsModal({
  onClose,
  onChanged,
  onToast,
}: {
  onClose: () => void
  /** The employee list shows position names — refresh the page. */
  onChanged: () => void
  onToast: (message: string, tone: 'success' | 'error') => void
}) {
  const [version, setVersion] = useState(0)
  const positions = useLoad(`positions|modal|${version}`, () => getPositions({ limit: HR_MAX_PAGE_SIZE }))
  const items = positions.data?.items ?? positions.previous?.items ?? []

  const [editingId, setEditingId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  function changed(message: string): void {
    onToast(message, 'success')
    setVersion((v) => v + 1)
    onChanged()
  }

  async function run(id: string, action: () => Promise<unknown>, message: string): Promise<void> {
    setBusyId(id)
    try {
      await action()
      setEditingId(null)
      setConfirmId(null)
      changed(message)
    } catch (err) {
      // 409 `PositionInUse` etc. — the text comes from the backend.
      onToast(hrErrorMessage(err), 'error')
    } finally {
      setBusyId(null)
    }
  }

  function submitRename(position: HrPosition): void {
    const name = editName.trim()
    if (name.length < 2 || name.length > POSITION_NAME_MAX) {
      onToast('Lavozim nomi 2–120 belgi bo‘lsin', 'error')
      return
    }
    void run(position.id, () => updatePosition(position.id, { name }), 'Lavozim nomi yangilandi')
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Lavozimlar"
      footer={
        <div className="flex w-full justify-end">
          <Button onClick={onClose}>Yopish</Button>
        </div>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-slate-400">Xodim va takroriy topshiriqlarni guruhlash uchun</p>
        <Notice kind="warning">
          Lavozim panelga kirish huquqini <b>bermaydi</b> — u yorliq va guruhlash vositasi. Huquq Boshqaruv → Rollar
          bo‘limida beriladi.
        </Notice>

        <NewPositionInline onCreated={() => changed('Lavozim qo‘shildi')} />

        {positions.loading && items.length === 0 ? (
          <div className="space-y-2">
            {Array.from({ length: 3 }, (_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : positions.error ? (
          <EmptyState
            title="Lavozimlarni yuklab bo‘lmadi"
            hint={positions.error}
            action={
              <Button variant="secondary" onClick={positions.reload}>
                Qayta urinish
              </Button>
            }
          />
        ) : items.length === 0 ? (
          <p className="rounded-xl border border-dashed border-slate-300 p-4 text-sm text-slate-500">
            Lavozim qo‘shilmagan — xodimlarni lavozimsiz ham yuritish mumkin.
          </p>
        ) : (
          <ul className="divide-y divide-slate-100">
            {items.map((position) => {
              const busy = busyId === position.id
              if (editingId === position.id) {
                return (
                  <li key={position.id} className="flex items-center gap-2 py-2">
                    <input
                      aria-label="Lavozim nomi"
                      className={inputCls}
                      maxLength={POSITION_NAME_MAX}
                      autoFocus
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                          e.preventDefault()
                          submitRename(position)
                        }
                        if (e.key === 'Escape') setEditingId(null)
                      }}
                    />
                    <button type="button" className={iconBtn} aria-label="Saqlash" disabled={busy} onClick={() => submitRename(position)}>
                      <Check className="h-4 w-4" />
                    </button>
                    <button type="button" className={iconBtn} aria-label="Bekor qilish" onClick={() => setEditingId(null)}>
                      <X className="h-4 w-4" />
                    </button>
                  </li>
                )
              }
              if (confirmId === position.id) {
                return (
                  <li key={position.id} className="space-y-2 py-2">
                    <p className="text-sm text-slate-700">
                      «{position.name}» ro‘yxatlardan yashirilsinmi? Yozuv o‘chmaydi — shablon tarixida nomi qoladi,
                      kerak bo‘lsa qayta faollashtiriladi.
                    </p>
                    <div className="flex flex-col gap-2 sm:flex-row">
                      <Button
                        variant="danger"
                        disabled={busy}
                        onClick={() => void run(position.id, () => deactivatePosition(position.id), 'Lavozim nofaol qilindi')}
                      >
                        Nofaol qilish
                      </Button>
                      <Button variant="secondary" onClick={() => setConfirmId(null)}>
                        Bekor qilish
                      </Button>
                    </div>
                  </li>
                )
              }
              return (
                <li key={position.id} className="flex items-center gap-2 py-2">
                  <div className="min-w-0 flex-1">
                    <p className={position.isActive ? 'truncate text-sm text-slate-800' : 'truncate text-sm text-slate-400 line-through'}>
                      {position.name}
                    </p>
                    {position.employeesCount !== undefined && (
                      <p className="text-xs text-slate-400">{position.employeesCount} ta xodim</p>
                    )}
                  </div>
                  {!position.isActive && <Badge>Nofaol</Badge>}
                  {position.isActive ? (
                    <>
                      <button
                        type="button"
                        className={iconBtn}
                        aria-label={`«${position.name}» nomini tahrirlash`}
                        onClick={() => {
                          setConfirmId(null)
                          setEditingId(position.id)
                          setEditName(position.name)
                        }}
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        type="button"
                        className={iconBtn}
                        aria-label={`«${position.name}» ni nofaol qilish`}
                        onClick={() => {
                          setEditingId(null)
                          setConfirmId(position.id)
                        }}
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </>
                  ) : (
                    <button
                      type="button"
                      className={iconBtn}
                      aria-label={`«${position.name}» ni qayta faollashtirish`}
                      disabled={busy}
                      onClick={() => void run(position.id, () => updatePosition(position.id, { isActive: true }), 'Lavozim qayta faollashtirildi')}
                    >
                      <RotateCcw className="h-4 w-4" />
                    </button>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </Modal>
  )
}
