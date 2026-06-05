import { useEffect, useState } from 'react'
import { Plus, Search, Eye, Pencil, Trash2, Archive, RotateCcw, Download } from 'lucide-react'
import type { Gender, SchoolClass, Subject, Teacher } from '@/types'
import type { TeacherPayload } from '@/api/services/teachers'
import {
  getTeachers,
  getArchivedTeachers,
  createTeacher,
  updateTeacher,
  deleteTeacher,
  archiveTeacher,
  restoreTeacher,
  downloadTeacherCredentials,
} from '@/api/services/teachers'
import { useAuth } from '@/context/auth-context'
import { getSubjects } from '@/api/services/subjects'
import { getClasses } from '@/api/services/classes'
import { genderLabels, teacherCategoryLabel } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Textarea } from '@/components/ui/Input'
import { TeacherFormModal } from './TeacherFormModal'
import { TeacherViewModal } from './TeacherViewModal'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type Tab = 'active' | 'archived'

export function TeachersPage() {
  const { user } = useAuth()
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [archived, setArchived] = useState<Teacher[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [loading, setLoading] = useState(true)

  const [tab, setTab] = useState<Tab>('active')
  const [search, setSearch] = useState('')
  const [genderFilter, setGenderFilter] = useState<'all' | Gender>('all')
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Teacher | null>(null)
  const [viewing, setViewing] = useState<Teacher | null>(null)

  // Arxivga ko'chirish tasdiq oynasi
  const [archiveTarget, setArchiveTarget] = useState<Teacher | null>(null)
  const [reason, setReason] = useState('')

  useEffect(() => {
    Promise.all([getTeachers(), getArchivedTeachers(), getSubjects(), getClasses()])
      .then(([t, a, s, c]) => {
        setTeachers(t)
        setArchived(a)
        setSubjects(s)
        setClasses(c)
      })
      .finally(() => setLoading(false))
  }, [])

  const subjectName = (id: string) => subjects.find((s) => s.id === id)?.name ?? id

  const source = tab === 'active' ? teachers : archived
  const filtered = source.filter((t) => {
    const q = search.trim().toLowerCase()
    const matchSearch = !q || t.fullName.toLowerCase().includes(q)
    const matchGender = genderFilter === 'all' || t.gender === genderFilter
    return matchSearch && matchGender
  })

  const handleSubmit = (values: TeacherPayload) => {
    if (editing) {
      updateTeacher(editing.id, values).then((u) =>
        setTeachers((prev) => prev.map((t) => (t.id === u.id ? u : t))),
      )
    } else {
      createTeacher(values).then((c) => {
        setTeachers((prev) => [c, ...prev])
        setViewing(c)
      })
    }
    setFormOpen(false)
    setEditing(null)
  }

  const confirmArchive = () => {
    if (!archiveTarget) return
    const t = archiveTarget
    const today = new Date().toISOString().slice(0, 10)
    archiveTeacher(t.id, reason.trim()).then(() => {
      setTeachers((prev) => prev.filter((x) => x.id !== t.id))
      setArchived((prev) => [
        { ...t, isArchived: true, archivedAt: today, archiveReason: reason.trim() },
        ...prev,
      ])
    })
    setArchiveTarget(null)
    setReason('')
  }

  const handleRestore = (t: Teacher) => {
    if (!confirm(`"${t.fullName}" o'qituvchini arxivdan qaytarasizmi?`)) return
    restoreTeacher(t.id).then(() => {
      setArchived((prev) => prev.filter((x) => x.id !== t.id))
      setTeachers((prev) =>
        [{ ...t, isArchived: false, archivedAt: null, archiveReason: null }, ...prev].sort((a, b) =>
          a.fullName.localeCompare(b.fullName),
        ),
      )
    })
  }

  const handleDelete = (t: Teacher) => {
    if (!confirm(`"${t.fullName}" o'qituvchini BUTUNLAY o'chirasizmi? Bu amalni ortga qaytarib bo'lmaydi.`))
      return
    if (!confirm('Aniq ishonchingiz komilmi? Barcha ma\'lumotlari o\'chadi.')) return
    deleteTeacher(t.id).then(() => setArchived((prev) => prev.filter((x) => x.id !== t.id)))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">O'qituvchilar</h1>
          <p className="text-sm text-slate-400">
            Faol {teachers.length} ta · Arxivda {archived.length} ta
          </p>
        </div>
        <div className="flex items-center gap-2">
          {/* Faqat superadmin: o'qituvchilarni login/parol bilan Excel'ga yuklab olish.
              Parol faqat o'qituvchi hali kirmagan bo'lsa ko'rinadi. */}
          {user?.role === 'superadmin' && (
            <Button variant="secondary" onClick={() => downloadTeacherCredentials()}>
              <Download className="h-4 w-4" /> Login/parollar
            </Button>
          )}
          {tab === 'active' && (
            <Button
              onClick={() => {
                setEditing(null)
                setFormOpen(true)
              }}
            >
              <Plus className="h-4 w-4" /> Yangi qo'shish
            </Button>
          )}
        </div>
      </div>

      {/* Faol | Arxiv toggle */}
      <div className="inline-flex rounded-lg border border-slate-200 bg-white p-1">
        <TabButton active={tab === 'active'} onClick={() => setTab('active')}>
          Faol ({teachers.length})
        </TabButton>
        <TabButton active={tab === 'archived'} onClick={() => setTab('archived')}>
          Arxiv ({archived.length})
        </TabButton>
      </div>

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative flex-1 min-w-[200px]">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="F.I.SH bo'yicha qidirish..."
              className={cn(control, 'w-full pl-9')}
            />
          </div>
          <select
            value={genderFilter}
            onChange={(e) => setGenderFilter(e.target.value as 'all' | Gender)}
            className={control}
          >
            <option value="all">Barcha jinslar</option>
            <option value="male">{genderLabels.male}</option>
            <option value="female">{genderLabels.female}</option>
          </select>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Jinsi</th>
                  {tab === 'active' ? (
                    <>
                      <th className="px-4 py-3">Tug'ilgan kun</th>
                      <th className="px-4 py-3">Sinf rahbarligi</th>
                      <th className="px-4 py-3">Fanlar</th>
                      <th className="px-4 py-3">Toifa</th>
                    </>
                  ) : (
                    <>
                      <th className="px-4 py-3">Fanlar</th>
                      <th className="px-4 py-3">Arxiv sanasi</th>
                      <th className="px-4 py-3">Sabab</th>
                    </>
                  )}
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((t, i) => (
                  <tr key={t.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{t.fullName}</td>
                    <td className="px-4 py-3 text-slate-600">{genderLabels[t.gender]}</td>
                    {tab === 'active' ? (
                      <>
                        <td className="px-4 py-3 text-slate-600">{formatDate(t.birthDate)}</td>
                        <td className="px-4 py-3">
                          {t.homeroomClass ? (
                            <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                              {t.homeroomClass}
                            </span>
                          ) : (
                            <span className="text-slate-400">—</span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <SubjectTags ids={t.subjectIds} name={subjectName} />
                        </td>
                        <td className="px-4 py-3">
                          {t.category ? (
                            <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                              {teacherCategoryLabel(t.category)}
                            </span>
                          ) : (
                            <span className="text-slate-400">—</span>
                          )}
                        </td>
                      </>
                    ) : (
                      <>
                        <td className="px-4 py-3">
                          <SubjectTags ids={t.subjectIds} name={subjectName} />
                        </td>
                        <td className="px-4 py-3 text-slate-600">
                          {t.archivedAt ? formatDate(t.archivedAt) : '—'}
                        </td>
                        <td className="max-w-[220px] truncate px-4 py-3 text-slate-500" title={t.archiveReason ?? ''}>
                          {t.archiveReason || '—'}
                        </td>
                      </>
                    )}
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        <IconBtn icon={Eye} title="Ko'rish" onClick={() => setViewing(t)} />
                        {tab === 'active' ? (
                          <>
                            <IconBtn
                              icon={Pencil}
                              title="Tahrirlash"
                              onClick={() => {
                                setEditing(t)
                                setFormOpen(true)
                              }}
                            />
                            <IconBtn
                              icon={Archive}
                              title="Arxivga ko'chirish"
                              onClick={() => {
                                setReason('')
                                setArchiveTarget(t)
                              }}
                            />
                          </>
                        ) : (
                          <>
                            <IconBtn
                              icon={RotateCcw}
                              title="Arxivdan qaytarish"
                              onClick={() => handleRestore(t)}
                            />
                            <IconBtn
                              icon={Trash2}
                              title="Butunlay o'chirish"
                              danger
                              onClick={() => handleDelete(t)}
                            />
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={tab === 'active' ? 8 : 7} className="px-4 py-12 text-center text-slate-400">
                      {tab === 'active' ? 'Hech narsa topilmadi' : 'Arxivda o\'qituvchi yo\'q'}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <TeacherFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
        subjects={subjects}
        classes={classes}
      />
      <TeacherViewModal teacher={viewing} subjects={subjects} onClose={() => setViewing(null)} />

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
            <span className="font-semibold text-slate-800">{archiveTarget?.fullName}</span> arxivga
            ko'chiriladi: faol ro'yxatdan yashiriladi va tizimga kirishi bloklanadi. Jurnal va
            hisobot ma'lumotlari saqlanib qoladi.
          </p>
          <Textarea
            label="Sabab (ixtiyoriy)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={3}
            placeholder="Masalan: ishdan bo'shadi"
          />
        </div>
      </Modal>
    </div>
  )
}

function SubjectTags({ ids, name }: { ids: string[]; name: (id: string) => string }) {
  if (ids.length === 0) return <span className="text-slate-400">—</span>
  return (
    <div className="flex flex-wrap gap-1">
      {ids.map((id) => (
        <span
          key={id}
          className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700"
        >
          {name(id)}
        </span>
      ))}
    </div>
  )
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
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

interface IconBtnProps {
  icon: typeof Eye
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
