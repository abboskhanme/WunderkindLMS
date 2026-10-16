import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Plus,
  Pencil,
  Trash2,
  Users,
  Archive,
  ArchiveRestore,
  ClipboardList,
  Search,
  Download,
  ShieldAlert,
} from 'lucide-react'
import type { SchoolClass } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import {
  getClasses,
  createClass,
  updateClass,
  deleteClass,
  getArchivedClasses,
  unarchiveClass,
  downloadClasses,
  setHomeroomTeachers,
  getAllHomeroomTeachers,
} from '@/api/services/classes'
import { getClassesStats, type ClassStats } from '@/api/services/classPerformance'
import { getGroups, type StudyGroupListItem } from '@/api/services/groups'
import { languageLabels } from '@/config/constants'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { ClassFormModal } from './ClassFormModal'
import { ClassGroupsModal } from './ClassGroupsModal'
import { ClassPointsModal } from './ClassPointsModal'
import { ArchiveClassModal } from './ArchiveClassModal'

export function ClassesPage() {
  const navigate = useNavigate()
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [stats, setStats] = useState<Record<string, ClassStats>>({})
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<SchoolClass | null>(null)
  /** Sinf guruhlarini boshqarish oynasi (Guruhlar tugmasi bilan ochiladi) */
  const [groupsFor, setGroupsFor] = useState<SchoolClass | null>(null)
  /** Sinfga ball qo'yish oynasi (C-6) */
  const [pointsFor, setPointsFor] = useState<SchoolClass | null>(null)
  /** Sinfni arxivlash oynasi — ketish sababi bilan (student-flow.md) */
  const [archiveFor, setArchiveFor] = useState<SchoolClass | null>(null)
  /** Arxivlangan sinflar ro'yxati + arxiv ko'rinishi yoqilganmi */
  const [archived, setArchived] = useState<SchoolClass[]>([])
  const [showArchived, setShowArchived] = useState(false)
  /** C-3: nom/xona bo'yicha qidiruv. */
  const [search, setSearch] = useState('')
  const [exporting, setExporting] = useState(false)
  /**
   * Faol yo'nalish guruhlari — sinflar JADVALINING oxirgi qatorlari (mijoz, 2026-09-26;
   * docs/modules/track-groups-as-classes.md). Sinflar (9-A ...) bu sahifada JOYIDA qoladi:
   * hujjat, moliya va shartnomalar ularga bog'liq.
   */
  const [tracks, setTracks] = useState<StudyGroupListItem[]>([])
  /** Sinf id'si → sinf rahbarlari (mijoz, 2026-10-09: "sinf rahbari degan ustun ham qo'sh"). */
  const [homeroom, setHomeroom] = useState<Record<string, string[]>>({})

  const loadHomeroom = useCallback(
    () =>
      getAllHomeroomTeachers()
        .then(setHomeroom)
        .catch(() => setHomeroom({})),
    [],
  )

  useEffect(() => {
    Promise.all([getClassesStats(), getArchivedClasses()]).then(([st, ar]) => {
      setStats(st)
      setArchived(ar)
    })
    getGroups()
      .then((gs) => setTracks(gs.filter((g) => g.isTrack && !g.isArchived)))
      .catch(() => setTracks([]))
    loadHomeroom()
  }, [loadHomeroom])

  const loadClasses = useCallback(() => {
    setLoading(true)
    return getClasses({ search: search.trim() || undefined }).then(setClasses).finally(() => setLoading(false))
  }, [search])

  // Debounce: qidiruv har harfda so'rov yubormasin (RoomsPage'dagi bilan bir xil naqsh).
  useEffect(() => {
    const timer = setTimeout(loadClasses, 250)
    return () => clearTimeout(timer)
  }, [loadClasses])

  const handleExport = () => {
    setExporting(true)
    downloadClasses(search.trim() || undefined).finally(() => setExporting(false))
  }

  const applyUpdate = (id: string, values: ClassPayload) =>
    updateClass(id, values).then((u) =>
      setClasses((prev) => prev.map((c) => (c.id === u.id ? u : c))),
    )

  // P1-21: "Yangi narxni joriy oyga qo'llaymizmi?" savoli OLIB TASHLANDI.
  // Sinf narxi endi mavjud obunalarga ta'sir qilmaydi — u faqat yangi obuna
  // uchun standart qiymat. Savolni qoldirish "Ha" tugmasi hech nima
  // qilmaydigan tugmaga aylanardi.
  //
  // C-5: sinf rahbari(lari) alohida endpoint bilan saqlanadi (sinf CRUD'idan mustaqil —
  // `ClassGroupsModal` guruhlarni qanday boshqarsa, shu ham xuddi shunday).
  const handleSubmit = (values: ClassPayload, homeroomTeacherIds: string[]) => {
    const saveHomeroom = (classId: string) =>
      setHomeroomTeachers(classId, homeroomTeacherIds)
        .then(loadHomeroom)
        .catch(() => alert("Sinf saqlandi, lekin sinf rahbarini belgilab bo'lmadi — qayta urinib ko'ring."))
    if (editing) {
      applyUpdate(editing.id, values).then(() => saveHomeroom(editing.id))
    } else {
      createClass(values).then((c) => {
        setClasses((prev) => [...prev, c])
        return saveHomeroom(c.id)
      })
    }
    setFormOpen(false)
    setEditing(null)
  }

  const handleDelete = (c: SchoolClass) => {
    if (!confirm(`"${c.name}" sinfini o'chirasizmi?`)) return
    deleteClass(c.id)
      .then(() => {
        setClasses((prev) => prev.filter((x) => x.id !== c.id))
        setArchived((prev) => prev.filter((x) => x.id !== c.id))
      })
      .catch((e) => alert(e?.response?.data?.message ?? "Sinfni o'chirib bo'lmadi"))
  }

  // The confirm became a dialog: the pupils' leave reason is chosen there (student-flow.md).
  const handleArchive = (c: SchoolClass) => setArchiveFor(c)

  const onArchived = (c: SchoolClass, archivedStudents: number) => {
    setArchiveFor(null)
    setClasses((prev) => prev.filter((x) => x.id !== c.id))
    setArchived((prev) => [{ ...c, isArchived: true }, ...prev])
    alert(`"${c.name}" arxivlandi — ${archivedStudents} ta o'quvchi ham arxivlandi.`)
  }

  const handleUnarchive = (c: SchoolClass) => {
    if (!confirm(`"${c.name}" sinfini arxivdan chiqarasizmi?\nSinf bilan arxivlangan o'quvchilar ham qaytariladi.`))
      return
    unarchiveClass(c.id)
      .then((r) => {
        setArchived((prev) => prev.filter((x) => x.id !== c.id))
        setClasses((prev) => [...prev, { ...c, isArchived: false }])
        alert(`"${c.name}" arxivdan chiqarildi — ${r.restoredStudents} ta o'quvchi ham qaytarildi.`)
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Arxivdan chiqarishda xatolik'))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">
            {showArchived ? 'Arxivlangan sinflar' : 'Sinflar va xonalar'}
          </h1>
          <p className="text-sm text-slate-400">
            {showArchived ? `${archived.length} ta arxivlangan sinf` : `Jami ${classes.length} ta sinf`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {!showArchived && (
            <Button variant="secondary" onClick={handleExport} disabled={exporting || classes.length === 0}>
              <Download className="h-4 w-4" />
              {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
            </Button>
          )}
          <Button variant="secondary" onClick={() => setShowArchived((v) => !v)}>
            {showArchived ? (
              <>
                <Users className="h-4 w-4" /> Faol sinflar
              </>
            ) : (
              <>
                <Archive className="h-4 w-4" /> Arxiv ({archived.length})
              </>
            )}
          </Button>
          {!showArchived && (
            <Button
              onClick={() => {
                setEditing(null)
                setFormOpen(true)
              }}
            >
              <Plus className="h-4 w-4" /> Yangi sinf
            </Button>
          )}
        </div>
      </div>

      {/* C-3: qidiruv — nom yoki xona bo'yicha. */}
      {!showArchived && (
        <Card className="flex items-end gap-3">
          <div className="relative min-w-[200px] flex-1">
            <Search className="absolute left-3 top-9 h-4 w-4 text-slate-400" />
            <Input
              label="Qidirish"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Sinf nomi yoki xona"
              className="pl-9"
            />
          </div>
        </Card>
      )}

      <Card className="p-0">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : showArchived ? (
          <ArchivedTable items={archived} onUnarchive={handleUnarchive} onDelete={handleDelete} />
        ) : (
          <div className="overflow-x-auto">
            {/* Kengliklar qat'iy (mijoz, 2026-10-09: "ustunlar sig'may qolyapti, kengligi bo'yicha
                tekislang"): sinf rahbari qolgan joyni oladi, matn qatorga sinmaydi — sig'masa "…"
                va to'lig'i hoverda; tor ekranda jadval yonga suriladi. */}
            <table className="w-full min-w-[60rem] table-fixed text-left text-sm">
              <colgroup>
                <col className="w-14" />
                <col className="w-36" />
                <col />
                <col className="w-24" />
                <col className="w-32" />
                <col className="w-32" />
                <col className="w-24" />
                <col className="w-56" />
              </colgroup>
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">#</th>
                  <th className="px-4 py-3">Sinf nomi</th>
                  <th className="px-4 py-3">Sinf rahbari</th>
                  <th className="px-4 py-3">Til</th>
                  <th className="px-4 py-3">Xona</th>
                  <th className="px-4 py-3">O'rtacha baho</th>
                  <th className="px-4 py-3">Davomat</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {classes.map((c, i) => (
                  <tr
                    key={c.id}
                    onClick={() => navigate(`/admin/classes/${c.id}`)}
                    className="cursor-pointer hover:bg-slate-50/60"
                  >
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block truncate" title={c.name}>
                        {c.name}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <HomeroomNames names={homeroom[c.id] ?? []} />
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium',
                          c.language === 'uz'
                            ? 'bg-blue-50 text-blue-700'
                            : 'bg-amber-50 text-amber-700',
                        )}
                      >
                        {languageLabels[c.language]}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      <span className="block truncate" title={c.room || undefined}>
                        {c.room || '—'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      {stats[c.id] ? (
                        <span className={cn('font-semibold', gradeColor(stats[c.id].averageGrade))}>
                          {stats[c.id].averageGrade.toFixed(1)}
                        </span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {stats[c.id] && stats[c.id].attendance != null ? (
                        <span className={cn('font-medium', attColor(stats[c.id].attendance!))}>
                          {stats[c.id].attendance}%
                        </span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <div
                        className="flex items-center justify-end gap-0.5"
                        onClick={(e) => e.stopPropagation()}
                      >
                        {/* C-2: sinf ro'yxati — qo'shish, sabab bilan chiqarish, o'tkazish. */}
                        <IconBtn
                          icon={ClipboardList}
                          title="Sinf ro'yxati"
                          onClick={() => navigate(`/admin/classes/${c.id}/roster`)}
                        />
                        <IconBtn
                          icon={Users}
                          title="Guruhlar (1/2)"
                          onClick={() => setGroupsFor(c)}
                        />
                        {/* C-6: butun sinfga bitta intizomiy ball. */}
                        <IconBtn
                          icon={ShieldAlert}
                          title="Sinfga ball qo'yish"
                          onClick={() => setPointsFor(c)}
                        />
                        <IconBtn
                          icon={Pencil}
                          title="Tahrirlash"
                          onClick={() => {
                            setEditing(c)
                            setFormOpen(true)
                          }}
                        />
                        <IconBtn
                          icon={Archive}
                          title="Arxivlash (o'quvchilari bilan)"
                          onClick={() => handleArchive(c)}
                        />
                        <IconBtn
                          icon={Trash2}
                          title="O'chirish"
                          danger
                          onClick={() => handleDelete(c)}
                        />
                      </div>
                    </td>
                  </tr>
                ))}
                {/* Yo'nalish guruhlari — sinflardan keyin, o'sha ustunlar bilan. Qidiruv ularga ham
                    qo'llanadi. Til va xona sinfniki — yo'nalishda "—". */}
                {tracks
                  .filter((g) => !search.trim() || g.name.toLowerCase().includes(search.trim().toLowerCase()))
                  .map((g, j) => (
                    <tr
                      key={g.id}
                      onClick={() => navigate(`/admin/groups/${g.id}/students`)}
                      className="cursor-pointer bg-violet-50/30 hover:bg-violet-50/60"
                    >
                      <td className="px-4 py-3 text-slate-400">{classes.length + j + 1}</td>
                      <td className="px-4 py-3 font-medium text-slate-800">
                        <span
                          className="block min-w-0"
                          title={`${g.name} · ${g.classes.map((c) => c.name).join(', ')} · ${g.memberCount} ta o'quvchi`}
                        >
                          <span className="block truncate">{g.name}</span>
                          <span className="mt-0.5 inline-block rounded-md bg-violet-100 px-1.5 py-0.5 text-[11px] font-medium text-violet-700">
                            yo'nalish
                          </span>
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        {/* Yo'nalish guruhining sinf rahbari — guruh o'qituvchisi (guruh formasida
                            "Sinf rahbari", 2026-09-29). */}
                        <HomeroomNames names={g.teachers.map((t) => t.fullName)} />
                      </td>
                      <td className="px-4 py-3 text-slate-400">—</td>
                      <td className="px-4 py-3 text-slate-400">—</td>
                      <td className="px-4 py-3">
                        {stats[g.id] && stats[g.id].averageGrade > 0 ? (
                          <span className={cn('font-semibold', gradeColor(stats[g.id].averageGrade))}>
                            {stats[g.id].averageGrade.toFixed(1)}
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        {stats[g.id] && stats[g.id].attendance != null ? (
                          <span className={cn('font-medium', attColor(stats[g.id].attendance!))}>
                            {stats[g.id].attendance}%
                          </span>
                        ) : (
                          <span className="text-slate-400">—</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <div
                          className="flex items-center justify-end gap-0.5"
                          onClick={(e) => e.stopPropagation()}
                        >
                          <IconBtn
                            icon={ClipboardList}
                            title="Yo'nalish ro'yxati"
                            onClick={() => navigate(`/admin/groups/${g.id}/students`)}
                          />
                          <IconBtn
                            icon={Pencil}
                            title="Tahrirlash"
                            onClick={() => navigate(`/admin/groups/${g.id}`)}
                          />
                        </div>
                      </td>
                    </tr>
                  ))}
                {classes.length === 0 && tracks.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-12 text-center text-slate-400">
                      Sinflar yo'q
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <ClassFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />

      <ClassGroupsModal
        open={!!groupsFor}
        classId={groupsFor?.id ?? ''}
        className={groupsFor?.name ?? ''}
        onClose={() => setGroupsFor(null)}
      />

      <ClassPointsModal
        open={!!pointsFor}
        classId={pointsFor?.id ?? ''}
        className={pointsFor?.name ?? ''}
        onClose={() => setPointsFor(null)}
        onDone={() => setPointsFor(null)}
      />

      <ArchiveClassModal cls={archiveFor} onClose={() => setArchiveFor(null)} onArchived={onArchived} />
    </div>
  )
}

function gradeColor(g: number): string {
  if (g >= 4.5) return 'text-emerald-600'
  if (g >= 4) return 'text-brand-600'
  if (g >= 3.5) return 'text-amber-600'
  return 'text-red-600'
}

function attColor(a: number): string {
  if (a >= 95) return 'text-emerald-600'
  if (a >= 90) return 'text-amber-600'
  return 'text-red-600'
}

function ArchivedTable({
  items,
  onUnarchive,
  onDelete,
}: {
  items: SchoolClass[]
  onUnarchive: (c: SchoolClass) => void
  onDelete: (c: SchoolClass) => void
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
          <tr>
            <th className="w-10 px-4 py-3">#</th>
            <th className="px-4 py-3">Sinf nomi</th>
            <th className="px-4 py-3">Til</th>
            <th className="px-4 py-3">Xona</th>
            <th className="px-4 py-3">Arxiv sanasi</th>
            <th className="px-4 py-3 text-right">Amallar</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {items.map((c, i) => (
            <tr key={c.id} className="hover:bg-slate-50/60">
              <td className="px-4 py-3 text-slate-400">{i + 1}</td>
              <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={c.name}>
                        {c.name}
                      </span>
                    </td>
              <td className="px-4 py-3">
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-xs font-medium',
                    c.language === 'uz' ? 'bg-blue-50 text-blue-700' : 'bg-amber-50 text-amber-700',
                  )}
                >
                  {languageLabels[c.language]}
                </span>
              </td>
              <td className="px-4 py-3 text-slate-600">{c.room || '—'}</td>
              <td className="px-4 py-3 text-slate-500">{c.archivedAt || '—'}</td>
              <td className="px-4 py-3">
                <div className="flex items-center justify-end gap-0.5">
                  <IconBtn
                    icon={ArchiveRestore}
                    title="Arxivdan chiqarish (o'quvchilari bilan)"
                    onClick={() => onUnarchive(c)}
                  />
                  <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => onDelete(c)} />
                </div>
              </td>
            </tr>
          ))}
          {items.length === 0 && (
            <tr>
              <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                Arxivlangan sinf yo'q
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
}

/** Sinf rahbarlari — har biri o'z qatorida; ko'p bo'lsa ikkitasi va "+N". */
function HomeroomNames({ names }: { names: string[] }) {
  if (names.length === 0) return <span className="text-slate-400">—</span>
  const shown = names.length > 2 ? names.slice(0, 2) : names
  return (
    <div className="min-w-0 text-slate-700" title={names.join('\n')}>
      {shown.map((n) => (
        <div key={n} className="truncate">
          {n}
        </div>
      ))}
      {names.length > shown.length && <div className="text-xs text-slate-400">+{names.length - shown.length} ta</div>}
    </div>
  )
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
