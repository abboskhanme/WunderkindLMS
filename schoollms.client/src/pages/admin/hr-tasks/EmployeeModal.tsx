import { useMemo, useState } from 'react'
import { Check, Search, UserPlus, Users } from 'lucide-react'
import {
  createEmployee,
  hrErrorCode,
  hrErrorMessage,
  hrErrorStatus,
  issueInvite,
  searchUserCandidates,
  setEmployeeManagers,
  updateEmployee,
} from '@/api/services/hrTasks'
import type { HrEmployee, HrEmployeeCreateResult, HrPosition, HrUserCandidate } from '@/types/hrTasks'
import { Button } from '@/components/ui/Button'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { Modal } from '@/components/ui/Modal'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { EmptyState, Field, Notice, Skeleton, inputCls } from '@/components/hr-tasks/ui'
import { useDebounced } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { HR_ROLE_HINT, HR_ROLE_LABEL, USER_ROLE_LABEL } from '@/components/hr-tasks/lib'
import { formatPhone, localDigits } from '@/lib/phone'
import { cn } from '@/lib/utils'
import { InviteLinkBox } from './InviteLink'
import { HR_MAX_MANAGERS, ManagerPicker } from './ManagerPicker'
import { hrSameManagerIds } from './employeeTree'

type HrRole = HrEmployee['hrRole']

const HR_ROLES: HrRole[] = ['employee', 'manager', 'director']

/** Human text for the backend's 409 `hr_manager_cycle`. */
export const HR_MANAGER_CYCLE_MESSAGE = 'Bu xodim tanlangan rahbarning rahbari — halqa hosil bo‘ladi. Boshqa rahbar tanlang.'

function isManagerCycle(err: unknown): boolean {
  return hrErrorCode(err) === 'hr_manager_cycle' || hrErrorStatus(err) === 409
}

/** `90 123 45 67` → `+998901234567` (contract form). */
function toContractPhone(value: string | null | undefined): string {
  const digits = localDigits(value)
  return digits ? `+998${digits}` : ''
}

interface Saved {
  employee: HrEmployeeCreateResult
  invite: { code: string | null; url: string | null } | null
}

/**
 * Add / edit an employee (the old `EmployeeDialog`, hr-tasks.md §2): a new employee is
 * either an existing user OR name + phone (the system creates a `staff` user, the password
 * is shown once). After saving, the invite link is shown. Panel access is NOT granted
 * here — that is Boshqaruv → Rollar. Mounted with a `key`.
 */
export function EmployeeModal({
  employee,
  positions,
  canSetHrRole,
  onClose,
  onSaved,
}: {
  /** `null` — a new employee. */
  employee: HrEmployee | null
  positions: HrPosition[]
  /** Director — may change the HR scope (`hrRole`). */
  canSetHrRole: boolean
  onClose: () => void
  /** Refresh the list (the modal may stay open to show the invite link). */
  onSaved: (message: string, close: boolean) => void
}) {
  const isEdit = employee !== null
  const [mode, setMode] = useState<'existing' | 'new'>('existing')
  const [candidate, setCandidate] = useState<HrUserCandidate | null>(null)
  const [userSearch, setUserSearch] = useState('')
  const userQ = useDebounced(userSearch.trim())

  const [fullName, setFullName] = useState(employee?.fullName ?? '')
  const [phone, setPhone] = useState(employee?.phone ?? '')
  const [positionId, setPositionId] = useState(employee?.positionId ?? '')
  const [hrRole, setHrRole] = useState<HrRole>(employee?.hrRole ?? 'employee')
  const [managerIds, setManagerIds] = useState<string[]>(employee?.managerIds ?? [])
  const [note, setNote] = useState(employee?.note ?? '')

  const [submitted, setSubmitted] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [managersError, setManagersError] = useState<string | null>(null)
  const [saved, setSaved] = useState<Saved | null>(null)

  const candidates = useLoad(!isEdit && mode === 'existing' ? `candidates|${userQ}` : null, () => searchUserCandidates(userQ))

  const managerNameById = useMemo(() => {
    const map = new Map<string, string>()
    const ids = employee?.managerIds ?? []
    const names = employee?.managerNames ?? []
    ids.forEach((id, index) => {
      const name = names[index]
      if (id && name) map.set(id, name)
    })
    return map
  }, [employee])

  const usingExisting = !isEdit && mode === 'existing'
  // Editing: name and phone come from Boshqaruv → Xodimlar (client, 2026-09-28) — not validated here.
  const nameError =
    usingExisting || isEdit ? null : fullName.trim().length < 3 ? 'F.I.Sh. kamida 3 ta belgidan iborat bo‘lsin' : fullName.trim().length > 160 ? 'F.I.Sh. 160 belgidan oshmasin' : null
  // A new user needs a phone (it is the login). On edit the phone is Boshqaruv's, not ours.
  const phoneError =
    usingExisting || isEdit ? null : localDigits(phone).length !== 9 ? 'Telefon: 9 ta raqam kiriting (masalan 90 123 45 67)' : null
  const candidateError = usingExisting && !candidate ? 'Foydalanuvchini tanlang' : null
  const noteError = note.trim().length > 2000 ? 'Izoh 2000 belgidan oshmasin' : null
  const managersLimit = managerIds.length > HR_MAX_MANAGERS ? `Rahbarlar soni ${HR_MAX_MANAGERS} tadan oshmasin` : null

  async function save(): Promise<void> {
    setSubmitted(true)
    if (nameError || phoneError || candidateError || noteError || managersLimit) return
    setBusy(true)
    setError(null)
    try {
      if (employee) {
        // Order: MANAGERS first — on a cycle (409) nothing is half-saved.
        const nextManagers = [...new Set(managerIds.filter((id) => id && id !== employee.id))]
        if (!hrSameManagerIds(employee.managerIds ?? [], nextManagers)) {
          try {
            await setEmployeeManagers(employee.id, nextManagers)
          } catch (err) {
            if (isManagerCycle(err)) {
              setManagersError(HR_MANAGER_CYCLE_MESSAGE)
              setError(HR_MANAGER_CYCLE_MESSAGE)
              return
            }
            throw err
          }
        }
        await updateEmployee(employee.id, {
          note: note.trim() || null,
          ...(canSetHrRole ? { hrRole } : {}),
        })
        onSaved('Xodim yangilandi', true)
        return
      }

      const created = await createEmployee({
        userId: usingExisting ? candidate?.id : null,
        fullName: usingExisting ? (candidate?.fullName ?? '') : fullName.trim(),
        phone: usingExisting ? toContractPhone(candidate?.phone) : toContractPhone(phone),
        positionId: positionId || null,
        note: note.trim() || null,
        ...(canSetHrRole ? { hrRole } : {}),
        ...(managerIds.length > 0 ? { managerIds } : {}),
      })

      let invite: Saved['invite'] = null
      if (!created.telegramUserId) {
        try {
          const issued = await issueInvite(created.id)
          invite = { code: issued.inviteCode, url: issued.inviteUrl }
        } catch {
          invite = created.inviteCode ? { code: created.inviteCode, url: null } : null
        }
      }
      setSaved({ employee: created, invite })
      onSaved('Xodim qo‘shildi', false)
    } catch (err) {
      setError(hrErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  if (saved) {
    const credentials = saved.employee.credentials
    return (
      <Modal
        open
        onClose={onClose}
        title="Xodim saqlandi"
        footer={
          <div className="flex w-full justify-end">
            <Button onClick={onClose}>Tayyor</Button>
          </div>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-500">
            <b className="text-slate-800">{saved.employee.fullName}</b> HR xodimi sifatida qo‘shildi.
          </p>
          {credentials && (
            <>
              <CredentialsBox credentials={{ login: credentials.login, password: credentials.password, role: 'staff' }} />
              <p className="text-xs text-slate-400">Parol faqat hozir ko‘rsatiladi — xodimga topshirib qo‘ying.</p>
            </>
          )}
          {saved.invite ? (
            <InviteLinkBox code={saved.invite.code} url={saved.invite.url} />
          ) : (
            <Notice kind="success">Xodimning Telegrami allaqachon bog‘langan — xabarlar botga boradi.</Notice>
          )}
        </div>
      </Modal>
    )
  }

  const positionOptions = positions.filter((p) => p.isActive || p.id === employee?.positionId)

  return (
    <Modal
      open
      onClose={onClose}
      title={isEdit ? 'Xodimni tahrirlash' : 'Yangi xodim'}
      footer={
        <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void save()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </div>
      }
    >
      <form
        className="space-y-4"
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <p className="text-sm text-slate-400">
          {isEdit
            ? 'Ism, telefon va lavozim Boshqaruv → Xodimlar bo‘limida o‘zgartiriladi; bu yerda — rahbarlar va HR doirasi.'
            : 'HR xodimi — bizning tizim foydalanuvchisi. Telefon bot bilan bog‘lashda ham ishlatiladi.'}
        </p>

        {!isEdit && (
          <div className="grid grid-cols-2 gap-1 rounded-xl bg-slate-100 p-1">
            {(
              [
                { value: 'existing', label: 'Mavjud foydalanuvchi', icon: Users },
                { value: 'new', label: 'Yangi (ism + telefon)', icon: UserPlus },
              ] as const
            ).map((option) => (
              <button
                key={option.value}
                type="button"
                onClick={() => setMode(option.value)}
                className={cn(
                  'inline-flex items-center justify-center gap-1.5 rounded-lg px-2 py-2 text-xs font-medium transition-colors sm:text-sm',
                  mode === option.value ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                <option.icon className="h-4 w-4 shrink-0" />
                <span className="truncate">{option.label}</span>
              </button>
            ))}
          </div>
        )}

        {usingExisting ? (
          <Field label="Foydalanuvchi" required error={submitted ? candidateError : null} hint="Xodim, o‘qituvchi, administrator yoki kassir">
            <div className="space-y-2">
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  className={cn(inputCls, 'pl-9')}
                  placeholder="Ism yoki telefon bo‘yicha qidiring"
                  value={userSearch}
                  onChange={(e) => setUserSearch(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') e.preventDefault()
                  }}
                />
              </div>
              <div className="max-h-56 overflow-y-auto rounded-xl border border-slate-200">
                {candidates.loading && !candidates.previous ? (
                  <div className="space-y-2 p-3">
                    {Array.from({ length: 3 }, (_, i) => (
                      <Skeleton key={i} className="h-10 w-full" />
                    ))}
                  </div>
                ) : candidates.error ? (
                  <EmptyState title="Ro‘yxatni yuklab bo‘lmadi" hint={candidates.error} />
                ) : (candidates.data ?? candidates.previous ?? []).length === 0 ? (
                  <EmptyState title="Foydalanuvchi topilmadi" hint="Qidiruvni o‘zgartiring yoki «Yangi» ni tanlang." />
                ) : (
                  <ul className={cn('divide-y divide-slate-100', candidates.loading && 'opacity-60')}>
                    {(candidates.data ?? candidates.previous ?? []).map((user) => {
                      const taken = Boolean(user.employeeId)
                      const checked = candidate?.id === user.id
                      return (
                        <li key={user.id}>
                          <button
                            type="button"
                            disabled={taken}
                            aria-pressed={checked}
                            onClick={() => setCandidate(user)}
                            className="flex w-full items-center gap-3 px-3 py-2.5 text-left transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
                          >
                            <span
                              aria-hidden
                              className={cn(
                                'inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full border',
                                checked ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-300',
                              )}
                            >
                              {checked && <Check className="h-3.5 w-3.5" />}
                            </span>
                            <span className="min-w-0 flex-1">
                              <span className="block truncate text-sm text-slate-800">{user.fullName}</span>
                              <span className="block truncate text-xs text-slate-400">
                                {[USER_ROLE_LABEL[user.role] ?? user.role, user.phone ? formatPhone(user.phone) : null, taken ? 'allaqachon HR xodimi' : null]
                                  .filter(Boolean)
                                  .join(' · ')}
                              </span>
                            </span>
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                )}
              </div>
            </div>
          </Field>
        ) : isEdit ? (
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5">
            <p className="text-sm font-medium text-slate-800">{employee?.fullName}</p>
            <p className="text-xs tabular-nums text-slate-500">
              {[employee?.positionName ?? 'Lavozim belgilanmagan', employee?.phone ? formatPhone(employee.phone) : 'Telefon kiritilmagan'].join(' · ')}
            </p>
          </div>
        ) : (
          <>
            <Field label="F.I.Sh." required error={submitted ? nameError : null}>
              <input className={inputCls} placeholder="Aliyev Aziz Alisher o‘g‘li" value={fullName} onChange={(e) => setFullName(e.target.value)} />
            </Field>
            <Field
              error={submitted ? phoneError : null}
              hint="Login — telefon raqami; parol saqlangach bir marta ko‘rsatiladi"
            >
              <PhoneInput label="Telefon" required value={phone} onChange={setPhone} />
            </Field>
          </>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          {/* Editing: the position is Boshqaruv's (shown in the summary above), not chosen here. */}
          {!isEdit && (
            <Field label="Lavozim" hint="Lavozim — yorliq va guruhlash vositasi; panelga kirish huquqini bermaydi">
              <select className={inputCls} value={positionId} onChange={(e) => setPositionId(e.target.value)}>
                <option value="">Tanlanmagan</option>
                {positionOptions.map((position) => (
                  <option key={position.id} value={position.id}>
                    {position.isActive ? position.name : `${position.name} (nofaol)`}
                  </option>
                ))}
              </select>
            </Field>
          )}
          {canSetHrRole && (
            <Field label="HR doirasi" hint={HR_ROLE_HINT[hrRole]}>
              <select className={inputCls} value={hrRole} onChange={(e) => setHrRole(e.target.value as HrRole)}>
                {HR_ROLES.map((role) => (
                  <option key={role} value={role}>
                    {HR_ROLE_LABEL[role]}
                  </option>
                ))}
              </select>
            </Field>
          )}
        </div>

        <ManagerPicker
          selfId={employee?.id ?? null}
          value={managerIds}
          error={managersError ?? (submitted ? managersLimit : null)}
          resolveName={(id) => managerNameById.get(id) ?? null}
          onChange={(next) => {
            setManagersError(null)
            setManagerIds(next)
          }}
        />

        <Field label="Izoh" error={submitted ? noteError : null}>
          <textarea
            className={`${inputCls} resize-none`}
            rows={3}
            placeholder="Ichki eslatma (ixtiyoriy)"
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
        </Field>

        {error && (
          <p className="text-sm text-red-600" role="alert">
            {error}
          </p>
        )}
      </form>
    </Modal>
  )
}
