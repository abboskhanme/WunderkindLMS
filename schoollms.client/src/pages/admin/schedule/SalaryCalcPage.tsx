import { useCallback, useEffect, useMemo, useState } from 'react'
import { Wallet, Save, Clock, Percent } from 'lucide-react'
import { getSalaryRates, saveSalaryRates, setBonusBulk, type SalaryRates } from '@/api/services/salaryRates'
import { teacherCategoryLabel } from '@/config/constants'
import { formatMoney, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { TeacherSalaryDetailModal } from './TeacherSalaryDetailModal'

const rateFields = [
  { key: 'oliy', label: 'Oliy toifa' },
  { key: 't1', label: '1-toifa' },
  { key: 't2', label: '2-toifa' },
  { key: 'mutaxasis', label: 'Mutaxasis' },
] as const

function currentMonth(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

export function SalaryCalcPage() {
  const [data, setData] = useState<SalaryRates | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [month, setMonth] = useState(currentMonth())
  const [rates, setRates] = useState({ oliy: 0, t1: 0, t2: 0, mutaxasis: 0 })
  const [detailId, setDetailId] = useState<string | null>(null)
  // Ustama tayinlash uchun tanlash
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [bonusModal, setBonusModal] = useState(false)
  const [assignPct, setAssignPct] = useState(50)
  const [assigning, setAssigning] = useState(false)

  const load = useCallback((m: string) => {
    setLoading(true)
    getSalaryRates(m)
      .then((d) => {
        setData(d)
        setRates({ oliy: d.oliy, t1: d.t1, t2: d.t2, mutaxasis: d.mutaxasis })
        setSelected(new Set())
      })
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => load(month), [month, load])

  // Joriy kiritilgan narxlar bo'yicha jonli qayta hisob (kelmagan kun darslari chegiriladi, ustama qo'shiladi).
  const liveTeachers = useMemo(() => {
    if (!data) return []
    const rateOf = (c: string) =>
      c === 'oliy' ? rates.oliy : c === '1' ? rates.t1 : c === '2' ? rates.t2 : c === 'mutaxasis' ? rates.mutaxasis : 0
    return data.teachers.map((t) => {
      const base = Math.max(0, t.monthlyLessons - t.missedLessons) * rateOf(t.category)
      return { ...t, monthlySalary: base * (1 + (t.bonusPct || 0) / 100) }
    })
  }, [data, rates])

  const totalMonthly = useMemo(
    () => liveTeachers.reduce((sum, t) => sum + t.monthlySalary, 0),
    [liveTeachers],
  )

  const save = () => {
    setSaving(true)
    saveSalaryRates(rates)
      .then(() => load(month))
      .catch((e) => alert(e?.response?.data?.message ?? 'Saqlashda xatolik'))
      .finally(() => setSaving(false))
  }

  const toggleSelect = (id: string) =>
    setSelected((s) => {
      const n = new Set(s)
      if (n.has(id)) n.delete(id)
      else n.add(id)
      return n
    })

  const allSelected = !!data && data.teachers.length > 0 && selected.size === data.teachers.length
  const toggleSelectAll = () =>
    setSelected(() => (allSelected ? new Set() : new Set(data?.teachers.map((t) => t.id))))

  const assignBonus = () => {
    setAssigning(true)
    setBonusBulk([...selected], assignPct)
      .then(() => {
        setBonusModal(false)
        load(month)
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Ustamani tayinlashda xatolik'))
      .finally(() => setAssigning(false))
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Oylik hisoblash</h1>
          <p className="text-sm text-slate-400">
            Oylik maosh = (oydagi haqiqiy darslar − kelmagan kun darslari) × toifa soat narxi (+ ustama).
            Darslar har oy jadval bo'yicha haqiqiy sanaladi — hafta kunlari soniga qarab oydan-oyga farq qiladi.
          </p>
        </div>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <span className="font-medium">Oy:</span>
          <input
            type="month"
            value={month}
            onChange={(e) => setMonth(e.target.value)}
            className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
          />
        </label>
      </div>

      {/* Toifa soat narxlari */}
      <Card>
        <div className="mb-4 flex items-center gap-2">
          <Clock className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">Bir soat dars narxi (toifa bo'yicha)</h2>
        </div>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {rateFields.map((f) => (
            <label key={f.key} className="block">
              <span className="mb-1 block text-sm font-medium text-slate-600">{f.label}</span>
              <div className="relative">
                <input
                  type="number"
                  min={0}
                  step={1000}
                  value={rates[f.key]}
                  onChange={(e) => setRates((r) => ({ ...r, [f.key]: Number(e.target.value) }))}
                  className="w-full rounded-lg border border-slate-200 px-3 py-2 pr-14 text-sm outline-none focus:border-brand-400"
                />
                <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-xs text-slate-400">
                  so'm
                </span>
              </div>
            </label>
          ))}
        </div>
        <div className="mt-4 flex justify-end">
          <Button onClick={save} disabled={saving}>
            <Save className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : 'Narxlarni saqlash'}
          </Button>
        </div>
      </Card>

      {/* O'qituvchilar bo'yicha hisob */}
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-2 px-5 py-4">
          <div className="flex items-center gap-2">
            <Wallet className="h-5 w-5 text-brand-600" />
            <h2 className="font-semibold text-slate-800">O'qituvchilar oyligi</h2>
          </div>
          {selected.size > 0 ? (
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium text-slate-500">{selected.size} ta tanlandi</span>
              <Button
                onClick={() => {
                  setAssignPct(50)
                  setBonusModal(true)
                }}
              >
                <Percent className="h-4 w-4" /> Ustama tayinlash
              </Button>
              <Button variant="secondary" onClick={() => setSelected(new Set())}>
                Bekor
              </Button>
            </div>
          ) : (
            <div className="text-right">
              <div className="text-xs text-slate-400">Jami oylik fond</div>
              <div className="font-semibold text-slate-800">{formatMoney(totalMonthly)}</div>
            </div>
          )}
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="w-10 px-4 py-3">
                  <input
                    type="checkbox"
                    checked={allSelected}
                    onChange={toggleSelectAll}
                    className="h-4 w-4 accent-brand-600"
                    title="Hammasini tanlash"
                  />
                </th>
                <th className="w-8 px-2 py-3">#</th>
                <th className="px-4 py-3">F.I.SH</th>
                <th className="px-4 py-3">Toifa</th>
                <th className="px-4 py-3 text-center">Haftalik dars</th>
                <th className="px-4 py-3 text-center">Oylik dars</th>
                <th className="px-4 py-3 text-center">Kelmagan</th>
                <th className="px-4 py-3 text-center">Ustama</th>
                <th className="px-4 py-3 text-right">Oylik maosh</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {liveTeachers.map((t, i) => (
                <tr
                  key={t.id}
                  onClick={() => setDetailId(t.id)}
                  className="cursor-pointer hover:bg-slate-50/60"
                  title="Tafsilotni ko'rish"
                >
                  <td className="px-4 py-3" onClick={(e) => e.stopPropagation()}>
                    <input
                      type="checkbox"
                      checked={selected.has(t.id)}
                      onChange={() => toggleSelect(t.id)}
                      className="h-4 w-4 accent-brand-600"
                    />
                  </td>
                  <td className="px-2 py-3 text-slate-400">{i + 1}</td>
                  <td className="px-4 py-3 font-medium text-slate-800">{t.fullName}</td>
                  <td className="px-4 py-3">
                    {t.category ? (
                      <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                        {teacherCategoryLabel(t.category)}
                      </span>
                    ) : (
                      <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                        Toifasiz
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center text-slate-600">{t.weeklyLessons}</td>
                  <td className="px-4 py-3 text-center text-slate-600">{t.monthlyLessons}</td>
                  <td className="px-4 py-3 text-center">
                    {t.missedLessons > 0 ? (
                      <span className="font-medium text-red-600">−{t.missedLessons}</span>
                    ) : (
                      <span className="text-slate-300">0</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-center">
                    {t.bonusPct > 0 ? (
                      <span className="rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                        +{t.bonusPct}%
                      </span>
                    ) : (
                      <span className="text-slate-300">—</span>
                    )}
                  </td>
                  <td
                    className={cn(
                      'px-4 py-3 text-right font-semibold',
                      t.monthlySalary > 0 ? 'text-slate-800' : 'text-slate-300',
                    )}
                  >
                    {t.monthlySalary > 0 ? formatMoney(t.monthlySalary) : '—'}
                  </td>
                </tr>
              ))}
              {liveTeachers.length === 0 && (
                <tr>
                  <td colSpan={9} className="px-4 py-12 text-center text-slate-400">
                    O'qituvchi yo'q
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
        <p className="px-5 py-3 text-xs text-slate-400">
          Qatorni bosing — oylik tafsiloti (qancha, qancha/qachon kelmagan, qoldiq). Ustama berish uchun —
          chap tarafdagi katakchadan o'qituvchilarni tanlab, yuqoridagi "Ustama tayinlash" tugmasini bosing.
        </p>
      </Card>

      {/* Ustama tayinlash modali */}
      <Modal
        open={bonusModal}
        onClose={() => setBonusModal(false)}
        title="Ustama tayinlash"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setBonusModal(false)}>
              Bekor qilish
            </Button>
            <Button onClick={assignBonus} disabled={assigning}>
              {assigning ? 'Saqlanmoqda...' : 'Tayinlash'}
            </Button>
          </>
        }
      >
        <p className="mb-3 text-sm text-slate-500">
          <b className="text-slate-700">{selected.size} ta o'qituvchiga</b> ustama foizi tayinlanadi —
          har oy maoshiga shu foiz qo'shib boriladi.
        </p>
        <label className="block">
          <span className="mb-1 block text-sm font-medium text-slate-600">Ustama foizi (%)</span>
          <input
            type="number"
            min={0}
            max={1000}
            value={assignPct}
            onChange={(e) => setAssignPct(Number(e.target.value))}
            autoFocus
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400"
          />
        </label>
        <p className="mt-2 text-xs text-slate-400">
          Masalan 50 → har oy maoshga +50%. 0 kiritsangiz — tanlanganlardan ustama olib tashlanadi.
        </p>
      </Modal>

      <TeacherSalaryDetailModal teacherId={detailId} month={month} onClose={() => setDetailId(null)} />
    </div>
  )
}
