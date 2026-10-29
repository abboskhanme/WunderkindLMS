/**
 * Akkauntlar — bitta Telegram'da bir nechta akkaunt (mijoz, 2026-10-08; xodim paneli 2026-10-28:
 * "xodim yoki teacher accountida kirganda ... farzandini akkountiga login qilib kirish mumkin bo'lsin,
 * va accountlarni logout qilmasdan alishtirish").
 *
 * Mini App'dagi `AccountSwitcher` bilan bir xil API (`/api/tg/accounts`). Faqat Telegram'dan ochilgan
 * sessiyada ko'rinadi: o'sha Telegram'ga qaysi akkauntlar saqlanganini server shu bilan biladi.
 * Almashgandan keyin: xodim/admin/kassir — veb-panel qayta yuklanadi, o'qituvchi va ota-ona/o'quvchi —
 * Mini App ochiladi (server Telegram bog'lanishini yangi akkauntga o'tkazgan, Mini App o'shani ochadi).
 */
import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { Check, Loader2, Plus, X } from 'lucide-react'
import {
  addTelegramAccount,
  forgetTelegramAccount,
  listTelegramAccounts,
  switchTelegramAccount,
  type LoginResult,
  type TelegramSavedAccount,
} from '@/api/services/auth'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { UserAvatar } from '@/components/ui/UserAvatar'

/** Veb-panelda ishlaydigan rollar — Mini App'dagi PANEL_ROLES / HOME bilan bir xil. */
const PANEL_HOME: Record<string, string> = {
  staff: '/admin',
  admin: '/admin',
  superadmin: '/admin',
  cashier: '/cashier',
}

/** Yangi sessiyani saqlab, shu akkauntning ish joyini to'liq qayta yuklab ochadi. */
function openSession({ token, user }: LoginResult) {
  try {
    localStorage.setItem('token', token)
    localStorage.setItem('user', JSON.stringify(user))
    // Mini App'ning tokeni (ui-tg/src/lib/api.js, `tg_token`): botdagi tugmadan ochilgan ilovada
    // imzo yo'q va kalit sahifa almashganda yo'qoladi — Mini App shu token bilan ochiladi.
    sessionStorage.setItem('tg_token', token)
  } catch {
    /* private rejim — sahifa login so'raydi */
  }
  window.location.replace(PANEL_HOME[user.role] ?? '/tg/')
}

function errorText(e: unknown): string {
  if (axios.isAxiosError(e)) {
    const message = (e.response?.data as { message?: string } | undefined)?.message
    if (message) return message
  }
  return 'Xatolik yuz berdi. Qaytadan urinib ko\'ring.'
}

export function TelegramAccounts() {
  const [accounts, setAccounts] = useState<TelegramSavedAccount[] | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setAccounts(await listTelegramAccounts())
    } catch (e) {
      setError(errorText(e))
    }
  }, [])

  useEffect(() => {
    let alive = true
    listTelegramAccounts()
      .then((list) => alive && setAccounts(list))
      .catch((e: unknown) => alive && setError(errorText(e)))
    return () => {
      alive = false
    }
  }, [])

  const pick = async (a: TelegramSavedAccount) => {
    if (a.active || busyId) return
    setBusyId(a.userId)
    setError(null)
    try {
      openSession(await switchTelegramAccount(a.userId))
    } catch (e) {
      setError(errorText(e))
      setBusyId(null)
      void load()
    }
  }

  const forget = async (a: TelegramSavedAccount) => {
    setError(null)
    try {
      await forgetTelegramAccount(a.userId)
      void load()
    } catch (e) {
      setError(errorText(e))
    }
  }

  if (adding) return <AddAccountForm onCancel={() => setAdding(false)} />

  return (
    <div className="space-y-3">
      <p className="text-sm text-slate-400">
        Bir nechta akkauntingiz bo'lsa (masalan, farzandingizniki) — bir marta qo'shing, keyin chiqmasdan almashtirasiz.
      </p>

      <div className="overflow-hidden rounded-xl border border-slate-200">
        {accounts === null && !error && (
          <p className="flex items-center gap-2 px-4 py-3 text-sm text-slate-400">
            <Loader2 className="h-4 w-4 animate-spin" /> Yuklanmoqda…
          </p>
        )}
        {accounts?.map((a) => (
          <div key={a.userId} className="flex items-center gap-3 border-b border-slate-100 px-4 py-3 last:border-b-0">
            <button
              type="button"
              onClick={() => pick(a)}
              disabled={Boolean(busyId)}
              className="flex min-w-0 flex-1 items-center gap-3 text-left disabled:opacity-60"
            >
              <UserAvatar fullName={a.fullName} className="h-9 w-9 text-sm" />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium text-slate-700">{a.fullName}</span>
                <span className="block truncate text-xs text-slate-400">
                  {busyId === a.userId ? 'Almashtirilmoqda…' : a.subtitle}
                </span>
              </span>
              {a.active && <Check className="h-4 w-4 shrink-0 text-emerald-600" aria-label="Hozirgi akkaunt" />}
            </button>
            {!a.active && (
              <button
                type="button"
                onClick={() => forget(a)}
                title="Ro'yxatdan olib tashlash"
                aria-label={`${a.fullName} — ro'yxatdan olib tashlash`}
                className="rounded-lg p-1.5 text-slate-300 transition-colors hover:bg-slate-100 hover:text-slate-500"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>
        ))}
        <button
          type="button"
          onClick={() => setAdding(true)}
          className="flex w-full items-center gap-3 border-t border-slate-100 px-4 py-3 text-left transition-colors hover:bg-slate-50"
        >
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-brand-50 text-brand-600">
            <Plus className="h-4 w-4" />
          </span>
          <span className="min-w-0 flex-1">
            <span className="block text-sm font-medium text-slate-700">Akkaunt qo'shish</span>
            <span className="block text-xs text-slate-400">Farzandingiz yoki boshqa akkaunt logini — bir marta</span>
          </span>
        </button>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
    </div>
  )
}

/** Login va parol — qo'shilgan akkauntga darhol o'tiladi, hozirgisi ro'yxatda qoladi. */
function AddAccountForm({ onCancel }: { onCancel: () => void }) {
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setSaving(true)
    setError(null)
    try {
      openSession(await addTelegramAccount(login.trim(), password))
    } catch (err) {
      setError(errorText(err))
      setSaving(false)
    }
  }

  return (
    <form onSubmit={onSubmit} className="space-y-4">
      <p className="text-sm text-slate-400">
        Akkauntning o'z login va paroli. Keyingi safar parolsiz almashtirasiz.
      </p>
      <Input
        label="Login"
        type="text"
        autoComplete="off"
        placeholder="Login"
        value={login}
        onChange={(e) => setLogin(e.target.value)}
        required
      />
      <Input
        label="Parol"
        type="password"
        autoComplete="off"
        placeholder="parol"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
        required
      />
      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      <div className="flex items-center gap-3">
        <Button type="submit" disabled={saving || !login.trim() || !password}>
          {saving ? 'Kirilmoqda…' : 'Qo\'shish va o\'tish'}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={saving}>
          Orqaga
        </Button>
      </div>
    </form>
  )
}
