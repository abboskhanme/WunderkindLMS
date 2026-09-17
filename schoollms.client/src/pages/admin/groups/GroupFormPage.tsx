import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, ArrowRightLeft, Search, Trash2, Users } from 'lucide-react'
import type { SchoolClass, Subject, Teacher } from '@/types'
import type { GroupCandidate, StudyGroupMember } from '@/api/services/groups'
import { createGroup, getGroup, getGroupCandidates, updateGroup } from '@/api/services/groups'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { GroupTransferModal } from './GroupTransferModal'

/** O'ng paneldagi qator — nomzoddan ham, mavjud a'zodan ham quriladi. */
interface PickedStudent {
  studentId: string
  fullName: string
  className: string
  /** Mavjud a'zolik id'si (faqat tahrirlashda) — o'tkazish shu bilan ishlaydi. */
  memberId?: string
}

/**
 * Guruh formasi — `docs/modules/students-parity.md` §2.1 (G-8).
 *
 * IKKI PANELLI TANLOV. Chapda nomzodlar (tanlangan sinflardan), o'ngda guruh
 * ro'yxati. SHU FAN bo'yicha allaqachon boshqa guruhda bo'lgan bola chapda
 * KULRANG va tanlanmaydi — "bitta fandan bitta guruh" qoidasi ekranda ham
 * ko'rinadi, faqat serverdagi xato matnida emas.
 *
 * FAN TAHRIRLASHDA QULFLANADI. Guruhning fani a'zolik yozuvlaridagi nusxa
 * bilan bog'langan kalit; uni almashtirish aslida boshqa guruh ochish demak
 * (server ham rad etadi).
 */
export function GroupFormPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const isEdit = Boolean(id)

  const [subjects, setSubjects] = useState<Subject[]>([])
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [name, setName] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [grades, setGrades] = useState<number[]>([])
  const [classIds, setClassIds] = useState<string[]>([])
  const [teacherIds, setTeacherIds] = useState<string[]>([])
  const [gender, setGender] = useState('')
  const [isArchived, setIsArchived] = useState(false)

  const [candidates, setCandidates] = useState<GroupCandidate[]>([])
  const [picked, setPicked] = useState<PickedStudent[]>([])
  const [candidateSearch, setCandidateSearch] = useState('')
  const [transferFor, setTransferFor] = useState<PickedStudent | null>(null)

  /* ---------- Ma'lumotnomalar ---------- */

  useEffect(() => {
    Promise.all([getSubjects(true), getClasses(), getTeachers()])
      .then(([s, c, t]) => {
        setSubjects(s)
        setClasses(c)
        setTeachers(t)
      })
      .finally(() => {
        if (!id) setLoading(false)
      })
  }, [id])

  /* ---------- Tahrirlash: mavjud guruh ---------- */

  useEffect(() => {
    if (!id) return
    getGroup(id)
      .then((g) => {
        setName(g.name)
        setSubjectId(g.subjectId)
        setClassIds(g.classes.map((c) => c.id))
        setGrades([...new Set(g.classes.map((c) => c.grade))])
        setTeacherIds(g.teachers.map((t) => t.id))
        setGender(g.gender ?? '')
        setIsArchived(g.isArchived)
        setPicked(g.members.map(toPicked))
      })
      .finally(() => setLoading(false))
  }, [id])

  /* ---------- Chap panel ---------- */

  const loadCandidates = useCallback(() => {
    if (!subjectId || classIds.length === 0) {
      setCandidates([])
      return
    }
    getGroupCandidates({
      classIds,
      subjectId,
      gender: gender || null,
      excludeGroupId: id,
    }).then(setCandidates)
  }, [subjectId, classIds, gender, id])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- ma'lumot kelganda formani to'ldiramiz (maqsadli, loyihadagi mavjud naqsh)
    loadCandidates()
  }, [loadCandidates])

  const pickedIds = useMemo(() => new Set(picked.map((p) => p.studentId)), [picked])

  const visibleCandidates = useMemo(() => {
    const term = candidateSearch.trim().toLowerCase()
    return candidates.filter(
      (c) => !pickedIds.has(c.studentId) && (!term || c.fullName.toLowerCase().includes(term)),
    )
  }, [candidates, pickedIds, candidateSearch])

  /** Tanlanadigan (band bo'lmagan) nomzodlar — "hammasini qo'shish" shular uchun. */
  const selectable = useMemo(
    () => visibleCandidates.filter((c) => !c.currentGroupId),
    [visibleCandidates],
  )

  const addCandidate = (c: GroupCandidate) => {
    if (c.currentGroupId) return
    setPicked((prev) => [
      ...prev,
      { studentId: c.studentId, fullName: c.fullName, className: c.className },
    ])
  }

  const addAll = () =>
    setPicked((prev) => [
      ...prev,
      ...selectable.map((c) => ({
        studentId: c.studentId,
        fullName: c.fullName,
        className: c.className,
      })),
    ])

  const removePicked = (studentId: string) =>
    setPicked((prev) => prev.filter((p) => p.studentId !== studentId))

  /* ---------- Sinf tanlovi ---------- */

  const classOptions = useMemo(
    () =>
      classes
        .filter((c) => !c.isArchived && (grades.length === 0 || grades.includes(c.grade)))
        .sort((a, b) => a.grade - b.grade || a.name.localeCompare(b.name)),
    [classes, grades],
  )

  const gradeOptions = useMemo(
    () =>
      [...new Set(classes.filter((c) => !c.isArchived).map((c) => c.grade))].sort((a, b) => a - b),
    [classes],
  )

  const toggleGrade = (g: number) => {
    const next = grades.includes(g) ? grades.filter((x) => x !== g) : [...grades, g]
    setGrades(next)
    // Daraja olib tashlansa, o'sha darajadagi tanlangan sinflar ham tushib
    // qolsin — aks holda ko'rinmaydigan sinf jimgina guruhda qolardi.
    setClassIds((prev) =>
      prev.filter((cid) => {
        const cls = classes.find((c) => c.id === cid)
        return cls ? next.length === 0 || next.includes(cls.grade) : false
      }),
    )
  }

  const toggleClass = (cid: string) =>
    setClassIds((prev) => (prev.includes(cid) ? prev.filter((x) => x !== cid) : [...prev, cid]))

  const toggleTeacher = (tid: string) =>
    setTeacherIds((prev) => (prev.includes(tid) ? prev.filter((x) => x !== tid) : [...prev, tid]))

  /* ---------- Saqlash ---------- */

  const handleSave = async () => {
    setError(null)
    if (name.trim().length < 3) {
      setError("Guruh nomi kamida 3 belgidan iborat bo'lsin")
      return
    }
    if (!subjectId) {
      setError('Fanni tanlang')
      return
    }
    if (classIds.length === 0) {
      setError('Kamida bitta sinf tanlang')
      return
    }
    if (teacherIds.length === 0) {
      setError("Kamida bitta o'qituvchi tanlang")
      return
    }

    setSaving(true)
    const payload = {
      name: name.trim(),
      subjectId,
      classIds,
      teacherIds,
      gender: gender || null,
      studentIds: picked.map((p) => p.studentId),
    }
    try {
      const saved = isEdit ? await updateGroup(id!, payload) : await createGroup(payload)
      navigate(`/admin/groups/${saved.id}/students`)
    } catch (e) {
      setError(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Guruhni saqlab bo'lmadi",
      )
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button variant="ghost" onClick={() => navigate('/admin/groups')}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">
              {isEdit ? 'Guruhni tahrirlash' : "Yangi o'quv guruhi"}
            </h1>
            <p className="text-sm text-slate-400">
              Bir nechta sinfdan yig'iladigan, bitta fan bo'yicha o'qiydigan guruh
            </p>
          </div>
        </div>
        <Button onClick={handleSave} disabled={saving || isArchived}>
          {saving ? 'Saqlanmoqda...' : 'Saqlash'}
        </Button>
      </div>

      {isArchived && (
        <Card className="border-amber-200 bg-amber-50 text-sm text-amber-800">
          Bu guruh arxivda — uni tahrirlab bo'lmaydi. Avval arxivdan chiqaring.
        </Card>
      )}

      {error && <Card className="border-red-200 bg-red-50 text-sm text-red-700">{error}</Card>}

      <Card className="space-y-4">
        <div className="grid gap-4 md:grid-cols-2">
          <Select
            label="Fan"
            required
            value={subjectId}
            disabled={isEdit}
            onChange={(e) => {
              setSubjectId(e.target.value)
              setPicked([])
            }}
          >
            <option value="">Tanlang...</option>
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>

          <Input
            label="Guruh nomi"
            required
            placeholder="Masalan: Kuchli ingliz tili"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </div>

        {isEdit && (
          <p className="-mt-2 text-xs text-slate-400">
            Guruhning fanini o'zgartirib bo'lmaydi — kerak bo'lsa yangi guruh oching.
          </p>
        )}

        {subjects.length === 0 && (
          <p className="text-sm text-amber-700">
            Guruhlarga bo'linadigan fan yo'q. Avval "Fanlar" bo'limida kerakli fanni
            "guruhlarga bo'linadi" deb belgilang.
          </p>
        )}

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Sinf darajalari</span>
          <div className="flex flex-wrap gap-1.5">
            {gradeOptions.map((g) => (
              <Chip key={g} active={grades.includes(g)} onClick={() => toggleGrade(g)}>
                {g}-sinf
              </Chip>
            ))}
          </div>
        </div>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">
            Guruhni boqadigan sinflar <span className="text-red-500">*</span>
          </span>
          {classOptions.length === 0 ? (
            <p className="text-sm text-slate-400">Avval sinf darajasini tanlang</p>
          ) : (
            <div className="flex flex-wrap gap-1.5">
              {classOptions.map((c) => (
                <Chip key={c.id} active={classIds.includes(c.id)} onClick={() => toggleClass(c.id)}>
                  {c.name}
                </Chip>
              ))}
            </div>
          )}
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">
              O'qituvchilar <span className="text-red-500">*</span>
            </span>
            <div className="flex max-h-32 flex-wrap gap-1.5 overflow-y-auto">
              {teachers.map((t) => (
                <Chip
                  key={t.id}
                  active={teacherIds.includes(t.id)}
                  onClick={() => toggleTeacher(t.id)}
                >
                  {t.fullName}
                </Chip>
              ))}
            </div>
          </div>

          <Select
            label="Jins (ixtiyoriy)"
            value={gender}
            onChange={(e) => setGender(e.target.value)}
          >
            <option value="">Aralash</option>
            <option value="male">O'g'il bolalar</option>
            <option value="female">Qizlar</option>
          </Select>
        </div>
      </Card>

      {/* ---------- Ikki panelli tanlov ---------- */}
      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="space-y-3 p-0">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
            <h2 className="text-sm font-semibold text-slate-700">
              Nomzodlar ({visibleCandidates.length})
            </h2>
            {selectable.length > 0 && (
              <button
                type="button"
                onClick={addAll}
                className="text-xs font-medium text-brand-600 hover:underline"
              >
                Hammasini qo'shish ({selectable.length})
              </button>
            )}
          </div>

          <div className="px-4">
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
                placeholder="Ism bo'yicha qidirish..."
                value={candidateSearch}
                onChange={(e) => setCandidateSearch(e.target.value)}
              />
            </div>
          </div>

          <div className="max-h-96 overflow-y-auto px-2 pb-3">
            {!subjectId || classIds.length === 0 ? (
              <p className="px-2 py-8 text-center text-sm text-slate-400">
                Fan va sinflarni tanlang — nomzodlar shundan keyin chiqadi
              </p>
            ) : visibleCandidates.length === 0 ? (
              <p className="px-2 py-8 text-center text-sm text-slate-400">Nomzod yo'q</p>
            ) : (
              visibleCandidates.map((c) => {
                const busy = Boolean(c.currentGroupId)
                return (
                  <button
                    key={c.studentId}
                    type="button"
                    disabled={busy}
                    onClick={() => addCandidate(c)}
                    title={
                      busy
                        ? `Bu o'quvchi shu fan bo'yicha "${c.currentGroupName}" guruhida`
                        : undefined
                    }
                    className={cn(
                      'flex w-full items-center justify-between rounded-lg px-2 py-2 text-left text-sm transition-colors',
                      busy
                        ? 'cursor-not-allowed text-slate-300'
                        : 'text-slate-700 hover:bg-brand-50',
                    )}
                  >
                    <span className="truncate">{c.fullName}</span>
                    <span className="ml-2 shrink-0 text-xs text-slate-400">
                      {busy ? c.currentGroupName : c.className}
                    </span>
                  </button>
                )
              })
            )}
          </div>
        </Card>

        <Card className="space-y-3 p-0">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
            <h2 className="flex items-center gap-2 text-sm font-semibold text-slate-700">
              <Users className="h-4 w-4 text-brand-600" /> Guruh ro'yxati ({picked.length})
            </h2>
          </div>

          <div className="max-h-[27rem] overflow-y-auto px-2 pb-3">
            {picked.length === 0 ? (
              <p className="px-2 py-8 text-center text-sm text-slate-400">
                Chapdan o'quvchi tanlang
              </p>
            ) : (
              picked.map((p) => (
                <div
                  key={p.studentId}
                  className="flex items-center justify-between rounded-lg px-2 py-2 text-sm hover:bg-slate-50"
                >
                  <span className="truncate text-slate-700">{p.fullName}</span>
                  <span className="flex shrink-0 items-center gap-1">
                    <span className="mr-1 text-xs text-slate-400">{p.className}</span>
                    {p.memberId && (
                      <button
                        type="button"
                        title="Boshqa guruhga o'tkazish"
                        onClick={() => setTransferFor(p)}
                        className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                      >
                        <ArrowRightLeft className="h-4 w-4" />
                      </button>
                    )}
                    <button
                      type="button"
                      title="Ro'yxatdan olib tashlash"
                      onClick={() => removePicked(p.studentId)}
                      className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </span>
                </div>
              ))
            )}
          </div>
        </Card>
      </div>

      <GroupTransferModal
        open={Boolean(transferFor)}
        memberId={transferFor?.memberId ?? ''}
        studentName={transferFor?.fullName ?? ''}
        subjectId={subjectId}
        currentGroupId={id ?? ''}
        onClose={() => setTransferFor(null)}
        onDone={() => {
          const moved = transferFor
          setTransferFor(null)
          if (moved) removePicked(moved.studentId)
          loadCandidates()
        }}
      />
    </div>
  )
}

function toPicked(m: StudyGroupMember): PickedStudent {
  return {
    studentId: m.studentId,
    fullName: m.fullName,
    className: m.className,
    memberId: m.id,
  }
}

function Chip({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-lg border px-2.5 py-1 text-sm transition-colors',
        active
          ? 'border-brand-600 bg-brand-600 text-white'
          : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}
