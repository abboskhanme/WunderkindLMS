/**
 * Attach a catalog discount type to a student (docs/modules/billing-plans.md §8).
 *
 * NO APPROVAL (client choice): the discount is written approved and works at once. Months that
 * are already invoiced from the start month on are reduced right away (only what is still
 * unpaid); later months get it at accrual. The modal says both before the user confirms.
 *
 * Discounts run in whole months: the start month becomes its first day, the optional end
 * month its last day.
 */
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { RefreshCw } from 'lucide-react'
import type { DiscountType } from '@/types'
import { getDiscountTypes } from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { MonthPicker } from '@/components/ui/DatePicker'
import { Notice } from './BillingUi'
import { currentMonth, discountValueLabel, monthEnd, monthStart } from './discountUtils'

export interface AttachDiscountValues {
  discountTypeId: string
  /** "yyyy-MM-01" */
  startsOn: string
  /** Last day of the end month, or null = open-ended. */
  endsOn: string | null
}

interface Props {
  open: boolean
  studentName: string
  /** How many discounts the student has running — to explain "one discount per month". */
  runningCount: number
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: (values: AttachDiscountValues) => void
}

const scopeLabel = (t: DiscountType) => t.categoryName ?? 'Barcha abonementlar'

export function AttachDiscountModal({
  open,
  studentName,
  runningCount,
  busy,
  error,
  onClose,
  onConfirm,
}: Props) {
  const [types, setTypes] = useState<DiscountType[]>([])
  const [typesLoading, setTypesLoading] = useState(true)
  const [typesError, setTypesError] = useState<string | null>(null)

  const [typeId, setTypeId] = useState('')
  const [fromMonth, setFromMonth] = useState(currentMonth())
  const [toMonth, setToMonth] = useState('')

  const loadTypes = useCallback(() => {
    setTypesLoading(true)
    setTypesError(null)
    getDiscountTypes(true)
      .then((rows) => setTypes([...rows].sort((a, b) => a.name.localeCompare(b.name, 'uz'))))
      .catch((e: unknown) => setTypesError(billingErrorMessage(e, "Chegirma turlarini yuklab bo'lmadi")))
      .finally(() => setTypesLoading(false))
  }, [])

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- reset the form and reload the catalog each time the modal opens (intentional) */
    setTypeId('')
    setFromMonth(currentMonth())
    setToMonth('')
    loadTypes()
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, loadTypes])

  const selected = types.find((t) => t.id === typeId) ?? null
  const periodValid = toMonth === '' || toMonth >= fromMonth
  const valid = selected !== null && fromMonth !== '' && periodValid

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy || !selected) return
    onConfirm({
      discountTypeId: selected.id,
      startsOn: monthStart(fromMonth),
      endsOn: toMonth === '' ? null : monthEnd(toMonth),
    })
  }

  const body = () => {
    if (typesLoading) return <Loader label="Chegirma turlari yuklanmoqda..." />

    if (typesError) {
      return (
        <div className="space-y-3">
          <Notice>{typesError}</Notice>
          <Button variant="secondary" onClick={loadTypes}>
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        </div>
      )
    }

    if (types.length === 0) {
      return (
        <div className="flex flex-col items-center gap-2 px-4 py-8 text-center">
          <p className="text-sm text-slate-500">Faol chegirma turi yo'q.</p>
          <p className="text-xs text-slate-400">
            Avval{' '}
            <Link
              to="/admin/billing/settings?tab=discounts"
              className="font-medium text-brand-600 hover:text-brand-700"
            >
              Moliya sozlamalari → Chegirmalar
            </Link>{' '}
            sahifasida chegirma turini qo'shing.
          </p>
        </div>
      )
    }

    return (
      <form id="attach-discount-form" onSubmit={handleSubmit} className="space-y-4">
        <div>
          <Select
            label="Chegirma turi"
            required
            autoFocus
            value={typeId}
            onChange={(e) => setTypeId(e.target.value)}
          >
            <option value="">Tanlang...</option>
            {types.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name} — {discountValueLabel(t.percent, t.amount)}
              </option>
            ))}
          </Select>
          {selected && (
            <p className="mt-1 text-xs text-slate-500">
              {discountValueLabel(selected.percent, selected.amount)} · {scopeLabel(selected)}
            </p>
          )}
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <MonthPicker
            label="Qaysi oydan"
            required
            value={fromMonth}
            onChange={setFromMonth}
            className="w-full"
          />
          <div>
            <MonthPicker
              label="Qaysi oygacha"
              value={toMonth}
              onChange={setToMonth}
              min={fromMonth || undefined}
              placeholder="Muddatsiz"
              clearable
              className="w-full"
              invalid={!periodValid}
            />
            <p className={periodValid ? 'mt-1 text-xs text-slate-400' : 'mt-1 text-xs text-red-600'}>
              {periodValid ? "Bo'sh = muddatsiz." : "Tugash oyi boshlanish oyidan oldin bo'la olmaydi."}
            </p>
          </div>
        </div>

        <Notice tone="info">
          Tasdiq shart emas — darhol amalga kiradi. Tanlangan oydan boshlab hisoblangan oylarning
          to'lanmagan qarzidan ham darhol ayriladi; to'langan qismi o'zgarmaydi.
        </Notice>

        {runningCount > 0 && (
          <p className="text-xs text-slate-500">
            Bu o'quvchida amaldagi chegirma bor. Bir oyga bitta chegirma qo'llanadi: abonement
            turiga xos chegirma umumiysidan ustun, teng bo'lsa eng oxirgisi.
          </p>
        )}

        {error && <Notice>{error}</Notice>}
      </form>
    )
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Chegirma biriktirish — ${studentName}`}
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button
            type="submit"
            form="attach-discount-form"
            disabled={!valid || busy || typesLoading || typesError !== null}
          >
            {busy ? 'Biriktirilmoqda...' : 'Biriktirish'}
          </Button>
        </>
      }
    >
      {body()}
    </Modal>
  )
}
