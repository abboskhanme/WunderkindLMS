import { useEffect, useMemo, useState } from 'react'
import type { LucideIcon } from 'lucide-react'
import {
  BookOpen, Cake, CalendarCheck, CalendarPlus, ClipboardCheck, GraduationCap,
  IdCard, KeyRound, Languages, MapPin, Phone, ShieldAlert, User, Users,
} from 'lucide-react'
import {
  Area, AreaChart, Bar, BarChart, Cell, CartesianGrid, Legend,
  PolarAngleAxis, PolarGrid, PolarRadiusAxis, Radar, RadarChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import type { StudentNotebook } from '@/api/services/studentNotebook'
import type { StudentCard } from '@/api/services/studentProfile'
import { genderLabels } from '@/config/constants'
import { cn, formatDate } from '@/lib/utils'
import { StatCard } from '@/components/ui/StatCard'
import { ProfileEmpty, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Umumiy" tab'i — docs/modules/students-parity.md §2.3 (S-10:
 * *Umumiy* — bugungi analitika, O'ZGARMAGAN holda).
 *
 * Bu fayl ilgari `StudentDetailPage.tsx` ichida turgan bo'limlarning AYNAN
 * o'zi: shaxsiy ma'lumot, stat kartalar, davomat va uy vazifa diagrammalari,
 * fan baholari dinamikasi, baholar matritsasi, davomat sabablari, oylik
 * feedback va topshiriqlar ballari. Hisob-kitobda birorta o'zgarish yo'q —
 * faqat tab'ga ko'chdi.
 */

const quarters = ['1', '2', '3', '4']
const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
const monthLabel = (m: string) =>
  m && m.length >= 7 ? `${uzMonths[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}` : m

const evalColors = ['#1f47f5', '#16a34a', '#f59e0b', '#dc2626', '#7c3aed', '#0891b2', '#db2777', '#65a30d']
// Har fan uchun alohida rang (statistika uslubidagi rangli nuqtalar/legend uchun)
const dynColors = [
  '#3b82f6', '#f59e0b', '#34d399', '#f472b6', '#a78bfa', '#22d3ee', '#fb7185', '#a3e635',
  '#ef4444', '#14b8a6', '#eab308', '#8b5cf6',
]
const gridStroke = '#eef0f4'
const axisTick = { fontSize: 12, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0' }

/**
 * O'quvchining o'qish tili (§2.3, S-8). Sinf tili (`languageLabels`) faqat
 * uz/ru dan iborat, o'quvchiniki esa kengroq — shuning uchun alohida ro'yxat.
 */
const studentLanguages: Record<string, string> = {
  uz: "O'zbek",
  ru: 'Rus',
  en: 'Ingliz',
  kaa: 'Qoraqalpoq',
}

function gradeCls(g: number): string {
  return cn(
    'inline-flex h-6 min-w-6 items-center justify-center rounded px-1 text-sm font-semibold',
    g >= 5 ? 'bg-emerald-50 text-emerald-700' : g >= 4 ? 'bg-brand-50 text-brand-700' : g >= 3 ? 'bg-amber-50 text-amber-700' : 'bg-red-50 text-red-600',
  )
}

function InfoRow({
  icon: Icon,
  label,
  value,
}: {
  icon: LucideIcon
  label: string
  value: string
}) {
  return (
    <div className="flex items-start gap-2.5">
      <Icon className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
      <div className="min-w-0">
        <p className="text-xs text-slate-400">{label}</p>
        <p className="break-words text-sm font-medium text-slate-700">{value}</p>
      </div>
    </div>
  )
}

interface Props {
  data: StudentNotebook
  /** Kartochka ma'lumoti — telefon, til, login, hujjat. Kelmagan bo'lsa qatorlar chiqmaydi. */
  card: StudentCard | null
}

export function OverviewTab({ data, card }: Props) {
  /** Oylik baholash jadvalida tanlangan oy ("YYYY-MM"). */
  const [evalMonth, setEvalMonth] = useState('')
  /** Fan baholari dinamikasida tanlangan chorak ("1".."4"). */
  const [gradeQuarter, setGradeQuarter] = useState('1')

  const attendanceChart = useMemo(
    () =>
      quarters.map((q) => ({
        name: `${q}-chorak`,
        Qoldirgan: data.attendance.missedLessons[q] ?? 0,
        'Kech keldi': data.attendance.lateCount[q] ?? 0,
      })),
    [data],
  )

  // Standart chorak — bahosi bor eng oxirgi chorak.
  const lastGradeQuarter = useMemo(() => {
    for (const q of [...quarters].reverse())
      if (data.subjects.some((s) => data.grades[s.id]?.[q] != null)) return q
    return '1'
  }, [data])
  // eslint-disable-next-line react-hooks/set-state-in-effect -- boshqa o'quvchi ochilganda standart chorakni qayta tanlaymiz (maqsadli)
  useEffect(() => setGradeQuarter(lastGradeQuarter), [lastGradeQuarter])

  // Tanlangan chorakda har fan o'rtacha bahosi (bar chart) — fan rangi barqaror (subjects tartibida).
  const quarterBars = useMemo(
    () =>
      data.subjects
        .map((s, idx) => ({ s, idx }))
        .filter(({ s }) => data.grades[s.id]?.[gradeQuarter] != null)
        .map(({ s, idx }) => ({
          name: s.name,
          baho: data.grades[s.id]?.[gradeQuarter] ?? 0,
          color: dynColors[idx % dynColors.length],
        })),
    [data, gradeQuarter],
  )

  // Oylik baholash — barcha oylar (fanlar bo'yicha) katalogi.
  const evalMonths = useMemo(() => {
    const set = new Set<string>()
    data.evaluationsBySubject.forEach((s) => s.evaluations.forEach((e) => set.add(e.month)))
    return [...set].sort()
  }, [data])

  // Feedback dinamikasi uchun tur nomlari (yozma, og'zaki/suhbat... — har biri alohida chiziq).
  const evalTypeNames = useMemo(() => data.evaluationTypes.map((t) => t.name), [data])

  // Feedback dinamikasi: har oy uchun HAR TUR (yozma, og'zaki/suhbat) bo'yicha o'rtacha — oyda bir
  // marta qo'yiladigan baholar (barcha fanlar bo'yicha o'rtacha), fanlar o'rtachasi EMAS.
  const typeDynamics = useMemo(
    () =>
      data.evaluations.map((e) => {
        const row: Record<string, string | number> = { name: monthLabel(e.month) }
        data.evaluationTypes.forEach((t) => {
          const v = e.grades[t.id]
          if (v != null) row[t.name] = v
        })
        return row
      }),
    [data],
  )

  // Tanlangan oy yo'q yoki ro'yxatda bo'lmasa — eng oxirgi oyni standart tanlaymiz.
  useEffect(() => {
    if (evalMonths.length === 0) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oylar katalogi kelganda oxirgi oyni tanlaymiz (maqsadli)
    setEvalMonth((prev) => (evalMonths.includes(prev) ? prev : evalMonths[evalMonths.length - 1]))
  }, [evalMonths])

  // Turlar bo'yicha UMUMIY o'rtacha — har bir baholash turi uchun barcha fan va oylar bo'yicha o'rtacha (radar uchun).
  const evalTypeAvg = useMemo(
    () =>
      data.evaluationTypes.map((t) => {
        const vals: number[] = []
        data.evaluationsBySubject.forEach((s) =>
          s.evaluations.forEach((e) => {
            const v = e.grades[t.id]
            if (v != null) vals.push(v)
          }),
        )
        const avg = vals.length ? Math.round((vals.reduce((a, b) => a + b, 0) / vals.length) * 10) / 10 : 0
        return { type: t.name, avg }
      }),
    [data],
  )

  const marksChart = useMemo(
    () =>
      data.marksTrend.map((m) => ({
        name: `${m.quarter}-chorak`,
        'Uy vazifa ✓': m.homeworkDone,
        'Uy vazifa ✗': m.homeworkMissed,
        'Xulq ✓': m.behaviorGood,
        'Xulq ✗': m.behaviorBad,
      })),
    [data],
  )

  return (
    <div className="space-y-6">
      {/* Shaxsiy ma'lumotlar */}
      <ProfileSection title="Shaxsiy ma'lumotlar" icon={User}>
        <div className="grid gap-x-6 gap-y-4 sm:grid-cols-2 lg:grid-cols-3">
          <InfoRow icon={User} label="Jinsi" value={genderLabels[data.gender as 'male' | 'female'] ?? data.gender} />
          <InfoRow icon={Cake} label="Tug'ilgan kun" value={data.birthDate ? formatDate(data.birthDate) : '—'} />
          <InfoRow icon={CalendarPlus} label="Qabul sanasi" value={data.enrollmentDate ? formatDate(data.enrollmentDate) : '—'} />
          <InfoRow icon={MapPin} label="Manzil" value={data.address || '—'} />
          <InfoRow icon={Users} label="Guruh" value={data.subGroup === 0 ? 'Butun sinf' : `${data.subGroup}-guruh`} />
          <InfoRow icon={GraduationCap} label="Sinf rahbari" value={data.homeroomTeacher || '—'} />
          <InfoRow icon={User} label="Ota-ona" value={data.parentFullName || '—'} />
          <InfoRow icon={Phone} label="Ota-ona telefoni" value={data.parentPhone || '—'} />
          {/*
            §2.3 (S-8) — o'quvchining O'Z telefoni, o'qish tili va logini.
            Kartochka so'rovidan keladi; eski javobda bo'lmasa qator chiqmaydi.
          */}
          {card && <InfoRow icon={Phone} label="O'quvchi telefoni" value={card.phone || '—'} />}
          {card && (
            <InfoRow
              icon={Languages}
              label="O'qish tili"
              value={card.language ? studentLanguages[card.language] ?? card.language : '—'}
            />
          )}
          {card && <InfoRow icon={KeyRound} label="Login" value={card.login || '—'} />}
          {/*
            Chegirma qatori BU YERDAN OLIB TASHLANDI (P1-21). Sabab: chegirma
            endi TOIFAGA bog'liq (o'qish alohida, avtobus alohida) va direktor
            tasdig'idan o'tadi. Bitta "20%" yozuvi qaysi toifa ekanini
            aytmasdi. Toifa kesimidagi haqiqiy holat "Moliya" tab'ida —
            hisob-fakturadagi `discount` ustuni bilan.
          */}
        </div>
        {(data.photoUrl || data.parentPassportUrl || card?.documentUrl) && (
          <div className="mt-4 flex flex-wrap gap-4 border-t border-slate-100 pt-4">
            {data.photoUrl && (
              <a href={data.photoUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline">
                <IdCard className="h-4 w-4" /> O'quvchi hujjati / surati
              </a>
            )}
            {card?.documentUrl && (
              <a href={card.documentUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline">
                <IdCard className="h-4 w-4" /> Hujjat nusxasi
              </a>
            )}
            {data.parentPassportUrl && (
              <a href={data.parentPassportUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline">
                <IdCard className="h-4 w-4" /> Ota-ona passporti
              </a>
            )}
          </div>
        )}
      </ProfileSection>

      {/* Stat kartalar */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="O'rtacha baho"
          value={data.avgGrade || '—'}
          icon={GraduationCap}
          iconBg="bg-brand-50"
          iconColor="text-brand-600"
        />
        <StatCard
          label="Davomat"
          value={data.conducted > 0 ? `${data.attendancePct}%` : '—'}
          hint={data.conducted > 0 ? `${data.attended} / ${data.conducted} dars` : undefined}
          icon={CalendarCheck}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
        <StatCard
          label="Intizomiy ball"
          value={data.disciplineScore}
          hint={`+${data.disciplinePlus} / −${data.disciplineMinus}`}
          icon={ShieldAlert}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
        />
        <StatCard
          label="Topshiriqlar"
          value={`${data.assignments.gradedCount}/${data.assignments.count}`}
          hint={data.assignments.totalMax > 0 ? `${data.assignments.totalScore}/${data.assignments.totalMax} ball` : undefined}
          icon={ClipboardCheck}
          iconBg="bg-indigo-50"
          iconColor="text-indigo-600"
        />
      </div>

      {/* Diagrammalar */}
      <div className="grid gap-6 lg:grid-cols-2">
        <ProfileSection title="Davomat (chorak bo'yicha)" icon={CalendarCheck}>
          <ResponsiveContainer width="100%" height={280}>
            <BarChart data={attendanceChart} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
              <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
              <YAxis allowDecimals={false} tickLine={false} axisLine={false} tick={axisTick} />
              <Tooltip contentStyle={tooltipStyle} />
              <Legend />
              <Bar dataKey="Qoldirgan" fill="#dc2626" radius={[6, 6, 0, 0]} />
              <Bar dataKey="Kech keldi" fill="#f59e0b" radius={[6, 6, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </ProfileSection>

        {marksChart.length > 0 && (
          <ProfileSection title="Uy vazifa va xulq (choraklik)" icon={BookOpen}>
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={marksChart} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
                <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                <YAxis allowDecimals={false} tickLine={false} axisLine={false} tick={axisTick} />
                <Tooltip contentStyle={tooltipStyle} />
                <Legend />
                <Bar dataKey="Uy vazifa ✓" fill="#16a34a" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Uy vazifa ✗" fill="#dc2626" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Xulq ✓" fill="#1f47f5" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Xulq ✗" fill="#f59e0b" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </ProfileSection>
        )}
      </div>

      {/* Fan baholari dinamikasi — chorak tanlanadi, har fan o'rtacha bahosi bar chartda */}
      {data.subjects.length > 0 && (
        <ProfileSection title="Fan baholari dinamikasi (chorak bo'yicha)" icon={GraduationCap}>
          <div className="mb-4 flex flex-wrap gap-1.5">
            {quarters.map((q) => (
              <button
                key={q}
                type="button"
                onClick={() => setGradeQuarter(q)}
                className={cn(
                  'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                  gradeQuarter === q
                    ? 'bg-brand-600 text-white'
                    : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
                )}
              >
                {q}-chorak
              </button>
            ))}
          </div>

          {quarterBars.length === 0 ? (
            <ProfileEmpty>Bu chorakda baho yo'q</ProfileEmpty>
          ) : (
            <ResponsiveContainer width="100%" height={340}>
              <BarChart data={quarterBars} margin={{ top: 16, right: 20, left: 8, bottom: 8 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
                <XAxis
                  dataKey="name"
                  tickLine={false}
                  axisLine={false}
                  tick={axisTick}
                  interval={0}
                  angle={-20}
                  textAnchor="end"
                  height={70}
                />
                <YAxis domain={[0, 5]} ticks={[1, 2, 3, 4, 5]} tickLine={false} axisLine={false} width={28} tick={axisTick} />
                <Tooltip contentStyle={tooltipStyle} cursor={{ fill: 'rgba(0,0,0,0.04)' }} />
                <Bar dataKey="baho" name="O'rtacha baho" radius={[6, 6, 0, 0]} maxBarSize={52}>
                  {quarterBars.map((b) => (
                    <Cell key={b.name} fill={b.color} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          )}
        </ProfileSection>
      )}

      {/* Baholar matritsasi (fan × chorak) */}
      <ProfileSection title="Baholar (fan × chorak)" icon={GraduationCap}>
        {data.subjects.length === 0 ? (
          <ProfileEmpty>Fan yo'q</ProfileEmpty>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Fan</th>
                  {quarters.map((q) => (
                    <th key={q} className="px-3 py-2 text-center">{q}-chorak</th>
                  ))}
                  <th className="px-3 py-2 text-center">O'rtacha</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.subjects.map((s) => {
                  const byQ = data.grades[s.id] ?? {}
                  const vals = Object.values(byQ)
                  const avg = vals.length ? Math.round((vals.reduce((a, b) => a + b, 0) / vals.length) * 10) / 10 : null
                  return (
                    <tr key={s.id} className="hover:bg-slate-50/60">
                      <td className="px-3 py-2 font-medium text-slate-700">{s.name}</td>
                      {quarters.map((q) => (
                        <td key={q} className="px-3 py-2 text-center">
                          {byQ[q] != null ? <span className={gradeCls(byQ[q])}>{byQ[q]}</span> : <span className="text-slate-300">—</span>}
                        </td>
                      ))}
                      <td className="px-3 py-2 text-center font-semibold text-slate-800">{avg ?? '—'}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </ProfileSection>

      {/* Davomat sabablari */}
      <ProfileSection title="Davomat sabablari" icon={CalendarCheck}>
        {data.reasons.length === 0 ? (
          <ProfileEmpty>Davomat belgilari yo'q</ProfileEmpty>
        ) : (
          <div className="flex flex-wrap gap-2">
            {data.reasons.map((r) => (
              <span
                key={r.reasonId}
                className={cn(
                  'inline-flex items-center gap-1 rounded-md px-2 py-1 text-sm font-medium',
                  r.isLate ? 'bg-amber-50 text-amber-700' : 'bg-red-50 text-red-600',
                )}
              >
                {r.name} <span className="font-semibold">×{r.count}</span>
              </span>
            ))}
          </div>
        )}
      </ProfileSection>

      {/* Oylik baholash */}
      {data.evaluationTypes.length > 0 && (
        <ProfileSection title="Oylik feedback" icon={ClipboardCheck}>
          {data.evaluationsBySubject.length === 0 ? (
            <ProfileEmpty>Hali baholanmagan</ProfileEmpty>
          ) : (
            <>
              <div className="grid gap-5 lg:grid-cols-5">
                {/* Feedback dinamikasi — turlar bo'yicha (yozma, og'zaki/suhbat...), oylar bo'yicha */}
                <div className="rounded-2xl border border-slate-100 bg-slate-50/40 p-4 lg:col-span-3">
                  <p className="mb-3 text-sm font-medium text-slate-600">
                    Feedback dinamikasi (turlar bo'yicha)
                  </p>
                  <ResponsiveContainer width="100%" height={300}>
                    <AreaChart data={typeDynamics} margin={{ top: 10, right: 12, left: -18, bottom: 0 }}>
                      <defs>
                        {evalTypeNames.map((name, i) => {
                          const c = evalColors[i % evalColors.length]
                          return (
                            <linearGradient key={name} id={`evalArea${i}`} x1="0" y1="0" x2="0" y2="1">
                              <stop offset="5%" stopColor={c} stopOpacity={0.3} />
                              <stop offset="95%" stopColor={c} stopOpacity={0.03} />
                            </linearGradient>
                          )
                        })}
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
                      <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                      <YAxis domain={[0, 5]} ticks={[1, 2, 3, 4, 5]} tickLine={false} axisLine={false} tick={axisTick} />
                      <Tooltip contentStyle={tooltipStyle} />
                      <Legend />
                      {evalTypeNames.map((name, i) => (
                        <Area
                          key={name}
                          type="monotone"
                          dataKey={name}
                          stroke={evalColors[i % evalColors.length]}
                          strokeWidth={2.5}
                          fill={`url(#evalArea${i})`}
                          fillOpacity={1}
                          dot={false}
                          activeDot={{ r: 4 }}
                          connectNulls
                        />
                      ))}
                    </AreaChart>
                  </ResponsiveContainer>
                </div>

                {/* Turlar bo'yicha UMUMIY o'rtacha — radar (≥3 tur), aks holda gorizontal bar */}
                <div className="rounded-2xl border border-slate-100 bg-slate-50/40 p-4 lg:col-span-2">
                  <p className="mb-3 text-sm font-medium text-slate-600">Turlar bo'yicha umumiy o'rtacha</p>
                  <ResponsiveContainer width="100%" height={300}>
                    {evalTypeAvg.length >= 3 ? (
                      <RadarChart data={evalTypeAvg} outerRadius="72%">
                        <defs>
                          <radialGradient id="evalRadarFill">
                            <stop offset="0%" stopColor="#7c3aed" stopOpacity={0.55} />
                            <stop offset="100%" stopColor="#7c3aed" stopOpacity={0.12} />
                          </radialGradient>
                        </defs>
                        <PolarGrid stroke={gridStroke} />
                        <PolarAngleAxis dataKey="type" tick={{ fontSize: 11, fill: '#64748b' }} />
                        <PolarRadiusAxis domain={[0, 5]} tick={false} axisLine={false} />
                        <Radar dataKey="avg" name="O'rtacha" stroke="#7c3aed" strokeWidth={2} fill="url(#evalRadarFill)" />
                        <Tooltip contentStyle={tooltipStyle} />
                      </RadarChart>
                    ) : (
                      <BarChart layout="vertical" data={evalTypeAvg} margin={{ top: 6, right: 18, left: 6, bottom: 6 }}>
                        <CartesianGrid strokeDasharray="3 3" horizontal={false} stroke={gridStroke} />
                        <XAxis type="number" domain={[0, 5]} ticks={[1, 2, 3, 4, 5]} tickLine={false} axisLine={false} tick={axisTick} />
                        <YAxis type="category" dataKey="type" width={90} tickLine={false} axisLine={false} tick={{ fontSize: 11, fill: '#64748b' }} />
                        <Tooltip contentStyle={tooltipStyle} />
                        <Bar dataKey="avg" name="O'rtacha" radius={[0, 6, 6, 0]} barSize={22}>
                          {evalTypeAvg.map((item, i) => (
                            <Cell key={item.type} fill={evalColors[i % evalColors.length]} />
                          ))}
                        </Bar>
                      </BarChart>
                    )}
                  </ResponsiveContainer>
                </div>
              </div>

              {/* Oy tanlovi — faqat oy o'zgaradi; pastda shu oydagi BARCHA fanlar natijasi */}
              <div className="mt-6 flex flex-wrap items-center gap-2">
                <span className="text-sm font-medium text-slate-600">Oy:</span>
                <select
                  value={evalMonth}
                  onChange={(e) => setEvalMonth(e.target.value)}
                  className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
                >
                  {evalMonths.map((m) => (
                    <option key={m} value={m}>
                      {monthLabel(m)}
                    </option>
                  ))}
                </select>
                <span className="text-xs text-slate-400">Tanlangan oydagi barcha fanlar</span>
              </div>
              <div className="mt-3 overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-3 py-2">Fan</th>
                      {data.evaluationTypes.map((t) => (
                        <th key={t.id} className="px-3 py-2 text-center">{t.name}</th>
                      ))}
                      <th className="px-3 py-2 text-center">O'rtacha</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {data.evaluationsBySubject.map((s) => {
                      const e = s.evaluations.find((x) => x.month === evalMonth)
                      return (
                        <tr key={s.subjectId || 'umumiy'} className="hover:bg-slate-50/60">
                          <td className="whitespace-nowrap px-3 py-2 font-medium text-slate-700">{s.subjectName}</td>
                          {data.evaluationTypes.map((t) => (
                            <td key={t.id} className="px-3 py-2 text-center">
                              {e && e.grades[t.id] != null ? (
                                <span className={gradeCls(e.grades[t.id])}>{e.grades[t.id]}</span>
                              ) : (
                                <span className="text-slate-300">—</span>
                              )}
                            </td>
                          ))}
                          <td className="px-3 py-2 text-center font-semibold text-slate-800">{e?.avg || '—'}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </ProfileSection>
      )}

      {/* Topshiriqlar */}
      <ProfileSection title="Topshiriqlar ballari" icon={ClipboardCheck}>
        {data.assignments.items.length === 0 ? (
          <ProfileEmpty>Topshiriq yo'q</ProfileEmpty>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Topshiriq</th>
                  <th className="px-3 py-2">Fan</th>
                  <th className="px-3 py-2 text-center">Holat</th>
                  <th className="px-3 py-2 text-center">Ball</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.assignments.items.map((a) => (
                  <tr key={a.assignmentId} className="hover:bg-slate-50/60">
                    <td className="px-3 py-2 font-medium text-slate-700">{a.title}</td>
                    <td className="px-3 py-2 text-slate-500">{a.subjectName}</td>
                    <td className="px-3 py-2 text-center">
                      {a.completed ? (
                        <span className="rounded bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">Bajardi</span>
                      ) : (
                        <span className="rounded bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-center font-semibold text-slate-800">
                      {a.score != null ? `${a.score}/${a.maxScore}` : <span className="text-slate-300">—</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </ProfileSection>
    </div>
  )
}
