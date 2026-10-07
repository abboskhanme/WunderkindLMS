import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  ArrowLeft, Award, BarChart3, CalendarCheck, CalendarDays, ChevronDown, FileSignature,
  History, MapPin, MessageSquare, Repeat, ShieldAlert, User, Users, Wallet,
} from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import type { Credentials, Student } from '@/types'
import { getStudentNotebook, type StudentNotebook } from '@/api/services/studentNotebook'
import { getStudentCard, type StudentCard } from '@/api/services/studentProfile'
import {
  getStudentCredentials, resetStudentPassword, restoreStudent, updateStudent,
  type StudentPayload,
} from '@/api/services/students'
import { getStudentStatuses, setStudentStatus, type StudentStatusTag } from '@/api/services/studentStatuses'
import { cn, formatMoney } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { FinanceView } from '@/pages/portal/FinanceView'
import { ArchiveStudentsModal } from '@/pages/admin/students/ArchiveStudentsModal'
import { StudentFormModal } from '@/pages/admin/students/StudentFormModal'
import { ActivityTab } from '@/pages/admin/students/profile/ActivityTab'
import { AttendanceTab } from '@/pages/admin/students/profile/AttendanceTab'
import { CertificatesTab } from '@/pages/admin/students/profile/CertificatesTab'
import { CommentsTab } from '@/pages/admin/students/profile/CommentsTab'
import { ContractsTab } from '@/pages/admin/students/profile/ContractsTab'
import { DisciplineTab } from '@/pages/admin/students/profile/DisciplineTab'
import { LocationTab } from '@/pages/admin/students/profile/LocationTab'
import { MembershipsTab } from '@/pages/admin/students/profile/MembershipsTab'
import { OverviewTab } from '@/pages/admin/students/profile/OverviewTab'
import { SubscriptionsTab } from '@/pages/admin/students/profile/SubscriptionsTab'
import { useBillingAccess } from '@/pages/admin/billing/access'
import { ProfileError } from '@/pages/admin/students/profile/ProfileUi'
import { ProfileSidebar } from '@/pages/admin/students/profile/ProfileSidebar'
import { WithdrawModal } from '@/pages/admin/students/profile/WithdrawModal'
import { WithdrawalNotices } from '@/pages/admin/students/profile/WithdrawalNotices'
import { hasFinanceAccess } from '@/pages/admin/billing/access'
import { getPortalFinance } from '@/api/services/portalFinance'
import { TimetableTab } from '@/pages/admin/students/profile/TimetableTab'
import { canUseCashDesk } from '@/pages/cashier/cashDeskRoles'
import { StudentPaymentModal } from '@/pages/cashier/StudentPaymentModal'

/**
 * O'quvchi kartochkasi — docs/modules/students-parity.md §2.3 (S-10).
 *
 * ENDI TAB'LAR. Ilgari bu bitta uzun ustun edi: analitika, moliya, a'zolik va
 * shartnomalar ketma-ket. §2.3.1 dagi EduSchool kartochkasi esa chap panel +
 * tab'lardan iborat, va sabab amaliy — bitta bolaning sahifasida o'nlab bo'lim
 * bor, ularning hammasi bir vaqtda kerak emas va bir vaqtda yuklanmasligi ham
 * kerak. HAR BIR ESKI BO'LIM JOYIDA QOLDI, faqat o'z tab'iga ko'chdi:
 *
 *   Umumiy   — shaxsiy ma'lumot, stat kartalar, diagrammalar, baholar
 *              matritsasi, davomat sabablari, oylik feedback, topshiriqlar
 *   To'lovlar — o'sha `FinanceView` (moliya roli darvozasi serverda)
 *   Intizom  — o'sha ball tarixi
 *   Sinf va guruhlar / Shartnomalar — avvalgi to'lqinlar qo'shgan tab'lar
 *
 * YANGI: Jadval, Davomat, Sertifikatlar, Izohlar, Manzil, Faoliyat tarixi.
 * Abonementlar — o'sha bolaning obunalari (`SubscriptionsTab`), faqat moliya rollariga.
 *
 * PUL QOIDASI: sarlavhada balans KO'RSATILMAYDI. U "To'lovlar" tab'ida,
 * moliya rolining orqasida qoladi; ruxsat yetmasa ekran o'qiladigan yozuv
 * beradi, cheksiz aylanuvchi doira emas.
 */

type TabId =
  | 'overview' | 'timetable' | 'finance' | 'subscriptions' | 'memberships' | 'attendance'
  | 'certificates' | 'contracts' | 'comments' | 'discipline' | 'location' | 'activity'

type TabDef = { id: TabId; label: string; icon: typeof User }

/**
 * Tuzilish EduSchool'dagidek (mijoz, 2026-10-06): to'rtta asosiy tab doim ko'rinadi, qolgani
 * "Ko'proq" ichida. Ko'rinish (rang, komponentlar) o'zimizniki — CLAUDE.md.
 */
const primaryTabs: TabDef[] = [
  { id: 'timetable', label: 'Dars jadvali', icon: CalendarDays },
  { id: 'finance', label: 'Tranzaksiya tarixi', icon: Wallet },
  { id: 'subscriptions', label: 'Abonement', icon: Repeat },
  { id: 'comments', label: 'Izoh', icon: MessageSquare },
]

const moreTabs: TabDef[] = [
  { id: 'overview', label: "O'zlashtirish ko'rsatkichlari", icon: BarChart3 },
  { id: 'memberships', label: 'Sinf va guruhlar', icon: Users },
  { id: 'attendance', label: 'Davomat', icon: CalendarCheck },
  { id: 'certificates', label: 'Sertifikatlar', icon: Award },
  { id: 'contracts', label: 'Shartnomalar', icon: FileSignature },
  { id: 'discipline', label: 'Xulq-atvor', icon: ShieldAlert },
  { id: 'location', label: 'Manzil', icon: MapPin },
  { id: 'activity', label: 'Harakat tarixi', icon: History },
]

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

export function StudentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [data, setData] = useState<StudentNotebook | null>(null)
  const [card, setCard] = useState<StudentCard | null>(null)
  const [statuses, setStatuses] = useState<StudentStatusTag[]>([])
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [tab, setTab] = useState<TabId>('timetable')
  const [banner, setBanner] = useState<string | null>(null)

  const [editOpen, setEditOpen] = useState(false)
  const [archiveTargets, setArchiveTargets] = useState<Student[]>([])
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [credentials, setCredentials] = useState<Credentials | null>(null)
  const [payOpen, setPayOpen] = useState(false)
  const [withdrawOpen, setWithdrawOpen] = useState(false)
  // "Chiqib ketdi" amalidan keyin abonementlar va ularning yozuvlari qayta yuklansin.
  const [withdrawKey, setWithdrawKey] = useState(0)
  // To'lovdan keyin "To'lovlar" tab'i qaytadan yuklanishi uchun.
  const [financeKey, setFinanceKey] = useState(0)
  const { user } = useAuth()
  const canTakePayment = canUseCashDesk(user)
  // Abonementlar — moliya rollari (admin, direktor); server ham shu chegarada.
  const { canManageSubscriptions } = useBillingAccess()
  const visiblePrimary = primaryTabs.filter((t) => t.id !== 'subscriptions' || canManageSubscriptions)
  // Balans — faqat moliya huquqi bor xodimga (server ham shu chegarada). Boshqalarda qator yo'q.
  const canSeeBalance = hasFinanceAccess(user)
  const [balance, setBalance] = useState<number | null>(null)

  const load = useCallback(() => {
    if (!id) return
    setLoading(true)
    // Kartochka sarlavhasi alohida so'rov: u yiqilsa ham daftar ko'rinadi.
    getStudentCard(id).then(setCard).catch(() => setCard(null))
    getStudentNotebook(id)
      .then(setData)
      .catch(() => setNotFound(true))
      .finally(() => setLoading(false))
  }, [id])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sahifa ochilganda o'quvchi ma'lumoti yuklanadi (maqsadli, loyihadagi mavjud naqsh)
    load()
  }, [load])

  useEffect(() => {
    if (!id || !canSeeBalance) return
    let active = true
    getPortalFinance(id)
      .then((f) => {
        if (active) setBalance(f.credit - f.debt)
      })
      .catch(() => {
        if (active) setBalance(null)
      })
    return () => {
      active = false
    }
  }, [id, canSeeBalance, financeKey])

  useEffect(() => {
    let active = true
    getStudentStatuses()
      .then((list) => {
        if (active) setStatuses(list)
      })
      .catch(() => {
        /* holat katalogi bo'lmasa tanlov ko'rinmaydi — kartochka baribir ishlaydi */
      })
    return () => {
      active = false
    }
  }, [])

  const changeStatus = async (statusId: string) => {
    if (!card) return
    try {
      await setStudentStatus(card.student.id, statusId || null)
      const fresh = await getStudentCard(card.student.id)
      setCard(fresh)
    } catch (e) {
      alert(errorText(e, "Holatni o'zgartirib bo'lmadi"))
    }
  }

  const saveEdit = async (values: StudentPayload) => {
    if (!id) return
    try {
      await updateStudent(id, values)
      setEditOpen(false)
      setBanner("O'quvchi ma'lumoti yangilandi")
      load()
    } catch (e) {
      alert(errorText(e, "Saqlab bo'lmadi"))
    }
  }

  const restore = async () => {
    if (!id || !confirm("O'quvchini arxivdan qaytarasizmi?")) return
    try {
      await restoreStudent(id)
      setBanner("O'quvchi arxivdan qaytarildi")
      load()
    } catch (e) {
      alert(errorText(e, "Qaytarib bo'lmadi"))
    }
  }

  const openPassword = async () => {
    if (!id) return
    setCredentials(null)
    setPasswordOpen(true)
    try {
      setCredentials(await getStudentCredentials(id))
    } catch (e) {
      alert(errorText(e, "Login ma'lumotini olib bo'lmadi"))
      setPasswordOpen(false)
    }
  }

  const resetPassword = async () => {
    if (!id) return
    setCredentials(await resetStudentPassword(id))
    // Kartochkadagi "Parol" qatori yangisini ko'rsatsin.
    getStudentCard(id).then(setCard).catch(() => undefined)
  }

  if (loading && !data) return <Loader label="Yuklanmoqda..." />
  if (notFound || !data)
    return (
      <div className="space-y-4">
        <BackLink />
        <Card className="py-16 text-center text-slate-400">O'quvchi topilmadi</Card>
      </div>
    )

  return (
    <div className="space-y-6">
      <BackLink />

      {banner && (
        <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {banner}
        </div>
      )}

      <div className="grid items-start gap-6 lg:grid-cols-[320px_minmax(0,1fr)]">
        {/* ---------- Chap ustun: doimiy kartochka ---------- */}
        <aside className="lg:sticky lg:top-4">
          <ProfileSidebar
            data={data}
            card={card}
            statuses={statuses}
            balance={canSeeBalance ? balance : null}
            canTakePayment={canTakePayment}
            onStatusChange={changeStatus}
            onPay={() => setPayOpen(true)}
            onContract={() => setTab('contracts')}
            onEdit={() => setEditOpen(true)}
            onPassword={openPassword}
            onArchive={() => card && setArchiveTargets([card.student])}
            onRestore={restore}
            onWithdraw={canManageSubscriptions && !card?.student.isArchived ? () => setWithdrawOpen(true) : undefined}
          />
        </aside>

        {/* ---------- O'ng tomon: tab'lar ---------- */}
        <div className="min-w-0 space-y-4">
          <div className="flex flex-wrap gap-1 rounded-xl bg-slate-100 p-1">
            {visiblePrimary.map((t) => (
              <TabButton key={t.id} def={t} active={tab === t.id} onClick={() => setTab(t.id)} />
            ))}
            <MoreTabs tabs={moreTabs} active={tab} onPick={setTab} />
          </div>

      {tab === 'overview' && <OverviewTab data={data} card={card} showPersonal={false} />}

      {tab === 'timetable' && <TimetableTab studentId={data.id} />}

      {/*
        Moliya — P1-19. Bu ota-ona ko'radigan AYNAN O'SHA ko'rinish
        (`FinanceView`), ataylab: admin ota-ona bilan telefonda gaplashganda
        ikkalasi bir xil ekranga qaraydi. Ma'lumot ham, ruxsat ham serverda
        hal qilinadi (`/api/student/billing` egani JWT'dan aniqlaydi;
        `studentId` faqat admin/xodim uchun ishlaydi).
      */}
      {tab === 'finance' && <FinanceView key={financeKey} studentId={data.id} embedded />}

      {tab === 'subscriptions' && canManageSubscriptions && (
        <WithdrawalNotices
          studentId={data.id}
          reloadKey={withdrawKey}
          onChanged={() => {
            setBanner("Chiqib ketish bekor qilindi — qarz va abonement qaytdi")
            setFinanceKey((k) => k + 1)
            setWithdrawKey((k) => k + 1)
            load()
          }}
        />
      )}
      {tab === 'subscriptions' && canManageSubscriptions && (
        <SubscriptionsTab
          key={withdrawKey}
          studentId={data.id}
          studentName={data.fullName}
          className={data.className}
          onChanged={() => setFinanceKey((k) => k + 1)}
          archived={!!card?.student.isArchived}
        />
      )}

      {tab === 'memberships' && <MembershipsTab studentId={data.id} />}

      {tab === 'attendance' && <AttendanceTab studentId={data.id} />}

      {tab === 'certificates' &&
        (card ? (
          <CertificatesTab student={card.student} />
        ) : (
          <ProfileError message="Kartochka ma'lumoti yuklanmadi — sahifani yangilang" />
        ))}

      {tab === 'contracts' && <ContractsTab studentId={data.id} studentName={data.fullName} />}

      {tab === 'comments' && <CommentsTab studentId={data.id} />}

      {tab === 'discipline' && <DisciplineTab points={data.disciplinePoints} />}

      {tab === 'location' &&
        (card ? (
          <LocationTab card={card} onSaved={setCard} />
        ) : (
          <ProfileError message="Kartochka ma'lumoti yuklanmadi — sahifani yangilang" />
        ))}

      {tab === 'activity' && <ActivityTab studentId={data.id} />}
        </div>
      </div>

      {/* ---------- Oynalar ---------- */}
      {payOpen && (
        <StudentPaymentModal
          open
          student={{
            id: data.id,
            fullName: data.fullName,
            className: data.className,
            parentFullName: data.parentFullName ?? '',
            parentPhone: card?.phone || data.parentPhone || '',
          }}
          onClose={() => setPayOpen(false)}
          onPaid={() => {
            setPayOpen(false)
            setFinanceKey((n) => n + 1)
            setBanner("To'lov qabul qilindi")
          }}
        />
      )}
      {withdrawOpen && (
        <WithdrawModal
          open
          studentId={data.id}
          studentName={data.fullName}
          onClose={() => setWithdrawOpen(false)}
          onDone={(result) => {
            setWithdrawOpen(false)
            setBanner(
              result.totalWriteOff > 0
                ? `Abonement bekor qilindi: ${formatMoney(result.totalWriteOff)} hisobdan chiqarildi. Balans: ${formatMoney(result.balanceAfter)}`
                : 'Abonement oxirgi o\'qigan kundan yopildi',
            )
            setFinanceKey((k) => k + 1)
            setWithdrawKey((k) => k + 1)
            setTab('subscriptions')
            load()
          }}
        />
      )}
      <StudentFormModal
        open={editOpen}
        onClose={() => setEditOpen(false)}
        onSubmit={saveEdit}
        initial={card?.student ?? null}
      />
      <ArchiveStudentsModal
        students={archiveTargets}
        onClose={() => setArchiveTargets([])}
        onArchived={() => {
          setArchiveTargets([])
          setBanner("O'quvchi arxivga ko'chirildi")
          load()
        }}
      />
      <Modal
        open={passwordOpen}
        onClose={() => setPasswordOpen(false)}
        title="Tizimga kirish"
        size="sm"
        footer={<Button onClick={() => setPasswordOpen(false)}>Yopish</Button>}
      >
        <CredentialsBox credentials={credentials} loading={!credentials} onReset={resetPassword} />
      </Modal>
    </div>
  )
}

function TabButton({ def, active, onClick }: { def: TabDef; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
      )}
    >
      <def.icon className="h-4 w-4" /> {def.label}
    </button>
  )
}

/** "Ko'proq" — qolgan tab'lar. Ichidagi biri ochiq bo'lsa tugmada uning nomi turadi. */
function MoreTabs({ tabs, active, onPick }: { tabs: TabDef[]; active: TabId; onPick: (id: TabId) => void }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const current = tabs.find((t) => t.id === active)

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

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-haspopup="menu"
        className={cn(
          'inline-flex items-center gap-1.5 whitespace-nowrap rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
          current ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
        )}
      >
        {current ? <current.icon className="h-4 w-4" /> : null}
        {current ? current.label : "Ko'proq"}
        <ChevronDown className={cn('h-4 w-4 transition-transform', open && 'rotate-180')} />
      </button>
      {open && (
        <div role="menu" className="absolute right-0 z-20 mt-1 w-64 overflow-hidden rounded-xl border border-slate-200 bg-white py-1 shadow-lg">
          {tabs.map((t) => (
            <button
              key={t.id}
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false)
                onPick(t.id)
              }}
              className={cn(
                'flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-slate-50',
                t.id === active ? 'font-semibold text-brand-700' : 'text-slate-700',
              )}
            >
              <t.icon className="h-4 w-4 text-slate-400" /> {t.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

function BackLink() {
  return (
    <Link
      to="/admin/students"
      className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-800"
    >
      <ArrowLeft className="h-4 w-4" /> O'quvchilar ro'yxati
    </Link>
  )
}
