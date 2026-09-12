/**
 * Telegram hisobi hali hech kimga bog'lanmagan.
 *
 * ATAYLAB telefon raqami SO'RALMAYDI: raqamni yozgan har kim o'sha odamning
 * hisobiga kira olardi. Maktab bir martalik kod beradi, kod esa qaysi
 * hisobga tegishli ekanini server biladi.
 */
import { useState } from 'react'
import { KeyRound } from 'lucide-react'
import { api } from '../lib/api'
import { useSession } from '../lib/session'
import { haptic } from '../lib/telegram'
import { Hero, Screen } from '../components/ui'

export function LinkScreen() {
  const { retry } = useSession()
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const submit = async (e) => {
    e.preventDefault()
    const clean = code.trim()
    if (clean.length < 4) {
      setError('Kod juda qisqa.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await api.post('/tg/link', { code: clean })
      haptic('medium')
      await retry()
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
      </form>
    </Screen>
  )
}
