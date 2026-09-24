import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  Archive, ArrowLeft, Award, CalendarCheck, CalendarDays, FileSignature,
  GraduationCap, History, KeyRound, MapPin, MessageSquare, Pencil, Phone,
  RotateCcw, ShieldAlert, User, Users, Wallet,
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
import { cn, formatDate } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { FinanceView } from '@/pages/portal/FinanceView'
import { ArchiveStudentsModal } from '@/pages/admin/students/ArchiveStudentsModal'
import { StatusChip } from '@/pages/admin/students/StatusChip'
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
import { ProfileError } from '@/pages/admin/students/profile/ProfileUi'
import { TimetableTab } from '@/pages/admin/students/profile/TimetableTab'
import { CASH_DESK_ROLES } from '@/pages/cashier/cashDeskRoles'
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
 *
 * PUL QOIDASI: sarlavhada balans KO'RSATILMAYDI. U "To'lovlar" tab'ida,
 * moliya rolining orqasida qoladi; ruxsat yetmasa ekran o'qiladigan yozuv
 * beradi, cheksiz aylanuvchi doira emas.
 */

type TabId =
  | 'overview' | 'timetable' | 'finance' | 'memberships' | 'attendance'
  | 'certificates' | 'contracts' | 'comments' | 'discipline' | 'location' | 'activity'

const tabs: { id: TabId; label: string; icon: typeof User }[] = [
  { id: 'overview', label: 'Umumiy', icon: User },
  { id: 'timetable', label: 'Jadval', icon: CalendarDays },
  { id: 'finance', label: "To'lovlar", icon: Wallet },
  { id: 'memberships', label: 'Sinf va guruhlar', icon: Users },
  { id: 'attendance', label: 'Davomat', icon: CalendarCheck },
  { id: 'certificates', label: 'Sertifikatlar', icon: Award },
  { id: 'contracts', label: 'Shartnomalar', icon: FileSignature },
  { id: 'comments', label: 'Izohlar', icon: MessageSquare },
  { id: 'discipline', label: 'Intizom', icon: ShieldAlert },
  { id: 'location', label: 'Manzil', icon: MapPin },
  { id: 'activity', label: 'Faoliyat tarixi', icon: History },
]

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

export function StudentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [data, setData] = useState<StudentNotebook | null>(null)
  const [card, setCard] = useState<StudentCard | null>(null)
  const [statuses, setStatuses] = useState<StudentStatusTag[]>([])
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  const [tab, setTab] = useState<TabId>('overview')
  const [banner, setBanner] = useState<string | null>(null)

  const [editOpen, setEditOpen] = useState(false)
  const [archiveTargets, setArchiveTargets] = useState<Student[]>([])
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [credentials, setCredentials] = useState<Credentials | null>(null)
  const [payOpen, setPayOpen] = useState(false)
  // To'lovdan keyin "To'lovlar" tab'i qaytadan yuklanishi uchun.
  const [financeKey, setFinanceKey] = useState(0)
  const { user } = useAuth()
  const canTakePayment = user !== null && CASH_DESK_ROLES.includes(user.role)

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
  }

  if (loading && !data) return <Loader label="Yuklanmoqda..." />
  if (notFound || !data)
    return (
      <div className="space-y-4">
        <BackLink />
        <Card className="py-16 text-center text-slate-400">O'quvchi topilmadi</Card>
      </div>
    )

  const archived = card?.student.isArchived ?? false

  return (
    <div className="space-y-6">
      <BackLink />

      {banner && (
        <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          {banner}
        </div>
      )}

      {/* ---------- Sarlavha (chap panel) ---------- */}
      <Card className="flex flex-wrap items-start gap-5">
        <div className="flex h-20 w-20 shrink-0 items-center justify-center overflow-hidden rounded-2xl bg-brand-50 text-2xl font-semibold text-brand-600">
          {data.photoUrl ? (
            <img src={data.photoUrl} alt={data.fullName} className="h-full w-full object-cover" />
          ) : (
            initials(data.fullName)
          )}
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold text-slate-800">{data.fullName}</h1>
            {card?.statusName && <StatusChip name={card.statusName} color={card.statusColor} />}
            {archived && (
              <span className="rounded-md bg-slate-200 px-2 py-0.5 text-xs font-medium text-slate-600">
                Arxivda
                {card?.student.archivedAt ? ` · ${formatDate(card.student.archivedAt)}` : ''}
              </span>
            )}
          </div>

          <div className="mt-1 flex flex-wrap gap-x-5 gap-y-1 text-sm text-slate-500">
            <span className="inline-flex items-center gap-1.5">
              <GraduationCap className="h-4 w-4 text-slate-400" /> {data.className || '—'}
            </span>
            {data.homeroomTeacher && (
              <span className="inline-flex items-center gap-1.5">
                <User className="h-4 w-4 text-slate-400" /> {data.homeroomTeacher}
              </span>
            )}
            {data.parentFullName && (
              <span className="inline-flex items-center gap-1.5">
                <User className="h-4 w-4 text-slate-400" /> Ota-ona: {data.parentFullName}
              </span>
            )}
            {(card?.phone || data.parentPhone) && (
              <span className="inline-flex items-center gap-1.5">
                <Phone className="h-4 w-4 text-slate-400" /> {card?.phone || data.parentPhone}
              </span>
            )}
            {card && (
              <span className="inline-flex items-center gap-1.5">
                <CalendarDays className="h-4 w-4 text-slate-400" />
                Maktabda {card.activeDays} kun
              </span>
            )}
          </div>

          {/* Arxiv sababi — arxivlangan bolada eng kerakli ma'lumot. */}
          {archived && card?.student.archiveReason && (
            <p className="mt-2 text-sm text-slate-500">Sabab: {card.student.archiveReason}</p>
          )}
        </div>

        {/* ---------- Amallar ---------- */}
        <div className="flex flex-wrap items-center gap-2">
          {statuses.length > 0 && card && (
            <select
              value={card.statusId ?? ''}
              onChange={(e) => changeStatus(e.target.value)}
              title="Holat tagi"
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              <option value="">Holatsiz</option>
              {statuses.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          )}
          {canTakePayment && !archived && (
            <Button onClick={() => setPayOpen(true)} className="px-5 py-2.5 text-base">
              <Wallet className="h-5 w-5" /> To'lov qilish
            </Button>
          )}
          <Button variant="secondary" onClick={() => setTab('contracts')}>
            <FileSignature className="h-4 w-4" /> Shartnoma
          </Button>
          <Button variant="secondary" onClick={openPassword}>
            <KeyRound className="h-4 w-4" /> Parol
          </Button>
          {card && (
            <Button variant="secondary" onClick={() => setEditOpen(true)}>
              <Pencil className="h-4 w-4" /> Tahrirlash
            </Button>
          )}
          {card && !archived && (
            <Button variant="secondary" onClick={() => setArchiveTargets([card.student])}>
              <Archive className="h-4 w-4" /> Arxivlash
            </Button>
          )}
          {archived && (
            <Button variant="secondary" onClick={restore}>
              <RotateCcw className="h-4 w-4" /> Qaytarish
            </Button>
          )}
        </div>
      </Card>

      {/* ---------- Tab'lar ---------- */}
      <div className="overflow-x-auto">
        <div className="flex w-max gap-1 rounded-xl bg-slate-100 p-1">
          {tabs.map((t) => (
            <button
              key={t.id}
              type="button"
              onClick={() => setTab(t.id)}
              className={cn(
                'inline-flex items-center gap-1.5 whitespace-nowrap rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                tab === t.id ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              <t.icon className="h-4 w-4" /> {t.label}
            </button>
          ))}
        </div>
      </div>

      {tab === 'overview' && <OverviewTab data={data} card={card} />}

      {tab === 'timetable' && <TimetableTab studentId={data.id} />}

      {/*
        Moliya — P1-19. Bu ota-ona ko'radigan AYNAN O'SHA ko'rinish
        (`FinanceView`), ataylab: admin ota-ona bilan telefonda gaplashganda
        ikkalasi bir xil ekranga qaraydi. Ma'lumot ham, ruxsat ham serverda
        hal qilinadi (`/api/student/billing` egani JWT'dan aniqlaydi;
        `studentId` faqat admin/xodim uchun ishlaydi).
      */}
      {tab === 'finance' && <FinanceView key={financeKey} studentId={data.id} embedded />}

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
