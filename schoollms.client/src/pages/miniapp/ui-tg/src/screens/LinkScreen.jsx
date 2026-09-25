/**
 * Telegram hisobi hali hech kimga bog'lanmagan.
 *
 * IKKI YO'L (mijoz, 2026-09-25: "telegramdan kirsa ham login paroli saqlanadimi ... har safar parol termasligi
 * kerak"):
 *   · LOGIN VA PAROL — birinchi marta bir marta kiritiladi, Telegram hisobi o'sha foydalanuvchiga bog'lanadi va
 *     keyingi ochilishlarda Mini App uni Telegram imzosidan o'zi taniydi (parol so'ralmaydi);
 *   · BIR MARTALIK KOD — maktab kotibiyati bergan kod bilan (avvalgi yo'l).
 *
 * ATAYLAB telefon raqami SO'RALMAYDI: raqamni yozgan har kim o'sha odamning hisobiga kira olardi.
 */
import { useState } from 'react'
import { KeyRound, LogIn } from 'lucide-react'
import { api } from '../lib/api'
import { useSession } from '../lib/session'
import { haptic, initData } from '../lib/telegram'
import { Hero, Screen } from '../components/ui'

const inputClass =
  'mt-3 w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-[16px] outline-none focus:border-brand'

export function LinkScreen() {
  const { retry } = useSession()
  const [mode, setMode] = useState('login')
  const [code, setCode] = useState('')
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const run = async (request) => {
    setBusy(true)
    setError(null)
    try {
      await request()
      haptic('medium')
      await retry()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  const submitCode = (e) => {
    e.preventDefault()
    const clean = code.trim()
    if (clean.length < 4) {
      setError('Kod juda qisqa.')
      return
    }
    void run(() => api.post('/tg/link', { code: clean, initData }))
  }

  const submitLogin = (e) => {
    e.preventDefault()
    if (!login.trim() || !password) {
      setError('Login va parolni kiriting.')
      return
    }
    void run(() => api.post('/tg/link-login', { login: login.trim(), password, initData }))
  }

  const switchMode = (next) => {
    setMode(next)
    setError(null)
  }

  return (
    <Screen>
      <Hero title="Wunderkind" subtitle="Hisobni ulash" />

      {mode === 'login' ? (
        <form onSubmit={submitLogin} className="mx-4 mt-4 rounded-card bg-white p-5">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-brand/25">
            <LogIn className="h-6 w-6 text-brand-ink" />
          </div>
          <h1 className="mt-3 text-[18px] font-bold">Login va parol</h1>
          <p className="mt-1 text-[14px] leading-relaxed text-slate-500">
            Maktab bergan login va parolni bir marta kiriting. Keyingi safar Telegram'dan kirganingizda parol
            so'ralmaydi.
          </p>

          <input
            value={login}
            onChange={(e) => setLogin(e.target.value)}
            autoCapitalize="none"
            autoCorrect="off"
            autoComplete="username"
            placeholder="Login"
            className={inputClass}
          />
          <input
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            type="password"
            autoComplete="current-password"
            placeholder="Parol"
            className={inputClass}
          />

          {error && <p className="mt-3 text-[13px] text-red-600">{error}</p>}

          <button
            type="submit"
            disabled={busy}
            className="mt-4 w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
          >
            {busy ? 'Tekshirilmoqda…' : 'Kirish'}
          </button>
          <button
            type="button"
            onClick={() => switchMode('code')}
            className="mt-3 w-full py-2 text-[14px] font-semibold text-slate-500"
          >
            Bir martalik kod bilan ulash
          </button>
        </form>
      ) : (
        <form onSubmit={submitCode} className="mx-4 mt-4 rounded-card bg-white p-5">
          <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-brand/25">
            <KeyRound className="h-6 w-6 text-brand-ink" />
          </div>
          <h1 className="mt-3 text-[18px] font-bold">Bir martalik kod</h1>
          <p className="mt-1 text-[14px] leading-relaxed text-slate-500">
            Maktab kotibiyatidan olingan kodni kiriting. Kod bir marta ishlaydi va
            Telegram hisobingizni maktabdagi hisobingizga bog'laydi.
          </p>

          <input
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            inputMode="text"
            autoComplete="one-time-code"
            placeholder="Masalan: 7K4M92"
            className="mt-4 w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-center text-[20px] font-bold tracking-[0.2em] outline-none focus:border-brand"
          />

          {error && <p className="mt-3 text-[13px] text-red-600">{error}</p>}

          <button
            type="submit"
            disabled={busy}
            className="mt-4 w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
          >
            {busy ? 'Tekshirilmoqda…' : 'Ulash'}
          </button>
          <button
            type="button"
            onClick={() => switchMode('login')}
            className="mt-3 w-full py-2 text-[14px] font-semibold text-slate-500"
          >
            Login va parol bilan kirish
          </button>
        </form>
      )}
    </Screen>
  )
}
