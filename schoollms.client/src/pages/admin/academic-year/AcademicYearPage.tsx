import { useEffect, useState } from 'react'
import { Download, GraduationCap, Check, AlertTriangle } from 'lucide-react'
import {
  getAcademicYearInfo,
  getYearArchives,
  downloadArchiveZip,
  rolloverYear,
  type AcademicYearInfo,
  type YearArchive,
  type RolloverResult,
} from '@/api/services/academicYear'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

const nextYearOf = (cur: string): string => {
  const m = cur.match(/^(\d{4})\/(\d{4})$/)
  if (m) return `${Number(m[1]) + 1}/${Number(m[2]) + 1}`
  const now = new Date()
  const start = now.getMonth() + 1 >= 9 ? now.getFullYear() : now.getFullYear() - 1
  return `${start + 1}/${start + 2}`
}

export function AcademicYearPage() {
  const [info, setInfo] = useState<AcademicYearInfo | null>(null)
  const [archives, setArchives] = useState<YearArchive[]>([])
  const [loading, setLoading] = useState(true)

  const [newYear, setNewYear] = useState('')
  const [promoteStudents, setPromoteStudents] = useState(true)
  const [clearGrades, setClearGrades] = useState(true)
  const [clearSchedule, setClearSchedule] = useState(true)
  const [clearQuarters, setClearQuarters] = useState(true)
  const [clearFinance, setClearFinance] = useState(true)

  const [confirmOpen, setConfirmOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<RolloverResult | null>(null)
  const [error, setError] = useState('')

  const load = () => {
    setLoading(true)
    Promise.all([getAcademicYearInfo(), getYearArchives()])
      .then(([i, a]) => {
        setInfo(i)
        setArchives(a)
        setNewYear((prev) => prev || nextYearOf(i.currentYear))
      })
      .finally(() => setLoading(false))
  }

  // eslint-disable-next-line react-hooks/set-state-in-effect -- boshlang'ich yuklash (maqsadli)
  useEffect(load, [])

  const submit = () => {
    setSubmitting(true)
    setError('')
    rolloverYear({ newYear: newYear.trim(), promoteStudents, clearGrades, clearSchedule, clearQuarters, clearFinance })
      .then((r) => {
        setResult(r)
        setConfirmOpen(false)
        load()
      })
      .catch((e) => setError(e?.response?.data?.message ?? "O'tishda xatolik yuz berdi"))
      .finally(() => setSubmitting(false))
  }

  const download = async (a: YearArchive) => {
    const blob = await downloadArchiveZip(a.id)
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${a.year.replace('/', '-')}-arxiv.zip`
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Yangi o'quv yiliga o'tish</h1>
        <p className="text-sm text-slate-400">
          Joriy yilni arxivlab, o'quvchilarni keyingi sinfga ko'tarib, yangi o'quv yilini boshlang
        </p>
      </div>

      {loading || !info ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {result && (
            <Card className="border-emerald-200 bg-emerald-50">
              <div className="flex items-center gap-2 text-emerald-700">
                <Check className="h-5 w-5" />
                <span className="font-medium">
                  Yangi o'quv yili boshlandi: {result.oldYear || '—'} → {result.newYear}. Ko'tarildi:{' '}
                  {result.promoted}, bitirdi: {result.graduated}.
                </span>
              </div>
            </Card>
          )}

          {/* Joriy holat */}
          <Card>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-sm text-slate-400">Joriy o'quv yili</p>
                <p className="text-2xl font-semibold text-slate-800">
                  {info.currentYear || 'Belgilanmagan'}
                </p>
              </div>
              <div className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm text-slate-600 sm:grid-cols-4">
                <Stat label="O'quvchilar" value={info.students} />
                <Stat label="Sinflar" value={info.classes} />
                <Stat label="Baholar" value={info.journalEntries} />
                <Stat label="Moliya amallari" value={info.financeTransactions} />
              </div>
            </div>
          </Card>

          {/* O'tish formasi */}
          <Card className="space-y-4">
            <h2 className="font-semibold text-slate-800">Yangi yilga o'tish</h2>
            <div className="max-w-xs">
              <Input
                label="Yangi o'quv yili"
                placeholder="2026/2027"
                value={newYear}
                onChange={(e) => setNewYear(e.target.value)}
              />
            </div>

            <div className="space-y-2">
              <Toggle
                checked={promoteStudents}
                onChange={setPromoteStudents}
                label="O'quvchilarni keyingi sinfga ko'tarish"
                hint="Har sinf bir pog'ona oshadi (1-A → 2-A). Oxirgi sinf (11) bitiruvchi bo'lib chiqariladi."
              />
              <Toggle
                checked={clearGrades}
                onChange={setClearGrades}
                label="Baholar va jurnalni tozalash"
                hint="Barcha baholar va davomat yozuvlari o'chiriladi (arxivda saqlanadi)."
              />
              <Toggle
                checked={clearSchedule}
                onChange={setClearSchedule}
                label="Dars jadvalini tozalash"
                hint="Haftalik jadval biriktirishlari o'chiriladi (jadval shablonlari qoladi)."
              />
              <Toggle
                checked={clearQuarters}
                onChange={setClearQuarters}
                label="Choraklar sanasini tozalash"
                hint="Chorak sanalari tozalanadi — yangi yil sanalarini kiritasiz."
              />
              <Toggle
                checked={clearFinance}
                onChange={setClearFinance}
                label="Moliyani tozalash (to'lov / qarz / balans)"
                hint="Moliyaviy amallar, oylik hisoblar va balanslar nolga tushadi (arxivda saqlanadi). Belgilanmasa — qarzlar yangi yilga o'tadi."
                danger
              />
            </div>

            <div>
              <Button onClick={() => setConfirmOpen(true)} disabled={!newYear.trim()}>
                <GraduationCap className="h-4 w-4" /> Yangi yilga o'tish
              </Button>
            </div>
          </Card>

          {/* Arxivlar */}
          <Card className="p-0">
            <div className="border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">Arxivlangan o'quv yillari</h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">O'quv yili</th>
                    <th className="px-4 py-3">Arxivlangan sana</th>
                    <th className="px-4 py-3 text-right">O'quvchilar</th>
                    <th className="px-4 py-3 text-right">Sinflar</th>
                    <th className="px-4 py-3 text-right">Baholar</th>
                    <th className="px-4 py-3 text-right">Yuklab olish</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {archives.map((a) => (
                    <tr key={a.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 font-medium text-slate-800">{a.year}</td>
                      <td className="px-4 py-3 text-slate-600">{formatDate(a.createdAt.slice(0, 10))}</td>
                      <td className="px-4 py-3 text-right text-slate-600">{a.studentsCount}</td>
                      <td className="px-4 py-3 text-right text-slate-600">{a.classesCount}</td>
                      <td className="px-4 py-3 text-right text-slate-600">{a.journalCount}</td>
                      <td className="px-4 py-3 text-right">
                        <button
                          type="button"
                          title="ZIP yuklab olish (barcha ma'lumot)"
                          onClick={() => download(a)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                        >
                          <Download className="h-4 w-4" />
                        </button>
                      </td>
                    </tr>
                  ))}
                  {archives.length === 0 && (
                    <tr>
                      <td colSpan={6} className="px-4 py-10 text-center text-slate-400">
                        Hali arxiv yo'q
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}

      <Modal
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        title="Yangi o'quv yiliga o'tishni tasdiqlang"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setConfirmOpen(false)}>
              Bekor qilish
            </Button>
            <Button onClick={submit} disabled={submitting}>
              {submitting ? 'Bajarilmoqda...' : 'Ha, o‘tkazish'}
            </Button>
          </>
        }
      >
        <div className="space-y-3 text-sm text-slate-600">
          <div className="flex items-start gap-2 rounded-lg bg-amber-50 px-3 py-2 text-amber-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              Bu amal qaytarib bo'lmaydi. Joriy yil ({info?.currentYear || '—'}) to'liq arxivlanadi,
              so'ng tanlangan ma'lumotlar tozalanadi.
            </span>
          </div>
          <ul className="list-disc space-y-1 pl-5">
            <li>
              <b>{info?.currentYear || '—'}</b> → <b>{newYear}</b>
            </li>
            {promoteStudents && <li>O'quvchilar keyingi sinfga ko'tariladi</li>}
            {clearGrades && <li>Baholar/jurnal tozalanadi</li>}
            {clearSchedule && <li>Dars jadvali tozalanadi</li>}
            {clearQuarters && <li>Choraklar sanasi tozalanadi</li>}
            {clearFinance && <li className="text-red-600">Moliya/balanslar tozalanadi</li>}
          </ul>
          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-600">{error}</p>}
        </div>
      </Modal>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <span className="text-slate-400">{label}: </span>
      <span className="font-semibold text-slate-700">{value}</span>
    </div>
  )
}

function Toggle({
  checked,
  onChange,
  label,
  hint,
  danger,
}: {
  checked: boolean
  onChange: (v: boolean) => void
  label: string
  hint: string
  danger?: boolean
}) {
  return (
    <label
      className={cn(
        'flex cursor-pointer items-start gap-3 rounded-lg border p-3 transition-colors',
        checked ? (danger ? 'border-red-200 bg-red-50/50' : 'border-brand-200 bg-brand-50/50') : 'border-slate-200',
      )}
    >
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className={cn('mt-0.5 h-4 w-4', danger ? 'accent-red-600' : 'accent-brand-600')}
      />
      <span>
        <span className="block text-sm font-medium text-slate-700">{label}</span>
        <span className="block text-xs text-slate-400">{hint}</span>
      </span>
    </label>
  )
}
