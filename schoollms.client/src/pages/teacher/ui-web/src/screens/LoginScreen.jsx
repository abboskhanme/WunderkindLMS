import { useState } from 'react'
import { User, Lock, Eye, EyeOff, AlertCircle } from 'lucide-react'
import AppButton from '../components/AppButton'
import { useSession } from '../lib/session'

// Login — gradient logo, decorative radial blobs, login/parol maydonlari.
export default function LoginScreen() {
  const { login } = useSession()
  const [showPass, setShowPass] = useState(false)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  const submit = async () => {
    if (loading) return
    if (!email.trim() || !password) {
      setError('Login va parolni kiriting')
      return
    }
    setLoading(true)
    setError('')
    try {
      await login(email.trim(), password)
    } catch (e) {
      const status = e?.status
      setError(
        status === 401
          ? "Login yoki parol noto'g'ri"
          : e?.message || 'Kirishda xatolik',
      )
    } finally {
      setLoading(false)
    }
  }

  const onKey = (e) => e.key === 'Enter' && submit()

  return (
    <div className="relative h-full bg-bg overflow-hidden">
      {/* Decorative blobs */}
      <div
        className="absolute -top-24 -right-16 w-72 h-72 rounded-full"
        style={{ background: 'radial-gradient(circle, rgba(94,234,212,0.45), transparent 70%)' }}
      />
      <div
        className="absolute -bottom-20 -left-20 w-60 h-60 rounded-full"
        style={{ background: 'radial-gradient(circle, rgba(20,184,166,0.20), transparent 70%)' }}
      />

      <div className="relative h-full overflow-y-auto no-scrollbar px-7 pt-16 pb-6">
        {/* Logo */}
        <div className="flex flex-col items-center">
          <div
            className="w-[76px] h-[76px] rounded-[22px] flex items-center justify-center text-white shadow-glow"
            style={{ background: 'linear-gradient(135deg, #14B8A6, #0F766E)' }}
          >
            <BookGlyph />
          </div>
          <p className="mt-5 text-[26px] font-extrabold text-text" style={{ letterSpacing: '-0.03em' }}>
            Maktab
          </p>
          <p className="mt-1 text-[13px] text-muted">O'qituvchi kabineti</p>
        </div>

        <div className="mt-12">
          <p className="text-[24px] font-extrabold text-text" style={{ letterSpacing: '-0.025em' }}>
            Xush kelibsiz!
          </p>
          <p className="mt-1.5 text-[14px] text-muted leading-relaxed">
            Hisobingizga kirib, darslar va o'quvchilarni boshqarishni davom ettiring.
          </p>
        </div>

        {error && (
          <div className="mt-5 flex items-center gap-2 px-3.5 py-3 rounded-xl bg-danger/10 text-danger text-[13px] font-medium">
            <AlertCircle size={16} className="shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <div className="mt-6 space-y-3.5">
          <Field
            label="Login"
            icon={<User size={18} className="text-faint" />}
            placeholder="Login (maktab bergan)"
            value={email}
            onChange={setEmail}
            onKeyDown={onKey}
            autoFocus
          />
          <Field
            label="Parol"
            icon={<Lock size={18} className="text-faint" />}
            placeholder="Parolingiz"
            type={showPass ? 'text' : 'password'}
            value={password}
            onChange={setPassword}
            onKeyDown={onKey}
            trailing={
              <button onClick={() => setShowPass((v) => !v)} className="text-faint">
                {showPass ? <EyeOff size={18} /> : <Eye size={18} />}
              </button>
            }
          />
        </div>

        <div className="mt-5">
          <AppButton label="Kirish" expand height={54} radius={16} loading={loading} onClick={submit} />
        </div>
      </div>
    </div>
  )
}

function Field({ label, icon, placeholder, type = 'text', trailing, value, onChange, onKeyDown, autoFocus }) {
  return (
    <div>
      <p className="text-[12px] font-bold text-muted uppercase mb-1.5">{label}</p>
      <div className="flex items-center gap-2.5 px-4 h-12 rounded-xl bg-surface2 border border-border focus-within:border-primary focus-within:border-[1.5px]">
        {icon}
        <input
          type={type}
          placeholder={placeholder}
          value={value}
          autoFocus={autoFocus}
          autoCapitalize="none"
          autoCorrect="off"
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={onKeyDown}
          className="flex-1 bg-transparent outline-none text-[15px] text-text placeholder:text-faint"
        />
        {trailing}
      </div>
    </div>
  )
}

// The little open-book glyph drawn in the Flutter logo.
function BookGlyph() {
  return (
    <svg width="38" height="38" viewBox="0 0 19 19" fill="none">
      <path d="M3 4.25C3 3.56 3.56 3 4.25 3H9.5V16H4.25C3.56 16 3 15.44 3 14.75V4.25Z" fill="white" fillOpacity="0.95" />
      <path d="M16 4.25C16 3.56 15.44 3 14.75 3H9.5V16H14.75C15.44 16 16 15.44 16 14.75V4.25Z" fill="white" fillOpacity="0.7" />
      <path d="M5.5 6H8.5M5.5 8H8.5M5.5 10H7.5" stroke="#0F766E" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  )
}
