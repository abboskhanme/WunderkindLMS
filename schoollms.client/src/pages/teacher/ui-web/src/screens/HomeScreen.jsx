import { useState, useMemo } from 'react'
import { Bell, Calendar, ClipboardList, Clock, ArrowRight, ChevronRight } from 'lucide-react'
import Avatar from '../components/Avatar'
import SegmentedControl from '../components/SegmentedControl'
import { TapScale } from '../components/AppCard'
import { SectionLabel } from '../components/ui'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import { Loading, ErrorState } from '../components/State'

const DAYS = { 0: 'Dushanba', 1: 'Seshanba', 2: 'Chorshanba', 3: 'Payshanba', 4: 'Juma', 5: 'Shanba' }
const MONTHS = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr']

// Bugungi (Mon=0..Sat=5) weekday; JS getDay(): Sun=0..Sat=6 → (getDay()+6)%7.
const todayIdx = (new Date().getDay() + 6) % 7

function nowHHmm() {
  const d = new Date()
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

function todayLabel() {
  const d = new Date()
  return `${d.getDate()}-${MONTHS[d.getMonth()]}, ${DAYS[todayIdx]}`
}

// Dashboard (Home) — greeting bar, current-lesson hero, quick stats, schedule.
export default function HomeScreen({ onNavigate, onSwitchTab }) {
  const [view, setView] = useState('day')
  const q = useFetch(
    () => Promise.all([api.profile(), api.meta(), api.schedule(), api.classes()]),
    [],
  )

  const [profile, meta, schedule, classes] = q.data || [null, null, null, null]

  // Bugungi darslar (period bo'yicha tartiblangan).
  const todayLessons = useMemo(() => {
    return (schedule || [])
      .filter((l) => l.day === todayIdx)
      .sort((a, b) => a.period - b.period)
  }, [schedule])

  // Hozirgi yoki keyingi dars.
  const hero = useMemo(() => {
    const now = nowHHmm()
    const current = todayLessons.find((l) => l.startTime <= now && now < l.endTime)
    if (current) {
      const prog = timeProgress(current.startTime, current.endTime, now)
      return { lesson: current, current: true, progress: prog.frac, remaining: prog.remaining }
    }
    const next = todayLessons.find((l) => l.startTime > now)
    if (next) return { lesson: next, current: false }
    return null
  }, [todayLessons])

  // Haftalik darslar (kun bo'yicha guruh).
  const weekByDay = useMemo(() => {
    const map = { 0: [], 1: [], 2: [], 3: [], 4: [], 5: [] }
    for (const l of schedule || []) {
      if (map[l.day]) map[l.day].push(l)
    }
    for (const k of Object.keys(map)) map[k].sort((a, b) => a.period - b.period)
    return map
  }, [schedule])

  if (q.loading && !q.data) return <Loading />
  if (q.error) return <ErrorState error={q.error} onRetry={q.reload} />

  const firstName = (profile?.fullName || '').trim().split(/\s+/)[0] || ''
  const homeroom = profile?.homeroomClass
  const homeroomCls = homeroom ? (classes || []).find((c) => c.isHomeroom) : null
  const homeroomSubjects = (homeroomCls?.subjects || []).map((s) => s.name).join(', ')

  return (
    <div className="h-full flex flex-col bg-bg">
      {/* Top bar */}
      <div className="flex items-center gap-2.5 px-5 pt-2 pb-3">
        <div className="flex-1">
          <p className="text-[12px] font-medium text-muted">{todayLabel()}</p>
          <p className="text-[22px] font-extrabold text-text" style={{ letterSpacing: '-0.025em' }}>
            Assalomu alaykum, <span className="text-primary">{firstName}</span> 👋
          </p>
        </div>
        <button
          onClick={() => onNavigate?.('notifications')}
          className="relative w-10 h-10 rounded-xl bg-surface2 flex items-center justify-center text-text"
        >
          <Bell size={21} />
        </button>
        <Avatar name={profile?.fullName} size={40} imageUrl={profile?.photoUrl} />
      </div>

      <div className="flex-1 overflow-y-auto no-scrollbar pb-6">
        {/* Current lesson hero */}
        <div className="px-4 pt-1 pb-4">
          {hero ? (
            <div
              className="p-[18px] rounded-6xl text-white shadow-glow"
              style={{ background: 'linear-gradient(135deg, #0D9488, #115E59)' }}
              onClick={() => onSwitchTab?.('journal')}
            >
              <div className="flex items-center">
                <span className="w-2 h-2 rounded-full" style={{ background: '#FBBF24' }} />
                <span className="ml-2 text-[11px] font-bold tracking-wide">
                  {hero.current ? 'HOZIR DARS KETMOQDA' : 'KEYINGI DARS'}
                </span>
                <span className="ml-auto px-2.5 py-1 rounded-lg bg-white/20 text-[12px] font-extrabold font-mono">
                  {hero.lesson.period}-dars
                </span>
              </div>
              <div className="mt-3.5 flex items-start">
                <div className="flex-1">
                  <p className="text-[26px] font-extrabold leading-tight" style={{ letterSpacing: '-0.025em' }}>
                    {hero.lesson.subjectName}
                  </p>
                  <p className="mt-1 text-[14px] text-white/85">{hero.lesson.className}</p>
                  <div className="mt-2 flex items-center gap-1.5">
                    <Clock size={13} />
                    <span className="text-[13px] font-bold font-mono text-white/90">
                      {hero.lesson.startTime} – {hero.lesson.endTime}
                    </span>
                  </div>
                </div>
                <div className="ml-3 px-4 py-2.5 rounded-xl bg-white flex items-center gap-1.5 shadow-lg">
                  <span className="text-[14px] font-extrabold text-teal-700">Jurnal</span>
                  <ArrowRight size={15} className="text-teal-700" />
                </div>
              </div>
              {hero.current && (
                <div className="mt-4 flex items-center gap-3">
                  <div className="flex-1 h-1.5 rounded bg-white/20 overflow-hidden">
                    <div className="h-full rounded" style={{ width: `${hero.progress * 100}%`, background: '#FBBF24' }} />
                  </div>
                  <span className="text-[11px] font-bold text-white/90">{hero.remaining} daqiqa qoldi</span>
                </div>
              )}
            </div>
          ) : (
            <div className="p-[18px] rounded-6xl bg-surface border border-border text-center">
              <p className="text-[13px] text-muted">Bugun darslar yo'q</p>
            </div>
          )}
        </div>

        {/* Quick stats */}
        <div className="px-4 pb-3 flex gap-2.5">
          <StatCard color="#0D9488" icon={<Calendar size={16} />} value={`${todayLessons.length}`} label="Bugungi darslar" />
          <StatCard
            color="#F59E0B"
            icon={<ClipboardList size={16} />}
            value={`${meta?.currentQuarter ?? '-'}-chorak`}
            label={`${meta?.currentWeek ?? '-'}-hafta`}
          />
        </div>

        {/* Schedule */}
        <div className="px-5 pt-2 pb-2 flex items-center gap-2">
          <SectionLabel className="flex-1">Dars jadvali</SectionLabel>
          <div className="w-40">
            <SegmentedControl
              value={view}
              onChange={setView}
              options={[
                { value: 'day', label: 'Bugun' },
                { value: 'week', label: 'Hafta' },
              ]}
            />
          </div>
        </div>

        {view === 'day'
          ? todayLessons.map((l, i) => <LessonRow key={i} lesson={l} />)
          : Object.entries(weekByDay).map(([day, lessons]) =>
              lessons.length === 0 ? null : (
                <div key={day}>
                  <div className="px-5 pt-2 pb-1.5 flex items-center gap-2">
                    <span className="text-[13px] font-extrabold text-text">{DAYS[day]}</span>
                    <span className="text-[12px] text-faint">· {lessons.length} dars</span>
                  </div>
                  {lessons.map((l, i) => (
                    <LessonRow key={i} lesson={l} />
                  ))}
                </div>
              ),
            )}

        {/* Homeroom */}
        {homeroom && (
          <>
            <div className="px-5 pt-2 pb-2">
              <SectionLabel>Sinf rahbarligi</SectionLabel>
            </div>
            <div className="px-4 pb-6">
              <TapScale onClick={() => onNavigate?.('homeroom')}>
                <div className="p-[18px] rounded-4xl bg-surface border border-border shadow-card flex items-center gap-3.5">
                  <div
                    className="w-14 h-14 rounded-2xl flex items-center justify-center text-white text-[18px] font-extrabold"
                    style={{ background: 'linear-gradient(135deg, #14B8A6, #0F766E)' }}
                  >
                    {homeroom.replace(/[-\s]/g, '').slice(0, 3)}
                  </div>
                  <div className="flex-1">
                    <p className="text-[16px] font-bold text-text">{homeroom} sinfi</p>
                    {homeroomSubjects && <p className="text-[13px] text-muted">{homeroomSubjects}</p>}
                  </div>
                  <ChevronRight size={20} className="text-faint" />
                </div>
              </TapScale>
            </div>
          </>
        )}
      </div>
    </div>
  )
}

// "HH:mm" oraliqdagi o'tilgan ulush + qolgan daqiqa.
function timeProgress(start, end, now) {
  const toMin = (s) => {
    const [h, m] = s.split(':').map(Number)
    return h * 60 + m
  }
  const s = toMin(start)
  const e = toMin(end)
  const n = toMin(now)
  const total = Math.max(1, e - s)
  const frac = Math.min(1, Math.max(0, (n - s) / total))
  return { frac, remaining: Math.max(0, e - n) }
}

function StatCard({ color, icon, value, label }) {
  return (
    <div className="flex-1 p-3.5 rounded-3xl bg-surface border border-border">
      <div className="w-8 h-8 rounded-[10px] flex items-center justify-center" style={{ background: `color-mix(in srgb, ${color} 12%, transparent)`, color }}>
        {icon}
      </div>
      <p className="mt-3 text-[22px] font-extrabold text-text font-mono" style={{ letterSpacing: '-0.02em' }}>
        {value}
      </p>
      <p className="mt-1 text-[11px] text-muted leading-tight">{label}</p>
    </div>
  )
}

function LessonRow({ lesson }) {
  return (
    <div className="px-4 pb-2">
      <TapScale>
        <div className="p-3.5 rounded-3xl bg-surface border border-border flex items-center gap-3.5">
          <div className="w-[50px] flex flex-col items-center">
            <span className="text-[14px] font-bold text-text font-mono">{lesson.startTime}</span>
            <span className="w-6 h-px bg-border my-0.5" />
            <span className="text-[11px] text-faint font-mono">{lesson.endTime}</span>
          </div>
          <div className="flex-1">
            <div className="flex items-center gap-2">
              <span className="text-[15px] font-bold text-text">{lesson.subjectName}</span>
              {lesson.subGroup > 0 && (
                <span className="px-1.5 py-0.5 rounded bg-surface3 text-[10px] font-bold text-muted">{lesson.subGroup}-G</span>
              )}
            </div>
            <p className="text-[13px] text-muted">{lesson.className}</p>
          </div>
        </div>
      </TapScale>
    </div>
  )
}
