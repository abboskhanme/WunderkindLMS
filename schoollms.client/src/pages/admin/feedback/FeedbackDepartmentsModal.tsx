import { useState } from 'react'
import { AlertTriangle, Plus } from 'lucide-react'
import {
  saveFeedbackDepartment,
  type FeedbackDepartment,
  type FeedbackDepartmentMember,
} from '@/api/services/feedback'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { cn } from '@/lib/utils'

interface Props {
  departments: FeedbackDepartment[]
  assignees: FeedbackDepartmentMember[]
  onClose: () => void
  onChanged: (departments: FeedbackDepartment[]) => void
}

/**
 * Bo'limlar (O'quv bo'limi, Moliya, Ovqatlanish …) and their responsible people. A new item sent to
 * a department reaches its people through the bot; a department with nobody falls back to everyone
 * who works the inbox. A person without the "Taklif va shikoyatlar" permission gets the message but
 * cannot open the screen — they are marked so the admin can grant it.
 */
export function FeedbackDepartmentsModal({ departments, assignees, onClose, onChanged }: Props) {
  const [editing, setEditing] = useState<FeedbackDepartment | 'new' | null>(null)
  const [name, setName] = useState('')
  const [isActive, setIsActive] = useState(true)
  const [members, setMembers] = useState<string[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const open = (d: FeedbackDepartment | 'new') => {
    setEditing(d)
    setName(d === 'new' ? '' : d.name)
    setIsActive(d === 'new' ? true : d.isActive)
    setMembers(d === 'new' ? [] : d.members.map((m) => m.userId))
    setError('')
  }

  const save = async () => {
    if (!editing || !name.trim()) return
    setSaving(true)
    setError('')
    try {
      const position = editing === 'new' ? departments.length + 1 : editing.position
      const saved = await saveFeedbackDepartment(editing === 'new' ? null : editing.id, {
        name: name.trim(),
        position,
        isActive,
        memberUserIds: members,
      })
      onChanged(
        editing === 'new' ? [...departments, saved] : departments.map((d) => (d.id === saved.id ? saved : d)),
      )
      setEditing(null)
    } catch (err) {
      setError(hrErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const toggle = (userId: string) =>
    setMembers((m) => (m.includes(userId) ? m.filter((x) => x !== userId) : [...m, userId]))

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title="Bo'limlar va mas'ullar"
      footer={
        editing ? (
          <>
            <Button variant="secondary" onClick={() => setEditing(null)}>
              Orqaga
            </Button>
            <Button onClick={save} disabled={!name.trim() || saving}>
              Saqlash
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
        )
      }
    >
      {editing ? (
        <div className="space-y-4">
          <Input label="Bo'lim nomi" required value={name} onChange={(e) => setName(e.target.value)} />
          <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            Faol (yuboruvchilar tanlay oladi)
          </label>
          <div>
            <p className="mb-2 text-sm font-medium text-slate-700">Mas'ullar — yangi murojaat botda shularga boradi</p>
            <div className="max-h-72 space-y-1 overflow-y-auto rounded-lg border border-slate-200 p-2">
              {assignees.map((a) => (
                <label
                  key={a.userId}
                  className={cn(
                    'flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-slate-50',
                    members.includes(a.userId) && 'bg-brand-50',
                  )}
                >
                  <input
                    type="checkbox"
                    checked={members.includes(a.userId)}
                    onChange={() => toggle(a.userId)}
                    className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                  />
                  <span className="flex-1 text-slate-700">{a.fullName}</span>
                  {!a.hasAccess && (
                    <span className="inline-flex items-center gap-1 text-xs text-amber-600" title="Rollar sahifasida «Taklif va shikoyatlar» ruxsatini bering">
                      <AlertTriangle className="h-3.5 w-3.5" /> ruxsati yo'q
                    </span>
                  )}
                </label>
              ))}
            </div>
          </div>
          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
        </div>
      ) : (
        <div className="space-y-2">
          {departments.map((d) => (
            <button
              key={d.id}
              type="button"
              onClick={() => open(d)}
              className="flex w-full items-center justify-between gap-3 rounded-lg border border-slate-200 px-3 py-2.5 text-left hover:bg-slate-50"
            >
              <div className="min-w-0">
                <p className={cn('text-sm font-medium', d.isActive ? 'text-slate-800' : 'text-slate-400 line-through')}>
                  {d.name}
                </p>
                <p className="truncate text-xs text-slate-400">
                  {d.members.length > 0
                    ? d.members.map((m) => m.fullName + (m.hasAccess ? '' : ' ⚠')).join(', ')
                    : "Mas'ul yo'q — murojaat barcha administratorlarga boradi"}
                </p>
              </div>
              <span className="shrink-0 text-xs font-medium text-brand-700">Tahrirlash</span>
            </button>
          ))}
          <Button variant="secondary" onClick={() => open('new')}>
            <Plus className="h-4 w-4" /> Yangi bo'lim
          </Button>
        </div>
      )}
    </Modal>
  )
}
