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
 * 3. Chegaradan yuqori chiqim ikkinchi tasdiqni talab qiladi va tepadagi
 *    navbatga tushadi. Undan pastlari darrov yozib qo'yiladi.
 *
 * HOLAT VA CHEGARA — SERVERDAN (F1.02)
 * ------------------------------------
 * Ilgari sahifa chiqim holatini KLIENTDA, 5 000 000 so'mlik konstanta bilan
 * hisoblardi. Chegara esa sozlama: uni bir marta o'zgartirish yetardi va
 * tasdiq navbati jimgina bo'shab qolardi ("tasdiq kutmoqda" o'rniga "yozib
 * olingan"). Endi holat `ExpenseRecord.status` dan, chegara esa
 * `GET /admin/expenses/approval-policy` dan keladi va faqat forma
 * ogohlantirishi uchun ishlatiladi.
 *
 * S2 QO'SHGANLARI
 * ---------------
 * F1.09 — "O'qituvchi" ustuni: maosh chiqimi KIMGA berilgani endi ro'yxatda
 *   ko'rinadi (ilgari faqat izoh matnida bo'lardi).
 * F1.08 — "Hujjat" ustuni: chiqimga biriktirilgan chek/shartnoma soni va
 *   ularni ochadigan oyna. O'CHIRISH TUGMASI YO'Q — hujjat dalil, jadval
 *   bazada faqat qo'shiladi.
 * F1.03 — naqd chiqim ochiq smenani talab qiladi; forma buni oldindan aytadi
 *   va server 409 `no_open_shift` bilan rad etadi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Check, Clock, Paperclip, Plus, ShieldCheck, Undo2, Wallet } from 'lucide-react'
import type { ExpenseAttachment, ExpenseInput, ExpenseRecord } from '@/api/services/expenses'
import type { PaymentMethod } from '@/types'
import {
  approveExpense,
  attachExpenseFile,
  createExpense,
  expenseState,
  getExpenseApprovalThreshold,
  getExpenseAttachments,
  getExpenses,
  needsApproval,
  reverseExpense,
} from '@/api/services/expenses'
import { uploadAdminFile } from '@/api/services/students'
import { billingErrorMessage, isEndpointMissing } from '@/api/services/billingError'
import { expenseCategories, financeCategoryLabel } from '@/config/constants'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { formatDate, formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, Notice, PendingQueue, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { ApproveExpenseModal } from './ApproveExpenseModal'
import { ExpenseFormModal } from './ExpenseFormModal'
import { ReasonModal } from './ReasonModal'
import { DatePicker } from '@/components/ui/DatePicker'

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
  const { user, canRecordExpense, canApproveExpense } = useBillingAccess()

  const [rows, setRows] = useState<ExpenseRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notWired, setNotWired] = useState(false)

  const [from, setFrom] = useState(monthStart())
  const [to, setTo] = useState(today())
  const [category, setCategory] = useState('')

  const [formOpen, setFormOpen] = useState(false)
  const [approving, setApproving] = useState<ExpenseRecord | null>(null)
  const [reversing, setReversing] = useState<ExpenseRecord | null>(null)
  /** Hujjatlari ko'rilayotgan chiqim (F1.08). */
  const [viewingFiles, setViewingFiles] = useState<ExpenseRecord | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  /** Ikkinchi tasdiq chegarasi — serverdan. Yuklanmagunicha `null`. */
  const [threshold, setThreshold] = useState<number | null>(null)

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

  // Chegara bir marta o'qiladi. Olinmasa sahifa ISHLAYVERADI — shunchaki
  // formadagi ogohlantirish ko'rsatilmaydi; klientda zaxira raqam saqlash
  // aynan tuzatilgan xatoning o'zi bo'lardi.
  useEffect(() => {
    let alive = true
    getExpenseApprovalThreshold()
      .then((value) => {
        if (alive) setThreshold(value)
      })
      .catch(() => undefined)
    return () => {
      alive = false
    }
  }, [])

  // Tasdiq navbati — jurnalga hali tushmagan (server: `pending`) chiqimlar.
  const pending = useMemo(
    () => rows.filter(needsApproval).sort((a, b) => a.createdAt.localeCompare(b.createdAt)),
    [rows],
  )

  /**
   * Yozuvni JORIY foydalanuvchi yaratganmi — id bo'yicha (server `createdBy`
   * ni id sifatida qaytaradi). Ism bo'yicha taxminiy solishtiruv endi kerak
   * emas.
   */
  const isOwn = useCallback(
    (row: ExpenseRecord) => !!user && row.createdBy === user.id,
    [user],
  )

  /**
   * Jurnalga KIM qo'ygan: chegaradan past chiqimni yozgan odam, tasdiqdan
   * o'tganini esa tasdiqlovchi. Storno'ni o'sha odam qila olmaydi
   * (`self_reversal`, SPEC §4.5) — tugma ham ko'rsatilmaydi.
   */
  const postedBy = (row: ExpenseRecord) => row.approvedBy ?? row.createdBy

  const approvableCount = useMemo(
    () => pending.filter((e) => canApproveExpense && !isOwn(e)).length,
    [pending, canApproveExpense, isOwn],
  )

  /**
   * Chiqim yoziladi, so'ng (bo'lsa) hujjatlar biriktiriladi.
   *
   * TARTIB MUHIM: hujjat chiqimning id'siga bog'lanadi, ya'ni avval chiqim
   * yozilishi kerak. Fayl yuklashda xato bo'lsa CHIQIM QOLDIRILADI va
   * foydalanuvchiga aniq aytiladi — chiqimni "orqaga qaytarish" degan narsa
   * yo'q (SPEC §4.1: tuzatish faqat storno bilan), shuning uchun uni
   * yashirish eng yomon variant bo'lardi. Hujjatni keyin ham biriktirsa
   * bo'ladi.
   */
  const handleCreate = async (values: ExpenseInput, files: File[]) => {
    setBusy(true)
    setActionError(null)
    try {
      const created = await createExpense(values)
      let attachError: string | null = null
      for (const file of files) {
        try {
          const uploaded = await uploadAdminFile(file)
          await attachExpenseFile(created.id, {
            fileUrl: uploaded.url,
            fileName: uploaded.name,
            contentType: uploaded.contentType,
            sizeBytes: uploaded.size,
          })
        } catch (e: unknown) {
          attachError = billingErrorMessage(e, `"${file.name}" biriktirilmadi`)
          break
        }
      }
      setFormOpen(false)
      setNotice(
        (needsApproval(created)
          ? `Chiqim yozildi va tasdiq navbatiga tushdi (${formatMoney(created.amount)}).`
          : `Chiqim yozildi (${formatMoney(created.amount)}).`) +
          (attachError === null ? '' : ` Lekin hujjat biriktirilmadi: ${attachError}`),
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

  const handleApprove = async (method: PaymentMethod) => {
    if (!approving) return
    const row = approving
    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      await approveExpense(row.id, method)
      setApproving(null)
      setNotice(`${formatMoney(row.amount)} chiqim tasdiqlandi va jurnalga tushdi.`)
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

    // Tasdiqlash ham, storno ham — DIREKTORNING amali (`FinanceAction.ApproveExpense`,
    // SPEC §4.5). Ilgari storno tugmasi adminga ham, tasdiq kutayotgan qatorga ham
    // ko'rsatilardi: birinchisi har safar 403, ikkinchisi 409 bilan qaytardi.
    const showApprove = state === 'pending' && canApproveExpense && !isOwn(row)
    const showReverse =
      (state === 'approved' || state === 'recorded') &&
      canApproveExpense &&
      postedBy(row) !== user?.id

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
            {threshold === null
              ? "Chegaradan yuqori chiqim ikkinchi tasdiqni talab qiladi."
              : `${formatMoney(threshold)} dan yuqori chiqim ikkinchi tasdiqni talab qiladi.`}
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
      {actionError && !formOpen && !reversing && !approving && <Notice>{actionError}</Notice>}

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
              <thead className="whitespace-nowrap bg-amber-100/60 text-xs uppercase tracking-wide text-amber-800">
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
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{formatDate(row.onDate)}</td>
                    <td className="whitespace-nowrap px-4 py-3 font-medium text-slate-800">
                      {financeCategoryLabel(row.category)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right font-semibold tabular-nums text-slate-800">
                      {formatMoney(row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      <span className="block max-w-[14rem] truncate" title={row.note ?? ''}>
                        {row.note || '—'}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.createdByName}</td>
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
          <DatePicker
            label="Sanadan"
            value={from}
            onChange={(value: string) => setFrom(value)}
          />
          <DatePicker
            label="Sanagacha"
            value={to}
            onChange={(value: string) => setTo(value)}
          />
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
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">Toifa</th>
                  <th className="px-4 py-3">O'qituvchi</th>
                  <th className="px-4 py-3 text-right">Summa</th>
                  <th className="px-4 py-3">Izoh</th>
                  <th className="px-4 py-3">Kim yozdi</th>
                  <th className="px-4 py-3">Kim tasdiqladi</th>
                  <th className="px-4 py-3">Hujjat</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => {
                  const reversed = expenseState(row) === 'reversed'
                  return (
                    <tr key={row.id} className="hover:bg-slate-50/60">
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">{formatDate(row.onDate)}</td>
                      <td className="px-4 py-3">
                        <span className="flex items-center gap-3">
                          <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-slate-100 text-slate-500">
                            <Wallet className="h-4 w-4" />
                          </span>
                          <span className="whitespace-nowrap font-medium text-slate-800">
                            {financeCategoryLabel(row.category)}
                          </span>
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                        <span className="block max-w-[13rem] truncate" title={row.teacherName ?? ''}>
                          {row.teacherName ?? '—'}
                        </span>
                      </td>
                      <td
                        className={`whitespace-nowrap px-4 py-3 text-right tabular-nums ${
                          reversed ? 'text-slate-400 line-through' : 'font-medium text-slate-800'
                        }`}
                      >
                        {formatMoney(row.amount)}
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        <span className="block max-w-[14rem] truncate" title={row.note ?? ''}>
                          {row.note || '—'}
                        </span>
                        {row.reversalReason && (
                          <span
                            className="block max-w-[14rem] truncate text-xs text-red-600"
                            title={row.reversalReason}
                          >
                            Storno sababi: {row.reversalReason}
                          </span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                        <span className="block max-w-[11rem] truncate" title={row.createdByName}>
                          {row.createdByName}
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                        <span className="block max-w-[11rem] truncate" title={row.approvedByName ?? ''}>
                          {row.approvedByName ?? '—'}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        {row.attachmentCount > 0 ? (
                          <button
                            type="button"
                            className="inline-flex items-center gap-1 rounded-md bg-slate-100 px-2 py-1 text-xs font-medium text-slate-600 hover:bg-slate-200"
                            onClick={() => setViewingFiles(row)}
                          >
                            <Paperclip className="h-3 w-3" /> {row.attachmentCount}
                          </button>
                        ) : (
                          <span className="text-xs text-slate-300">—</span>
                        )}
                      </td>
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
          approvalThreshold={threshold}
          onClose={() => setFormOpen(false)}
          onSubmit={handleCreate}
        />
      )}

      {canApproveExpense && (
        <ApproveExpenseModal
          expense={approving}
          busy={busy}
          error={actionError}
          onClose={() => setApproving(null)}
          onConfirm={handleApprove}
        />
      )}

      {canApproveExpense && (
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

      {viewingFiles && (
        <AttachmentsModal expense={viewingFiles} onClose={() => setViewingFiles(null)} />
      )}
    </div>
  )
}

/**
 * Chiqimning hujjatlari (F1.08) — FAQAT ko'rish.
 *
 * O'chirish tugmasi yo'q va bo'lmaydi: hujjat pul yozuvining dalili va
 * `expense_attachments` bazada faqat qo'shiladi (`app_rw` da UPDATE/DELETE
 * yo'q). Noto'g'ri fayl yuklansa, to'g'risi yangi qator bo'lib qo'shiladi.
 */
function AttachmentsModal({
  expense,
  onClose,
}: {
  expense: ExpenseRecord
  onClose: () => void
}) {
  const [rows, setRows] = useState<ExpenseAttachment[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getExpenseAttachments(expense.id)
      .then((data) => {
        if (alive) setRows(data)
      })
      .catch((e: unknown) => {
        if (alive) setError(billingErrorMessage(e, "Hujjatlarni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [expense.id])

  return (
    <Modal open onClose={onClose} title="Chiqim hujjatlari" size="md">
      {error && <Notice>{error}</Notice>}
      {!error && rows === null && <p className="text-sm text-slate-400">Yuklanmoqda...</p>}
      {!error && rows !== null && rows.length === 0 && (
        <p className="text-sm text-slate-400">Bu chiqimga hujjat biriktirilmagan.</p>
      )}
      {!error && rows !== null && rows.length > 0 && (
        <ul className="space-y-2">
          {rows.map((file) => (
            <li
              key={file.id}
              className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 px-3 py-2 text-sm"
            >
              <a
                href={file.fileUrl}
                target="_blank"
                rel="noreferrer"
                className="flex min-w-0 items-center gap-2 text-brand-600 hover:underline"
              >
                <Paperclip className="h-4 w-4 shrink-0" />
                <span className="truncate">{file.fileName}</span>
              </a>
              <span className="shrink-0 text-xs text-slate-400">{file.uploadedByName}</span>
            </li>
          ))}
        </ul>
      )}
      <p className="mt-3 text-xs text-slate-400">
        Hujjat o'chirilmaydi va almashtirilmaydi — u pul yozuvining dalili. Noto'g'ri fayl
        yuklansa, to'g'risini yangi hujjat sifatida biriktiring.
      </p>
    </Modal>
  )
}
