import { useEffect, useRef, useState } from 'react'
import type { LucideIcon } from 'lucide-react'
import {
  Archive, CalendarDays, CalendarPlus, Check, Copy, FileSignature, GraduationCap, Hourglass, IdCard,
  KeyRound, Languages, MapPin, MoreVertical, Pencil, Phone, RotateCcw, ShieldCheck, User, UserMinus, Users, Wallet,
} from 'lucide-react'
import type { StudentNotebook } from '@/api/services/studentNotebook'
import type { StudentCard } from '@/api/services/studentProfile'
import type { StudentStatusTag } from '@/api/services/studentStatuses'
import { genderLabels } from '@/config/constants'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { formatPhone } from '@/lib/phone'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { StatusChip } from '@/pages/admin/students/StatusChip'

const studentLanguages: Record<string, string> = {
  uz: "O'zbek",
  ru: 'Rus',
  en: 'Ingliz',
  kaa: 'Qoraqalpoq',
}

function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

interface Props {
  data: StudentNotebook
  card: StudentCard | null
  statuses: StudentStatusTag[]
  /** Balans (haq − qarz). `null` — moliya huquqi yo'q yoki yuklanmadi: qator ko'rinmaydi. */
  balance: number | null
  canTakePayment: boolean
  onStatusChange: (statusId: string) => void
  onPay: () => void
  onContract: () => void
  onEdit: () => void
  onPassword: () => void
  onArchive: () => void
  onRestore: () => void
  /** "Chiqib ketdi — abonementni bekor qilish"; berilmasa (huquq yo'q / arxivda) menyuda yo'q. */
  onWithdraw?: () => void
}

/**
 * O'quvchi kartochkasining chap ustuni (mijoz, 2026-10-06: EduSchool'dagi TUZILISH — chapda doimiy
 * kartochka, o'ngda asosiy tab'lar va "Ko'proq"). Ko'rinish o'zimizniki; bu yerda faqat sahifa
 * ochiq turganda doim kerak bo'ladigan ma'lumot va amallar.
 */
export function ProfileSidebar({
  data, card, statuses, balance, canTakePayment,
  onStatusChange, onPay, onContract, onEdit, onPassword, onArchive, onRestore, onWithdraw,
}: Props) {
  const archived = card?.student.isArchived ?? false
  const odob = data.disciplinePoints.reduce((sum, p) => sum + p.points, 0)

  return (
    <Card className="relative space-y-5">
      {/* "⋮" — kamroq ishlatiladigan amallar, kartochka burchagida (EduSchool tuzilishi). */}
      <div className="absolute right-3 top-3">
        <ActionsMenu
          archived={archived}
          canArchive={!!card}
          onPassword={onPassword}
          onArchive={onArchive}
          onRestore={onRestore}
          onWithdraw={onWithdraw}
        />
      </div>

      {/* ---------- Kim ---------- */}
      <div className="flex flex-col items-center text-center">
        <div className="flex h-24 w-24 items-center justify-center overflow-hidden rounded-full bg-brand-50 text-3xl font-semibold text-brand-600">
          {data.photoUrl ? (
            <img src={data.photoUrl} alt={data.fullName} className="h-full w-full object-cover" />
          ) : (
            initials(data.fullName)
          )}
        </div>
        <h1 className="mt-3 text-lg font-semibold leading-snug text-slate-800">{data.fullName}</h1>
        <div className="mt-1.5 flex flex-wrap justify-center gap-1.5">
          {card?.statusName && <StatusChip name={card.statusName} color={card.statusColor} />}
          {archived && (
            <span className="rounded-md bg-slate-200 px-2 py-0.5 text-xs font-medium text-slate-600">Arxivda</span>
          )}
        </div>
        <CopyId id={data.id} />
        {statuses.length > 0 && card && (
          <select
            value={card.statusId ?? ''}
            onChange={(e) => onStatusChange(e.target.value)}
            title="Holat tagi"
            aria-label="Holat tagi"
            className="mt-3 w-full rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
          >
            <option value="">Holatsiz</option>
            {statuses.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        )}
      </div>

      {/* ---------- Ko'rsatkichlar ---------- */}
      <div className="space-y-2">
        {balance !== null && (
          <Metric icon={Wallet} label="Balans" value={formatMoney(balance)} tone={balance < 0 ? 'bad' : balance > 0 ? 'good' : 'plain'} />
        )}
        <Metric icon={ShieldCheck} label="Odob (intizom)" value={odob > 0 ? `+${odob}` : String(odob)} tone={odob < 0 ? 'bad' : 'plain'} />
        {card && <Metric icon={Hourglass} label="Maktabda" value={`${card.activeDays} kun`} tone="plain" />}
      </div>

      {/* ---------- Amallar ---------- */}
      <div className="space-y-2">
        {canTakePayment && !archived && (
          <Button onClick={onPay} className="w-full justify-center py-2.5 text-base">
            <Wallet className="h-5 w-5" /> To'lov qilish
          </Button>
        )}
        <div className="grid grid-cols-2 gap-2">
          <Button variant="secondary" onClick={onContract} className="min-w-0 justify-center px-2">
            <FileSignature className="h-4 w-4 shrink-0" /> <span className="truncate">Shartnoma</span>
          </Button>
          {card && (
            <Button variant="secondary" onClick={onEdit} className="min-w-0 justify-center px-2">
              <Pencil className="h-4 w-4 shrink-0" /> <span className="truncate">Tahrirlash</span>
            </Button>
          )}
        </div>
      </div>

      {/* ---------- Asosiy ma'lumotlar ---------- */}
      <div>
        <h2 className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Asosiy ma'lumotlar</h2>
        <div className="space-y-3">
          {card && <Row icon={Phone} label="Telefon" value={formatPhone(card.phone) || '—'} />}
          <Row icon={User} label="Ota-ona" value={data.parentFullName || '—'} />
          <Row icon={Phone} label="Ota-ona telefoni" value={formatPhone(data.parentPhone) || '—'} />
          <Row icon={GraduationCap} label="Sinf" value={data.className || '—'} />
          <Row icon={User} label="Sinf rahbari" value={data.homeroomTeacher || '—'} />
          <Row icon={Users} label="Guruh" value={data.subGroup === 0 ? 'Butun sinf' : `${data.subGroup}-guruh`} />
          <Row icon={CalendarDays} label="Tug'ilgan kun" value={data.birthDate ? formatDate(data.birthDate) : '—'} />
          <Row icon={User} label="Jinsi" value={genderLabels[data.gender as 'male' | 'female'] ?? data.gender ?? '—'} />
          <Row icon={CalendarPlus} label="Qabul sanasi" value={data.enrollmentDate ? formatDate(data.enrollmentDate) : '—'} />
          {card && (
            <Row icon={Languages} label="O'qish tili" value={card.language ? studentLanguages[card.language] ?? card.language : '—'} />
          )}
          <Row icon={MapPin} label="Manzil" value={data.address || '—'} />
          {card && <Row icon={KeyRound} label="Login" value={card.login || '—'} />}
          {card && <PasswordRow password={card.password ?? null} hasLogin={!!card.login} onNew={onPassword} />}
          {archived && (
            <Row
              icon={Archive}
              label="Arxiv"
              value={[card?.student.archivedAt ? formatDate(card.student.archivedAt) : null, card?.student.archiveReason]
                .filter(Boolean)
                .join(' · ') || 'Arxivda'}
            />
          )}
        </div>
      </div>

      {/* ---------- Hujjatlar ---------- */}
      {(data.photoUrl || data.parentPassportUrl || card?.documentUrl) && (
        <div className="space-y-1.5 border-t border-slate-100 pt-4">
          {data.photoUrl && <DocLink href={data.photoUrl} label="O'quvchi hujjati / surati" />}
          {card?.documentUrl && <DocLink href={card.documentUrl} label="Hujjat nusxasi" />}
          {data.parentPassportUrl && <DocLink href={data.parentPassportUrl} label="Ota-ona passporti" />}
        </div>
      )}
    </Card>
  )
}

function Metric({ icon: Icon, label, value, tone }: { icon: LucideIcon; label: string; value: string; tone: 'good' | 'bad' | 'plain' }) {
  return (
    <div className="flex items-center gap-3 rounded-xl bg-slate-50 px-3 py-2.5">
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-white text-brand-600">
        <Icon className="h-4 w-4" />
      </span>
      <div className="min-w-0">
        <p
          className={cn(
            'text-base font-semibold tabular-nums',
            tone === 'bad' ? 'text-red-600' : tone === 'good' ? 'text-emerald-600' : 'text-slate-800',
          )}
        >
          {value}
        </p>
        <p className="text-xs text-slate-400">{label}</p>
      </div>
    </div>
  )
}

function Row({ icon: Icon, label, value }: { icon: LucideIcon; label: string; value: string }) {
  return (
    <div className="flex items-start gap-2.5">
      <Icon className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
      <div className="min-w-0">
        <p className="break-words text-sm font-medium text-slate-700">{value}</p>
        <p className="text-xs text-slate-400">{label}</p>
      </div>
    </div>
  )
}

/**
 * Parol: boshlang'ich parol bo'lsa — o'zi va nusxalash. O'quvchi tizimga kirib o'zgartirgan bo'lsa,
 * u ko'rsatilmaydi (faqat hash qoladi) — yonida yangisini berish tugmasi.
 */
function PasswordRow({ password, hasLogin, onNew }: { password: string | null; hasLogin: boolean; onNew: () => void }) {
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    if (!password) return
    try {
      await navigator.clipboard.writeText(password)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      /* brauzer ruxsat bermadi — parol baribir ekranda */
    }
  }
  return (
    <div className="flex items-start gap-2.5">
      <KeyRound className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
      <div className="min-w-0 flex-1">
        {password ? (
          <p className="flex items-center gap-1.5 text-sm font-medium text-slate-700">
            <span className="break-all font-mono">{password}</span>
            <button
              type="button"
              onClick={copy}
              title="Nusxalash"
              aria-label="Parolni nusxalash"
              className="rounded p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
            >
              {copied ? <Check className="h-3.5 w-3.5 text-emerald-500" /> : <Copy className="h-3.5 w-3.5" />}
            </button>
          </p>
        ) : (
          <p className="text-sm font-medium text-slate-400">{hasLogin ? "Ko'rinmaydi" : '—'}</p>
        )}
        <p className="text-xs text-slate-400">
          Parol{!password && hasLogin ? " · o'quvchi tizimga kirgan" : ''}
        </p>
      </div>
      {!password && hasLogin && (
        <button
          type="button"
          onClick={onNew}
          className="shrink-0 rounded-md px-2 py-0.5 text-xs font-medium text-brand-600 hover:bg-brand-50"
        >
          Yangi parol
        </button>
      )}
    </div>
  )
}

function DocLink({ href, label }: { href: string; label: string }) {
  return (
    <a href={href} target="_blank" rel="noreferrer" className="flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline">
      <IdCard className="h-4 w-4" /> {label}
    </a>
  )
}

/** ID — qisqa ko'rinadi, nusxalashda to'liq olinadi. */
function CopyId({ id }: { id: string }) {
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(id)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      /* brauzer ruxsat bermadi — ID baribir ekranda (title) */
    }
  }
  return (
    <button
      type="button"
      onClick={copy}
      title={id}
      className="mt-2 inline-flex items-center gap-1.5 rounded-md px-2 py-0.5 text-xs text-slate-400 hover:bg-slate-100 hover:text-slate-600"
    >
      ID: <span className="font-mono">{id.slice(0, 8)}</span>
      {copied ? <Check className="h-3.5 w-3.5 text-emerald-500" /> : <Copy className="h-3.5 w-3.5" />}
    </button>
  )
}

/** "⋮" — kamroq ishlatiladigan amallar. Tashqariga bosilsa yoki Esc — yopiladi. */
function ActionsMenu({
  archived, canArchive, onPassword, onArchive, onRestore, onWithdraw,
}: {
  archived: boolean
  canArchive: boolean
  onPassword: () => void
  onArchive: () => void
  onRestore: () => void
  onWithdraw?: () => void
}) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const item = 'flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50'
  const run = (fn: () => void) => () => {
    setOpen(false)
    fn()
  }

  return (
    <div ref={ref} className="relative">
      <Button variant="secondary" onClick={() => setOpen((o) => !o)} aria-label="Boshqa amallar" aria-expanded={open} className="px-2.5">
        <MoreVertical className="h-4 w-4" />
      </Button>
      {open && (
        <div role="menu" className="absolute right-0 z-20 mt-1 w-52 overflow-hidden rounded-xl border border-slate-200 bg-white py-1 shadow-lg">
          <button type="button" role="menuitem" className={item} onClick={run(onPassword)}>
            <KeyRound className="h-4 w-4 text-slate-400" /> Parol
          </button>
          {onWithdraw && (
            <button type="button" role="menuitem" className={item} onClick={run(onWithdraw)}>
              <UserMinus className="h-4 w-4 text-slate-400" /> Chiqib ketdi — abonement
            </button>
          )}
          {archived ? (
            <button type="button" role="menuitem" className={item} onClick={run(onRestore)}>
              <RotateCcw className="h-4 w-4 text-slate-400" /> Arxivdan qaytarish
            </button>
          ) : (
            canArchive && (
              <button type="button" role="menuitem" className={cn(item, 'text-red-600')} onClick={run(onArchive)}>
                <Archive className="h-4 w-4" /> Arxivlash
              </button>
            )
          )}
        </div>
      )}
    </div>
  )
}
