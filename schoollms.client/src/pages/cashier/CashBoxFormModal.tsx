import { useEffect, useState } from 'react'
import type { CashBox } from '@/api/services/cashBoxes'
import { createCashBox, updateCashBox } from '@/api/services/cashBoxes'
import { financeErrorMessage } from '@/api/services/cashier'
import { getStaff } from '@/api/services/staff'
import type { Staff } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Notice } from '@/pages/admin/billing/BillingUi'

interface Props {
  /** `null` — yangi kassa, aks holda tahrirlanayotgan kassa. */
  box: CashBox | null
  onClose: () => void
  onSaved: () => void
}

/**
 * Kassa qo'shish / tahrirlash — mijoz talabi: "bizda bir nechta kassa
 * bo'lishi mumkin" (2026-09-18). Smena degan tushuncha bu yerda yo'q,
 * faqat nom, mas'ul xodim, standart belgisi va faollik.
 *
 * MAS'UL XODIM ro'yxati `GET /admin/staff` dan olinadi — alohida
 * "kassa mas'uli" ro'yxati YO'Q, yangi endpoint o'ylab topilmadi.
 *
 * Tahrirlashda `responsibleUserId` oldindan TANLANMAYDI: server faqat
 * `responsibleName` (matn) qaytaradi, id'sini emas — shuning uchun eng
 * yaqin moslikni ismi bo'yicha topamiz, aniq mos kelmasa bo'sh qoladi.
 */
export function CashBoxFormModal({ box, onClose, onSaved }: Props) {
  const [name, setName] = useState(box?.name ?? '')
  const [responsibleId, setResponsibleId] = useState('')
  const [isDefault, setIsDefault] = useState(box?.isDefault ?? false)
  const [isActive, setIsActive] = useState(box?.isActive ?? true)
  const [staff, setStaff] = useState<Staff[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getStaff()
      .then((rows) => {
        if (!alive) return
        setStaff(rows)
        if (box?.responsibleName) {
          const match = rows.find((s) => s.fullName === box.responsibleName)
          if (match) setResponsibleId(match.id)
        }
      })
      .catch(() => undefined)
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- faqat oyna ochilganda bir marta (box o'zgarmaydi, oyna qayta ochiladi)
  }, [])

  const trimmedName = name.trim()
  const valid = trimmedName.length > 0

  const submit = async () => {
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      if (box) {
        await updateCashBox(box.id, {
          name: trimmedName,
          responsibleUserId: responsibleId || undefined,
          isDefault,
          isActive,
        })
      } else {
        await createCashBox({
          name: trimmedName,
          responsibleUserId: responsibleId || undefined,
          isDefault,
        })
      }
      onSaved()
    } catch (err) {
      setError(financeErrorMessage(err, "Kassani saqlab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={box ? 'Kassani tahrirlash' : 'Yangi kassa'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
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
          placeholder="Masalan: Bosh kassa"
          autoFocus
        />

        <Select
          label="Mas'ul xodim"
          value={responsibleId}
          onChange={(e) => setResponsibleId(e.target.value)}
        >
          <option value="">Tanlanmagan</option>
          {staff.map((s) => (
            <option key={s.id} value={s.id}>
              {s.fullName}
            </option>
          ))}
        </Select>

        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={isDefault}
            onChange={(e) => setIsDefault(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
          />
          Standart kassa (yulduzcha bilan belgilanadi)
        </label>

        {box && (
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
            />
            Faol (nofaol kassa ro'yxatda ko'z belgisi bosilganda ko'rinadi)
          </label>
        )}

        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
