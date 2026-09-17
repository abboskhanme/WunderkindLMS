/**
 * Chiqimlar: ro'yxat, kiritish va TASDIQ NAVBATI (P1-17).
 *
 * UCHTA QAT'IY QOIDA
 * ------------------
 * 1. O'CHIRISH TUGMASI YO'Q. Yozilgan chiqim o'chirilmaydi va tahrirlanmaydi
 *    (SPEC §4.1) — xato yozuv ustiga STORNO qo'yiladi, sabab bilan. Shu
 *    sababli bu faylda `delete` so'zi faqat shu izohda uchraydi.
 * 2. IKKI QAVATLI NAZORAT (SPEC §4.5): tasdiqlash tugmasi chiqimni YOZGAN
 *    odamga ko'rsatilmaydi. Baza ham buni `ck_expenses_approver_differs`
 *    bilan rad etadi, lekin interfeys uni taklif ham qilmasligi kerak.
 * 3. 5 000 000 so'mdan yuqori chiqim ikkinchi tasdiqni talab qiladi va
 *    tepadagi navbatga tushadi. Undan pastlari darrov yozib qo'yiladi.
 *
 * BACKEND HOLATI: `/api/admin/billing/expenses` marshruti hali yozilmagan
 * (P1-13). Sahifa 404 ni "server buzildi" emas, "bu bo'lim hali ulanmagan"
 * holati sifatida ko'rsatadi — qarang `isEndpointMissing`.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Check, Clock, Plus, ShieldCheck, Undo2, Wallet } from 'lucide-react'
import type { ExpenseInput, ExpenseRecord } from '@/api/services/expenses'
import {
  approveExpense,
  createExpense,
  expenseState,
  getExpenses,
  needsApproval,
  reverseExpense,
} from '@/api/services/expenses'
import { billingErrorMessage, isEndpointMissing } from '@/api/services/billingError'
import type { PaymentMethod } from '@/types'
import { paymentMethodLabels } from '@/pages/admin/finance/reportLabels'
import { expenseCategories, financeCategoryLabel } from '@/config/constants'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, Notice, PendingQueue, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { ExpenseFormModal } from './ExpenseFormModal'
import { ReasonModal } from './ReasonModal'

const today = () => new Date().toISOString().slice(0, 10)
const monthStart = () => `${new Date().toISOString().slice(0, 7)}-01`

export function ExpensesPage() {
  return (
    <BillingGuard>
      <ExpensesView />
    </BillingGuard>
  )
}

function ExpensesView() {
  const { canRecordExpense, canApproveExpense, canReverseExpense, canApproveRecord, isOwn } =
    useBillingAccess()

  const [rows, setRows] = useState<ExpenseRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notWired, setNotWired] = useState(false)

  const [from, setFrom] = useState(monthStart())
  const [to, setTo] = useState(today())
  const [category, setCategory] = useState('')

  const [formOpen, setFormOpen] = useState(false)
  const [reversing, setReversing] = useState<ExpenseRecord | null>(null)
  const [busy, setBusy] = useState(false)
  // Tasdiqlashda pul qaysi usulda chiqqani — qator bo'yicha, chunki navbatda
  // bir nechta chiqim turishi mumkin va ular har xil usulda to'lanadi.
  const [approveMethods, setApproveMethods] = useState<Record<string, PaymentMethod>>({})
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    setNotWired(false)
    getExpenses({ from: from || undefined, to: to || undefined, category: category || undefined })
      .then(setRows)
      .catch((e: unknown) => {
        const missing = isEndpointMissing(e)
        setNotWired(missing)
        setError(
          missing
            ? "Chiqim endpoint'lari hali serverga ulanmagan (P1-13). Ma'lumot bazada bor, marshrut yozilgach bu sahifa o'zi ishlaydi."
            : billingErrorMessage(e, "Chiqimlarni yuklab bo'lmadi"),
        )
      })
      .finally(() => setLoading(false))
  }, [from, to, category])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (FinancePage bilan bir xil naqsh)
  useEffect(() => load(), [load])

  // Tasdiq navbati — chegaradan yuqori va hali tasdiqlanmagan chiqimlar.
  const pending = useMemo(
    () => rows.filter(needsApproval).sort((a, b) => a.createdAt.localeCompare(b.createdAt)),
    [rows],
  )

  const approvableCount = useMemo(
    () => pending.filter((e) => canApproveRecord(canApproveExpense, e)).length,
    [pending, canApproveExpense, canApproveRecord],
  )

  const handleCreate = async (values: ExpenseInput) => {
    setBusy(true)
    setActionError(null)
    try {
      const created = await createExpense(values)
      setFormOpen(false)
      setNotice(
        needsApproval(created)
          ? `Chiqim yozildi va tasdiq navbatiga tushdi (${formatMoney(created.amount)}).`
          : `Chiqim yozildi (${formatMoney(created.amount)}).`,
      )
      load()
    } catch (e: unknown) {
      setActionError(
        isEndpointMissing(e)
          ? "Chiqim yozish endpoint'i hali ulanmagan (P1-13)."
          : billingErrorMessage(e, "Chiqimni saqlab bo'lmadi"),
      )
    } finally {
      setBusy(false)
    }
  }

  /**
   * Tasdiqlash — pul AYNAN shu lahzada jurnalga tushadi, shuning uchun qaysi
   * usulda chiqqani ham shu yerda tanlanadi. Ilgari bu so'rov tanasiz ketardi
   * va tasdiqlash har safar xato bilan qaytardi.
   */
  const handleApprove = async (row: ExpenseRecord, method: PaymentMethod) => {
    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      await approveExpense(row.id, method)
      setNotice(`${formatMoney(row.amount)} chiqim tasdiqlandi.`)
      load()
    } catch (e: unknown) {
      setActionError(
        isEndpointMissing(e)
          ? "Tasdiqlash endpoint'i hali ulanmagan (P1-13)."
          : billingErrorMessage(e, "Tasdiqlab bo'lmadi"),
      )
    } finally {
      setBusy(false)
    }
  }

  const handleReverse = async (reason: string) => {
    if (!reversing) return
    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      await reverseExpense(reversing.id, reason)
      setReversing(null)
      setNotice(`${formatMoney(reversing.amount)} chiqim storno qilindi.`)
      load()
    } catch (e: unknown) {
      setActionError(
        isEndpointMissing(e)
          ? "Storno endpoint'i hali ulanmagan (P1-13)."
          : billingErrorMessage(e, "Storno qilib bo'lmadi"),
      )
    } finally {
      setBusy(false)
    }
  }

  const renderState = (row: ExpenseRecord) => {
    switch (expenseState(row)) {
      case 'reversed':
        return <StatusPill tone="danger">Storno qilingan</StatusPill>
      case 'approved':
        return <StatusPill tone="success">Tasdiqlangan</StatusPill>
      case 'pending':
        return (
          <StatusPill tone="warning">
            <Clock className="h-3 w-3" /> Tasdiq kutmoqda
          </StatusPill>
        )
      default:
        return <StatusPill tone="info">Yozib olingan</StatusPill>
    }
  }

  const renderActions = (row: ExpenseRecord) => {
    const state = expenseState(row)
    const showApprove = state === 'pending' && canApproveRecord(canApproveExpense, row)
    const showReverse = state !== 'reversed' && canReverseExpense

    if (!showApprove && !showReverse) {
      if (state === 'pending' && isOwn(row)) {
        return (
          <span className="text-xs text-slate-500">
            O'zingiz yozgansiz — tasdiqni boshqa mas'ul beradi.
          </span>
        )
      }
      return <span className="text-xs text-slate-300">—</span>
    }

    return (
      <span className="flex justify-end gap-2">
        {showApprove && (
          <span className="flex items-center gap-2">
            <select
              aria-label="Pul qaysi usulda chiqadi"
              value={approveMethods[row.id] ?? 'cash'}
              disabled={busy}
              onChange={(e) =>
                setApproveMethods((prev) => ({ ...prev, [row.id]: e.target.value as PaymentMethod }))
              }
              className="rounded-lg border border-slate-200 bg-white px-2 py-1.5 text-xs text-slate-700 outline-none focus:border-brand-400"
            >
              {(Object.keys(paymentMethodLabels) as PaymentMethod[]).map((m) => (
                <option key={m} value={m}>
                  {paymentMethodLabels[m]}
                </option>
              ))}
            </select>
            <Button
              disabled={busy}
              onClick={() => handleApprove(row, approveMethods[row.id] ?? 'cash')}
            >
              <Check className="h-4 w-4" /> Tasdiqlash
            </Button>
          </span>
        )}
        {showReverse && (
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => {
              setActionError(null)
              setReversing(row)
            }}
          >
            <Undo2 className="h-4 w-4" /> Storno
          </Button>
        )}
      </span>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Chiqimlar</h1>
          <p className="text-sm text-slate-400">
            Chegaradan yuqori chiqim ikkinchi tasdiqni talab qiladi. Chegara moliya
            sozlamalarida turadi va holatni server belgilaydi.
          </p>
        </div>
        {canRecordExpense && (
          <Button
            onClick={() => {
              setActionError(null)
              setFormOpen(true)
            }}
          >
            <Plus className="h-4 w-4" /> Yangi chiqim
          </Button>
        )}
      </div>

      {notice && <Notice tone="success">{notice}</Notice>}
      {actionError && !formOpen && !reversing && <Notice>{actionError}</Notice>}

      {/* ---- Tasdiq navbati ---- */}
      {!loading && !error && pending.length > 0 && (
        <PendingQueue
          title="Tasdiq kutayotgan chiqimlar"
          hint={
            canApproveExpense ? `Sizga ochiq: ${approvableCount} ta` : 'Qarorni direktor qabul qiladi'
          }
          count={pending.length}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-amber-100/60 text-xs uppercase tracking-wide text-amber-800">
                <tr>
                  <th className="px-4 py-2">Sana</th>
                  <th className="px-4 py-2">Toifa</th>
                  <th className="px-4 py-2 text-right">Summa</th>
                  <th className="px-4 py-2">Izoh</th>
                  <th className="px-4 py-2">Kim yozdi</th>
                  <th className="px-4 py-2 text-right">Qaror</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-amber-100">
                {pending.map((row) => (
                  <tr key={row.id}>
                    <td className="px-4 py-3 text-slate-600">{row.onDate}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      {financeCategoryLabel(row.category)}
                    </td>
                    <td className="px-4 py-3 text-right font-semibold tabular-nums text-slate-800">
                      {formatMoney(row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.note || '—'}</td>
                    <td className="px-4 py-3 text-slate-600">{row.createdByName}</td>
                    <td className="px-4 py-3">{renderActions(row)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PendingQueue>
      )}

      {!loading && !error && pending.length === 0 && (
        <div className="flex items-center gap-2 rounded-2xl border border-emerald-200 bg-emerald-50 px-5 py-3 text-sm text-emerald-800">
          <ShieldCheck className="h-4 w-4" />
          Tasdiq navbati bo'sh — kutayotgan chiqim yo'q.
        </div>
      )}

      {/* ---- Filtrlar ---- */}
      <Card>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Input label="Sanadan" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          <Input label="Sanagacha" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          <Select
            label="Toifa bo'yicha"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
          >
            <option value="">Barcha toifalar</option>
            {expenseCategories.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </Select>
        </div>
      </Card>

      {/* ---- Ro'yxat ---- */}
      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          notWired={notWired}
          empty={rows.length === 0}
          emptyText="Tanlangan davrda chiqim yozilmagan."
          emptyAction={
            canRecordExpense ? (
              <Button variant="secondary" onClick={() => setFormOpen(true)}>
                <Plus className="h-4 w-4" /> Chiqim yozish
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">Toifa</th>
                  <th className="px-4 py-3 text-right">Summa</th>
                  <th className="px-4 py-3">Izoh</th>
                  <th className="px-4 py-3">Kim yozdi</th>
                  <th className="px-4 py-3">Kim tasdiqladi</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => {
                  const reversed = expenseState(row) === 'reversed'
                  return (
                    <tr key={row.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-600">{row.onDate}</td>
                      <td className="px-4 py-3">
                        <span className="flex items-center gap-3">
                          <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-slate-100 text-slate-500">
                            <Wallet className="h-4 w-4" />
                          </span>
                          <span className="font-medium text-slate-800">
                            {financeCategoryLabel(row.category)}
                          </span>
                        </span>
                      </td>
                      <td
                        className={`px-4 py-3 text-right tabular-nums ${
                          reversed ? 'text-slate-400 line-through' : 'font-medium text-slate-800'
                        }`}
                      >
                        {formatMoney(row.amount)}
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        {row.note || '—'}
                        {row.reversalReason && (
                          <span className="block text-xs text-red-600">
                            Storno sababi: {row.reversalReason}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-slate-600">{row.createdByName}</td>
                      <td className="px-4 py-3 text-slate-600">{row.approvedByName ?? '—'}</td>
                      <td className="px-4 py-3">{renderState(row)}</td>
                      <td className="px-4 py-3">{renderActions(row)}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </AsyncBlock>
      </Card>

      <p className="text-xs text-slate-400">
        Chiqim o'chirilmaydi va tahrirlanmaydi. Xato yozuv storno bilan tuzatiladi — sabab
        yozuvda qoladi, asl qator esa ko'rinib turadi.
      </p>

      {canRecordExpense && (
        <ExpenseFormModal
          open={formOpen}
          busy={busy}
          error={actionError}
          onClose={() => setFormOpen(false)}
          onSubmit={handleCreate}
        />
      )}

      {canReverseExpense && (
        <ReasonModal
          open={reversing !== null}
          title="Chiqimni storno qilish"
          description={
            reversing
              ? `${formatMoney(reversing.amount)} — ${financeCategoryLabel(reversing.category)} ` +
                'chiqimi bekor qilinadi. Yozuv o\'chmaydi: ustiga teskari yozuv qo\'yiladi.'
              : ''
          }
          confirmLabel="Storno qilish"
          busy={busy}
          error={actionError}
          onClose={() => setReversing(null)}
          onConfirm={handleReverse}
        />
      )}
    </div>
  )
}
