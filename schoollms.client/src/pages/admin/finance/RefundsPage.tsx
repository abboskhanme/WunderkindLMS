/**
 * O'quvchiga pul qaytarish — F1.05 (docs/modules/finance-parity.md §2.1.3,
 * §4 — slice S3).
 *
 * IKKI QAVATLI NAZORAT (SPEC §4.5)
 * ---------------------------------
 * Admin SO'RAYDI, direktor TASDIQLAYDI yoki RAD ETADI. Tasdiqlash tugmasi
 * so'ragan odamga KO'RSATILMAYDI — server ham buni `self_approval` (403)
 * bilan rad etadi, lekin interfeys uni taklif ham qilmasligi kerak
 * (`ExpensesPage`/`access.ts` dagi bilan bir xil uslub).
 *
 * QATOR O'CHIRILMAYDI, TAHRIRLANMAYDI (SPEC §4.1)
 * --------------------------------------------------
 * Xato (allaqachon tasdiqlangan) qaytarimni tuzatishning yagona yo'li —
 * STORNO: yangi qator so'raladi (`reverse`) va u ham xuddi shunday
 * tasdiqlanadi (`approve`). Shu sabab bu faylda "o'chirish" tugmasi umuman
 * yo'q — faqat "Storno so'rash".
 *
 * AVANS — SERVERDAN
 * -------------------
 * So'rov formasidagi "eng ko'pi shuncha" — `GET .../advance` javobi.
 * Klientda hisoblanmaydi: avans allaqachon taqsimlangan pulni, boshqa
 * kassirning bir vaqtdagi to'lovini va qaytarimlarni hisobga oladi — bularni
 * bu yerda takrorlash ertami-kechmi serverdan uzoqlashardi.
 */
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertCircle, Check, Clock, Plus, ShieldCheck, Undo2, X } from 'lucide-react'
import type { PaymentMethod } from '@/types'
import {
  approveRefund,
  getRefundAdvance,
  getRefunds,
  refundStatusLabels,
  rejectRefund,
  requestRefund,
  requestRefundReversal,
  type StudentRefund,
  type StudentRefundStatus,
} from '@/api/services/refunds'
import { billingErrorMessage, isEndpointMissing } from '@/api/services/billingError'
import { useAuth } from '@/context/auth-context'
import { useStudentsIndex } from '@/pages/admin/billing/useStudents'
import { StudentSelect } from '@/pages/admin/billing/StudentSelect'
import { AsyncBlock, Notice, PendingQueue, StatusPill } from '@/pages/admin/billing/BillingUi'
import { ReasonModal } from '@/pages/admin/billing/ReasonModal'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { formatMoney } from '@/lib/utils'
import { formatDateTime, paymentMethodLabel } from './reportLabels'

const methodOptions: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

function statusTone(status: StudentRefundStatus): 'neutral' | 'success' | 'warning' | 'danger' | 'info' {
  switch (status) {
    case 'approved':
      return 'success'
    case 'pending':
      return 'warning'
    case 'rejected':
      return 'danger'
    case 'reversal':
      return 'info'
    case 'reversed':
      return 'neutral'
    default:
      return 'neutral'
  }
}

export function RefundsPage() {
  const { user } = useAuth()
  const canRequest = hasFinanceAccess(user ?? null)
  const canApprove = user?.role === 'superadmin'

  const students = useStudentsIndex()

  const [rows, setRows] = useState<StudentRefund[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notWired, setNotWired] = useState(false)

  const [studentFilter, setStudentFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState<StudentRefundStatus | ''>('')

  const [requestOpen, setRequestOpen] = useState(false)
  const [approving, setApproving] = useState<StudentRefund | null>(null)
  const [rejecting, setRejecting] = useState<StudentRefund | null>(null)
  const [reversing, setReversing] = useState<StudentRefund | null>(null)

  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  // To'liq ro'yxat BIR MARTA yuklanadi, filtrlar esa xotirada qo'llanadi —
  // "storno so'rash" tugmasi allaqachon so'ralgan qatorni yashirishi uchun
  // BUTUN ro'yxat kerak, faqat joriy filtrga mos qismi emas.
  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    setNotWired(false)
    getRefunds({})
      .then(setRows)
      .catch((e: unknown) => {
        const missing = isEndpointMissing(e)
        setNotWired(missing)
        setError(
          missing
            ? "Qaytarim endpoint'lari hali serverga ulanmagan. Marshrut yozilgach bu sahifa o'zi ishlaydi."
            : billingErrorMessage(e, "Qaytarimlarni yuklab bo'lmadi"),
        )
      })
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- birinchi yuklash (loyihadagi umumiy naqsh)
  useEffect(() => load(), [load])

  const pending = useMemo(
    () => rows.filter((r) => r.status === 'pending').sort((a, b) => a.requestedAt.localeCompare(b.requestedAt)),
    [rows],
  )

  const filtered = useMemo(
    () =>
      rows
        .filter((r) => !studentFilter || r.studentId === studentFilter)
        .filter((r) => !statusFilter || r.status === statusFilter)
        .sort((a, b) => b.requestedAt.localeCompare(a.requestedAt)),
    [rows, studentFilter, statusFilter],
  )

  const isOwnRequest = useCallback(
    (row: StudentRefund) => !!user && row.requestedBy === user.id,
    [user],
  )

  // Bu qaytarim (ASL, tasdiqlangan) uchun storno so'rovi ALLAQACHON
  // yuborilganmi (tasdiqlangan, kutilayotgan yoki rad etilgan) — BUTUN
  // ro'yxatdan, joriy filtrdan mustaqil (izoh yuqorida).
  const hasReversalRequest = useCallback(
    (row: StudentRefund) => rows.some((r) => r.reversalOf === row.id),
    [rows],
  )

  const approvableCount = useMemo(
    () => pending.filter((r) => canApprove && !isOwnRequest(r)).length,
    [pending, canApprove, isOwnRequest],
  )

  const runAction = async (action: () => Promise<StudentRefund>, successMessage: string) => {
    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      await action()
      setApproving(null)
      setRejecting(null)
      setReversing(null)
      setRequestOpen(false)
      setNotice(successMessage)
      load()
    } catch (e: unknown) {
      setActionError(
        isEndpointMissing(e) ? "Bu amal endpoint'i hali ulanmagan." : billingErrorMessage(e, "Amalni bajarib bo'lmadi"),
      )
    } finally {
      setBusy(false)
    }
  }

  const handleRequest = (input: { studentId: string; amount: number; method: PaymentMethod; reason: string }) =>
    runAction(
      () => requestRefund(input),
      `${formatMoney(input.amount)} qaytarim so'raldi — direktor tasdig'ini kutmoqda.`,
    )

  const handleApprove = () => {
    if (!approving) return
    const row = approving
    return runAction(
      () => approveRefund(row.id),
      row.reversalOf
        ? `${formatMoney(row.amount)} qaytarimning STORNOSI tasdiqlandi.`
        : `${formatMoney(row.amount)} qaytarim tasdiqlandi va jurnalga tushdi.`,
    )
  }

  const handleReject = (reason: string) => {
    if (!rejecting) return
    const row = rejecting
    return runAction(() => rejectRefund(row.id, reason), `${formatMoney(row.amount)} so'rov rad etildi.`)
  }

  const handleReverse = (reason: string) => {
    if (!reversing) return
    const row = reversing
    return runAction(
      () => requestRefundReversal(row.id, reason),
      `Storno so'rovi yuborildi — direktor tasdig'ini kutmoqda.`,
    )
  }

  const renderDecisionActions = (row: StudentRefund) => {
    if (!canApprove) return <span className="text-xs text-slate-300">—</span>

    const own = isOwnRequest(row)
    return (
      <span className="flex flex-wrap justify-end gap-2">
        {!own && (
          <Button
            disabled={busy}
            onClick={() => {
              setActionError(null)
              setApproving(row)
            }}
          >
            <Check className="h-4 w-4" /> Tasdiqlash
          </Button>
        )}
        <Button
          variant="secondary"
          disabled={busy}
          onClick={() => {
            setActionError(null)
            setRejecting(row)
          }}
        >
          <X className="h-4 w-4" /> Rad etish
        </Button>
        {own && (
          <span className="text-xs text-slate-500">O'zingiz so'ragansiz — tasdiqni boshqa direktor beradi.</span>
        )}
      </span>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Qaytarimlar</h1>
          <p className="text-sm text-slate-400">
            O'quvchiga (yoki ota-onaga) taqsimlanmagan avansdan pul qaytarish — admin so'raydi,
            direktor tasdiqlaydi.
          </p>
        </div>
        {canRequest && (
          <Button
            onClick={() => {
              setActionError(null)
              setRequestOpen(true)
            }}
          >
            <Plus className="h-4 w-4" /> Yangi so'rov
          </Button>
        )}
      </div>

      {notice && <Notice tone="success">{notice}</Notice>}
      {actionError && !requestOpen && !approving && !rejecting && !reversing && <Notice>{actionError}</Notice>}

      {/* ---- Tasdiq navbati ---- */}
      {!loading && !error && pending.length > 0 && (
        <PendingQueue
          title="Tasdiq kutayotgan qaytarimlar"
          hint={canApprove ? `Sizga ochiq: ${approvableCount} ta` : 'Qarorni direktor qabul qiladi'}
          count={pending.length}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-amber-100/60 text-xs uppercase tracking-wide text-amber-800">
                <tr>
                  <th className="px-4 py-2">So'ralgan</th>
                  <th className="px-4 py-2">O'quvchi</th>
                  <th className="px-4 py-2 text-right">Summa</th>
                  <th className="px-4 py-2">Usul</th>
                  <th className="px-4 py-2">Sabab</th>
                  <th className="px-4 py-2">Kim so'radi</th>
                  <th className="px-4 py-2 text-right">Qaror</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-amber-100">
                {pending.map((row) => (
                  <tr key={row.id}>
                    <td className="px-4 py-3 text-slate-600">{formatDateTime(row.requestedAt)}</td>
                    <td className="px-4 py-3">
                      <span className="font-medium text-slate-800">{row.studentName}</span>
                      {row.reversalOf && (
                        <span className="ml-2 inline-flex items-center gap-1 text-xs text-brand-700">
                          <Undo2 className="h-3 w-3" /> Storno so'rovi
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-right font-semibold tabular-nums text-slate-800">
                      {formatMoney(row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{paymentMethodLabel(row.method)}</td>
                    <td className="px-4 py-3 text-slate-600">{row.reason}</td>
                    <td className="px-4 py-3 text-slate-600">{row.requestedByName}</td>
                    <td className="px-4 py-3">{renderDecisionActions(row)}</td>
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
          Tasdiq navbati bo'sh — kutayotgan qaytarim so'rovi yo'q.
        </div>
      )}

      {/* ---- Filtrlar ---- */}
      <Card>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <StudentSelect
            label="O'quvchi bo'yicha"
            emptyLabel="Barcha o'quvchilar"
            options={students.options}
            loading={students.loading}
            error={students.error}
            value={studentFilter}
            onChange={setStudentFilter}
          />
          <Select
            label="Holat bo'yicha"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as StudentRefundStatus | '')}
          >
            <option value="">Barcha holatlar</option>
            {(Object.keys(refundStatusLabels) as StudentRefundStatus[]).map((status) => (
              <option key={status} value={status}>
                {refundStatusLabels[status]}
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
          empty={filtered.length === 0}
          emptyText="Tanlangan filtrga mos qaytarim yo'q."
          emptyAction={
            canRequest ? (
              <Button variant="secondary" onClick={() => setRequestOpen(true)}>
                <Plus className="h-4 w-4" /> Yangi so'rov
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">So'ralgan</th>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3 text-right">Summa</th>
                  <th className="px-4 py-3">Usul</th>
                  <th className="px-4 py-3">Sabab</th>
                  <th className="px-4 py-3">Kim so'radi</th>
                  <th className="px-4 py-3">Kim hal qildi</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((row) => {
                  const canReverse =
                    canRequest && row.status === 'approved' && !row.reversed && !hasReversalRequest(row)
                  return (
                    <tr key={row.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-600">{formatDateTime(row.requestedAt)}</td>
                      <td className="px-4 py-3">
                        <span className="font-medium text-slate-800">{row.studentName}</span>
                        {row.reversalOf && (
                          <span className="block text-xs text-slate-400">
                            Storno: {row.reason}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-right font-medium tabular-nums text-slate-800">
                        {formatMoney(row.amount)}
                      </td>
                      <td className="px-4 py-3 text-slate-600">{paymentMethodLabel(row.method)}</td>
                      <td className="px-4 py-3 text-slate-600">{row.reversalOf ? '—' : row.reason}</td>
                      <td className="px-4 py-3 text-slate-600">{row.requestedByName}</td>
                      <td className="px-4 py-3 text-slate-600">
                        {row.approvedByName ?? (row.rejectedReason ? '—' : '—')}
                      </td>
                      <td className="px-4 py-3">
                        <StatusPill tone={statusTone(row.status)}>
                          {row.status === 'pending' && <Clock className="h-3 w-3" />}
                          {refundStatusLabels[row.status]}
                        </StatusPill>
                        {row.rejectedReason && (
                          <span className="mt-1 block text-xs text-red-600">
                            Sabab: {row.rejectedReason}
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        {canReverse ? (
                          <span className="flex justify-end">
                            <Button
                              variant="secondary"
                              disabled={busy}
                              onClick={() => {
                                setActionError(null)
                                setReversing(row)
                              }}
                            >
                              <Undo2 className="h-4 w-4" /> Storno so'rash
                            </Button>
                          </span>
                        ) : (
                          <span className="flex justify-end text-xs text-slate-300">—</span>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </AsyncBlock>
      </Card>

      <p className="flex items-start gap-2 text-xs text-slate-400">
        <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        Qaytarim qatori o'chirilmaydi va tahrirlanmaydi. Xato (tasdiqlangan) qaytarim faqat
        STORNO bilan tuzatiladi — yangi qator so'raladi va u ham direktor tomonidan
        tasdiqlanadi; asl qator o'zgarishsiz qoladi.
      </p>

      {canRequest && (
        <RequestRefundModal
          open={requestOpen}
          busy={busy}
          error={actionError}
          students={students}
          onClose={() => setRequestOpen(false)}
          onSubmit={handleRequest}
        />
      )}

      {canApprove && (
        <Modal
          open={approving !== null}
          onClose={() => setApproving(null)}
          title={approving?.reversalOf ? 'Storno so\'rovini tasdiqlash' : 'Qaytarimni tasdiqlash'}
          size="sm"
          footer={
            <>
              <Button variant="secondary" onClick={() => setApproving(null)} disabled={busy}>
                Bekor qilish
              </Button>
              <Button disabled={busy} onClick={handleApprove}>
                <Check className="h-4 w-4" /> {busy ? 'Bajarilmoqda...' : 'Tasdiqlash'}
              </Button>
            </>
          }
        >
          {approving && (
            <div className="space-y-4">
              <SummaryBox row={approving} />
              <p className="text-sm text-slate-600">
                {approving.reversalOf
                  ? "Bu STORNO qaytariladi: asl qaytarimning jurnal yozuvi teskari qilinadi va pul qaytadi."
                  : approving.method === 'cash'
                    ? "Naqd qaytarim SIZNING ochiq smenangizdan chiqadi — avval smenani ochganingizga ishonch hosil qiling."
                    : "Pul bank hisobidan chiqadi."}
              </p>
              {actionError && <Notice>{actionError}</Notice>}
            </div>
          )}
        </Modal>
      )}

      {canApprove && (
        <ReasonModal
          open={rejecting !== null}
          title="Qaytarim so'rovini rad etish"
          description={
            rejecting
              ? `${formatMoney(rejecting.amount)} — ${rejecting.studentName} uchun so'rov rad etiladi. Pul harakati bo'lmaydi.`
              : ''
          }
          confirmLabel="Rad etish"
          busy={busy}
          error={actionError}
          onClose={() => setRejecting(null)}
          onConfirm={handleReject}
        />
      )}

      {canRequest && (
        <ReasonModal
          open={reversing !== null}
          title="Qaytarimni storno qilishni so'rash"
          description={
            reversing
              ? `${formatMoney(reversing.amount)} — ${reversing.studentName} ga qaytarilgan pul bekor qilinadi. `
                + "Yangi (storno) qator so'raladi va uni direktor tasdiqlaganda jurnal yozuvi teskari bo'ladi."
              : ''
          }
          confirmLabel="Storno so'rash"
          busy={busy}
          error={actionError}
          onClose={() => setReversing(null)}
          onConfirm={handleReverse}
        />
      )}
    </div>
  )
}

/** Tasdiqlash oynasidagi qisqa xulosa — direktor NIMANI tasdiqlayotganini ko'rsin. */
function SummaryBox({ row }: { row: StudentRefund }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50/70 px-4 py-3 text-sm">
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-slate-500">O'quvchi</span>
        <span className="font-medium text-slate-800">{row.studentName}</span>
      </div>
      <div className="mt-1 flex items-baseline justify-between gap-3">
        <span className="text-slate-500">Summa</span>
        <span className="font-semibold text-slate-800">{formatMoney(row.amount)}</span>
      </div>
      <div className="mt-1 flex items-baseline justify-between gap-3">
        <span className="text-slate-500">Usul</span>
        <span className="text-slate-700">{paymentMethodLabel(row.method)}</span>
      </div>
      <div className="mt-1 flex items-baseline justify-between gap-3">
        <span className="text-slate-500">Sabab</span>
        <span className="text-right text-slate-700">{row.reason}</span>
      </div>
      <div className="mt-1 flex items-baseline justify-between gap-3">
        <span className="text-slate-500">Kim so'radi</span>
        <span className="text-slate-700">{row.requestedByName}</span>
      </div>
    </div>
  )
}

/**
 * Yangi qaytarim so'rovi. Avans SERVERDAN o'qiladi (o'quvchi tanlanganda) —
 * fayl boshidagi izoh.
 */
function RequestRefundModal({
  open,
  busy,
  error,
  students,
  onClose,
  onSubmit,
}: {
  open: boolean
  busy: boolean
  error: string | null
  students: ReturnType<typeof useStudentsIndex>
  onClose: () => void
  onSubmit: (input: { studentId: string; amount: number; method: PaymentMethod; reason: string }) => void
}) {
  const [studentId, setStudentId] = useState('')
  const [amount, setAmount] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [reason, setReason] = useState('')

  const [advance, setAdvance] = useState<number | null>(null)
  const [advanceError, setAdvanceError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli)
    setStudentId('')
    setAmount('')
    setMethod('cash')
    setReason('')
    setAdvance(null)
    setAdvanceError(null)
  }, [open])

  useEffect(() => {
    if (!studentId) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- o'quvchi tanlovi bekor qilinganda avansni tozalash (maqsadli)
      setAdvance(null)
      setAdvanceError(null)
      return
    }
    let alive = true
    setAdvance(null)
    setAdvanceError(null)
    getRefundAdvance(studentId)
      .then((value) => {
        if (alive) setAdvance(value)
      })
      .catch((e: unknown) => {
        if (alive) setAdvanceError(billingErrorMessage(e, "Avansni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [studentId])

  const numericAmount = Number(amount)
  const amountValid = amount !== '' && Number.isFinite(numericAmount) && numericAmount > 0
  const exceedsAdvance = advance !== null && amountValid && numericAmount > advance
  const reasonValid = reason.trim().length >= 3
  const canSubmit = studentId !== '' && amountValid && !exceedsAdvance && reasonValid && !busy

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Yangi qaytarim so'rovi"
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button
            disabled={!canSubmit}
            onClick={() =>
              onSubmit({ studentId, amount: numericAmount, method, reason: reason.trim() })
            }
          >
            {busy ? 'Yuborilmoqda...' : "So'rash"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <StudentSelect
          required
          options={students.options}
          loading={students.loading}
          error={students.error}
          value={studentId}
          onChange={setStudentId}
        />

        {studentId && (
          <p className="text-sm text-slate-500">
            {advanceError
              ? advanceError
              : advance === null
                ? 'Joriy avans yuklanmoqda...'
                : `Joriy avans: ${formatMoney(advance)} — eng ko'pi shuncha qaytariladi.`}
          </p>
        )}

        <Input
          label="Summa (so'm)"
          required
          type="number"
          min={1}
          step={1}
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
        />
        {exceedsAdvance && (
          <p className="text-xs text-red-600">
            So'ralgan summa joriy avansdan katta — eng ko'pi {formatMoney(advance ?? 0)} bo'lishi mumkin.
          </p>
        )}

        <Select label="To'lov usuli" required value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
          {methodOptions.map((m) => (
            <option key={m} value={m}>
              {paymentMethodLabel(m)}
            </option>
          ))}
        </Select>

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">
            Sabab <span className="text-red-500">*</span>
          </label>
          <textarea
            rows={3}
            placeholder="Masalan: o'quvchi maktabni tark etdi, oldindan to'langan avans qaytarilmoqda"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            className="w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
        </div>

        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
