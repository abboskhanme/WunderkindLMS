/**
 * Kechki dars / yotoqxona ro'yxati — qo'lda (mijoz, 2026-10-11: "o'quvchilarni qo'lda qo'shadigan
 * qilish, abonomentga qaralmasin"). Chapda — hozirgi ro'yxat (chiqarish), o'ngda — qidiruv bilan
 * qo'shish. Har sessiyaning o'z ro'yxati; chiqarilgan o'quvchining o'tgan kunlardagi davomati qoladi.
 */
import { useEffect, useMemo, useState } from 'react'
import { Check, Search, UserMinus, UserPlus } from 'lucide-react'
import {
  addToBoardingRoster,
  getBoardingCandidates,
  getBoardingRoster,
  removeFromBoardingRoster,
  type BoardingCandidate,
  type BoardingRosterRow,
  type BoardingSession,
} from '@/api/services/boardingAttendance'
import { billingErrorMessage } from '@/api/services/billingError'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'

export function BoardingRosterModal({
  open,
  session,
  title,
  onClose,
  onChanged,
}: {
  open: boolean
  session: BoardingSession
  title: string
  onClose: () => void
  /** Ro'yxat o'zgardi — davomat sahifasi qayta yuklanadi. */
  onChanged: () => void
}) {
  const [roster, setRoster] = useState<BoardingRosterRow[] | null>(null)
  const [rosterQuery, setRosterQuery] = useState('')
  const [query, setQuery] = useState('')
  const [candidates, setCandidates] = useState<BoardingCandidate[]>([])
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [removing, setRemoving] = useState<string | null>(null)
  const [confirmRemove, setConfirmRemove] = useState<BoardingRosterRow | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    if (!open) return
    let active = true
    getBoardingRoster(session)
      .then((r) => active && setRoster(r))
      .catch((e: unknown) => active && setError(billingErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")))
    return () => {
      active = false
    }
  }, [open, session, tick])

  // Nomzodlar — qidiruv bilan (yozish to'xtagach).
  useEffect(() => {
    if (!open) return
    let active = true
    const t = setTimeout(() => {
      getBoardingCandidates(session, query.trim())
        .then((c) => active && setCandidates(c))
        .catch(() => active && setCandidates([]))
    }, 250)
    return () => {
      active = false
      clearTimeout(t)
    }
  }, [open, session, query, tick])

  useEffect(() => {
    if (open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna yopilganda holatni tozalaymiz (maqsadli)
    setPicked(new Set())
    setQuery('')
    setRosterQuery('')
    setError(null)
    setConfirmRemove(null)
  }, [open])

  const shownRoster = useMemo(() => {
    const q = rosterQuery.trim().toLowerCase()
    return (roster ?? []).filter(
      (r) => !q || r.fullName.toLowerCase().includes(q) || r.className.toLowerCase().includes(q) || (r.trackName ?? '').toLowerCase().includes(q),
    )
  }, [roster, rosterQuery])

  const toggle = (id: string) =>
    setPicked((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const add = async () => {
    if (picked.size === 0) return
    setBusy(true)
    setError(null)
    try {
      await addToBoardingRoster(session, [...picked])
      setPicked(new Set())
      setTick((n) => n + 1)
      onChanged()
    } catch (e) {
      setError(billingErrorMessage(e, "Qo'shib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const remove = async (row: BoardingRosterRow) => {
    setRemoving(row.studentId)
    setError(null)
    try {
      await removeFromBoardingRoster(session, row.studentId)
      setConfirmRemove(null)
      setTick((n) => n + 1)
      onChanged()
    } catch (e) {
      setError(billingErrorMessage(e, "Chiqarib bo'lmadi"))
    } finally {
      setRemoving(null)
    }
  }

  return (
    <Modal open={open} onClose={onClose} title={`${title} — ro'yxat`} size="xl">
      <div className="grid gap-5 lg:grid-cols-2">
        {/* Hozirgi ro'yxat */}
        <div className="min-w-0">
          <div className="mb-2 flex items-center justify-between gap-2">
            <p className="font-semibold text-slate-800">
              Ro'yxatda {roster ? <span className="text-slate-400">· {roster.length} ta</span> : null}
            </p>
            <label className="flex h-9 w-52 items-center gap-2 rounded-lg border border-slate-200 px-2">
              <Search className="h-4 w-4 text-slate-400" />
              <input
                value={rosterQuery}
                onChange={(e) => setRosterQuery(e.target.value)}
                placeholder="Ism, sinf, guruh"
                className="min-w-0 flex-1 bg-transparent text-sm outline-none"
              />
            </label>
          </div>
          {!roster ? (
            <Loader label="Yuklanmoqda..." />
          ) : (
            <ul className="max-h-[55vh] divide-y divide-slate-100 overflow-y-auto rounded-xl border border-slate-100">
              {shownRoster.map((r) => (
                <li key={r.studentId} className="flex items-center gap-3 px-3 py-2">
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-medium text-slate-800">{r.fullName}</span>
                    <span className="block truncate text-xs text-slate-400">
                      {r.className || '—'}
                      {r.trackName && ` · ${r.trackName}`}
                    </span>
                  </span>
                  {confirmRemove?.studentId === r.studentId ? (
                    <span className="flex shrink-0 items-center gap-1">
                      <Button variant="secondary" onClick={() => setConfirmRemove(null)} disabled={removing === r.studentId}>
                        Yo'q
                      </Button>
                      <Button onClick={() => void remove(r)} disabled={removing === r.studentId}>
                        Chiqarish
                      </Button>
                    </span>
                  ) : (
                    <button
                      type="button"
                      onClick={() => setConfirmRemove(r)}
                      title="Ro'yxatdan chiqarish (bugundan)"
                      className="shrink-0 rounded-lg p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600"
                    >
                      <UserMinus className="h-4 w-4" />
                    </button>
                  )}
                </li>
              ))}
              {shownRoster.length === 0 && (
                <li className="px-3 py-8 text-center text-sm text-slate-400">
                  {roster.length === 0 ? "Ro'yxat bo'sh — o'ngdan o'quvchilarni qo'shing." : "Mos o'quvchi yo'q"}
                </li>
              )}
            </ul>
          )}
        </div>

        {/* Qo'shish */}
        <div className="min-w-0">
          <div className="mb-2 flex items-center justify-between gap-2">
            <p className="font-semibold text-slate-800">Qo'shish</p>
            <label className="flex h-9 w-52 items-center gap-2 rounded-lg border border-slate-200 px-2">
              <Search className="h-4 w-4 text-slate-400" />
              <input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Ism yoki sinf"
                className="min-w-0 flex-1 bg-transparent text-sm outline-none"
              />
            </label>
          </div>
          <ul className="max-h-[48vh] space-y-1 overflow-y-auto">
            {candidates.map((c) => {
              const on = picked.has(c.studentId)
              return (
                <li key={c.studentId}>
                  <button
                    type="button"
                    onClick={() => toggle(c.studentId)}
                    className={cn(
                      'flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors',
                      on ? 'border-brand-300 bg-brand-50' : 'border-slate-100 hover:bg-slate-50',
                    )}
                  >
                    <span
                      className={cn(
                        'flex h-4 w-4 shrink-0 items-center justify-center rounded border',
                        on ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300',
                      )}
                    >
                      {on && <Check className="h-3 w-3" />}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium text-slate-700">{c.fullName}</span>
                      <span className="block truncate text-xs text-slate-400">
                        {c.className || '—'}
                        {c.trackName && ` · ${c.trackName}`}
                      </span>
                    </span>
                  </button>
                </li>
              )
            })}
            {candidates.length === 0 && (
              <li className="px-3 py-8 text-center text-sm text-slate-400">
                {query.trim() ? "Topilmadi (ro'yxatdagilar bu yerda chiqmaydi)" : "O'quvchi ismini yozing"}
              </li>
            )}
          </ul>
          <div className="mt-3 flex items-center justify-between gap-2">
            <span className="text-xs text-slate-400">{candidates.length >= 100 ? "Birinchi 100 ta — qidiruvni toraytiring" : ''}</span>
            <Button onClick={() => void add()} disabled={busy || picked.size === 0}>
              <UserPlus className="h-4 w-4" /> {busy ? "Qo'shilmoqda…" : `Qo'shish${picked.size ? ` (${picked.size})` : ''}`}
            </Button>
          </div>
        </div>
      </div>
      {error && <p className="mt-3 text-sm text-red-600">{error}</p>}
    </Modal>
  )
}
