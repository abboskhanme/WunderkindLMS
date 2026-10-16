import { useEffect, useState } from 'react'
import { Archive } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getArchiveReasons, type ArchiveReason } from '@/api/services/archiveReasons'
import { archiveClass } from '@/api/services/classes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select, Textarea } from '@/components/ui/Input'

/**
 * Sinfni arxivlash — o'quvchilar ham arxivlanadi. Ketish sababi katalogdan tanlanadi (ixtiyoriy) va
 * izoh yoziladi (student-flow.md, 2026-10-15): "O'quvchilar oqimi" hisobotida har o'quvchining
 * ketishi shu sabab bilan ko'rinadi. Ikkalasi bo'sh — izoh "Sinf arxivlandi (…)" bo'lib qoladi.
 */
export function ArchiveClassModal({
  cls,
  onClose,
  onArchived,
}: {
  cls: SchoolClass | null
  onClose: () => void
  onArchived: (cls: SchoolClass, archivedStudents: number) => void
}) {
  const [reasons, setReasons] = useState<ArchiveReason[]>([])
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!cls) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna har ochilganda holat tozalanadi (maqsadli)
    setReasonId('')
    setNote('')
    setError(null)
    getArchiveReasons()
      .then(setReasons)
      .catch(() => setReasons([]))
  }, [cls])

  const submit = () => {
    if (!cls) return
    setBusy(true)
    setError(null)
    archiveClass(cls.id, { archiveReasonId: reasonId || null, reason: note.trim() || null })
      .then((r) => onArchived(cls, r.archivedStudents))
      .catch((e) => setError(e?.response?.data?.message ?? 'Arxivlashda xatolik'))
      .finally(() => setBusy(false))
  }

  return (
    <Modal
      open={cls !== null}
      onClose={onClose}
      title={cls ? `"${cls.name}" sinfini arxivlash` : ''}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={submit} disabled={busy}>
            <Archive className="h-4 w-4" />
            {busy ? 'Arxivlanmoqda…' : 'Arxivlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm text-slate-600">
        <p>Sinfdagi barcha o'quvchilar ham arxivlanadi (login bloklanadi). Sinf arxivdan chiqarilsa, ular qaytadi.</p>
        <Select label="Ketish sababi" value={reasonId} onChange={(e) => setReasonId(e.target.value)}>
          <option value="">Tanlanmagan</option>
          {reasons.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </Select>
        <Textarea
          label="Izoh"
          rows={2}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder={cls ? `Sinf arxivlandi (${cls.name})` : ''}
        />
        {error && <p className="text-xs text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
