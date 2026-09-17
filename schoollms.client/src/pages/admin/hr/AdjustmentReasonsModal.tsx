/**
 * Sabab katalogini boshqarish (F11.02) — "sozlamalar" bo'limi, xuddi shu
 * sahifada oyna sifatida (finance-parity.md: "same service, settings tab").
 *
 * Moliyaviy EMAS: o'chirish tugmasi yo'q, faqat faollik almashtiriladi
 * (`CategoriesPage.tsx` naqshi) — sabab allaqachon yozuvlarda ishlatilgan
 * bo'lishi mumkin, uni o'chirish tarixni "yetim" qoldirardi.
 */
import { useCallback, useEffect, useState } from 'react'
import { Pencil, Plus } from 'lucide-react'
import type { AdjustmentKind, AdjustmentReason } from '@/api/services/payrollAdjustments'
import {
  adjustmentKindLabels,
  createAdjustmentReason,
  getAdjustmentReasons,
  updateAdjustmentReason,
} from '@/api/services/payrollAdjustments'
import { billingErrorMessage } from '@/api/services/billingError'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Notice, StatusPill } from '../billing/BillingUi'

interface Props {
  open: boolean
  kind: AdjustmentKind
  onClose: () => void
  /** Ro'yxat oynasi yopilganda (sabab qo'shilgan/o'zgargan bo'lishi mumkin) yangilanishi uchun. */
  onChanged: () => void
}

export function AdjustmentReasonsModal({ open, kind, onClose, onChanged }: Props) {
  const [rows, setRows] = useState<AdjustmentReason[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [editing, setEditing] = useState<AdjustmentReason | null>(null)
  const [name, setName] = useState('')
  const [position, setPosition] = useState(0)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getAdjustmentReasons(kind)
      .then(setRows)
      .catch((e: unknown) => setError(billingErrorMessage(e, "Sabablarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [kind])

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli) */
    setEditing(null)
    setName('')
    setPosition(0)
    setFormError(null)
    /* eslint-enable react-hooks/set-state-in-effect */
    load()
  }, [open, load])

  const startCreate = () => {
    setEditing(null)
    setName('')
    setPosition(rows.length)
    setFormError(null)
  }

  const startEdit = (row: AdjustmentReason) => {
    setEditing(row)
    setName(row.name)
    setPosition(row.position)
    setFormError(null)
  }

  const toggleActive = async (row: AdjustmentReason) => {
    try {
      const updated = await updateAdjustmentReason(row.id, {
        name: row.name, isActive: !row.isActive, position: row.position,
      })
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
      onChanged()
    } catch (e: unknown) {
      setError(billingErrorMessage(e, "O'zgartirib bo'lmadi"))
    }
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const trimmed = name.trim()
    if (!trimmed || saving) return

    setSaving(true)
    setFormError(null)
    try {
      if (editing) {
        const updated = await updateAdjustmentReason(editing.id, {
          name: trimmed, isActive: editing.isActive, position,
        })
        setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
      } else {
        const created = await createAdjustmentReason({ kind, name: trimmed, position })
        setRows((prev) => [...prev, created])
      }
      onChanged()
      startCreate()
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`${adjustmentKindLabels[kind]} sabablari`}
      size="md"
    >
      <div className="space-y-4">
        {error && <Notice>{error}</Notice>}

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-hidden rounded-xl border border-slate-100">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Nomi</th>
                  <th className="px-3 py-2">Holat</th>
                  <th className="px-3 py-2 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={3} className="px-3 py-6 text-center text-sm text-slate-400">
                      Hozircha sabab yo'q.
                    </td>
                  </tr>
                )}
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-3 py-2 font-medium text-slate-800">{row.name}</td>
                    <td className="px-3 py-2">
                      <button type="button" onClick={() => toggleActive(row)}>
                        {row.isActive
                          ? <StatusPill tone="success">Faol</StatusPill>
                          : <StatusPill>Faolsiz</StatusPill>}
                      </button>
                    </td>
                    <td className="px-3 py-2 text-right">
                      <button
                        type="button"
                        title="Tahrirlash"
                        onClick={() => startEdit(row)}
                        className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <form onSubmit={submit} className="flex items-end gap-2 border-t border-slate-100 pt-4">
          <div className="flex-1">
            <Input
              label={editing ? 'Sababni tahrirlash' : 'Yangi sabab'}
              required
              placeholder="masalan: Kechikish"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          <Button type="submit" disabled={!name.trim() || saving}>
            {editing ? 'Saqlash' : <><Plus className="h-4 w-4" /> Qo'shish</>}
          </Button>
          {editing && (
            <Button type="button" variant="secondary" onClick={startCreate}>
              Bekor
            </Button>
          )}
        </form>
        {formError && <Notice>{formError}</Notice>}
      </div>
    </Modal>
  )
}
