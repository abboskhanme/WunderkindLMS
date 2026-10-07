/**
 * Telegram hisobi hali hech kimga bog'lanmagan — LOGIN VA PAROL bilan ulanadi.
 *
 * Bir marta kiritiladi (mijoz, 2026-09-25: "telegramdan kirsa ham login paroli saqlanadimi ... har
 * safar parol termasligi kerak"): Telegram hisobi o'sha foydalanuvchiga bog'lanadi va keyingi
 * ochilishlarda Mini App uni o'zi taniydi. Bir martalik kod bilan ulash olib tashlandi (mijoz,
 * 2026-10-07: "kod bilan ulash kerakmas ... hamma login parol bilan ulaydi").
 *
 * Login yozilishi qat'iy emas — katta-kichik harf, bo'sh joy va "Familiya Ism" ham topiladi (server).
 * Tugmadan ochilgan ilovada imzo yo'q: kimligini chiqishda olingan chipta aytadi (lib/linkTicket.js).
 *
 * ATAYLAB telefon raqami SO'RALMAYDI: raqamni yozgan har kim o'sha odamning hisobiga kira olardi.
 */
import { useState } from 'react'
import { LogIn } from 'lucide-react'
import { api, tokenStore } from '../lib/api'
import { useSession } from '../lib/session'
import { haptic, initData } from '../lib/telegram'
import { linkTicket } from '../lib/linkTicket'
import { setPanelKey } from '../lib/panelKey'
import { Hero, Screen } from '../components/ui'

const inputClass =
  'mt-3 w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-[16px] outline-none focus:border-brand'

export function LinkScreen() {
  const { retry, adoptSession, error: notice } = useSession()
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const submit = async (e) => {
    e.preventDefault()
    if (!login.trim() || !password) {
      setError('Login va parolni kiriting.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const res = await api.post('/tg/link-login', { login: login.trim(), password, initData }, linkTicket.headers())
      haptic('medium')
      linkTicket.clear()
      // Tugmadan ochilgan ilovada server yangi tugma kalitini ham beradi — token eskirsa shu bilan kiriladi.
      if (res?.panelKey) setPanelKey(res.panelKey)
      // The server answers with the new account's session — use it as is. Re-authenticating would
      // need initData, which a Mini App opened from the bot's keyboard button never has.
      if (res?.token) {
        tokenStore.set(res.token)
        await adoptSession(res.user ?? null)
      } else {
        await retry()
      }
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Screen>
      <Hero title="Wunderkind" subtitle="Hisobni ulash" />
      <form onSubmit={submit} className="mx-4 mt-4 rounded-card bg-white p-5">
        <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-brand/25">
          <LogIn className="h-6 w-6 text-brand-ink" />
        </div>
        <h1 className="mt-3 text-[18px] font-bold">Login va parol</h1>
        <p className="mt-1 text-[14px] leading-relaxed text-slate-500">
          Maktab bergan login va parolni bir marta kiriting. Keyingi safar Telegram'dan kirganingizda parol so'ralmaydi.
        </p>
        {notice && <p className="mt-3 rounded-xl bg-amber-50 px-3 py-2 text-[13px] text-amber-800">{notice}</p>}

        <input
          value={login}
          onChange={(e) => setLogin(e.target.value)}
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          autoComplete="username"
          placeholder="Login"
          className={inputClass}
        />
        <input
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          type="password"
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
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
      </form>
    </Screen>
  )
}
