import { Check } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { Wallet } from 'lucide-react'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const MONTH_ABBR = {
  '01': 'Yan', '02': 'Fev', '03': 'Mar', '04': 'Apr', '05': 'May', '06': 'Iyun',
  '07': 'Iyul', '08': 'Avg', '09': 'Sen', '10': 'Okt', '11': 'Noy', '12': 'Dek',
}
const STATUS = {
  paid: ["To'liq", '#10B981'],
  partial: ['Qisman', '#F59E0B'],
  unpaid: ["Yo'q", '#EF4444'],
}
const ml = (v) => (Number(v || 0) / 1_000_000).toFixed(1)
const monthLabel = (ym) => (ym ? MONTH_ABBR[String(ym).split('-')[1]] || ym : '—')

// Salary — gradient hero card, overall stats, monthly bar chart, payments.
export default function SalaryScreen({ onBack }) {
  const q = useFetch(() => api.salary(), [])

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader title="Maosh" subtitle="O'quv yili" onBack={onBack} />
      <AsyncView query={q} loadingLabel="Maosh yuklanmoqda…">
        <SalaryBody d={q.data} />
      </AsyncView>
    </div>
  )
}

function SalaryBody({ d }) {
  if (!d || !d.months || d.months.length === 0) {
    return (
      <EmptyState
        icon={<EmptyIllustration><Wallet size={30} /></EmptyIllustration>}
        title="Maosh ma'lumoti yo'q"
        subtitle="Hozircha hisoblangan oylik mavjud emas"
      />
    )
  }

  const months = d.months
  const cur = months[months.length - 1]
  const paidPct = cur.expected > 0 ? cur.paid / cur.expected : 0
  const overallPct = d.totalExpected > 0 ? Math.round((d.totalPaid / d.totalExpected) * 100) : 0
  const payments = d.payments || []

  return (
    <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6 space-y-3.5">
      {/* Hero */}
      <div className="relative p-5 rounded-6xl overflow-hidden text-white" style={{ background: 'linear-gradient(135deg,#134E4A,#0F766E,#0D9488)' }}>
        <div className="absolute -top-8 -right-5 w-40 h-40 rounded-full bg-white/[0.06]" />
        <p className="relative text-[11px] font-bold tracking-wide text-white/80">JORIY OY · {monthLabel(cur.month).toUpperCase()}</p>
        <div className="relative flex items-baseline gap-1.5 mt-1.5">
          <span className="text-[32px] font-extrabold font-mono">{ml(cur.paid)}</span>
          <span className="text-[16px] font-semibold text-white/90">mln so'm</span>
        </div>
        <p className="relative text-[12px] text-white/85">{ml(cur.remaining)} mln so'm qoldi</p>
        <div className="relative mt-4 h-2 rounded bg-white/20 overflow-hidden">
          <div className="h-full rounded" style={{ width: `${paidPct * 100}%`, background: '#FFE48F' }} />
        </div>
        <div className="relative mt-4 flex gap-2.5">
          <HeroMini label="Maosh" value={ml(d.salary)} unit="mln so'm/oy" />
          <HeroMini label="To'langan" value={`${overallPct}%`} unit={`${months.length} oy`} />
          <HeroMini label="Qoldiq" value={ml(d.remaining)} unit="mln so'm" />
        </div>
      </div>

      {/* Stats */}
      <div className="p-4 rounded-4xl bg-surface border border-border">
        <p className="text-[13px] font-bold text-text mb-1.5">Umumiy statistika</p>
        <StatRow label="Jami hisoblangan" value={`${ml(d.totalExpected)} mln so'm`} />
        <StatRow label="Jami olingan" value={`${ml(d.totalPaid)} mln so'm`} color="#10B981" />
        <StatRow label="Qoldiq" value={`${ml(d.remaining)} mln so'm`} color="#F59E0B" />
        <StatRow label="To'langan ulushi" value={`${overallPct}%`} color="var(--primary)" />
        <StatRow label="To'lovlar soni" value={`${payments.length} ta`} last />
      </div>

      {/* Month chart */}
      <div className="p-4 rounded-4xl bg-surface border border-border">
        <p className="text-[13px] font-bold text-text">Oylar bo'yicha</p>
        <div className="mt-4 h-[152px] flex items-end">
          {months.map((m) => {
            const h = d.salary > 0 ? Math.min(1, m.paid / d.salary) : 0
            const [label, color] = STATUS[m.status] || ['—', '#94A3B8']
            const isPaid = m.status === 'paid'
            return (
              <div key={m.month} className="flex-1 flex flex-col items-center justify-end px-1.5">
                <span className="text-[11px] font-bold text-text font-mono">{ml(m.paid)}M</span>
                <div className="mt-1.5 w-8 h-20 rounded-lg bg-surface3 flex items-end overflow-hidden">
                  <div className="w-full rounded-lg" style={{ height: `${h * 100}%`, background: isPaid ? 'linear-gradient(to top,#0D9488,#2DD4BF)' : 'linear-gradient(to top,#D97706,#F59E0B)' }} />
                </div>
                <span className="mt-1.5 text-[11px] font-semibold text-muted">{monthLabel(m.month)}</span>
                <span className="mt-1 px-1.5 py-0.5 rounded text-[9px] font-extrabold tracking-wide" style={{ background: `${color}26`, color }}>
                  {label.toUpperCase()}
                </span>
              </div>
            )
          })}
        </div>
      </div>

      <p className="px-1 pt-1 text-[13px] font-bold text-text">To'lov tarixi</p>
      {payments.length === 0 ? (
        <div className="p-4 rounded-4xl bg-surface border border-border text-center text-[13px] text-muted">
          Hali to'lov amalga oshirilmagan
        </div>
      ) : (
        payments.map((p, i) => (
          <div key={`${p.month || p.date}-${i}`} className="p-3.5 rounded-4xl bg-surface border border-border flex items-center gap-3">
            <div className="w-[42px] h-[42px] rounded-xl bg-primary-soft flex items-center justify-center text-primary">
              <Check size={18} />
            </div>
            <div className="flex-1">
              <p className="text-[14px] font-bold text-text">{p.note || `${monthLabel(p.month)} oyligi`}</p>
              <p className="text-[11px] text-muted">{p.date}</p>
            </div>
            <span className="text-[15px] font-extrabold font-mono text-success">+{ml(p.amount)}M</span>
          </div>
        ))
      )}
    </div>
  )
}

function HeroMini({ label, value, unit }) {
  return (
    <div className="flex-1 p-2.5 rounded-xl bg-white/[0.12]">
      <p className="text-[10px] text-white/85">{label}</p>
      <p className="text-[15px] font-extrabold font-mono mt-1">{value}</p>
      <p className="text-[10px] text-white/70">{unit}</p>
    </div>
  )
}
function StatRow({ label, value, color = 'var(--text)', last }) {
  return (
    <div className={['flex items-center py-1.5', last ? '' : 'border-b border-border/50'].join(' ')}>
      <span className="flex-1 text-[13px] text-muted">{label}</span>
      <span className="text-[14px] font-extrabold font-mono" style={{ color }}>{value}</span>
    </div>
  )
}
