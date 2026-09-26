/**
 * Boshqaruv → Xodimlar — o'qituvchilar va boshqa xodimlar BITTA ro'yxatda
 * (docs/modules/employees-unified.md, mijoz 2026-09-26). Farqi faqat "Lavozim" ustunida.
 *
 * MA'LUMOT O'Z JOYIDA QOLADI: o'qituvchilar `teachers` jadvalida (jadval, jurnal, davomat
 * unga bog'langan), boshqa xodimlar `users` (role=staff) da. Sahifa ikkalasini birlashtirib
 * ko'rsatadi, har bir amal esa o'z API'siga boradi:
 *   o'qituvchi — ko'rish (login/parol shu yerda) / tahrirlash / arxiv / qaytarish / o'chirish;
 *   xodim      — login/parol / tahrirlash / o'chirish.
 *
 * RUXSAT: marshrut `staff` YOKI `teachers` bo'limi bilan ochiladi. Faqat bittasi bo'lsa —
 * ro'yxatda faqat o'sha qism, ikkinchisi so'ralmaydi ham (403 ni ekranga chiqarmaymiz).
 * Rollar sahifasida "Xodimlar" berilsa ikkala bo'lim ham beriladi (lib/access.ts).
 *
 * Eski `TeachersPage` va `StaffPage` fayllari o'chirilmadi — shunchaki marshrutdan uzildi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Navigate, useSearchParams } from 'react-router-dom'
import {
  Archive,
  Briefcase,
  Download,
  Eye,
  GraduationCap,
  KeyRound,
  Pencil,
  Plus,
  RotateCcw,
  Search,
  Trash2,
} from 'lucide-react'
import type { AccessRole, Credentials, SchoolClass, Staff, Subject, Teacher } from '@/types'
import {
  archiveTeacher,
  createTeacher,
  deleteTeacher,
  downloadTeacherCredentials,
  getArchivedTeachers,
  getTeachers,
  restoreTeacher,
  updateTeacher,
  type TeacherPayload,
} from '@/api/services/teachers'
import { deleteStaff, getStaff, getStaffCredentials, resetStaffPassword } from '@/api/services/staff'
import { getAccessRoles } from '@/api/services/accessRoles'
import { getSubjects } from '@/api/services/subjects'
import { getClasses } from '@/api/services/classes'
import { getSalaryRates } from '@/api/services/salaryRates'
import { billingErrorMessage, billingErrorStatus } from '@/api/services/billingError'
import { useAuth } from '@/context/auth-context'
import { grantsOf, hasSection } from '@/lib/access'
import { formatPhone } from '@/lib/phone'
import {
  TEACHER_POSITION,
  matchesPosition,
  positionFilterFromParam,
  positionOptions,
} from '@/lib/employees'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { UserAvatar } from '@/components/ui/UserAvatar'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { Toast } from '@/components/ui/Toast'
import { PositionFilter } from '@/components/employees/PositionFilter'
import { TeacherFormModal } from '../teachers/TeacherFormModal'
import { TeacherViewModal } from '../teachers/TeacherViewModal'
import { StaffFormDrawer, type StaffSaveResult } from './StaffFormDrawer'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type Tab = 'active' | 'archived'

interface EmployeeBase {
  /** `teacher:<id>` / `staff:<id>` — ikki jadvalning id'lari to'qnashmasin. */
  key: string
  id: string
  fullName: string
  phone: string
  position: string
  avatarUrl: string | null
}

type Employee =
  | (EmployeeBase & { kind: 'teacher'; teacher: Teacher })
  | (EmployeeBase & { kind: 'staff'; staff: Staff })

const fromTeacher = (t: Teacher): Employee => ({
  kind: 'teacher',
  key: `teacher:${t.id}`,
  id: t.id,
  fullName: t.fullName,
  phone: t.phone ?? '',
  position: TEACHER_POSITION,
  avatarUrl: t.photoUrl ?? null,
  teacher: t,
})

const fromStaff = (s: Staff): Employee => ({
  kind: 'staff',
  key: `staff:${s.id}`,
  id: s.id,
  fullName: s.fullName,
  phone: s.phone ?? '',
  position: s.position,
  avatarUrl: s.avatarUrl ?? null,
  staff: s,
})

const byName = (a: { fullName: string }, b: { fullName: string }) => a.fullName.localeCompare(b.fullName, 'uz')

function currentMonth(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

/** `/admin/teachers` → shu sahifa, `?position=teacher` bilan; `q` va `tab` saqlanadi (global qidiruv). */
export function TeachersRedirect() {
  const [params] = useSearchParams()
  const next = new URLSearchParams(params)
  next.set('position', 'teacher')
  return <Navigate to={`/admin/boshqaruv/staff?${next.toString()}`} replace />
}

export function EmployeesPage() {
  const { user } = useAuth()
  const canTeachers = hasSection(user, 'teachers')
  const canStaff = hasSection(user, 'staff')
  // Rol biriktirish va o'qituvchilar login/paroli eksporti — faqat tizim egasi (server ham shunday).
  const isSuperadmin = user?.role === 'superadmin'
  // O'qituvchi oyligi jadval va toifadan hisoblanadi — moliya ruxsati bo'lsa ko'rsatamiz.
  const canSeeTeacherPay = canTeachers && hasFinanceAccess(user)

  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [archived, setArchived] = useState<Teacher[]>([])
  const [staff, setStaff] = useState<Staff[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  /** Ikki manbadan biri yuklanmadi — qolgani ko'rsatiladi, bu yerda ogohlantirish. */
  const [partialError, setPartialError] = useState<string | null>(null)

  // Ikkinchi darajali ma'lumot — ro'yxatni to'xtatmaydi.
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [roles, setRoles] = useState<AccessRole[]>([])
  const [teacherPay, setTeacherPay] = useState<Map<string, number>>(new Map())

  // URL: `?q=` (global qidiruv), `?tab=archived`, `?position=teacher` (HR'dan eski havola).
  const [searchParams] = useSearchParams()
  const [search, setSearch] = useState(() => searchParams.get('q') ?? '')
  const [tab, setTab] = useState<Tab>(() => (searchParams.get('tab') === 'archived' ? 'archived' : 'active'))
  const [position, setPosition] = useState(() => positionFilterFromParam(searchParams.get('position')))
  // Sahifa ochiq turganda URL o'zgarsa (qidiruvdan boshqa natija) — holatni moslaymiz, effektsiz.
  const urlKey = searchParams.toString()
  const [seenUrlKey, setSeenUrlKey] = useState(urlKey)
  if (urlKey !== seenUrlKey) {
    setSeenUrlKey(urlKey)
    setSearch(searchParams.get('q') ?? '')
    setTab(searchParams.get('tab') === 'archived' ? 'archived' : 'active')
    setPosition(positionFilterFromParam(searchParams.get('position')))
  }
  const activeTab: Tab = canTeachers ? tab : 'active'

  // Oynalar
  const [choosingType, setChoosingType] = useState(false)
  const [teacherFormOpen, setTeacherFormOpen] = useState(false)
  const [editingTeacher, setEditingTeacher] = useState<Teacher | null>(null)
  const [viewingTeacher, setViewingTeacher] = useState<Teacher | null>(null)
  const [archiveTarget, setArchiveTarget] = useState<Teacher | null>(null)
  const [archiveReason, setArchiveReason] = useState('')
  const [staffForm, setStaffForm] = useState<{ editing: Staff | null } | null>(null)
  const [credOf, setCredOf] = useState<Staff | null>(null)
  const [creds, setCreds] = useState<Credentials | null>(null)
  const [credLoading, setCredLoading] = useState(false)
  const [credError, setCredError] = useState<string | null>(null)
  const [toast, setToast] = useState<{ message: string; tone: 'success' | 'error' } | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const notify = (message: string) => setToast({ message, tone: 'success' })
  /** Xato xabari. "Faqat ko'rish" xodimining 403 sini PageAccessGate o'zi aytadi — takrorlamaymiz. */
  const fail = (err: unknown, fallback: string) => {
    if (user?.role === 'staff' && billingErrorStatus(err) === 403) return
    setToast({ message: billingErrorMessage(err, fallback), tone: 'error' })
  }

  /** Asosiy ro'yxat. Holat faqat javobdan keyin o'rnatiladi (effekt ichida sinxron setState yo'q). */
  const fetchLists = useCallback(() => {
    const teachersReq = canTeachers ? Promise.all([getTeachers(), getArchivedTeachers()]) : Promise.resolve(null)
    const staffReq = canStaff ? getStaff() : Promise.resolve(null)
    return Promise.allSettled([teachersReq, staffReq]).then(([t, s]) => {
      const teachersFailed = t.status === 'rejected'
      const staffFailed = s.status === 'rejected'
      if (t.status === 'fulfilled' && t.value) {
        setTeachers(t.value[0])
        setArchived(t.value[1])
      }
      if (s.status === 'fulfilled' && s.value) setStaff(s.value)
      const enabled = Number(canTeachers) + Number(canStaff)
      const failed = Number(teachersFailed) + Number(staffFailed)
      if (enabled > 0 && failed === enabled) {
        const reason = t.status === 'rejected' ? t.reason : s.status === 'rejected' ? s.reason : null
        setLoadError(billingErrorMessage(reason, "Xodimlar ro'yxatini yuklab bo'lmadi"))
      } else {
        setLoadError(null)
        setPartialError(
          teachersFailed
            ? "O'qituvchilar ro'yxatini yuklab bo'lmadi — faqat boshqa xodimlar ko'rsatilmoqda."
            : staffFailed
              ? "Boshqa xodimlar ro'yxatini yuklab bo'lmadi — faqat o'qituvchilar ko'rsatilmoqda."
              : null,
        )
      }
      setLoading(false)
    })
  }, [canTeachers, canStaff])

  useEffect(() => {
    fetchLists()
    if (canTeachers) {
      // O'qituvchi formasi uchun; ruxsat bo'lmasa forma bo'sh ro'yxat bilan ochiladi.
      getSubjects().then(setSubjects).catch(() => undefined)
      getClasses().then(setClasses).catch(() => undefined)
    }
    if (canStaff) getAccessRoles().then(setRoles).catch(() => undefined)
    if (canSeeTeacherPay) {
      getSalaryRates(currentMonth())
        .then((r) => setTeacherPay(new Map(r.teachers.map((t) => [t.id, t.monthlySalary]))))
        .catch(() => undefined)
    }
  }, [fetchLists, canTeachers, canStaff, canSeeTeacherPay])

  const retry = () => {
    setLoading(true)
    setLoadError(null)
    fetchLists()
  }

  /* ---------------- Ro'yxat ---------------- */

  const activeList = useMemo(
    () => [...teachers.map(fromTeacher), ...staff.map(fromStaff)].sort(byName),
    [teachers, staff],
  )
  const archivedList = useMemo(() => archived.map(fromTeacher), [archived])
  const positions = useMemo(() => positionOptions(activeList), [activeList])

  const q = search.trim().toLocaleLowerCase('uz')
  const qDigits = search.replace(/\D/g, '')
  const source = activeTab === 'active' ? activeList : archivedList
  const filtered = source.filter((e) => {
    if (activeTab === 'active' && !matchesPosition(e, position)) return false
    if (!q) return true
    if (e.fullName.toLocaleLowerCase('uz').includes(q)) return true
    // Telefon bo'yicha — kamida 3 raqam, ajratgichlarsiz solishtiriladi.
    return qDigits.length >= 3 && e.phone.replace(/\D/g, '').includes(qDigits)
  })

  const staffPositionsForForm = positions.staffPositions

  /* ---------------- O'qituvchi amallari ---------------- */

  const openNew = () => {
    if (canTeachers && canStaff) setChoosingType(true)
    else if (canTeachers) openTeacherForm(null)
    else openStaffForm(null)
  }

  const openTeacherForm = (t: Teacher | null) => {
    setChoosingType(false)
    setEditingTeacher(t)
    setTeacherFormOpen(true)
  }

  const openStaffForm = (s: Staff | null) => {
    setChoosingType(false)
    setStaffForm({ editing: s })
  }

  const submitTeacher = (values: TeacherPayload) => {
    const target = editingTeacher
    setTeacherFormOpen(false)
    setEditingTeacher(null)
    if (target) {
      updateTeacher(target.id, values)
        .then((u) => {
          setTeachers((prev) => prev.map((t) => (t.id === u.id ? u : t)))
          notify("O'qituvchi saqlandi")
        })
        .catch((e: unknown) => fail(e, "O'qituvchini saqlab bo'lmadi"))
    } else {
      createTeacher(values)
        .then((c) => {
          setTeachers((prev) => [c, ...prev])
          setTab('active')
          setViewingTeacher(c) // login/parolni darrov ko'rsatamiz
        })
        .catch((e: unknown) => fail(e, "O'qituvchini qo'shib bo'lmadi"))
    }
  }

  const confirmArchive = () => {
    if (!archiveTarget) return
    const t = archiveTarget
    const reason = archiveReason.trim()
    const today = new Date().toISOString().slice(0, 10)
    setArchiveTarget(null)
    setArchiveReason('')
    archiveTeacher(t.id, reason)
      .then(() => {
        setTeachers((prev) => prev.filter((x) => x.id !== t.id))
        setArchived((prev) => [{ ...t, isArchived: true, archivedAt: today, archiveReason: reason }, ...prev])
        notify(`${t.fullName} arxivga ko'chirildi`)
      })
      .catch((e: unknown) => fail(e, "Arxivga ko'chirib bo'lmadi"))
  }

  const handleRestore = (t: Teacher) => {
    if (!confirm(`"${t.fullName}" o'qituvchini arxivdan qaytarasizmi?`)) return
    restoreTeacher(t.id)
      .then(() => {
        setArchived((prev) => prev.filter((x) => x.id !== t.id))
        setTeachers((prev) => [{ ...t, isArchived: false, archivedAt: null, archiveReason: null }, ...prev])
        notify(`${t.fullName} arxivdan qaytarildi`)
      })
      .catch((e: unknown) => fail(e, "Arxivdan qaytarib bo'lmadi"))
  }

  const handleDeleteTeacher = (t: Teacher) => {
    if (!confirm(`"${t.fullName}" o'qituvchini BUTUNLAY o'chirasizmi? Bu amalni ortga qaytarib bo'lmaydi.`)) return
    if (!confirm("Aniq ishonchingiz komilmi? Barcha ma'lumotlari o'chadi.")) return
    deleteTeacher(t.id)
      .then(() => {
        setArchived((prev) => prev.filter((x) => x.id !== t.id))
        notify(`${t.fullName} o'chirildi`)
      })
      .catch((e: unknown) => fail(e, "O'chirib bo'lmadi"))
  }

  /* ---------------- Xodim amallari ---------------- */

  const showCredentials = (s: Staff) => {
    setCredOf(s)
    setCreds(null)
    setCredError(null)
    setCredLoading(true)
    getStaffCredentials(s.id)
      .then(setCreds)
      .catch((e: unknown) => setCredError(billingErrorMessage(e, "Login/parolni yuklab bo'lmadi")))
      .finally(() => setCredLoading(false))
  }

  const handleStaffSaved = (s: Staff, { created, roleError }: StaffSaveResult) => {
    setStaffForm(null)
    setStaff((prev) => (created ? [s, ...prev] : prev.map((x) => (x.id === s.id ? s : x))))
    if (roleError) setToast({ message: roleError, tone: 'error' })
    else if (!created) notify('Xodim saqlandi')
    if (created) {
      setTab('active')
      showCredentials(s) // login/parolni darrov ko'rsatamiz
    }
  }

  const handleDeleteStaff = (s: Staff) => {
    if (!confirm(`"${s.fullName}" xodimni o'chirasizmi? Akkaunti ham o'chadi.`)) return
    deleteStaff(s.id)
      .then(() => {
        setStaff((prev) => prev.filter((x) => x.id !== s.id))
        notify(`${s.fullName} o'chirildi`)
      })
      .catch((e: unknown) => fail(e, "Xodimni o'chirib bo'lmadi"))
  }

  /* ---------------- Chizish ---------------- */

  const subtitle = [
    canTeachers && `O'qituvchilar ${teachers.length} ta`,
    canStaff && `Boshqa xodimlar ${staff.length} ta`,
    canTeachers && `Arxivda ${archived.length} ta`,
  ]
    .filter(Boolean)
    .join(' · ')

  const columns = activeTab === 'active' ? 8 : 7

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Xodimlar</h1>
          <p className="text-sm text-slate-400">{loading ? 'Yuklanmoqda...' : subtitle}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {/* Faqat superadmin: o'qituvchilarni login/parol bilan Excel'ga (parol — hali kirmaganlarda). */}
          {isSuperadmin && canTeachers && (
            <Button variant="secondary" onClick={() => downloadTeacherCredentials()}>
              <Download className="h-4 w-4" /> O'qituvchilar login/paroli
            </Button>
          )}
          <Button onClick={openNew}>
            <Plus className="h-4 w-4" /> Yangi xodim
          </Button>
        </div>
      </div>

      {/* Faol | Arxiv — arxiv faqat o'qituvchilarda bor. */}
      {canTeachers && (
        <div className="inline-flex rounded-lg border border-slate-200 bg-white p-1">
          <TabButton active={activeTab === 'active'} onClick={() => setTab('active')}>
            Faol ({activeList.length})
          </TabButton>
          <TabButton active={activeTab === 'archived'} onClick={() => setTab('archived')}>
            Arxiv ({archived.length})
          </TabButton>
        </div>
      )}

      {partialError && (
        <p className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-2 text-sm text-amber-800">
          {partialError}
        </p>
      )}

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative min-w-[200px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Ism yoki telefon bo'yicha qidirish..."
              className={cn(control, 'w-full pl-9')}
            />
          </div>
          {activeTab === 'active' && <PositionFilter value={position} onChange={setPosition} positions={positions} />}
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : loadError ? (
          <div className="flex flex-col items-center gap-3 px-4 py-12 text-center">
            <p className="font-medium text-slate-800">Ma'lumotni yuklab bo'lmadi</p>
            <p className="max-w-md text-sm text-slate-500">{loadError}</p>
            <Button variant="secondary" onClick={retry}>
              Qayta urinish
            </Button>
          </div>
        ) : source.length === 0 ? (
          <div className="flex flex-col items-center gap-3 px-4 py-12 text-center">
            <p className="text-sm text-slate-400">
              {activeTab === 'active' ? "Hali xodim qo'shilmagan." : "Arxivda o'qituvchi yo'q."}
            </p>
            {activeTab === 'active' && (
              <Button variant="secondary" onClick={openNew}>
                <Plus className="h-4 w-4" /> Birinchisini qo'shish
              </Button>
            )}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-12 px-4 py-3">№</th>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Lavozim</th>
                  {activeTab === 'active' ? (
                    <>
                      <th className="px-4 py-3">Rol</th>
                      <th className="px-4 py-3 text-right">Oylik</th>
                      <th className="px-4 py-3">Oxirgi faollik</th>
                    </>
                  ) : (
                    <>
                      <th className="px-4 py-3">Arxiv sanasi</th>
                      <th className="px-4 py-3">Sabab</th>
                    </>
                  )}
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((e, i) => (
                  <tr key={e.key} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-3">
                        <UserAvatar fullName={e.fullName} avatarUrl={e.avatarUrl} className="h-9 w-9 text-xs" />
                        <span className="block max-w-[16rem] truncate font-medium text-slate-800" title={e.fullName}>
                          {e.fullName}
                        </span>
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{formatPhone(e.phone) || '—'}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{e.position || '—'}</td>
                    {activeTab === 'active' ? (
                      <>
                        <td className="px-4 py-3">{e.kind === 'staff' ? <RoleCell s={e.staff} /> : <Dash />}</td>
                        <td className="whitespace-nowrap px-4 py-3 text-right text-slate-700">
                          <SalaryCell employee={e} teacherPay={teacherPay} />
                        </td>
                        <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                          {e.kind === 'staff' ? lastSeen(e.staff.lastLoginAt) : '—'}
                        </td>
                      </>
                    ) : (
                      <>
                        <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                          {e.kind === 'teacher' && e.teacher.archivedAt ? formatDate(e.teacher.archivedAt) : '—'}
                        </td>
                        <td
                          className="max-w-[220px] truncate px-4 py-3 text-slate-500"
                          title={e.kind === 'teacher' ? (e.teacher.archiveReason ?? '') : ''}
                        >
                          {(e.kind === 'teacher' && e.teacher.archiveReason) || '—'}
                        </td>
                      </>
                    )}
                    <td className="px-4 py-3">
                      <span className="flex justify-end gap-0.5">
                        {e.kind === 'teacher' ? (
                          <>
                            <IconBtn icon={Eye} title="Ko'rish va login/parol" onClick={() => setViewingTeacher(e.teacher)} />
                            {activeTab === 'active' ? (
                              <>
                                <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openTeacherForm(e.teacher)} />
                                <IconBtn
                                  icon={Archive}
                                  title="Arxivga ko'chirish"
                                  onClick={() => {
                                    setArchiveReason('')
                                    setArchiveTarget(e.teacher)
                                  }}
                                />
                              </>
                            ) : (
                              <>
                                <IconBtn icon={RotateCcw} title="Arxivdan qaytarish" onClick={() => handleRestore(e.teacher)} />
                                <IconBtn
                                  icon={Trash2}
                                  title="Butunlay o'chirish"
                                  danger
                                  onClick={() => handleDeleteTeacher(e.teacher)}
                                />
                              </>
                            )}
                          </>
                        ) : (
                          <>
                            <IconBtn icon={KeyRound} title="Login/parol" onClick={() => showCredentials(e.staff)} />
                            <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openStaffForm(e.staff)} />
                            <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDeleteStaff(e.staff)} />
                          </>
                        )}
                      </span>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={columns} className="px-4 py-12 text-center text-slate-400">
                      Hech narsa topilmadi
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Yangi xodim — avval turi */}
      <Modal open={choosingType} onClose={() => setChoosingType(false)} size="sm" title="Yangi xodim">
        <div className="grid gap-3">
          <TypeChoice
            icon={GraduationCap}
            title={TEACHER_POSITION}
            hint="Dars beradi: fanlar, sinf rahbarligi, jadval va jurnal. Oyligi jadval va toifadan hisoblanadi."
            onClick={() => openTeacherForm(null)}
          />
          <TypeChoice
            icon={Briefcase}
            title="Boshqa xodim"
            hint="Kassir, administrator, hisobchi va boshqalar: belgilangan oylik va tizimdagi rol."
            onClick={() => openStaffForm(null)}
          />
        </div>
      </Modal>

      {canTeachers && (
        <>
          <TeacherFormModal
            open={teacherFormOpen}
            onClose={() => {
              setTeacherFormOpen(false)
              setEditingTeacher(null)
            }}
            onSubmit={submitTeacher}
            initial={editingTeacher}
            subjects={subjects}
            classes={classes}
          />
          <TeacherViewModal teacher={viewingTeacher} subjects={subjects} onClose={() => setViewingTeacher(null)} />
        </>
      )}

      {/* Arxivga ko'chirish tasdiqi */}
      <Modal
        open={!!archiveTarget}
        onClose={() => setArchiveTarget(null)}
        size="md"
        title="Arxivga ko'chirish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setArchiveTarget(null)}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmArchive}>
              <Archive className="h-4 w-4" /> Arxivga ko'chirish
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-600">
            <span className="font-semibold text-slate-800">{archiveTarget?.fullName}</span> arxivga ko'chiriladi: faol
            ro'yxatdan yashiriladi va tizimga kirishi bloklanadi. Jurnal va hisobot ma'lumotlari saqlanib qoladi.
          </p>
          <Textarea
            label="Sabab (ixtiyoriy)"
            value={archiveReason}
            onChange={(e) => setArchiveReason(e.target.value)}
            rows={3}
            placeholder="Masalan: ishdan bo'shadi"
          />
        </div>
      </Modal>

      {staffForm && (
        <StaffFormDrawer
          editing={staffForm.editing}
          roles={roles}
          canManageRoles={isSuperadmin}
          knownPositions={staffPositionsForForm}
          onClose={() => setStaffForm(null)}
          onSaved={handleStaffSaved}
        />
      )}

      {/* Xodim login/paroli */}
      <Modal
        open={!!credOf}
        onClose={() => setCredOf(null)}
        title={credOf ? `${credOf.fullName} — akkaunt` : 'Akkaunt'}
        footer={
          <Button variant="secondary" onClick={() => setCredOf(null)}>
            Yopish
          </Button>
        }
      >
        {credError ? (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{credError}</p>
        ) : (
          <CredentialsBox
            credentials={creds}
            loading={credLoading}
            onReset={
              credOf
                ? async () => {
                    const c = await resetStaffPassword(credOf.id)
                    setCreds(c)
                  }
                : undefined
            }
          />
        )}
      </Modal>

      <Toast message={toast?.message ?? null} tone={toast?.tone} onClose={closeToast} />
    </div>
  )
}

/* ------------------------------------------------------------------ */

function Dash() {
  return <span className="text-slate-300">—</span>
}

function RoleCell({ s }: { s: Staff }) {
  if (s.accessRoleName) {
    return (
      <span className="whitespace-nowrap rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-700">
        {s.accessRoleName}
      </span>
    )
  }
  return (
    <span
      className="whitespace-nowrap text-xs text-slate-400"
      title={s.permissions.length > 0 ? 'Rol biriktirilmagan — eski shaxsiy ruxsatlar amal qilyapti' : undefined}
    >
      {s.permissions.length > 0 ? `Rolsiz · ${grantsOf(s.permissions).size} ta sahifa` : "Rol yo'q"}
    </span>
  )
}

/** Oylik: xodimda — belgilangan; o'qituvchida — joriy oy, jadval va toifa bo'yicha (bo'lsa). */
function SalaryCell({ employee, teacherPay }: { employee: Employee; teacherPay: Map<string, number> }) {
  if (employee.kind === 'staff') {
    return employee.staff.salary > 0 ? <>{formatMoney(employee.staff.salary)}</> : <Dash />
  }
  const pay = teacherPay.get(employee.id)
  if (pay === undefined || pay <= 0) {
    return (
      <span className="text-slate-300" title="O'qituvchi oyligi jadval va toifa bo'yicha hisoblanadi (Moliya → Ish haqi)">
        —
      </span>
    )
  }
  return <span title="Joriy oy — jadval va toifa bo'yicha">{formatMoney(pay)}</span>
}

function TypeChoice({
  icon: Icon,
  title,
  hint,
  onClick,
}: {
  icon: typeof Eye
  title: string
  hint: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex items-start gap-3 rounded-2xl border border-slate-200 bg-white p-4 text-left transition-colors hover:border-brand-300 hover:bg-brand-50/40"
    >
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-700">
        <Icon className="h-5 w-5" />
      </span>
      <span>
        <span className="block font-semibold text-slate-800">{title}</span>
        <span className="mt-0.5 block text-sm text-slate-500">{hint}</span>
      </span>
    </button>
  )
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}

function IconBtn({
  icon: Icon,
  title,
  onClick,
  danger,
}: {
  icon: typeof Eye
  title: string
  onClick: () => void
  danger?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger ? 'text-slate-400 hover:bg-red-50 hover:text-red-600' : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}

/** "2026-09-24T09:46:00" → "24.09.2026 | 09:46"; kirmagan bo'lsa "—". */
function lastSeen(iso: string | null | undefined): string {
  if (!iso) return '—'
  const [date, time = ''] = iso.split('T')
  const [y, m, d] = date.split('-')
  return d && m && y ? `${d}.${m}.${y} | ${time.slice(0, 5)}` : iso
}
