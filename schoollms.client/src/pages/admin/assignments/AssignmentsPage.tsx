import { useEffect, useMemo, useState } from 'react'
import {
  ClipboardCheck,
  CalendarClock,
  Paperclip,
  ListChecks,
  Users,
  Plus,
  Pencil,
  Trash2,
} from 'lucide-react'
import type { Assignment, AssignmentFormat, SchoolClass, Subject } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import {
  getAssignments,
  getAssignmentResults,
  createAssignment,
  updateAssignment,
  deleteAssignment,
  uploadAdminFile,
  setAdminSubmission,
} from '@/api/services/assignments'
import { formatDate } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { SubmissionsModal } from '@/components/assignments/SubmissionsModal'
import { AssignmentWizard } from '@/components/assignments/AssignmentWizard'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

const formatLabel: Record<AssignmentFormat, string> = {
  written: 'Yozma',
  file: 'Fayl',
  test: 'Test',
  video: 'Video',
}

/** Admin "Topshiriqlar" — o'qituvchidek topshiriq yaratadi/tahrirlaydi va BARCHA topshiriqlarni boshqaradi. */
export function AssignmentsPage() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [classId, setClassId] = useState('')
  const [assignments, setAssignments] = useState<Assignment[]>([])
  const [loading, setLoading] = useState(true)
  const [resultsFor, setResultsFor] = useState<Assignment | null>(null)
  const [wizardOpen, setWizardOpen] = useState(false)
  const [editing, setEditing] = useState<Assignment | null>(null)

  useEffect(() => {
    Promise.all([getClasses(), getSubjects()]).then(([cl, sb]) => {
      setClasses(cl)
      setSubjects(sb)
    })
  }, [])

  const reload = () =>
    getAssignments(classId || undefined)
      .then(setAssignments)
      .finally(() => setLoading(false))

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sinf filtri o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    reload()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- faqat classId o'zgarganda
  }, [classId])

  const wizardClasses = useMemo(() => classes.map((c) => ({ id: c.id, name: c.name })), [classes])

  const openNew = () => {
    setEditing(null)
    setWizardOpen(true)
  }
  const openEdit = (a: Assignment) => {
    setEditing(a)
    setWizardOpen(true)
  }
  const handleDelete = async (a: Assignment) => {
    if (!confirm(`"${a.title}" topshirig'ini o'chirasizmi?`)) return
    await deleteAssignment(a.id)
    setAssignments((prev) => prev.filter((x) => x.id !== a.id))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Topshiriqlar</h1>
          <p className="text-sm text-slate-400">Topshiriq va testlar — yaratish va barchasini boshqarish</p>
        </div>
        <Button onClick={openNew} disabled={classes.length === 0}>
          <Plus className="h-4 w-4" /> Yangi topshiriq
        </Button>
      </div>

      <Card>
        <div className="flex flex-wrap items-center gap-3">
          <select className={control} value={classId} onChange={(e) => setClassId(e.target.value)}>
            <option value="">Barcha sinflar</option>
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
      </Card>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : assignments.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Topshiriq yo'q</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
          {assignments.map((a) => (
            <Card key={a.id} className="space-y-2">
              <div className="flex items-start justify-between gap-2">
                <div className="flex items-center gap-2">
                  <ClipboardCheck className="h-5 w-5 shrink-0 text-brand-600" />
                  <p className="font-semibold text-slate-800">{a.title}</p>
                </div>
                <div className="flex shrink-0 items-center gap-0.5">
                  <button
                    type="button"
                    title="Tahrirlash"
                    onClick={() => openEdit(a)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    title="O'chirish"
                    onClick={() => handleDelete(a)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>

              <div className="flex flex-wrap items-center gap-1.5 text-xs">
                <span className="rounded-full bg-brand-50 px-2 py-0.5 font-medium text-brand-700">
                  {formatLabel[a.format]}
                </span>
                {a.subjectName && (
                  <span className="rounded-full bg-slate-100 px-2 py-0.5 text-slate-600">{a.subjectName}</span>
                )}
                {a.classNames.map((n) => (
                  <span key={n} className="rounded-full bg-slate-100 px-2 py-0.5 text-slate-600">
                    {n}
                  </span>
                ))}
              </div>

              {a.description.trim() && (
                <p className="line-clamp-3 whitespace-pre-wrap text-sm text-slate-600">{a.description}</p>
              )}

              <div className="flex flex-wrap items-center gap-3 text-xs text-slate-400">
                {a.dueDate && (
                  <span className="inline-flex items-center gap-1 text-amber-600">
                    <CalendarClock className="h-3.5 w-3.5" />
                    {formatDate(a.dueDate)}
                  </span>
                )}
                {a.materials.length > 0 && (
                  <span className="inline-flex items-center gap-1">
                    <Paperclip className="h-3.5 w-3.5" />
                    {a.materials.length} material
                  </span>
                )}
                {a.format === 'test' && (
                  <span className="inline-flex items-center gap-1">
                    <ListChecks className="h-3.5 w-3.5" />
                    {a.questions.length} savol
                  </span>
                )}
                <span>Maks: {a.maxScore} ball</span>
              </div>

              <button
                type="button"
                onClick={() => setResultsFor(a)}
                className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-1.5 text-sm font-medium text-slate-600 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
              >
                <Users className="h-4 w-4" /> Kim bajardi
              </button>
            </Card>
          ))}
        </div>
      )}

      <AssignmentWizard
        open={wizardOpen}
        onClose={() => {
          setWizardOpen(false)
          setEditing(null)
        }}
        onSaved={reload}
        classes={wizardClasses}
        subjects={subjects}
        initial={editing}
        onSubmit={async (input, id) => {
          if (id) await updateAssignment(id, input)
          else await createAssignment(input)
        }}
        onUpload={uploadAdminFile}
      />

      <SubmissionsModal
        open={!!resultsFor}
        onClose={() => setResultsFor(null)}
        title={resultsFor ? `${resultsFor.title} — kim bajardi` : ''}
        fetchResult={() => getAssignmentResults(resultsFor!.id)}
        onSave={(studentId, completed, score) =>
          setAdminSubmission(resultsFor!.id, studentId, completed, score)
        }
      />
    </div>
  )
}
