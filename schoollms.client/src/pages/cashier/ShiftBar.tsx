import { useState } from 'react'
import { AlertTriangle, CircleDot, LockKeyhole, RefreshCw, ShieldQuestion, Unlock } from 'lucide-react'
import type { CashShift } from '@/types'
import { closeShift, financeErrorMessage, openShift } from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { MoneyInput } from './MoneyInput'
import { formatDateTime, formatSumWithUnit, parseSum } from './format'

interface ShiftBarProps {
  shift: CashShift | null
  loading: boolean
  /** Smenani o'qishda xatolik bo'lsa — o'zbekcha matn. */
  error: string | null
  onRetry: () => void
  onShiftChange: (shift: CashShift | null) => void
}

/**
 * Smena chizig'i — kassa ekranining eng yuqori qatori (SPEC §4.2).
 *
 * TO'RT HOLAT, TO'RTALASI HAM CHIZILGAN: yuklanmoqda, xato, smena yopiq,
 * smena ochiq. "Smena yopiq" holati ekranning qolgan qismini ham
 * boshqaradi — buni `CashierPage` hal qiladi, bu komponent faqat holatni
 * ko'rsatadi va amallarni beradi.
 *
 * OCHIQ SMENADA PUL SUMMASI KO'RSATILMAYDI. `CashShift` DTO'sida `cashTotal`
 * bor, lekin uni ochiq smenada chiqarish — kassirga "bugun kassangizda
 * qancha bo'lishi kerak" deb aytish bilan bir xil. SPEC §4.2 ning butun
 * ma'nosi shunda: sanoq kutilayotgan summani KO'RMASDAN qilinadi. Shuning
 * uchun bu yerda faqat vaqt va to'lovlar SONI turadi.
 */
export function ShiftBar({ shift, loading, error, onRetry, onShiftChange }: ShiftBarProps) {
  const [openDialog, setOpenDialog] = useState(false)
  const [closeDialog, setCloseDialog] = useState(false)

  if (loading) {
    return (
      <Card>
        <Loader label="Smena holati tekshirilmoqda..." className="py-4" />
      </Card>
    )
  }

  if (error) {
    return (
      <Card className="border-red-200 bg-red-50/60">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-start gap-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
          <Button variant="secondary" onClick={onRetry}>
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        </div>
      </Card>
    )
  }

  if (!shift) {
    return (
      <>
        <Card className="border-amber-200 bg-amber-50/70">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-start gap-3">
              <LockKeyhole className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
              <div>
                <p className="font-semibold text-amber-900">Smenani oching</p>
                <p className="mt-0.5 text-sm text-amber-800">
                  Ochiq smenasiz to'lov qabul qilinmaydi: har bir chek qaysi smenaga
                  tegishli ekani bilan yoziladi.
                </p>
              </div>
            </div>
            <Button onClick={() => setOpenDialog(true)}>
              <Unlock className="h-4 w-4" /> Smenani ochish
            </Button>
          </div>
        </Card>

        <OpenShiftDialog
          open={openDialog}
          onClose={() => setOpenDialog(false)}
          onOpened={(opened) => {
            setOpenDialog(false)
            onShiftChange(opened)
          }}
        />
      </>
    )
  }

  return (
    <>
      <Card className="border-emerald-200 bg-emerald-50/60">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-start gap-3">
            <CircleDot className="mt-0.5 h-5 w-5 shrink-0 animate-pulse text-emerald-600" />
            <div>
              <p className="font-semibold text-emerald-900">
                Smena ochiq · {shift.cashierName}
              </p>
              <p className="mt-0.5 text-sm text-emerald-800">
                {formatDateTime(shift.openedAt)} dan beri · {shift.paymentsCount} ta chek
              </p>
            </div>
          </div>
          <Button variant="secondary" onClick={() => setCloseDialog(true)}>
            <LockKeyhole className="h-4 w-4" /> Smenani yopish
          </Button>
        </div>
      </Card>

      <CloseShiftDialog
        open={closeDialog}
        shift={shift}
        onClose={() => setCloseDialog(false)}
        onClosed={() => {
          setCloseDialog(false)
          onShiftChange(null)
        }}
      />
    </>
  )
}

/* ======================================================================
   Smena ochish
   ====================================================================== */

interface OpenShiftDialogProps {
  open: boolean
  onClose: () => void
  onOpened: (shift: CashShift) => void
}

function OpenShiftDialog({ open, onClose, onOpened }: OpenShiftDialogProps) {
  const [float, setFloat] = useState('0')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const parsed = parseSum(float)
  const valid = parsed !== null

  const submit = async () => {
    if (parsed === null || busy) return
    setBusy(true)
    setError(null)
    try {
      onOpened(await openShift(parsed))
      setFloat('0')
    } catch (err) {
      setError(financeErrorMessage(err, "Smenani ochib bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Smenani ochish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={!valid || busy}>
            {busy ? 'Ochilmoqda...' : 'Smenani ochish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-slate-500">
          Kassada smena boshida turgan pulni kiriting. Pul bo'lmasa — 0 qoldiring.
        </p>
        <MoneyInput
          label="Ochilish qoldig'i (so'm)"
          value={float}
          onValueChange={setFloat}
          disabled={busy}
          invalid={!valid}
          hint={valid ? undefined : 'Faqat raqam kiriting.'}
          autoFocus
        />
        {error && (
          <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
        )}
      </div>
    </Modal>
  )
}

/* ======================================================================
   Smena yopish — SPEC §4.2
   ====================================================================== */

interface CloseShiftDialogProps {
  open: boolean
  shift: CashShift
  onClose: () => void
  onClosed: () => void
}

/**
 * Yopish oynasi ikki bosqichli, va bu ATAYLAB shunday.
 *
 * 1-bosqich — kassir sanalgan naqdni kiritadi. Ekranda kutilayotgan summa
 *   ham, farq ham YO'Q. Backend ham ularni bermaydi: ochiq smenada
 *   `expectedCash` va `variance` null (jonli stack'da tekshirilgan).
 * 2-bosqich — YUBORILGANDAN KEYIN javobdagi kutilgan / sanalgan / farq
 *   ko'rsatiladi.
 *
 * Bu qulaylik masalasi emas, nazorat masalasi: kutilayotgan raqamni ko'rgan
 * kassir sanoqni o'shanga moslab yozib qo'yishi mumkin va farq har doim nol
 * chiqadi — ya'ni SPEC §4.6 dagi butun aniqlash mexanizmi ishlamay qoladi.
 */
function CloseShiftDialog({ open, shift, onClose, onClosed }: CloseShiftDialogProps) {
  const [counted, setCounted] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  /** Yopilgan smena — FAQAT shu holat farqni ko'rsatishga ruxsat beradi. */
  const [result, setResult] = useState<CashShift | null>(null)

  const parsed = parseSum(counted)
  /** Sanalgan naqd kiritilmaguncha tugma o'chiq (SPEC §4.2). */
  const canSubmit = parsed !== null && !busy

  const reset = () => {
    setCounted('')
    setNote('')
    setError(null)
    setResult(null)
  }

  const submit = async () => {
    if (parsed === null || busy) return
    setBusy(true)
    setError(null)
    try {
      setResult(await closeShift(shift.id, parsed, note.trim() || undefined))
    } catch (err) {
      setError(financeErrorMessage(err, "Smenani yopib bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  const finish = () => {
    reset()
    onClosed()
  }

  if (result) {
    return (
      <Modal
        open={open}
        onClose={finish}
        title="Smena yopildi"
        size="sm"
        footer={<Button onClick={finish}>Yopish</Button>}
      >
        <ShiftResult shift={result} />
      </Modal>
    )
  }

  return (
    <Modal
      open={open}
      onClose={() => {
        reset()
        onClose()
      }}
      title="Smenani yopish"
      size="sm"
      footer={
        <>
          <Button
            variant="secondary"
            onClick={() => {
              reset()
              onClose()
            }}
            disabled={busy}
          >
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={!canSubmit}>
            {busy ? 'Yopilmoqda...' : 'Smenani yopish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex items-start gap-2 rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
          <ShieldQuestion className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
          <span>
            Kassadagi naqd pulni sanang va shu yerga yozing. Kutilayotgan summa
            ataylab ko'rsatilmayapti — farqni tizim siz yuborganingizdan keyin
            hisoblaydi.
          </span>
        </div>

        <MoneyInput
          label="Sanalgan naqd (so'm)"
          value={counted}
          onValueChange={setCounted}
          disabled={busy}
          placeholder="0"
          hint={parsed === null ? 'Sanalgan naqdsiz smena yopilmaydi.' : undefined}
          invalid={counted.length > 0 && parsed === null}
          autoFocus
        />

        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={2}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          disabled={busy}
          placeholder="Masalan: kunning oxirida 50 000 so'm maydalikka almashtirildi"
        />

        {error && (
          <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
        )}
      </div>
    </Modal>
  )
}

/** Yopilgan smenaning yakuni: kutilgan, sanalgan va farq. */
function ShiftResult({ shift }: { shift: CashShift }) {
  const variance = shift.variance ?? 0
  const exact = Math.abs(variance) < 0.005

  return (
    <div className="space-y-3">
      <dl className="divide-y divide-slate-100 rounded-xl border border-slate-200">
        <Row label="Kutilgan naqd" value={formatSumWithUnit(shift.expectedCash ?? 0)} />
        <Row label="Sanalgan naqd" value={formatSumWithUnit(shift.countedCash ?? 0)} />
        <Row
          label="Farq"
          value={`${variance > 0 ? '+' : ''}${formatSumWithUnit(variance)}`}
          tone={exact ? 'ok' : 'warn'}
        />
      </dl>

      <p
        className={cn(
          'rounded-lg px-3 py-2 text-sm',
          exact ? 'bg-emerald-50 text-emerald-800' : 'bg-amber-50 text-amber-900',
        )}
      >
        {exact
          ? "Sanoq kutilgan summaga to'g'ri keldi."
          : "Farq qayd etildi va direktor panelida ko'rinadi. Uni bu yerdan tuzatib bo'lmaydi — sabab keyingi hisobotga yoziladi."}
      </p>
    </div>
  )
}

function Row({
  label,
  value,
  tone,
}: {
  label: string
  value: string
  tone?: 'ok' | 'warn'
}) {
  return (
    <div className="flex items-center justify-between px-3 py-2 text-sm">
      <dt className="text-slate-500">{label}</dt>
      <dd
        className={cn(
          'font-semibold tabular-nums',
          tone === 'ok' && 'text-emerald-700',
          tone === 'warn' && 'text-amber-700',
          !tone && 'text-slate-800',
        )}
      >
        {value}
      </dd>
    </div>
  )
}
