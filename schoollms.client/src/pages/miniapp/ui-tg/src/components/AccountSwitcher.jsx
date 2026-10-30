/**
 * AKKAUNTLAR — bitta Telegram'da bir nechta akkaunt (mijoz, 2026-10-08: "bitta oila farzandlari uchun
 * 2 xil login parol bilan kirsa ham boshqa farzandini akkauntiga o'tishi oson bo'lishi kerak ...
 * o'qituvchi va ota-ona akkauntini ham almashtirish oson bo'lishi kerak").
 *
 * Har akkaunt BIR MARTA o'z login-paroli bilan qo'shiladi; keyin bir bosishda, parolsiz almashadi.
 * Ro'yxatni server beradi (`GET /api/tg/accounts`), faol akkaunt birinchi. Arxivlangani o'zi chiqib
 * ketadi.
 *
 *   AccountsCard   — profil varag'idagi bo'lim (ota-ona / o'quvchi paneli).
 *   AccountsButton — o'qituvchi hero'sidagi tugma: varaqda ro'yxat, qo'shish va "Chiqish".
 *
 * Brauzerda (Telegram'siz) server `telegram_only` deydi — bo'lim umuman ko'rinmaydi.
 */
import { useState } from 'react'
import { Check, Plus, UserRound, Users, X } from 'lucide-react'
import { api } from '../lib/api'
import { useAsync } from '../lib/useAsync'
import { useSession } from '../lib/session'
import { haptic } from '../lib/telegram'
import { Card, PasswordField } from './ui'
import { LogoutButton } from './LogoutButton'

const inputClass =
  'mt-3 w-full rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-[16px] outline-none focus:border-brand'

function initials(fullName) {
  const parts = String(fullName ?? '').trim().split(/\s+/).filter(Boolean)
  return parts.slice(0, 2).map((p) => p.charAt(0).toUpperCase()).join('') || '?'
}

/**
 * "Akkauntlar" bo'limi. `onSwitched` — varaqni yopish uchun.
 *
 * `canAdd={false}` — ota-ona / o'quvchi profili (mijoz, 2026-10-08: "endi akkaunt qo'shish degan joyi
 * kerakmas"): oiladagi farzandlar tepadagi almashtirgichda, qo'shish shart emas. Ro'yxat u yerda faqat
 * bir nechta akkaunt bo'lsa chiqadi — o'qituvchi farzandi akkauntidan o'z akkauntiga qaytishi uchun.
 */
export function AccountsCard({ onSwitched, canAdd = true }) {
  const { user, switchAccount } = useSession()
  const q = useAsync(() => api.get('/tg/accounts'), [user?.id])
  const [adding, setAdding] = useState(false)
  const [busyId, setBusyId] = useState(null)
  const [error, setError] = useState(null)

  // Telegram'siz sessiya — almashtirish yo'q, bo'lim ham yo'q.
  if (q.error && /telegram/i.test(q.error)) return null

  const accounts = q.data?.accounts ?? []
  // Without the add row, a single account has nothing to switch to — no section at all.
  if (!canAdd && accounts.length < 2) return null

  const pick = async (account) => {
    if (account.active || busyId) return
    haptic('light')
    setBusyId(account.userId)
    setError(null)
    try {
      await switchAccount(account.userId)
      haptic('medium')
      onSwitched?.()
    } catch (e) {
      setError(e.message)
      q.reload()
    } finally {
      setBusyId(null)
    }
  }

  const forget = async (account) => {
    haptic('light')
    setError(null)
    try {
      await api.del(`/tg/accounts/${encodeURIComponent(account.userId)}`)
      q.reload()
    } catch (e) {
      setError(e.message)
    }
  }

  return (
    <>
      <Card title="Akkauntlar">
        {q.loading && !q.data && <p className="px-4 pb-4 text-[13px] text-slate-400">Yuklanmoqda…</p>}
        {accounts.map((a) => (
          <div key={a.userId} className="flex items-center gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0">
            <button
              type="button"
              onClick={() => pick(a)}
              disabled={Boolean(busyId)}
              className="flex min-w-0 flex-1 items-center gap-3 text-left disabled:opacity-60"
            >
              <span
                className={
                  'flex h-11 w-11 shrink-0 items-center justify-center rounded-full text-[14px] font-bold ' +
                  (a.active ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-600')
                }
              >
                {initials(a.fullName)}
              </span>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-[15px] font-semibold">{a.fullName}</span>
                <span className="block truncate text-[13px] text-slate-500">
                  {busyId === a.userId ? 'Almashtirilmoqda…' : a.subtitle}
                </span>
              </span>
              {a.active && <Check className="h-5 w-5 shrink-0 text-emerald-600" aria-label="Hozirgi akkaunt" />}
            </button>
            {!a.active && (
              <button
                type="button"
                onClick={() => forget(a)}
                aria-label={`${a.fullName} — ro'yxatdan olib tashlash`}
                className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-slate-300 hover:bg-slate-100 hover:text-slate-500"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>
        ))}
        {canAdd && (
        <button
          type="button"
          onClick={() => {
            haptic('light')
            setAdding(true)
          }}
          className="flex w-full items-center gap-3 border-t border-slate-100 px-4 py-3 text-left"
        >
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-brand/25 text-brand-ink">
            <Plus className="h-5 w-5" />
          </span>
          <span className="min-w-0 flex-1">
            <span className="block text-[15px] font-semibold">Akkaunt qo'shish</span>
            <span className="block text-[13px] text-slate-500">Farzandingiz logini — bir marta</span>
          </span>
        </button>
        )}
        {error && <p className="px-4 pb-3 text-[13px] text-red-600">{error}</p>}
      </Card>

      {adding && (
        <AddAccountSheet
          onClose={() => setAdding(false)}
          onAdded={() => {
            setAdding(false)
            onSwitched?.()
          }}
        />
      )}
    </>
  )
}

/** Login va parol — qo'shilgan akkauntga darhol o'tiladi, hozirgisi ro'yxatda qoladi. */
function AddAccountSheet({ onClose, onAdded }) {
  const { addAccount } = useSession()
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
      await addAccount(login.trim(), password)
      haptic('medium')
      onAdded()
    } catch (err) {
      setError(err.message)
      setBusy(false)
    }
  }

  return (
    <BottomSheet label="Akkaunt qo'shish" onClose={busy ? undefined : onClose}>
      <form onSubmit={submit}>
        <p className="text-[18px] font-bold">Akkaunt qo'shish</p>
        <p className="mt-1.5 text-[14px] leading-relaxed text-slate-500">
          Maktab bergan login va parolni bir marta kiriting. Keyin akkauntlar orasida parolsiz almashasiz.
        </p>
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
        <PasswordField
          value={password}
          onChange={(e) => setPassword(e.target.value)}
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
          {busy ? 'Tekshirilmoqda…' : "Qo'shish va o'tish"}
        </button>
        <button
          type="button"
          onClick={onClose}
          disabled={busy}
          className="mt-2 w-full py-3 text-[15px] font-semibold text-slate-500"
        >
          Bekor qilish
        </button>
      </form>
    </BottomSheet>
  )
}

/** O'qituvchi hero'sidagi tugma: varaqda akkauntlar, qo'shish va "Chiqish". */
export function AccountsButton() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button
        type="button"
        onClick={() => {
          haptic('light')
          setOpen(true)
        }}
        aria-label="Akkauntlar"
        className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink/10 text-brand-ink"
      >
        <Users className="h-5 w-5" />
      </button>
      {open && (
        <BottomSheet label="Akkauntlar" onClose={() => setOpen(false)} flush>
          <AccountsCard onSwitched={() => setOpen(false)} />
          <LogoutButton variant="block" />
        </BottomSheet>
      )}
    </>
  )
}

/** Pastdan chiqadigan varaq — `LogoutButton` dagi bilan bir xil ko'rinish. */
function BottomSheet({ label, onClose, children, flush = false }) {
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={label}
      onClick={() => onClose?.()}
      className="fixed inset-0 z-50 flex items-end bg-black/40"
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className={
          'mx-auto max-h-[85vh] w-full max-w-md overflow-y-auto rounded-t-[26px] pb-[calc(20px+var(--tg-safe-bottom))] pt-5 text-brand-ink ' +
          (flush ? 'bg-ground px-0' : 'bg-white px-5')
        }
      >
        {flush && (
          <div className="mb-2 flex items-center gap-2 px-5">
            <UserRound className="h-5 w-5" />
            <p className="text-[18px] font-bold">{label}</p>
          </div>
        )}
        {children}
      </div>
    </div>
  )
}
