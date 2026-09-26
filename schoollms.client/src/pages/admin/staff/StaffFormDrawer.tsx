/**
 * "Boshqa xodim" (o'qituvchi bo'lmagan) — yaratish / tahrirlash paneli.
 *
 * `StaffPage` dagi formaning o'zi + maosh (employees-unified.md): telefon, oylik va maosh
 * qaysi kundan hisoblanishi. Rolni faqat superadmin biriktiradi (server ham shuni talab
 * qiladi); rol xatosi xodimni saqlashga to'sqinlik qilmaydi — sahifa xabar beradi.
 *
 * Panel faqat ochiq paytda chiziladi (ota komponent shartli render qiladi), shuning uchun
 * holat har ochilishda boshlang'ich qiymatdan boshlanadi — effekt kerak emas.
 */
import { useState } from 'react'
import type { AccessRole, Staff } from '@/types'
import { createStaff, setStaffRole, updateStaff, type StaffPayload } from '@/api/services/staff'
import { billingErrorMessage } from '@/api/services/billingError'
import { localDigits, toPhoneValue } from '@/lib/phone'
import { randomPassword } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Drawer } from '@/components/ui/Drawer'
import { DatePicker } from '@/components/ui/DatePicker'
import { Input, Select } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { PhotoUpload } from '@/components/ui/PhotoUpload'
import { Notice } from '../billing/BillingUi'

/** Lavozim takliflari — ro'yxatda allaqachon borlari ham qo'shiladi. */
const DEFAULT_POSITIONS = ['Kassir', 'Administrator', "Direktor o'rinbosari", 'Qorovul', 'Hisobchi']

const todayIso = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export interface StaffSaveResult {
  created: boolean
  /** Xodim saqlandi, lekin rolni biriktirib bo'lmadi — ko'rsatiladigan xabar. */
  roleError: string | null
}

interface Props {
  /** null — yangi xodim. */
  editing: Staff | null
  roles: AccessRole[]
  canManageRoles: boolean
  /** Lavozim takliflari uchun mavjud lavozimlar. */
  knownPositions: string[]
  onClose: () => void
  onSaved: (staff: Staff, result: StaffSaveResult) => void
}

export function StaffFormDrawer({ editing, roles, canManageRoles, knownPositions, onClose, onSaved }: Props) {
  const [fullName, setFullName] = useState(editing?.fullName ?? '')
  const [position, setPosition] = useState(editing?.position ?? '')
  const [avatarUrl, setAvatarUrl] = useState(editing?.avatarUrl ?? '')
  const [phone, setPhone] = useState(editing?.phone ?? '')
  const [salary, setSalary] = useState(editing && editing.salary > 0 ? String(editing.salary) : '')
  // Yangi xodim odatda bugundan ishlaydi; tahrirda saqlangani (bo'sh bo'lishi ham mumkin).
  const [salaryStartDate, setSalaryStartDate] = useState(editing ? editing.salaryStartDate : todayIso())
  const [newPassword, setNewPassword] = useState('')
  const [roleId, setRoleId] = useState(editing?.accessRoleId ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const suggestions = [...new Set([...DEFAULT_POSITIONS, ...knownPositions])].sort((a, b) => a.localeCompare(b, 'uz'))

  const phoneDigits = localDigits(phone).length
  const salaryNum = salary.trim() === '' ? 0 : Number(salary)

  const validate = (): string | null => {
    if (!fullName.trim()) return 'F.I.SH kiritilishi shart.'
    if (phoneDigits > 0 && phoneDigits < 9) return "Telefon raqami to'liq emas."
    if (!Number.isFinite(salaryNum) || salaryNum < 0) return "Oylik manfiy bo'lmasligi kerak."
    if (salaryNum > 0 && !salaryStartDate) return 'Maosh qaysi kundan hisoblanishini tanlang.'
    return null
  }

  /** Rol o'zgargan bo'lsa biriktiradi; xato bo'lsa xodim saqlangan qoladi, xabar qaytadi. */
  const applyRole = async (s: Staff): Promise<{ staff: Staff; roleError: string | null }> => {
    if (!canManageRoles || (s.accessRoleId ?? '') === roleId) return { staff: s, roleError: null }
    try {
      return { staff: await setStaffRole(s.id, roleId || null), roleError: null }
    } catch (err) {
      return { staff: s, roleError: roleErrorMessage(err) }
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (saving) return
    const invalid = validate()
    if (invalid) {
      setError(invalid)
      return
    }
    setSaving(true)
    setError(null)
    const payload: StaffPayload = {
      fullName: fullName.trim(),
      position: position.trim(),
      avatarUrl,
      phone: toPhoneValue(phone),
      salary: salaryNum,
      salaryStartDate,
      ...(editing && newPassword.trim() ? { newPassword: newPassword.trim() } : {}),
    }
    try {
      const saved = editing ? await updateStaff(editing.id, payload) : await createStaff(payload)
      const { staff, roleError } = await applyRole(saved)
      onSaved(staff, { created: !editing, roleError })
    } catch (err) {
      setError(billingErrorMessage(err, "Xodimni saqlab bo'lmadi"))
      setSaving(false)
    }
  }

  return (
    <Drawer
      open
      onClose={onClose}
      title={editing ? 'Xodimni tahrirlash' : 'Yangi xodim'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button type="submit" form="staff-form" disabled={saving}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="staff-form" onSubmit={handleSubmit} className="space-y-4">
        <PhotoUpload label="Profil rasmi" value={avatarUrl || null} onChange={(url) => setAvatarUrl(url ?? '')} />
        <Input label="F.I.SH" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600" htmlFor="staff-position">
            Lavozim
          </label>
          <input
            id="staff-position"
            list="staff-positions"
            value={position}
            onChange={(e) => setPosition(e.target.value)}
            placeholder="Masalan: Kassir"
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          />
          <datalist id="staff-positions">
            {suggestions.map((p) => (
              <option key={p} value={p} />
            ))}
          </datalist>
        </div>
        <PhoneInput label="Telefon" value={phone} onChange={setPhone} />

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Oylik maosh</span>
            <div className="relative">
              <input
                type="number"
                min={0}
                step={1000}
                inputMode="numeric"
                placeholder="0"
                value={salary}
                onChange={(e) => setSalary(e.target.value)}
                className="w-full rounded-lg border border-slate-200 px-3 py-2 pr-14 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              />
              <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-xs text-slate-400">
                so'm
              </span>
            </div>
          </label>
          <DatePicker
            label="Maosh hisoblanadi"
            required={salaryNum > 0}
            value={salaryStartDate}
            onChange={setSalaryStartDate}
            clearable
            invalid={salaryNum > 0 && !salaryStartDate}
          />
        </div>
        <p className="-mt-2 text-xs text-slate-400">
          Oylik shu kundan hisoblanadi; oy o'rtasida boshlasa, birinchi oy kunlar bo'yicha qisman. Maosh
          Moliya → Ish haqi bo'limidan beriladi.
        </p>

        {editing && (
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600" htmlFor="staff-password">
              Parolni almashtirish
            </label>
            <div className="flex items-start gap-2">
              <input
                id="staff-password"
                type="text"
                autoComplete="new-password"
                placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              />
              <Button type="button" variant="secondary" onClick={() => setNewPassword(randomPassword())}>
                Generatsiya
              </Button>
            </div>
          </div>
        )}

        <div>
          <Select label="Rol" value={roleId} disabled={!canManageRoles} onChange={(e) => setRoleId(e.target.value)}>
            <option value="">— Rol biriktirilmagan —</option>
            {roles.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </Select>
          <p className="mt-1 text-xs text-slate-400">
            {canManageRoles
              ? "Xodim faqat shu rolga berilgan bo'limlarni ko'radi. Ruxsatlar Boshqaruv → Rollar sahifasida."
              : 'Rolni tizim egasi (superadmin) biriktiradi.'}
          </p>
          {editing && !editing.accessRoleId && editing.permissions.length > 0 && canManageRoles && (
            <p className="mt-1 text-xs text-amber-600">
              Hozir {editing.permissions.length} ta eski shaxsiy ruxsat amal qilyapti — rol tanlansa, ular rol
              ruxsatlari bilan almashadi.
            </p>
          )}
        </div>

        {!editing && (
          <p className="text-xs text-slate-400">
            Saqlangach tizimga kirish uchun login va parol avtomatik yaratiladi va ko'rsatiladi.
          </p>
        )}
        {error && <Notice>{error}</Notice>}
      </form>
    </Drawer>
  )
}

/** Rol biriktirish xatosi — ko'pincha rol o'zgargandan keyin eski sessiya (403): qayta kirish kerak. */
function roleErrorMessage(err: unknown): string {
  const status = (err as { response?: { status?: number } })?.response?.status
  if (status === 403 || status === 401)
    return "Rolni biriktirib bo'lmadi: sizning sessiyangizda superadmin huquqi yo'q. Tizimdan chiqib, qayta kiring."
  return "Rolni biriktirib bo'lmadi. Qaytadan urinib ko'ring."
}
