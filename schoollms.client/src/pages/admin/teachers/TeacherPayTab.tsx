import { useCallback, useEffect, useState } from 'react'
import { Award, Paperclip, Pencil, Plus, Trash2, Upload } from 'lucide-react'
import {
  createTeacherAllowance,
  deleteTeacherAllowance,
  getTeacherAllowances,
  setTeacherBonus,
  updateTeacherAllowance,
  type AllowanceKind,
  type AllowanceSource,
  type TeacherAllowance,
  type TeacherAllowancePayload,
  type TeacherAllowances,
} from '@/api/services/teacherAllowances'
import { uploadAdminFile } from '@/api/services/students'
import { billingErrorMessage } from '@/api/services/billingError'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { formatMonth } from '@/config/constants'
import { cn, formatDate, formatMoney } from '@/lib/utils'

const thisMonth = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

interface Form {
  title: string
  amountKind: AllowanceKind
  amount: string
  fromMonth: string
  toMonth: string
  level: string
  number: string
  issuedOn: string
  expiresOn: string
  fileUrl: string
  note: string
}

const emptyForm = (): Form => ({
  title: '',
  amountKind: 'fixed',
  amount: '',
  fromMonth: thisMonth(),
  toMonth: '',
  level: '',
  number: '',
  issuedOn: '',
  expiresOn: '',
  fileUrl: '',
  note: '',
})

const formOf = (a: TeacherAllowance): Form => ({
  title: a.title,
  amountKind: a.amountKind,
  amount: String(a.amount),
  fromMonth: a.fromMonth,
  toMonth: a.toMonth ?? '',
  level: a.level ?? '',
  number: a.number ?? '',
  issuedOn: a.issuedOn ?? '',
  expiresOn: a.expiresOn ?? '',
  fileUrl: a.fileUrl ?? '',
  note: a.note ?? '',
})

const amountText = (a: { amountKind: AllowanceKind; amount: number }) =>
  a.amountKind === 'percent' ? `${a.amount}% dars pulidan` : `${formatMoney(a.amount)} / oy`

const periodText = (a: TeacherAllowance) =>
  a.toMonth ? `${formatMonth(a.fromMonth)} — ${formatMonth(a.toMonth)}` : `${formatMonth(a.fromMonth)} dan`

/**
 * O'qituvchi kartochkasidagi «Sertifikatlari» va «Ustama» tablari (mijoz, 2026-10-15: "sertifikat va ustama
 * qismlarini qo'shish va buni to'g'ridan to'g'ri moliyaga bog'lash"). Har qator oylikka avtomatik qo'shiladi —
 * Moliya → Ish haqi, maosh tafsiloti va xodimlar maoshi daftari bir xil raqamni ko'radi. Faqat moliya ruxsati
 * borlarga ko'rsatiladi; `canEdit` — yozish (admin, direktor, «finance» xodim).
 */
export function TeacherPayTab({
  teacherId,
  source,
  canEdit,
}: {
  teacherId: string
  source: AllowanceSource
  canEdit: boolean
}) {
  const [data, setData] = useState<TeacherAllowances | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<TeacherAllowance | 'new' | null>(null)
  const [form, setForm] = useState<Form>(emptyForm)
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [deleting, setDeleting] = useState<string | null>(null)
  const [bonusPct, setBonusPct] = useState('')
  const [bonusSaving, setBonusSaving] = useState(false)

  const load = useCallback(() => {
    setError(null)
    return getTeacherAllowances(teacherId)
      .then((d) => {
        setData(d)
        setBonusPct(String(d.bonusPct))
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [teacherId])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- o'qituvchi almashganda qayta yuklaymiz (maqsadli)
    setLoading(true)
    setEditing(null)
    load()
  }, [load])

  const isCert = source === 'certificate'
  const items = (data?.items ?? []).filter((a) => a.source === source)
  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => ({ ...f, [key]: value }))

  const openNew = () => {
    setForm({ ...emptyForm(), amountKind: isCert ? 'percent' : 'fixed' })
    setFormError(null)
    setEditing('new')
  }

  const openEdit = (a: TeacherAllowance) => {
    setForm(formOf(a))
    setFormError(null)
    setEditing(a)
  }

  const upload = (file: File | undefined) => {
    if (!file) return
    setUploading(true)
    uploadAdminFile(file)
      .then((f) => set('fileUrl', f.url))
      .catch((e: unknown) => setFormError(billingErrorMessage(e, "Faylni yuklab bo'lmadi")))
      .finally(() => setUploading(false))
  }

  const save = (e: React.FormEvent) => {
    e.preventDefault()
    if (saving || !editing) return
    const amount = Number(form.amount.replace(/\s/g, '').replace(',', '.'))
    if (!form.title.trim()) return setFormError('Nomini kiriting')
    if (!Number.isFinite(amount) || amount < 0) return setFormError("Miqdorni to'g'ri kiriting")
    const payload: TeacherAllowancePayload = {
      source,
      title: form.title.trim(),
      amountKind: form.amountKind,
      amount,
      fromMonth: form.fromMonth,
      toMonth: form.toMonth || null,
      level: isCert ? form.level.trim() || null : null,
      number: isCert ? form.number.trim() || null : null,
      issuedOn: isCert ? form.issuedOn || null : null,
      expiresOn: isCert ? form.expiresOn || null : null,
      fileUrl: isCert ? form.fileUrl || null : null,
      note: form.note.trim() || null,
    }
    setSaving(true)
    setFormError(null)
    const request =
      editing === 'new'
        ? createTeacherAllowance(teacherId, payload)
        : updateTeacherAllowance(teacherId, editing.id, payload)
    request
      .then(() => {
        setEditing(null)
        return load()
      })
      .catch((err: unknown) => setFormError(billingErrorMessage(err, "Saqlab bo'lmadi")))
      .finally(() => setSaving(false))
  }

  const remove = (id: string) => {
    deleteTeacherAllowance(teacherId, id)
      .then(() => load())
      .catch((err: unknown) => setError(billingErrorMessage(err, "O'chirib bo'lmadi")))
      .finally(() => setDeleting(null))
  }

  const saveBonus = () => {
    const pct = Number(bonusPct.replace(',', '.'))
    if (!Number.isFinite(pct) || pct < 0 || pct > 1000) return setError("Ustama foizi 0–1000 oralig'ida bo'lsin")
    setBonusSaving(true)
    setTeacherBonus(teacherId, pct)
      .then(() => load())
      .catch((err: unknown) => setError(billingErrorMessage(err, "Saqlab bo'lmadi")))
      .finally(() => setBonusSaving(false))
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!data) return <p className="py-10 text-center text-sm text-slate-400">{error ?? "Ma'lumot yo'q"}</p>

  const m = data.currentMonth

  return (
    <div className="space-y-4">
      {/* Shu oy maoshi — har qator qayerga tushishi ko'rinib tursin */}
      <div className="rounded-2xl border border-slate-100 bg-slate-50/50 p-4">
        <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">
          {formatMonth(m.month)} — oylik tarkibi
        </p>
        <Line label={`Dars puli (${Math.max(0, m.plannedLessons - m.missedLessons + m.substitutionLessons)} dars)`} value={formatMoney(m.lessonPay)} />
        {m.ustamaAmount > 0 && <Line label={`Umumiy ustama (${m.ustamaPct}%)`} value={`+ ${formatMoney(m.ustamaAmount)}`} plus />}
        {m.allowanceAmount > 0 && <Line label="Sertifikat va ustamalar" value={`+ ${formatMoney(m.allowanceAmount)}`} plus />}
        {m.bonus > 0 && <Line label="Bonus" value={`+ ${formatMoney(m.bonus)}`} plus />}
        {m.penalty > 0 && <Line label="Jarima" value={`− ${formatMoney(m.penalty)}`} minus />}
        <div className="my-2 border-t border-slate-200" />
        <div className="flex items-center justify-between">
          <span className="text-sm font-semibold text-slate-700">Jami</span>
          <span className="text-base font-bold text-slate-900">{formatMoney(m.total)}</span>
        </div>
        {m.plannedLessons === 0 && (
          <p className="mt-2 text-xs text-slate-400">Bu oyda reja darslar yo'q — qat'iy summali ustamalar ham to'lanmaydi.</p>
        )}
      </div>

      {!isCert && (
        <div className="flex flex-wrap items-end gap-3 rounded-xl border border-slate-200 px-3 py-2.5">
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-slate-700">Umumiy ustama foizi</p>
            <p className="mt-0.5 text-xs text-slate-400">Dars puliga qo'shiladigan foiz (Moliya → Ish haqi dagi bilan bir xil).</p>
          </div>
          <div className="flex items-center gap-2">
            <input
              type="number"
              min={0}
              max={1000}
              step="0.5"
              value={bonusPct}
              disabled={!canEdit}
              onChange={(e) => setBonusPct(e.target.value)}
              className="w-24 rounded-lg border border-slate-200 px-3 py-2 text-right text-sm outline-none focus:border-brand-400 disabled:bg-slate-50"
            />
            <span className="text-sm text-slate-400">%</span>
            {canEdit && Number(bonusPct) !== data.bonusPct && (
              <Button onClick={saveBonus} disabled={bonusSaving} className="px-3 py-2">
                {bonusSaving ? 'Saqlanmoqda…' : 'Saqlash'}
              </Button>
            )}
          </div>
        </div>
      )}

      {error && <p className="text-sm text-red-600">{error}</p>}

      <div className="flex items-center justify-between">
        <p className="text-sm font-medium text-slate-600">
          {isCert ? "O'qituvchining sertifikatlari" : 'Ustamalar'} {items.length > 0 && `(${items.length})`}
        </p>
        {canEdit && editing === null && (
          <Button variant="secondary" onClick={openNew} className="px-3 py-1.5">
            <Plus className="h-4 w-4" />
            {isCert ? "Sertifikat qo'shish" : "Ustama qo'shish"}
          </Button>
        )}
      </div>

      {editing !== null && (
        <form onSubmit={save} className="space-y-3 rounded-2xl border border-brand-100 bg-white p-4">
          <Input
            label={isCert ? 'Sertifikat nomi' : 'Ustama nomi'}
            required
            placeholder={isCert ? 'IELTS, Milliy sertifikat — matematika…' : "Sinf rahbarligi, to'garak…"}
            value={form.title}
            onChange={(e) => set('title', e.target.value)}
          />
          {isCert && (
            <div className="grid gap-3 sm:grid-cols-2">
              <Input label="Daraja / ball" placeholder="C1, A+, 7.5" value={form.level} onChange={(e) => set('level', e.target.value)} />
              <Input label="Raqami" value={form.number} onChange={(e) => set('number', e.target.value)} />
              <Input label="Berilgan sana" type="date" value={form.issuedOn} onChange={(e) => set('issuedOn', e.target.value)} />
              <Input
                label="Amal qilish muddati"
                type="date"
                value={form.expiresOn}
                onChange={(e) => set('expiresOn', e.target.value)}
              />
            </div>
          )}
          <div className="grid gap-3 sm:grid-cols-2">
            <Select label="Oylikka qanday qo'shiladi" value={form.amountKind} onChange={(e) => set('amountKind', e.target.value as AllowanceKind)}>
              <option value="percent">Dars pulidan foiz</option>
              <option value="fixed">Har oy qat'iy summa</option>
            </Select>
            <Input
              label={form.amountKind === 'percent' ? 'Foiz (%)' : "Summa (so'm / oy)"}
              required
              inputMode="decimal"
              value={form.amount}
              onChange={(e) => set('amount', e.target.value)}
            />
            <Input label="Qaysi oydan" type="month" required value={form.fromMonth} onChange={(e) => set('fromMonth', e.target.value)} />
            <Input label="Qaysi oygacha (ixtiyoriy)" type="month" value={form.toMonth} onChange={(e) => set('toMonth', e.target.value)} />
          </div>
          {isCert && (
            <div className="flex flex-wrap items-center gap-2">
              <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-600 hover:bg-slate-50">
                <Upload className="h-4 w-4" />
                {uploading ? 'Yuklanmoqda…' : form.fileUrl ? 'Faylni almashtirish' : 'Fayl biriktirish'}
                <input type="file" accept="image/*,application/pdf" className="hidden" onChange={(e) => upload(e.target.files?.[0])} />
              </label>
              {form.fileUrl && (
                <a href={form.fileUrl} target="_blank" rel="noreferrer" className="text-sm text-brand-600 hover:underline">
                  Faylni ochish
                </a>
              )}
            </div>
          )}
          <Textarea label="Izoh" rows={2} value={form.note} onChange={(e) => set('note', e.target.value)} />
          {formError && <p className="text-sm text-red-600">{formError}</p>}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
              Bekor qilish
            </Button>
            <Button type="submit" disabled={saving || uploading}>
              {saving ? 'Saqlanmoqda…' : 'Saqlash'}
            </Button>
          </div>
        </form>
      )}

      {items.length === 0 && editing === null ? (
        <div className="flex flex-col items-center gap-2 py-8 text-center">
          <Award className="h-8 w-8 text-slate-300" />
          <p className="text-sm text-slate-400">
            {isCert ? "Hali sertifikat qo'shilmagan" : "Hali ustama qo'shilmagan"}
          </p>
        </div>
      ) : (
        <div className="space-y-2">
          {items.map((a) => (
            <div key={a.id} className="flex flex-wrap items-center gap-3 rounded-lg border border-slate-100 px-3 py-2">
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-slate-800">
                  {a.title}
                  {a.level && <span className="text-slate-400">— {a.level}</span>}
                  {a.number && <span className="text-slate-400">№ {a.number}</span>}
                  <span className="rounded bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">{amountText(a)}</span>
                  {a.isExpired ? (
                    <span className="rounded bg-red-50 px-2 py-0.5 text-xs font-medium text-red-600">Muddati o'tgan</span>
                  ) : (
                    !a.activeNow && (
                      <span className="rounded bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">Shu oy to'lanmaydi</span>
                    )
                  )}
                </p>
                <p className={cn('mt-0.5 text-xs', a.isExpired ? 'text-red-400' : 'text-slate-400')}>
                  {periodText(a)}
                  {a.issuedOn ? ` · berilgan ${formatDate(a.issuedOn)}` : ''}
                  {a.expiresOn ? ` · muddati ${formatDate(a.expiresOn)}` : ''}
                  {a.note ? ` · ${a.note}` : ''}
                </p>
              </div>
              {a.fileUrl && (
                <a
                  href={a.fileUrl}
                  target="_blank"
                  rel="noreferrer"
                  title="Faylni ochish"
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                >
                  <Paperclip className="h-4 w-4" />
                </a>
              )}
              {canEdit &&
                (deleting === a.id ? (
                  <span className="flex items-center gap-1 text-xs">
                    <span className="text-slate-500">O'chirilsinmi?</span>
                    <button type="button" onClick={() => remove(a.id)} className="rounded px-2 py-1 font-medium text-red-600 hover:bg-red-50">
                      Ha
                    </button>
                    <button type="button" onClick={() => setDeleting(null)} className="rounded px-2 py-1 text-slate-500 hover:bg-slate-100">
                      Yo'q
                    </button>
                  </span>
                ) : (
                  <span className="flex items-center">
                    <button
                      type="button"
                      title="Tahrirlash"
                      onClick={() => openEdit(a)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                    >
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      title="O'chirish (xato kiritilgan bo'lsa). Tugagan ustamaga «qaysi oygacha» qo'ying."
                      onClick={() => setDeleting(a.id)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </span>
                ))}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function Line({ label, value, plus, minus }: { label: string; value: string; plus?: boolean; minus?: boolean }) {
  return (
    <div className="flex items-center justify-between py-1 text-sm">
      <span className="text-slate-500">{label}</span>
      <span className={cn('font-medium text-slate-700', plus && 'text-emerald-600', minus && 'text-red-600')}>{value}</span>
    </div>
  )
}
