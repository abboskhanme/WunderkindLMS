/**
 * Telegramdan TASHQARIDA ochilganda — oddiy login.
 *
 * Nega kerak: Mini App'ni telefonda Telegram ichida ochmasdan turib ko'rib
 * bo'lmasdi, ya'ni demoga tayyorlanish ham, brauzerdan tekshirish ham
 * imkonsiz edi.
 *
 * Nega bu teshik EMAS: bu yerda hech qanday chetlab o'tish yo'q — o'sha
 * `POST /api/auth/login`, o'sha login va parol, o'sha JWT. Telegram imzosi
 * faqat "parolsiz kirish" yo'li; uni bu ekran zaiflashtirmaydi.
 */
import { useState } from 'react'
import { LogIn } from 'lucide-react'
import { api, tokenStore } from '../lib/api'
import { Hero, Screen } from '../components/ui'

export function BrowserLogin({ onSuccess }) {
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const submit = async (e) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const res = await api.post('/auth/login', { email: login.trim(), password })
      tokenStore.set(res.token)
      onSuccess(res.user ?? null)
    } catch (err) {
      setError(err.status === 401 ? "Login yoki parol noto'g'ri." : err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <Hero title="Wunderkind" subtitle="Mini App — brauzer ko'rinishi" />
      <form onSubmit={submit} className="mx-4 mt-4 rounded-card bg-white p-5">
        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-brand/25">
          <LogIn className="h-6 w-6 text-brand-ink" />
        </div>
        <h1 className="mt-3 text-[18px] font-bold">Kirish</h1>
        <p className="mt-1 text-[14px] leading-relaxed text-slate-500">
          Telegram ichida bu ekran ko'rinmaydi — u yerda hisob avtomatik
          aniqlanadi. Bu yerda esa odatdagi login va parol kerak.
        </p>

        <input
          value={login}
          onChange={(e) => setLogin(e.target.value)}
          autoCapitalize="none"
          autoCorrect="off"
          placeholder="Login"
          className="mt-4 w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-[16px] outline-none focus:border-brand"
        />
        <input
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          type="password"
          placeholder="Parol"
          className="mt-2 w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-[16px] outline-none focus:border-brand"
        />

        {error && <p className="mt-3 text-[13px] text-red-600">{error}</p>}

        <button
          type="submit"
          disabled={busy || !login || !password}
          className="mt-4 w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
        >
          {busy ? 'Kirilmoqda…' : 'Kirish'}
        </button>
      </form>
    </Screen>
  )
}
