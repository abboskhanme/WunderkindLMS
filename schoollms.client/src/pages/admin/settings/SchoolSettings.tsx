import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check } from 'lucide-react'
import {
  getContractNumberSettings,
  getGeneralSettings,
  getSchoolInfo,
  saveContractNumberSettings,
  saveGeneralSettings,
  saveSchoolInfo,
  type ContractNumberMode,
  type GeneralSettings,
  type SchoolInfo,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

const empty: SchoolInfo = {
  name: '',
  director: '',
  phone: '',
  email: '',
  address: '',
  region: '',
  district: '',
}

/**
 * §5.5 — to'rtta umumiy qoida. Har bir qatorda "nima o'zgaradi" ochiq yozilgan:
 * bu tugmalar pul va ma'lumot qoidalarini o'zgartiradi, ya'ni administrator
 * bosishdan oldin oqibatini bilishi kerak.
 */
const flagRows: { key: keyof GeneralSettings; title: string; hint: string }[] = [
  {
    key: 'archiveOnlyNonDebtorStudents',
    title: "Qarzi bor o'quvchini arxivlash taqiqlanadi",
    hint: "Arxivlash — qarzning yo'qolishining eng oson yo'li. Yoqiq bo'lsa qarzdorni arxivlab bo'lmaydi; faqat superadmin, ataylab tasdiqlab, chetlab o'ta oladi.",
  },
  {
    key: 'makeAttendanceReasonRequired',
    title: 'Davomat sababi majburiy',
    hint: "Jurnalda yo'qlik faqat ro'yxatdagi haqiqiy sabab bilan saqlanadi — bo'sh yoki o'chirilgan sabab rad etiladi.",
  },
  {
    key: 'isStudentGradeRequired',
    title: 'Baholarsiz darsni yopib bo\'lmaydi',
    hint: "\"Dars o'tildi\" belgisi darsda qatnashgan har bir o'quvchiga baho qo'yilgandan keyingina qo'yiladi. Kelmagan o'quvchidan baho kutilmaydi.",
  },
  {
    key: 'showLearningProgressInParentDashboard',
    title: "Ota-onalarga o'zlashtirish ko'rinadi",
    hint: "O'chirilsa ota-ona Telegram ilovasida baholar, reyting va topshiriq ballarini ko'rmaydi. O'quvchining o'zi va xodimlar ko'raveradi.",
  },
]

/** Maktabga oid umumiy ma'lumotlar (nomi, direktor, manzil va h.k.) kiritiladigan sozlama. */
export function SchoolSettings() {
  const [form, setForm] = useState<SchoolInfo>(empty)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  // null = bayroqlar yuklanmadi (masalan xodim — endpoint faqat admin/superadmin uchun).
  const [flags, setFlags] = useState<GeneralSettings | null>(null)
  const [flagsStatus, setFlagsStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [flagsError, setFlagsError] = useState<string | null>(null)

  // Shartnoma raqamlash qoidasi (K-6) — null = hali yuklanmadi.
  const [numberMode, setNumberMode] = useState<ContractNumberMode | null>(null)
  const [numberModeStatus, setNumberModeStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [numberModeError, setNumberModeError] = useState<string | null>(null)

  useEffect(() => {
    Promise.all([
      getSchoolInfo().then(setForm),
      getGeneralSettings()
        .then(setFlags)
        .catch(() => setFlags(null)),
      getContractNumberSettings()
        .then((s) => setNumberMode(s.numberMode))
        .catch(() => setNumberMode(null)),
    ]).finally(() => setLoading(false))
  }, [])

  const update = <K extends keyof SchoolInfo>(key: K, value: SchoolInfo[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    await saveSchoolInfo({ ...form, name: form.name.trim() })
    // Yon menyudagi maktab nomini darrov yangilash uchun.
    window.dispatchEvent(new Event('school:updated'))
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  const onSaveFlags = async () => {
    if (!flags) return
    setFlagsStatus('saving')
    setFlagsError(null)
    try {
      setFlags(await saveGeneralSettings(flags))
      setFlagsStatus('saved')
      setTimeout(() => setFlagsStatus('idle'), 2000)
    } catch (err) {
      setFlagsStatus('idle')
      setFlagsError(
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Saqlab bo'lmadi",
      )
    }
  }

  const onSaveNumberMode = async (mode: ContractNumberMode) => {
    setNumberModeStatus('saving')
    setNumberModeError(null)
    try {
      const saved = await saveContractNumberSettings(mode)
      setNumberMode(saved.numberMode)
      setNumberModeStatus('saved')
      setTimeout(() => setNumberModeStatus('idle'), 2000)
    } catch (err) {
      setNumberModeStatus('idle')
      setNumberModeError(
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Saqlab bo'lmadi",
      )
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <Card>
        <div className="mb-1 font-semibold text-slate-800">Maktab ma'lumotlari</div>
        <p className="mb-4 text-sm text-slate-400">
          Maktab nomi va umumiy ma'lumotlar — hisobotlar va hujjatlarda ishlatiladi.
        </p>
        <form onSubmit={onSubmit} className="max-w-2xl space-y-4">
          <Input
            label="Maktab nomi"
            placeholder="Masalan: 1-sonli umumiy o'rta ta'lim maktabi"
            value={form.name}
            onChange={(e) => update('name', e.target.value)}
            required
          />
          <Input
            label="Direktor (F.I.SH)"
            value={form.director}
            onChange={(e) => update('director', e.target.value)}
          />
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Input label="Telefon" value={form.phone} onChange={(e) => update('phone', e.target.value)} />
            <Input
              label="Email"
              type="email"
              value={form.email}
              onChange={(e) => update('email', e.target.value)}
            />
          </div>
          <Input label="Manzil" value={form.address} onChange={(e) => update('address', e.target.value)} />
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Input label="Viloyat" value={form.region} onChange={(e) => update('region', e.target.value)} />
            <Input label="Tuman" value={form.district} onChange={(e) => update('district', e.target.value)} />
          </div>

          <div className="flex items-center gap-3">
            <Button type="submit" disabled={status === 'saving'}>
              {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
            {status === 'saved' && (
              <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
                <Check className="h-4 w-4" /> Saqlandi
              </span>
            )}
          </div>
        </form>
      </Card>

      {flags && (
        <Card>
          <div className="mb-1 font-semibold text-slate-800">Umumiy qoidalar</div>
          <p className="mb-4 text-sm text-slate-400">
            Maktab bo'ylab amal qiladigan qoidalar. O'zgartirish darrov kuchga kiradi.
          </p>
          <div className="max-w-2xl divide-y divide-slate-100">
            {flagRows.map((row) => (
              <label key={row.key} className="flex cursor-pointer items-start gap-3 py-3">
                <input
                  type="checkbox"
                  checked={flags[row.key]}
                  onChange={(e) => setFlags({ ...flags, [row.key]: e.target.checked })}
                  className="mt-0.5 h-4 w-4 rounded border-slate-300 accent-brand-600"
                />
                <span>
                  <span className="block text-sm font-medium text-slate-800">{row.title}</span>
                  <span className="block text-xs text-slate-400">{row.hint}</span>
                </span>
              </label>
            ))}
          </div>
          <div className="mt-4 flex items-center gap-3">
            <Button onClick={onSaveFlags} disabled={flagsStatus === 'saving'}>
              {flagsStatus === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
            {flagsStatus === 'saved' && (
              <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
                <Check className="h-4 w-4" /> Saqlandi
              </span>
            )}
            {flagsError && <span className="text-sm text-red-600">{flagsError}</span>}
          </div>
        </Card>
      )}

      {numberMode && (
        <Card>
          <div className="mb-1 font-semibold text-slate-800">Shartnoma raqamlash</div>
          <p className="mb-4 text-sm text-slate-400">
            O'quvchi shartnomasi reyestriga (Shartnomalar) raqam qanday beriladi — tanlov
            darrov kuchga kiradi va butun maktab bo'ylab amal qiladi.
          </p>
          <div className="max-w-2xl space-y-2">
            {numberModeOptions.map((opt) => (
              <label
                key={opt.value}
                className="flex cursor-pointer items-start gap-3 rounded-lg border border-slate-200 p-3 transition-colors hover:bg-slate-50"
              >
                <input
                  type="radio"
                  name="contract-number-mode"
                  checked={numberMode === opt.value}
                  onChange={() => onSaveNumberMode(opt.value)}
                  className="mt-0.5 h-4 w-4 accent-brand-600"
                />
                <span>
                  <span className="block text-sm font-medium text-slate-800">{opt.title}</span>
                  <span className="block text-xs text-slate-400">{opt.hint}</span>
                </span>
              </label>
            ))}
          </div>
          <div className="mt-3 flex items-center gap-3">
            {numberModeStatus === 'saving' && (
              <span className="text-sm text-slate-400">Saqlanmoqda...</span>
            )}
            {numberModeStatus === 'saved' && (
              <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
                <Check className="h-4 w-4" /> Saqlandi
              </span>
            )}
            {numberModeError && <span className="text-sm text-red-600">{numberModeError}</span>}
          </div>
        </Card>
      )}
    </div>
  )
}

/** K-6 — ikkita rejim, har biri ANIQ nima qilishi ko'rsatilgan. */
const numberModeOptions: { value: ContractNumberMode; title: string; hint: string }[] = [
  {
    value: 'auto',
    title: 'Avtomatik',
    hint: "Raqamni tizim o'zi beradi (ketma-ket) — shartnoma hosil qilish formasidagi raqam maydoni faqat ko'rsatish uchun.",
  },
  {
    value: 'manual',
    title: "Qo'lda",
    hint: "Xodim raqamni o'zi kiritadi (masalan davlat blankasi yoki eski qog'oz arxivi bilan davom etish uchun). Takrorlangan raqam serverda rad etiladi.",
  },
]
