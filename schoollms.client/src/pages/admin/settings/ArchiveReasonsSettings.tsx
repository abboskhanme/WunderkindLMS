import { useEffect, useState } from 'react'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createArchiveReason,
  deleteArchiveReason,
  getArchiveReasons,
  updateArchiveReason,
  type ArchiveReason,
} from '@/api/services/archiveReasons'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

/**
 * Arxivlash sabablari katalogi (§2.2) — "Sozlamalar → Arxivlash sabablari".
 *
 * ISHLATILGAN SABAB O'CHIRILMAYDI. Aks holda arxivdagi o'quvchi "nega ketgani"ni
 * yo'qotardi. Ro'yxatdan chiqarish — "Faol" belgisini olib tashlash: eski yozuvlar
 * joyida qoladi, yangi arxivlashda esa tanlanmaydi. `Ishlatilgan` ustuni aynan
 * §2.2 ning maqsadi — "nega ketishyapti" degan savolga guruhlab javob.
 */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

export function ArchiveReasonsSettings() {
  const [rows, setRows] = useState<ArchiveReason[]>([])
  const [loading, setLoading] = useState(true)
  const [editing, setEditing] = useState<ArchiveReason | null>(null)
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [position, setPosition] = useState(0)
  const [isActive, setIsActive] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const sortRows = (list: ArchiveReason[]) =>
    [...list].sort((a, b) => a.position - b.position || a.name.localeCompare(b.name))

  useEffect(() => {
    getArchiveReasons(true)
      .then((list) => setRows(sortRows(list)))
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setName('')
    // "Boshqa" 99-o'rinda turadi — yangi sabab undan oldin chiqsin.
    setPosition(Math.max(0, ...rows.filter((r) => r.position < 99).map((r) => r.position)) + 1)
    setIsActive(true)
    setError(null)
    setOpen(true)
  }

  const openEdit = (r: ArchiveReason) => {
    setEditing(r)
    setName(r.name)
    setPosition(r.position)
    setIsActive(r.isActive)
    setError(null)
    setOpen(true)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    setBusy(true)
    setError(null)
    try {
      const input = { name: name.trim(), position, isActive }
      const saved = editing
        ? await updateArchiveReason(editing.id, input)
        : await createArchiveReason(input)
      setRows((prev) => sortRows([...prev.filter((x) => x.id !== saved.id), saved]))
      setOpen(false)
    } catch (err) {
      setError(errorText(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (r: ArchiveReason) => {
    if (!confirm(`"${r.name}" sababini o'chirasizmi?`)) return
    try {
      await deleteArchiveReason(r.id)
      setRows((prev) => prev.filter((x) => x.id !== r.id))
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
        <div>
          <p className="font-semibold text-slate-800">Arxivlash sabablari</p>
          <p className="text-sm text-slate-400">
            O'quvchini arxivlashda tanlanadi — "nega ketishyapti" degan savolga guruhlab javob beradi.
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" /> Yangi sabab
        </Button>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
            <tr>
              <th className="w-16 px-4 py-3">Tartib</th>
              <th className="px-4 py-3">Sabab</th>
              <th className="px-4 py-3">Ishlatilgan</th>
              <th className="px-4 py-3">Holat</th>
              <th className="px-4 py-3 text-right">Amallar</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {rows.map((r) => (
              <tr key={r.id} className={cn('hover:bg-slate-50/60', !r.isActive && 'text-slate-400')}>
                <td className="px-4 py-3 text-slate-400">{r.position}</td>
                <td className={cn('px-4 py-3 font-medium', r.isActive ? 'text-slate-800' : 'text-slate-400')}>
                  {r.name}
                </td>
                <td className="px-4 py-3 text-slate-600">{r.usedBy} ta o'quvchi</td>
                <td className="px-4 py-3">
                  <span
                    className={cn(
                      'rounded-md px-2 py-0.5 text-xs font-medium',
                      r.isActive ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-100 text-slate-400',
                    )}
                  >
                    {r.isActive ? 'Faol' : 'Faol emas'}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center justify-end gap-0.5">
                    <button
                      type="button"
                      title="Tahrirlash"
                      onClick={() => openEdit(r)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                    {r.usedBy === 0 && (
                      <button
                        type="button"
                        title="O'chirish"
                        onClick={() => remove(r)}
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-slate-400">
                  Hali sabab qo'shilmagan
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={editing ? 'Sababni tahrirlash' : 'Yangi sabab'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button type="submit" form="archive-reason-form" disabled={busy || !name.trim()}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="archive-reason-form" onSubmit={submit} className="space-y-4">
          <Input
            label="Sabab nomi"
            required
            placeholder="masalan: Boshqa maktabga o'tdi"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <Input
            label="Tartib raqami (kichigi tepada)"
            type="number"
            min={0}
            value={position}
            onChange={(e) => setPosition(Math.max(0, Number(e.target.value) || 0))}
          />
          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            Faol — arxivlash oynasida tanlanadi
          </label>
          {error && <p className="text-sm text-red-600">{error}</p>}
        </form>
      </Modal>
    </Card>
  )
}
