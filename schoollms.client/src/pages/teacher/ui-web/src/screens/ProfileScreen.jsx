import { useEffect, useState } from 'react'
import {
  GraduationCap, Wallet, BookOpen, Badge, LineChart,
  MessageSquareWarning, Moon, Lock, LogOut, ChevronRight, Download, Bell,
} from 'lucide-react'
import Avatar from '../components/Avatar'
import AppButton from '../components/AppButton'
import AppSheet from '../components/AppSheet'
import { SectionLabel } from '../components/ui'
import { Loading, ErrorState } from '../components/State'
import { useFetch, useSession } from '../lib/session'
import { api } from '../lib/api'
import { canInstall, promptInstall, onInstallChange, isStandalone } from '../lib/pwa'
import { enablePush, disablePush, pushSupported, notifPermission, hasPushToken } from '../lib/push'

const PUSH_MSG = {
  unsupported: "Bu brauzer bildirishnomani qo'llab-quvvatlamaydi.",
  disabled: 'Bildirishnoma administrator tomonidan sozlanmagan.',
  config: "Bildirishnoma sozlamasida xatolik.",
  denied: 'Brauzer ruxsati rad etilgan — brauzer sozlamalaridan yoqing.',
  permission: 'Ruxsat berilmadi.',
  token: "Bildirishnomaga ulanib bo'lmadi — qayta urinib ko'ring.",
}

const ml = (v) => (Number(v || 0) / 1_000_000).toFixed(1)

// Profile — gradient header card, info rows, "Bo'limlar" + "Hisob" lists.
export default function ProfileScreen({ onNavigate, dark, onToggleTheme }) {
  const { logout } = useSession()
  const q = useFetch(() => api.profile(), [])

  // Oylik — ixtiyoriy; xato bo'lsa ham profil ko'rinaveradi.
  const [monthly, setMonthly] = useState(null)
  useEffect(() => {
    let alive = true
    api.salary()
      .then((d) => {
        if (!alive || !d) return
        const months = d.months || []
        const cur = months[months.length - 1]
        if (cur) setMonthly(cur.paid)
        else if (d.salary != null) setMonthly(d.salary)
      })
      .catch(() => {})
    return () => { alive = false }
  }, [])

  // PWA o'rnatish tugmasi holati.
  const [installable, setInstallable] = useState(canInstall() && !isStandalone())
  useEffect(() => {
    const off = onInstallChange((can) => setInstallable(can && !isStandalone()))
    return off
  }, [])

  // Parolni o'zgartirish sheet.
  const [pwOpen, setPwOpen] = useState(false)

  // Bildirishnoma (web push) holati.
  const [pushOn, setPushOn] = useState(hasPushToken() && notifPermission() === 'granted')
  const [pushBusy, setPushBusy] = useState(false)
  const [pushMsg, setPushMsg] = useState(null)
  const togglePush = async () => {
    if (pushBusy) return
    setPushBusy(true)
    setPushMsg(null)
    try {
      if (pushOn) {
        await disablePush()
        setPushOn(false)
      } else {
        const res = await enablePush({ silent: false })
        if (res.ok) setPushOn(true)
        else setPushMsg(PUSH_MSG[res.reason] || PUSH_MSG.token)
      }
    } finally {
      setPushBusy(false)
    }
  }

  if (q.loading && !q.data) {
    return (
      <div className="h-full flex flex-col bg-bg">
        <div className="px-4 pt-2 pb-1"><p className="text-[17px] font-extrabold text-text">Profil</p></div>
        <Loading />
      </div>
    )
  }
  if (q.error) {
    return (
      <div className="h-full flex flex-col bg-bg">
        <div className="px-4 pt-2 pb-1"><p className="text-[17px] font-extrabold text-text">Profil</p></div>
        <ErrorState error={q.error} onRetry={q.reload} />
      </div>
    )
  }

  const p = q.data || {}
  const subjectNames = (p.subjects || []).map((s) => s.name).join(', ') || '—'

  return (
    <div className="h-full flex flex-col bg-bg">
      <div className="px-4 pt-2 pb-1">
        <p className="text-[17px] font-extrabold text-text">Profil</p>
      </div>
      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6">
        {/* Header card */}
        <div className="rounded-4xl bg-surface border border-border overflow-hidden">
          <div className="h-[90px]" style={{ background: 'linear-gradient(135deg,#14B8A6,#0F766E)' }} />
          <div className="flex flex-col items-center -mt-10 pb-3">
            <Avatar name={p.fullName} imageUrl={p.photoUrl} size={80} ring />
            <p className="mt-3 text-[18px] font-extrabold text-text">{p.fullName}</p>
            <p className="text-[12px] text-muted">{p.email}</p>
            <span className="mt-3 px-2.5 py-1 rounded-lg bg-primary-soft flex items-center gap-1 text-[12px] font-semibold text-primary">
              <GraduationCap size={12} /> O'qituvchi
            </span>
            <div className="w-full px-5 mt-4 space-y-0 divide-y divide-border/60">
              <InfoRow icon={<Badge size={18} />} label="Sinf rahbarligi" value={p.homeroomClass || '—'} />
              <InfoRow icon={<BookOpen size={18} />} label="Fanlar" value={subjectNames} />
              {monthly != null && (
                <InfoRow icon={<Wallet size={18} />} label="Oylik" value={`${ml(monthly)} mln so'm`} />
              )}
            </div>
          </div>
        </div>

        {installable && (
          <div className="mt-4">
            <AppButton
              label="Ilovani o'rnatish"
              style="soft"
              expand
              leadingIcon={<Download size={18} />}
              onClick={() => promptInstall()}
            />
          </div>
        )}

        {/* Sections */}
        <div className="mt-4">
          <SectionLabel className="pl-1 pb-2 block">Bo'limlar</SectionLabel>
          <Card>
            <Item icon={<LineChart size={16} />} title="Dars o'tilishi" hint="O'tilgan darslar progresi" onClick={() => onNavigate?.('progress')} />
            <Item icon={<BookOpen size={16} />} title="Ta'lim" hint="O'quv materiallar va progress" onClick={() => onNavigate?.('lms')} />
            <Item icon={<Wallet size={16} />} title="Maosh" hint="Oylik hisob-kitob" onClick={() => onNavigate?.('salary')} />
            <Item icon={<MessageSquareWarning size={16} />} title="Taklif va shikoyatlar" hint="Adminga murojaat yuborish" last onClick={() => onNavigate?.('feedback')} />
          </Card>
        </div>

        {/* Account */}
        <div className="mt-4">
          <SectionLabel className="pl-1 pb-2 block">Hisob</SectionLabel>
          <Card>
            {pushSupported() && (
              <ToggleItem icon={<Bell size={16} />} title="Bildirishnomalar" on={pushOn} onToggle={togglePush} />
            )}
            <ToggleItem icon={<Moon size={16} />} title="Tungi rejim" on={!!dark} onToggle={onToggleTheme} />
            <Item icon={<Lock size={16} />} title="Parolni o'zgartirish" last onClick={() => setPwOpen(true)} />
          </Card>
          {pushMsg && <p className="mt-2 px-1 text-[12px] text-muted">{pushMsg}</p>}
        </div>

        <div className="mt-4">
          <AppButton label="Chiqish" style="ghost" expand leadingIcon={<LogOut size={18} className="text-danger" />} onClick={logout} />
        </div>
        <p className="mt-5 text-center text-[11px] text-faint">Maktab Portali · v1.0.0 · 2026</p>
      </div>

      <PasswordSheet open={pwOpen} onClose={() => setPwOpen(false)} email={p.email} />
    </div>
  )
}

function PasswordSheet({ open, onClose, email }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)
  const [ok, setOk] = useState(false)

  const reset = () => { setCurrent(''); setNext(''); setError(null); setOk(false) }
  const close = () => { reset(); onClose?.() }

  const submit = async () => {
    if (!current.trim() || !next.trim()) {
      setError("Barcha maydonlarni to'ldiring")
      return
    }
    setBusy(true)
    setError(null)
    try {
      await api.account({ currentPassword: current, email, newPassword: next })
      setOk(true)
      setTimeout(close, 1200)
    } catch (e) {
      setError((e && e.message) || 'Xatolik')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AppSheet open={open} onClose={close} title="Parolni o'zgartirish">
      <div className="px-5 pb-6 space-y-3">
        {ok ? (
          <p className="py-4 text-center text-[14px] font-semibold text-success">Parol o'zgartirildi</p>
        ) : (
          <>
            <Field label="Joriy parol" value={current} onChange={setCurrent} />
            <Field label="Yangi parol" value={next} onChange={setNext} />
            {error && <p className="text-[13px] font-semibold text-danger">{error}</p>}
            <div className="pt-1">
              <AppButton label="Saqlash" expand height={50} loading={busy} onClick={submit} />
            </div>
          </>
        )}
      </div>
    </AppSheet>
  )
}

function Field({ label, value, onChange }) {
  return (
    <div>
      <p className="text-[12px] font-bold text-muted uppercase tracking-wide mb-1.5">{label}</p>
      <input
        type="password"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full p-3.5 rounded-xl bg-surface2 border border-border outline-none text-[15px] text-text placeholder:text-faint focus:border-primary"
      />
    </div>
  )
}

function InfoRow({ icon, label, value }) {
  return (
    <div className="flex items-start gap-3 py-2">
      <span className="text-muted">{icon}</span>
      <div>
        <p className="text-[11px] text-muted">{label}</p>
        <p className="text-[14px] font-semibold text-text">{value}</p>
      </div>
    </div>
  )
}
function Card({ children }) {
  return <div className="rounded-4xl bg-surface border border-border overflow-hidden">{children}</div>
}
function Item({ icon, title, hint, last, onClick }) {
  return (
    <button onClick={onClick} className={['w-full px-4 py-3.5 flex items-center gap-3 text-left', last ? '' : 'border-b border-border/50'].join(' ')}>
      <span className="w-8 h-8 rounded-[10px] bg-surface2 flex items-center justify-center text-text">{icon}</span>
      <div className="flex-1">
        <p className="text-[14px] font-semibold text-text">{title}</p>
        {hint && <p className="text-[11px] text-muted">{hint}</p>}
      </div>
      <ChevronRight size={16} className="text-faint" />
    </button>
  )
}
function ToggleItem({ icon, title, on, onToggle, last }) {
  return (
    <div className={['w-full px-4 py-3.5 flex items-center gap-3 text-left', last ? '' : 'border-b border-border/50'].join(' ')}>
      <span className="w-8 h-8 rounded-[10px] bg-surface2 flex items-center justify-center text-text">{icon}</span>
      <div className="flex-1">
        <p className="text-[14px] font-semibold text-text">{title}</p>
      </div>
      <button
        onClick={onToggle}
        className={['relative w-[46px] h-[26px] rounded-full transition-colors duration-200', on ? 'bg-primary' : 'bg-surface3'].join(' ')}
        aria-pressed={on}
      >
        <span
          className="absolute top-[3px] w-5 h-5 rounded-full bg-white shadow-soft transition-all duration-200"
          style={{ left: on ? '23px' : '3px' }}
        />
      </button>
    </div>
  )
}
