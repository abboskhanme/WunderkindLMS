import { useEffect, useState } from 'react'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createStudentStatus,
  deleteStudentStatus,
  getStudentStatuses,
  updateStudentStatus,
  type StudentStatusTag,
} from '@/api/services/studentStatuses'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { StatusChip } from './StatusChip'

/**
 * O'quvchi holatlari katalogi (§2.3, S-5) — "O'quv bo'limi → O'quvchi holatlari".
 *
 * NEGA SOZLAMALARDA EMAS. Sozlamalar marshruti `settings` ruxsatiga bog'langan,
 * bu API esa `students` ga — menyu va server zid bo'lardi. Aynan shu sabab
 * bilan "Sertifikat turlari" ham shu bo'limda turadi (navigation.ts izohi).
 *
 * ISHLATILGAN HOLAT O'CHIRILMAYDI: o'chirilsa o'quvchilarning tagi jimgina
 * bo'shardi. Ro'yxatdan chiqarish yo'li — "Faol" belgisini olib tashlash.
 */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/** Tayyor ranglar — iOS palitrasi; qo'lda ham yozish mumkin. */
const PRESETS = ['#34C759', '#007AFF', '#FF9500', '#FF3B30', '#AF52DE', '#8E8E93']

export function StudentStatusesPage() {
  const [rows, setRows] = useState<StudentStatusTag[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<StudentStatusTag | null>(null)
  const [name, setName] = useState('')
  const [color, setColor] = useState('')
  const [position, setPosition] = useState(0)
  const [isActive, setIsActive] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const sortRows = (list: StudentStatusTag[]) =>
    [...list].sort((a, b) => a.position - b.position || a.name.localeCompare(b.name))

  useEffect(() => {
    getStudentStatuses(true)
      .then((list) => setRows(sortRows(list)))
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setName('')
    setColor(PRESETS[rows.length % PRESETS.length])
    setPosition(Math.max(0, ...rows.map((r) => r.position)) + 1)
    setIsActive(true)
    setError(null)
    setOpen(true)
  }

  const openEdit = (r: StudentStatusTag) => {
    setEditing(r)
    setName(r.name)
    setColor(r.color ?? '')
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
      const input = { name: name.trim(), color: color.trim(), position, isActive }
      const saved = editing
        ? await updateStudentStatus(editing.id, input)
        : await createStudentStatus(input)
      setRows((prev) => sortRows([...prev.filter((x) => x.id !== saved.id), saved]))
      setOpen(false)
    } catch (err) {
      setError(errorText(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (r: StudentStatusTag) => {
    if (!confirm(`"${r.name}" holatini o'chirasizmi?`)) return
    try {
      await deleteStudentStatus(r.id)
      setRows((prev) => prev.filter((x) => x.id !== r.id))
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'quvchi holatlari</h1>
        <p className="text-sm text-slate-400">
          Ro'yxatdagi rangli nishon — "VIP", "Sinov muddatida", "Ko'chib ketmoqchi". Arxivlash
          sababi bilan chalkashtirmang: u o'quvchi ketgandan keyin qo'yiladi.
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <Card className="p-0">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
            <p className="text-sm text-slate-500">{rows.length} ta holat</p>
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Yangi holat
            </Button>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-16 px-4 py-3">Tartib</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3">Qo'yilgan</th>
                  <th className="px-4 py-3">Faol</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <tr key={r.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{r.position}</td>
                    <td className="px-4 py-3">
                      <StatusChip name={r.name} color={r.color} />
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
                        {!r.isDefault && (
                          <button
                            type="button"
                            title="Tahrirlash"
                            onClick={() => openEdit(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                        )}
                        {!r.isDefault && r.usedBy === 0 && (
                          <button
                            type="button"
                            title="O'chirish"
                            onClick={() => remove(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        )}
                        {r.isDefault && <span className="text-xs text-slate-400">Tizim holati</span>}
                      </div>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={5} className="px-4 py-8 text-center text-slate-400">
                      Hali holat qo'shilmagan
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={editing ? 'Holatni tahrirlash' : 'Yangi holat'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button type="submit" form="student-status-form" disabled={busy || !name.trim()}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="student-status-form" onSubmit={submit} className="space-y-4">
          <Input
            label="Holat nomi"
            required
            placeholder="masalan: Sinov muddatida"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />

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
            <p className="mt-1 text-xs text-slate-400">Bo'sh qoldirilsa — neytral (kulrang) nishon.</p>
          </div>

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
            Faol — o'quvchiga qo'yish mumkin
          </label>

          {error && <p className="text-sm text-red-600">{error}</p>}
        </form>
      </Modal>
    </div>
  )
}
