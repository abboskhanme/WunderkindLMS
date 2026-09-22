import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Plus,
  Pencil,
  Archive,
  ArchiveRestore,
  Copy,
  Users,
  Search,
  Eye,
} from 'lucide-react'
import type { Subject } from '@/types'
import type { StudyGroupListItem } from '@/api/services/groups'
import {
  getGroups,
  archiveGroup,
  unarchiveGroup,
  duplicateGroup,
} from '@/api/services/groups'
import { getSubjects } from '@/api/services/subjects'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { DataTable } from '@/components/table/DataTable'
import type { DataTableColumn } from '@/components/table/DataTable'
import { cn } from '@/lib/utils'

/** Sinf darajalari — filtr uchun (0 = maktabgacha tayyorlov). */
const GRADES = Array.from({ length: 12 }, (_, i) => i)

/**
 * Guruhlar ro'yxati — `docs/modules/students-parity.md` §2.1 (G-8).
 *
 * Guruh sinfdan YUQORIDA turadi: bir nechta sinfdan yig'iladi va bitta fanni
 * o'qiydi. Shuning uchun ro'yxatning asosiy ustunlari fan, boqadigan sinflar
 * va o'qituvchilar.
 */
export function GroupsPage() {
  const navigate = useNavigate()
  const [groups, setGroups] = useState<StudyGroupListItem[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [grades, setGrades] = useState<number[]>([])
  const [subjectId, setSubjectId] = useState('')
  const [showArchived, setShowArchived] = useState(false)
  const [gradesOpen, setGradesOpen] = useState(false)

  const load = useCallback(() => {
    setLoading(true)
    getGroups({ search, grades, subjectId, archived: showArchived })
      .then(setGroups)
      .finally(() => setLoading(false))
  }, [search, grades, subjectId, showArchived])

  useEffect(() => {
    getSubjects(undefined, true).then(setSubjects)
  }, [])

  useEffect(() => {
    // Qidiruv har harfda so'rov yubormasin.
    const t = setTimeout(load, 250)
    return () => clearTimeout(t)
  }, [load])

  const toggleGrade = (g: number) =>
    setGrades((prev) => (prev.includes(g) ? prev.filter((x) => x !== g) : [...prev, g]))

  const failed = (e: unknown, fallback: string) =>
    alert(
      (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback,
    )

  const handleArchive = (g: StudyGroupListItem) => {
    if (
      !confirm(
        `"${g.name}" guruhini arxivlaysizmi?\n` +
          `Guruhdagi ${g.memberCount} ta o'quvchining a'zoligi yopiladi (tarix saqlanadi).`,
      )
    )
      return
    archiveGroup(g.id)
      .then(load)
      .catch((e) => failed(e, 'Arxivlashda xatolik'))
  }

  const handleUnarchive = (g: StudyGroupListItem) => {
    unarchiveGroup(g.id)
      .then(load)
      .catch((e) => failed(e, 'Arxivdan chiqarishda xatolik'))
  }

  const handleDuplicate = (g: StudyGroupListItem) => {
    const name = prompt('Nusxaning nomi:', `${g.name} (nusxa)`)
    if (!name?.trim()) return
    // Ro'yxat faqat ARXIVLANGAN guruhdan ko'chiriladi: faol guruhning bolalari
    // bir vaqtda ikki guruhda bo'lib qolardi (bitta fandan bitta guruh qoidasi).
    const copyMembers = g.isArchived && confirm("O'quvchilar ro'yxati ham ko'chirilsinmi?")
    duplicateGroup(g.id, { name: name.trim(), copyMembers })
      .then((created) => navigate(`/admin/groups/${created.id}`))
      .catch((e) => failed(e, 'Nusxalashda xatolik'))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">
            {showArchived ? 'Arxivlangan guruhlar' : "O'quv guruhlari"}
          </h1>
          <p className="text-sm text-slate-400">
            Bir nechta sinfdan yig'iladigan, bitta fan bo'yicha o'qiydigan guruhlar — jami{' '}
            {groups.length} ta
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={() => setShowArchived((v) => !v)}>
            {showArchived ? (
              <>
                <Users className="h-4 w-4" /> Faol guruhlar
              </>
            ) : (
              <>
                <Archive className="h-4 w-4" /> Arxiv
              </>
            )}
          </Button>
          {!showArchived && (
            <Button onClick={() => navigate('/admin/groups/new')}>
              <Plus className="h-4 w-4" /> Yangi guruh
            </Button>
          )}
        </div>
      </div>

      <Card className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative min-w-[220px] flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              placeholder="Guruh nomi bo'yicha qidirish..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </div>

          <select
            className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            value={subjectId}
            onChange={(e) => setSubjectId(e.target.value)}
          >
            <option value="">Barcha fanlar</option>
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>

          <div className="relative">
            <Button variant="secondary" onClick={() => setGradesOpen((v) => !v)}>
              Sinf darajasi{grades.length > 0 ? ` (${grades.length})` : ''}
            </Button>
            {gradesOpen && (
              <div className="absolute right-0 z-20 mt-1 w-56 rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
                <div className="grid grid-cols-4 gap-1">
                  {GRADES.map((g) => (
                    <button
                      key={g}
                      type="button"
                      onClick={() => toggleGrade(g)}
                      className={cn(
                        'rounded-lg px-2 py-1.5 text-sm transition-colors',
                        grades.includes(g)
                          ? 'bg-brand-600 text-white'
                          : 'text-slate-600 hover:bg-slate-100',
                      )}
                    >
                      {g}
                    </button>
                  ))}
                </div>
                {grades.length > 0 && (
                  <button
                    type="button"
                    onClick={() => setGrades([])}
                    className="mt-2 w-full rounded-lg px-2 py-1.5 text-xs text-slate-500 hover:bg-slate-100"
                  >
                    Tozalash
                  </button>
                )}
              </div>
            )}
          </div>
        </div>
      </Card>

      <Card className="p-0">
        <DataTable
          pageKey="admin.groups"
          columns={groupColumns(navigate, handleDuplicate, handleArchive, handleUnarchive)}
          rows={groups}
          getRowId={(g) => g.id}
          onRowClick={(g) => navigate(`/admin/groups/${g.id}`)}
          loading={loading}
          emptyMessage={
            showArchived
              ? "Arxivlangan guruh yo'q"
              : "Guruh yo'q. Guruh ochish uchun avval \"Fanlar\" bo'limida fanni \"guruhlarga bo'linadi\" deb belgilang."
          }
        />
      </Card>
    </div>
  )
}

/**
 * Ustunlar ta'rifi — X-1: `DataTable` ularni yashirish/tartiblash/qadash
 * imkonini beradi. "#" va "Amallar" doim ko'rinadi (`alwaysVisible`).
 */
function groupColumns(
  navigate: ReturnType<typeof useNavigate>,
  onDuplicate: (g: StudyGroupListItem) => void,
  onArchive: (g: StudyGroupListItem) => void,
  onUnarchive: (g: StudyGroupListItem) => void,
): DataTableColumn<StudyGroupListItem>[] {
  return [
    {
      id: 'index',
      header: '#',
      headerClassName: 'w-10',
      alwaysVisible: true,
      cell: (_g, i) => <span className="text-slate-400">{i + 1}</span>,
    },
    {
      id: 'name',
      header: 'Guruh',
      alwaysVisible: true,
      cell: (g) => (
        // Jins bo'yicha ajratish maktabda yo'q (2026-09-23) — belgi ko'rsatilmaydi.
        <>
          <span className="font-medium text-slate-800">{g.name}</span>
          {g.isTrack && (
            <span className="ml-2 rounded-md bg-violet-50 px-1.5 py-0.5 text-[11px] font-medium text-violet-600">
              Yo'nalish
            </span>
          )}
        </>
      ),
    },
    {
      id: 'subject',
      header: 'Fan',
      cell: (g) => <span className="text-slate-600">{g.subjectName}</span>,
    },
    {
      id: 'classes',
      header: 'Sinflar',
      cell: (g) => (
        <span className="text-slate-600">{g.classes.map((c) => c.name).join(', ') || '—'}</span>
      ),
    },
    {
      id: 'teachers',
      header: "O'qituvchilar",
      cell: (g) => (
        <span className="text-slate-600">{g.teachers.map((t) => t.fullName).join(', ') || '—'}</span>
      ),
    },
    {
      id: 'members',
      header: "O'quvchilar",
      cell: (g) => <span className="text-slate-600">{g.memberCount} ta</span>,
    },
    {
      id: 'actions',
      header: 'Amallar',
      headerClassName: 'text-right',
      alwaysVisible: true,
      cell: (g) => (
        <div className="flex items-center justify-end gap-0.5" onClick={(e) => e.stopPropagation()}>
          <IconBtn
            icon={Eye}
            title="Ko'rish (ro'yxat)"
            onClick={() => navigate(`/admin/groups/${g.id}/students`)}
          />
          <IconBtn icon={Copy} title="Nusxalash" onClick={() => onDuplicate(g)} />
          {!g.isArchived && (
            <IconBtn
              icon={Pencil}
              title="Tahrirlash"
              onClick={() => navigate(`/admin/groups/${g.id}`)}
            />
          )}
          {g.isArchived ? (
            <IconBtn
              icon={ArchiveRestore}
              title="Arxivdan chiqarish"
              onClick={() => onUnarchive(g)}
            />
          ) : (
            <IconBtn icon={Archive} title="Arxivlash" onClick={() => onArchive(g)} />
          )}
        </div>
      ),
    },
  ]
}

interface IconBtnProps {
  icon: typeof Pencil
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
