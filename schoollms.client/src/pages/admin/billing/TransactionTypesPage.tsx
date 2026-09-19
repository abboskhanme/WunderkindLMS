/**
 * Tranzaksiya turi katalogi (Kirim/Chiqim) — mijoz yuborgan EduSchool kassa
 * kirim shakli ("Tranzaksiya turi *" — majburiy dropdown) va moliya
 * sozlamalari ekrani (pill-tab: Kirim · Chiqim · Bonus · Jarima, jadval
 * "№ / Nomi / Amallar"), 2026-09-18. `BillingSettingsPage.tsx` ning
 * kataloglar markazida oltinchi bo'lim sifatida ochiladi
 * (`/admin/billing/settings?tab=transaction-types`).
 *
 * FAQAT IKKI PILL, TO'RTTA EMAS — Bonus/Jarima uchun bu katalog
 * ALLAQACHON mavjud (`AdjustmentReasonsModal.tsx`, HR bo'limida, F11.02).
 * Uni bu yerda takrorlash ikkita mustaqil, bir-biridan uzoqlashadigan
 * katalog degani bo'lardi. Batafsil: `BillingSettingsPage.tsx` ning
 * "Tranzaksiya turi" xaritalash qatori.
 *
 * SEED QILINGAN QATOR — O'CHIRISH TUGMASI YO'Q, FAQAT TAHRIRLASH: mijoz
 * skrinshotidagi xatti-harakat. Server buni baribir 409 bilan rad etardi
 * (`seeded_type_protected`) — tugmani shu yerda ham yashirish "bosdim — xato
 * oldim" holatini oldini oladi (`ExpenseTemplatesPage.tsx` bilan bir xil
 * falsafa: kerak bo'lmagan tugmani yashirish kerakli tugmani ochib
 * qo'yishdan xavfsizroq).
 */
import { useCallback, useEffect, useState } from 'react'
import { ArrowDownCircle, ArrowUpCircle, Lock, Pencil, Plus, Trash2 } from 'lucide-react'
import type {
  TransactionType,
  TransactionTypeInput,
  TransactionTypeKind,
} from '@/api/services/transactionTypes'
import {
  createTransactionType,
  deleteTransactionType,
  getTransactionTypes,
  updateTransactionType,
} from '@/api/services/transactionTypes'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'
import { AsyncBlock, BillingGuard, IconBtn, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { TransactionTypeFormModal } from './TransactionTypeFormModal'

const KIND_TABS: Array<{ key: TransactionTypeKind; label: string; icon: typeof ArrowDownCircle }> = [
  { key: 'in', label: 'Kirim', icon: ArrowDownCircle },
  { key: 'out', label: 'Chiqim', icon: ArrowUpCircle },
]

export function TransactionTypesPage() {
  return (
    <BillingGuard>
      <TransactionTypesView />
    </BillingGuard>
  )
}

function TransactionTypesView() {
  const { canManageTransactionTypes } = useBillingAccess()

  const [kind, setKind] = useState<TransactionTypeKind>('in')
  const [rows, setRows] = useState<TransactionType[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<TransactionType | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [pageNotice, setPageNotice] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const load = useCallback((forKind: TransactionTypeKind) => {
    setLoading(true)
    setError(null)
    getTransactionTypes(forKind)
      .then(setRows)
      .catch((e: unknown) => setError(billingErrorMessage(e, "Turlarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- bo'lim (kind) almashganda ro'yxatni qayta yuklaymiz (maqsadli)
  useEffect(() => load(kind), [kind, load])

  const selectKind = (next: TransactionTypeKind) => {
    setKind(next)
    setPageNotice(null)
  }

  const openCreate = () => {
    setEditing(null)
    setFormError(null)
    setFormOpen(true)
  }

  const openEdit = (row: TransactionType) => {
    setEditing(row)
    setFormError(null)
    setFormOpen(true)
  }

  const handleSubmit = async (values: TransactionTypeInput) => {
    setSaving(true)
    setFormError(null)
    try {
      if (editing) {
        const updated = await updateTransactionType(editing.id, values)
        setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
        setPageNotice(`"${updated.name}" turi yangilandi.`)
      } else {
        const created = await createTransactionType(values)
        setRows((prev) => [...prev, created].sort((a, b) => a.position - b.position))
        setPageNotice(`"${created.name}" turi qo'shildi.`)
      }
      setFormOpen(false)
      setEditing(null)
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (row: TransactionType) => {
    if (!window.confirm(`"${row.name}" turi o'chiriladi. Davom etasizmi?`)) return
    setDeletingId(row.id)
    setPageNotice(null)
    try {
      await deleteTransactionType(row.id)
      setRows((prev) => prev.filter((r) => r.id !== row.id))
      setPageNotice(`"${row.name}" turi o'chirildi.`)
    } catch (e: unknown) {
      setError(billingErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setDeletingId(null)
    }
  }

  const activeCount = rows.filter((r) => r.isActive).length

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Tranzaksiya turi</h1>
          <p className="text-sm text-slate-400">
            Kassa kirim shaklidagi "Tranzaksiya turi" tanlovi — hisobotga (akkaunt) ta'sir qilmaydi,
            faqat yorliq. Jami {rows.length} ta · {activeCount} tasi faol.
          </p>
        </div>
        {canManageTransactionTypes && (
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi tur
          </Button>
        )}
      </div>

      <div className="inline-flex rounded-lg border border-slate-200 p-1">
        {KIND_TABS.map(({ key, label, icon: Icon }) => (
          <button
            key={key}
            type="button"
            onClick={() => selectKind(key)}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              kind === key ? 'bg-brand-600 text-white' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            <Icon className="h-4 w-4" /> {label}
          </button>
        ))}
      </div>

      {pageNotice && <Notice tone="success">{pageNotice}</Notice>}

      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText={`Hozircha "${KIND_TABS.find((t) => t.key === kind)?.label}" turkumida birorta tur yo'q.`}
          emptyAction={
            canManageTransactionTypes ? (
              <Button variant="secondary" onClick={openCreate}>
                <Plus className="h-4 w-4" /> Birinchi turni qo'shish
              </Button>
            ) : undefined
          }
          onRetry={() => load(kind)}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">№</th>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row, idx) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-500">{idx + 1}</td>
                    <td className="px-4 py-3">
                      <span className="font-medium text-slate-800">{row.name}</span>
                    </td>
                    <td className="px-4 py-3">
                      {row.isActive ? (
                        <StatusPill tone="success">Faol</StatusPill>
                      ) : (
                        <StatusPill>Faolsiz</StatusPill>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className="flex justify-end gap-1">
                        {canManageTransactionTypes ? (
                          <>
                            <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(row)} />
                            {row.isSeeded ? (
                              <span
                                title="Tizim tomonidan yaratilgan tur — o'chirib bo'lmaydi, faqat tahrirlash"
                                className="flex h-8 w-8 items-center justify-center text-slate-300"
                              >
                                <Lock className="h-4 w-4" />
                              </span>
                            ) : (
                              <IconBtn
                                icon={Trash2}
                                title="O'chirish"
                                tone="danger"
                                disabled={deletingId === row.id}
                                onClick={() => void handleDelete(row)}
                              />
                            )}
                          </>
                        ) : (
                          <span className="text-xs text-slate-300">—</span>
                        )}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </AsyncBlock>
      </Card>

      {canManageTransactionTypes && (
        <TransactionTypeFormModal
          open={formOpen}
          kind={kind}
          initial={editing}
          busy={saving}
          error={formError}
          onClose={() => {
            setFormOpen(false)
            setEditing(null)
          }}
          onSubmit={handleSubmit}
        />
      )}
    </div>
  )
}
