/**
 * QARZDOR HOLATLARI — rangli ma'lumotnoma
 * (docs/modules/existing-module-gaps.md §3.5).
 *
 * Bu maktabning O'Z ro'yxati: migratsiya to'rtta qator bilan boshlab beradi
 * ("Bog'lanildi", "To'lash va'da qilindi", "Javob bermayapti", "To'lov
 * qilindi"), qolganini administrator o'zi yozadi.
 *
 * O'CHIRISH TUGMASI YO'Q va bo'lmaydi. Holat amallarga bog'langan: uni
 * o'chirish eski amallarni "qaysi holat edi" degan savolga javobsiz
 * qoldirardi (bazada ham FK RESTRICT). Ishlatilmay qolgan holat KATALOGDAN
 * CHIQARILADI — eski amallarda ko'rinib turaveradi, yangisida tanlanmaydi.
 *
 * Izoh (hint) MUHIM: administratorlar almashganda "bu holat qachon
 * qo'yiladi" degan bilim shu yerda qoladi, odamlarning xotirasida emas.
 */
import { useState } from 'react'
import { Archive, Pencil, Plus, RotateCcw, Tag } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  createDebtorStatus,
  getDebtorStatuses,
  retireDebtorStatus,
  updateDebtorStatus,
  type DebtorStatus,
  type SaveDebtorStatus,
} from '@/api/services/debtorWorkflow'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

export function DebtorStatusesPage() {
  const { user } = useAuth()
  // SPEC §4.3: `/admin/finance/*` — faqat admin va direktor. Server baribir
  // 403 beradi; ekran esa 403 ni ko'rsatib o'tirmaydi, ochiq aytadi.
  const allowed = !!user && (user.role === 'admin' || user.role === 'superadmin')

  const { data, loading, error, refetch } = useAsync(
    () => (allowed ? getDebtorStatuses(true) : Promise.resolve<DebtorStatus[]>([])),
    [allowed],
  )

  const [editing, setEditing] = useState<DebtorStatus | null>(null)
  const [creating, setCreating] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [pageError, setPageError] = useState<string | null>(null)

  if (!allowed) {
    return (
      <Card className="p-6">
        <h1 className="text-xl font-semibold text-slate-800">Qarzdor holatlari</h1>
        <p className="mt-2 text-sm text-slate-500">
          Bu bo'lim faqat admin va direktor uchun.
        </p>
      </Card>
    )
  }

  const rows = data ?? []
  const active = rows.filter((r) => r.isActive)

  const retire = async (row: DebtorStatus) => {
    setPageError(null)
    try {
      await retireDebtorStatus(row.id)
      setNotice(`"${row.name}" katalogdan chiqarildi. Eski amallarda u ko'rinib turadi.`)
      refetch()
    } catch (e) {
      setPageError(e instanceof Error ? e.message : "Chiqarib bo'lmadi.")
    }
  }

  const restore = async (row: DebtorStatus) => {
    setPageError(null)
    try {
      await updateDebtorStatus(row.id, {
        name: row.name,
        color: row.color,
        hint: row.hint,
        position: row.position,
        isActive: true,
      })
      setNotice(`"${row.name}" katalogga qaytarildi.`)
      refetch()
    } catch (e) {
      setPageError(e instanceof Error ? e.message : "Qaytarib bo'lmadi.")
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Qarzdor holatlari</h1>
          <p className="text-sm text-slate-400">
            Jami {rows.length} ta holat · {active.length} tasi faol · qarzdorlar bo'limidagi
            "Amal qo'shish" oynasi shu ro'yxatdan tanlaydi
          </p>
        </div>
        <Button onClick={() => setCreating(true)}>
          <Plus className="h-4 w-4" /> Yangi holat
        </Button>
      </div>

      {notice && (
        <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">{notice}</p>
      )}
      {pageError && (
        <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{pageError}</p>
      )}

      <Card className="p-0">
        {loading && <Loader label="Yuklanmoqda..." />}
        {error && <p className="px-4 py-4 text-sm text-red-700">{error}</p>}

        {!loading && !error && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Tartib</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3">Izoh</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((r) => (
                  <tr key={r.id} className={cn('hover:bg-slate-50/60', !r.isActive && 'opacity-60')}>
                    <td className="px-4 py-3 text-slate-400">{r.position}</td>
                    <td className="px-4 py-3">
                      <span
                        className="inline-flex items-center gap-2 rounded-md px-2 py-0.5 text-xs font-medium"
                        style={
                          r.color
                            ? { backgroundColor: `${r.color}1a`, color: r.color }
                            : undefined
                        }
                      >
                        <Tag className="h-3.5 w-3.5" />
                        {r.name}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-500">{r.hint || '—'}</td>
                    <td className="px-4 py-3">
                      {r.isActive ? (
                        <span className="rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                          Faol
                        </span>
                      ) : (
                        <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                          Katalogdan chiqarilgan
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex justify-end gap-1">
                        <Button
                          variant="ghost"
                          className="px-2 py-1"
                          title="Tahrirlash"
                          onClick={() => setEditing(r)}
                        >
                          <Pencil className="h-4 w-4" />
                        </Button>
                        {r.isActive ? (
                          <Button
                            variant="ghost"
                            className="px-2 py-1"
                            title="Katalogdan chiqarish (qator o'chirilmaydi)"
                            onClick={() => retire(r)}
                          >
                            <Archive className="h-4 w-4" />
                          </Button>
                        ) : (
                          <Button
                            variant="ghost"
                            className="px-2 py-1"
                            title="Katalogga qaytarish"
                            onClick={() => restore(r)}
                          >
                            <RotateCcw className="h-4 w-4" />
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <p className="text-xs text-slate-400">
        Holat hech qachon o'chirilmaydi — u faqat katalogdan chiqariladi. Shunda eski
        amallar qaysi holatda yozilgani tarixda qolaveradi.
      </p>

      {(creating || editing) && (
        <StatusFormModal
          initial={editing}
          onClose={() => {
            setCreating(false)
            setEditing(null)
          }}
          onSaved={(message) => {
            setCreating(false)
            setEditing(null)
            setNotice(message)
            refetch()
          }}
        />
      )}
    </div>
  )
}

/** Yaratish va tahrirlash — bitta forma (maydonlar bir xil). */
function StatusFormModal({
  initial,
  onClose,
  onSaved,
}: {
  initial: DebtorStatus | null
  onClose: () => void
  onSaved: (message: string) => void
}) {
  const [name, setName] = useState(initial?.name ?? '')
  const [color, setColor] = useState(initial?.color || '#8E8E93')
  const [hint, setHint] = useState(initial?.hint ?? '')
  const [position, setPosition] = useState(String(initial?.position ?? 0))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canSave = name.trim().length > 0 && !saving

  const submit = async () => {
    if (!canSave) return
    setSaving(true)
    setError(null)
    const payload: SaveDebtorStatus = {
      name: name.trim(),
      color,
      hint: hint.trim() ? hint.trim() : null,
      position: Number(position) || 0,
      isActive: initial ? initial.isActive : true,
    }
    try {
      if (initial) {
        await updateDebtorStatus(initial.id, payload)
        onSaved(`"${payload.name}" yangilandi.`)
      } else {
        await createDebtorStatus(payload)
        onSaved(`"${payload.name}" qo'shildi.`)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Saqlab bo'lmadi.")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={initial ? 'Holatni tahrirlash' : 'Yangi holat'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={!canSave}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Input
          label="Nomi"
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Masalan: Sudga berildi"
        />

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Rang</span>
            <div className="flex items-center gap-2">
              <input
                type="color"
                value={color}
                onChange={(e) => setColor(e.target.value)}
                className="h-9 w-12 cursor-pointer rounded-lg border border-slate-200"
              />
              <span className="text-sm text-slate-500">{color.toUpperCase()}</span>
            </div>
          </label>

          <Input
            label="Tartib raqami"
            type="number"
            min={0}
            value={position}
            onChange={(e) => setPosition(e.target.value)}
          />
        </div>

        <Textarea
          label="Izoh — bu holat qachon qo'yiladi"
          rows={3}
          value={hint}
          onChange={(e) => setHint(e.target.value)}
          placeholder="Masalan: bir necha marta urinildi, ota-ona javob bermadi"
        />

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      </div>
    </Modal>
  )
}
