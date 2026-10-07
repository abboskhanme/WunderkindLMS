import { useCallback, useEffect, useMemo, useState } from 'react'
import { Gauge, Search, SlidersHorizontal, Smile, Users } from 'lucide-react'
import { getKpi, saveKpiWeights, type KpiReport, type KpiRow, type KpiWeights } from '@/api/services/kpi'
import { formatNps, npsTone } from '@/api/services/polls'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MonthPicker } from '@/components/ui/DatePicker'
import { Toast } from '@/components/ui/Toast'

const WEIGHT_LABELS: { key: keyof KpiWeights; label: string; hint: string }[] = [
  { key: 'tasks', label: 'HR topshiriqlari', hint: "Oy ichida muddati kelgan topshiriqlardan o'z vaqtida bajarilgani" },
  { key: 'lessonAnalysis', label: 'Dars tahlili', hint: "Yakunlangan dars tahlillarining o'rtacha bali" },
  { key: 'journal', label: 'Jurnal', hint: "Chorak bo'yicha reja darslardan o'tildi deb belgilangani" },
  { key: 'parentNps', label: 'Ota-onalar NPS', hint: "So'rovnomalarda ota-onalar ustozga bergan NPS" },
  { key: 'staffNps', label: 'Xodimlar NPS', hint: "So'rovnomalarda ustozlar rahbarga bergan NPS" },
  { key: 'penalties', label: 'Jarimalar', hint: '100 − har bir jarima uchun 20' },
  { key: 'attendance', label: 'Davomat', hint: "100 − har kelmagan kun uchun 10, kechikkan kun uchun 3" },
]

const HR_ROLE: Record<string, string> = { director: 'Direktor', manager: 'Rahbar', employee: 'Xodim' }

const currentMonth = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

function scoreTone(v: number | null): string {
  if (v === null) return 'bg-slate-50 text-slate-300'
  if (v >= 80) return 'bg-emerald-50 text-emerald-700'
  if (v >= 60) return 'bg-amber-50 text-amber-700'
  return 'bg-red-50 text-red-600'
}

/**
 * Xodimlar samaradorligi — KPI (client, 2026-10-07): one score per employee for a month, from HR
 * tasks, lesson analysis, the journal, parents' and staff NPS, fines and attendance.
 */
export function KpiPage() {
  const [month, setMonth] = useState(currentMonth())
  const [data, setData] = useState<KpiReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [weightsOpen, setWeightsOpen] = useState(false)
  const [error, setError] = useState('')
  const [toast, setToast] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    getKpi(month)
      .then(setData)
      .catch((e) => setError(hrErrorMessage(e, "KPI'ni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [month])
  // eslint-disable-next-line react-hooks/set-state-in-effect -- oy o'zgarganda qayta yuklash (maqsadli)
  useEffect(load, [load])

  const rows = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return (data?.rows ?? []).filter((r) => !needle || r.fullName.toLowerCase().includes(needle))
  }, [data, search])

  const scored = (data?.rows ?? []).filter((r) => r.total !== null)
  const average = scored.length ? scored.reduce((s, r) => s + (r.total ?? 0), 0) / scored.length : null

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Xodimlar samaradorligi (KPI)</h1>
          <p className="text-sm text-slate-400">
            Topshiriqlar, dars tahlili, jurnal, NPS, jarimalar va davomatdan — vaznlar bo'yicha 0–100 ball
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Xodim qidirish..."
              className="w-52 rounded-lg border border-slate-200 bg-white py-2 pl-8 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
          </div>
          <MonthPicker value={month} onChange={(v: string) => v && setMonth(v)} className="w-44" />
          <Button variant="secondary" onClick={() => setWeightsOpen(true)} disabled={!data}>
            <SlidersHorizontal className="h-4 w-4" /> Vaznlar
          </Button>
        </div>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {data && (
        <div className="grid gap-4 sm:grid-cols-3">
          <Summary
            icon={Gauge}
            label="O'rtacha KPI"
            value={average === null ? '—' : average.toFixed(1)}
            hint={`${scored.length} ta xodim baholandi`}
          />
          <Summary
            icon={Smile}
            label="Maktab NPS (ota-onalar)"
            value={formatNps(data.schoolNps)}
            tone={npsTone(data.schoolNps)}
            hint={`${data.schoolAnswers} ta javob`}
          />
          <Summary
            icon={Users}
            label="Rahbariyat NPS (ustozlar)"
            value={formatNps(data.managementNps)}
            tone={npsTone(data.managementNps)}
            hint={`${data.managementAnswers} ta javob`}
          />
        </div>
      )}

      {loading && !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Xodim topilmadi</p>
        </Card>
      ) : (
        <Card className="overflow-x-auto p-0">
          <table className="w-full min-w-[1000px] text-sm">
            <thead>
              <tr className="border-b border-slate-100 text-left text-xs font-medium uppercase tracking-wide text-slate-400">
                <th className="px-4 py-3">Xodim</th>
                <th className="px-3 py-3 text-center">KPI</th>
                <th className="px-3 py-3 text-center">Topshiriqlar</th>
                <th className="px-3 py-3 text-center">Dars tahlili</th>
                <th className="px-3 py-3 text-center">Jurnal</th>
                <th className="px-3 py-3 text-center">Ota-ona NPS</th>
                <th className="px-3 py-3 text-center">Xodim NPS</th>
                <th className="px-3 py-3 text-center">Jarima</th>
                <th className="px-3 py-3 text-center">Davomat</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <Row key={r.employeeId} r={r} />
              ))}
            </tbody>
          </table>
        </Card>
      )}

      {weightsOpen && data && (
        <WeightsModal
          weights={data.weights}
          onClose={() => setWeightsOpen(false)}
          onSaved={() => {
            setWeightsOpen(false)
            setToast('Vaznlar saqlandi')
            load()
          }}
        />
      )}

      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}

function Summary({
  icon: Icon,
  label,
  value,
  hint,
  tone = 'text-slate-800',
}: {
  icon: typeof Gauge
  label: string
  value: string
  hint: string
  tone?: string
}) {
  return (
    <Card className="flex items-center gap-4">
      <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
        <Icon className="h-5 w-5" />
      </div>
      <div>
        <p className="text-xs font-medium text-slate-400">{label}</p>
        <p className={'text-2xl font-bold ' + tone}>{value}</p>
        <p className="text-xs text-slate-400">{hint}</p>
      </div>
    </Card>
  )
}

function Cell({ value, hint }: { value: number | null; hint?: string }) {
  return (
    <td className="px-3 py-2.5 text-center" title={hint}>
      <span className={'inline-block min-w-12 rounded-md px-2 py-0.5 text-xs font-semibold ' + scoreTone(value)}>
        {value === null ? '—' : Math.round(value)}
      </span>
      {hint && <div className="mt-0.5 text-[11px] text-slate-400">{hint}</div>}
    </td>
  )
}

function Row({ r }: { r: KpiRow }) {
  return (
    <tr className="border-b border-slate-50 last:border-0">
      <td className="px-4 py-2.5">
        <div className="font-medium text-slate-800">{r.fullName}</div>
        <div className="text-xs text-slate-400">{[r.position, HR_ROLE[r.hrRole]].filter(Boolean).join(' · ')}</div>
      </td>
      <td className="px-3 py-2.5 text-center">
        <span className={'inline-block min-w-14 rounded-lg px-2.5 py-1 text-sm font-bold ' + scoreTone(r.total)}>
          {r.total === null ? '—' : r.total.toFixed(1)}
        </span>
      </td>
      <Cell value={r.tasks} hint={r.tasksTotal ? `${r.tasksOnTime}/${r.tasksTotal}` : undefined} />
      <Cell value={r.lessonAnalysis} hint={r.observations ? `${r.observations} ta tahlil` : undefined} />
      <Cell value={r.journal} />
      <Cell value={r.parentNps} hint={r.parentAnswers ? `NPS ${formatNps(r.parentNpsRaw)} · ${r.parentAnswers}` : undefined} />
      <Cell value={r.staffNps} hint={r.staffAnswers ? `NPS ${formatNps(r.staffNpsRaw)} · ${r.staffAnswers}` : undefined} />
      <Cell value={r.penalties} hint={r.penaltyCount ? `${r.penaltyCount} ta` : undefined} />
      <Cell
        value={r.attendance}
        hint={r.absentDays || r.lateDays ? `${r.absentDays} kelmadi · ${r.lateDays} kechikdi` : undefined}
      />
    </tr>
  )
}

function WeightsModal({ weights, onClose, onSaved }: { weights: KpiWeights; onClose: () => void; onSaved: () => void }) {
  const [draft, setDraft] = useState<KpiWeights>(weights)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const sum = Object.values(draft).reduce((s, v) => s + (Number(v) || 0), 0)

  const save = async () => {
    setSaving(true)
    setError('')
    try {
      await saveKpiWeights(draft)
      onSaved()
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      size="md"
      onClose={onClose}
      title="KPI vaznlari"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || sum === 0}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-slate-500">
          Har bir ko'rsatkichning ulushi. Xodimda bali yo'q ko'rsatkich (masalan, ustoz bo'lmagan xodimda dars tahlili) hisobga
          olinmaydi — qolganlari shu vaznlar bilan o'rtachalanadi. 0 — umuman hisoblanmaydi. Jarima va davomat faqat
          tuzatadi: ish ko'rsatkichlaridan birortasi bo'lmasa, KPI chiqmaydi.
        </p>
        {WEIGHT_LABELS.map((w) => (
          <div key={w.key} className="flex items-center gap-3">
            <div className="min-w-0 flex-1">
              <div className="text-sm font-medium text-slate-700">{w.label}</div>
              <div className="text-xs text-slate-400">{w.hint}</div>
            </div>
            <input
              type="number"
              min={0}
              max={100}
              value={draft[w.key]}
              onChange={(e) => setDraft((d) => ({ ...d, [w.key]: Math.max(0, Math.min(100, Number(e.target.value) || 0)) }))}
              className="w-20 rounded-lg border border-slate-200 px-3 py-1.5 text-right text-sm outline-none focus:border-brand-400"
            />
            <span className="w-12 text-right text-xs text-slate-400">{sum ? Math.round((100 * draft[w.key]) / sum) : 0}%</span>
          </div>
        ))}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
