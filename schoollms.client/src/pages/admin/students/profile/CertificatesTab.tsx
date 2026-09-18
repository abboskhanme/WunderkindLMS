import { useCallback, useEffect, useState } from 'react'
import { Award, Download, Pencil, Plus, Trash2 } from 'lucide-react'
import type { Student, Subject, Teacher } from '@/types'
import type { Certificate, CertificateType } from '@/api/services/certificates'
import { deleteCertificate, getCertificateTypes, getCertificates } from '@/api/services/certificates'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { CertificateFormModal } from '@/pages/admin/certificates/CertificateFormModal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { ProfileEmpty, ProfileError, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Sertifikatlar" tab'i — docs/modules/students-parity.md §2.7
 * (Z-1). Registr API'si allaqachon `studentId` bo'yicha filtrlaydi, shuning
 * uchun bu tab yangi endpoint talab qilmaydi.
 *
 * FORMA BITTA: registr ekranidagi `CertificateFormModal` ning o'zi, o'quvchi
 * oldindan qo'yilgan holda. Ikkinchi forma yozilmaydi — ball qoidasi (tur
 * "ball qo'yiladi" bo'lsagina ball) bir joyda qolishi kerak.
 */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

export function CertificatesTab({ student }: { student: Student }) {
  const [rows, setRows] = useState<Certificate[]>([])
  const [types, setTypes] = useState<CertificateType[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Certificate | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    Promise.all([
      getCertificates({ studentId: student.id }),
      getCertificateTypes(),
      getSubjects(),
      getTeachers(),
    ])
      .then(([list, t, sub, te]) => {
        setRows(list)
        setTypes(t)
        setSubjects(sub)
        setTeachers(te)
      })
      .catch((e) => setError(errorText(e, "Sertifikatlarni olib bo'lmadi")))
      .finally(() => setLoading(false))
  }, [student.id])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tab ochilganda hujjatlar yuklanadi (maqsadli, loyihadagi mavjud naqsh)
    load()
  }, [load])

  const remove = async (row: Certificate) => {
    if (!confirm(`${row.typeName} hujjatini o'chirasizmi?`)) return
    try {
      await deleteCertificate(row.id)
      setRows((prev) => prev.filter((r) => r.id !== row.id))
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  const add = (
    <Button
      onClick={() => {
        setEditing(null)
        setFormOpen(true)
      }}
      disabled={types.length === 0}
      title={types.length === 0 ? "Avval sertifikat turini qo'shing" : undefined}
    >
      <Plus className="h-4 w-4" /> Sertifikat
    </Button>
  )

  return (
    <ProfileSection title="Sertifikatlar" icon={Award} action={add}>
      {error ? (
        <ProfileError message={error} />
      ) : loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <ProfileEmpty>Bu o'quvchida hujjat yo'q</ProfileEmpty>
      ) : (
        <div className="space-y-2">
          {rows.map((row) => (
            <div
              key={row.id}
              className="flex flex-wrap items-center gap-3 rounded-lg border border-slate-100 px-3 py-2"
            >
              <div className="min-w-0 flex-1">
                <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-slate-800">
                  {row.typeName}
                  {row.number && <span className="text-slate-400">№ {row.number}</span>}
                  {row.typeIsScored && row.score != null && (
                    <span className="rounded bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">
                      {row.score} ball
                    </span>
                  )}
                  {row.isExpired && (
                    <span className="rounded bg-red-50 px-2 py-0.5 text-xs font-medium text-red-600">
                      Muddati o'tgan
                    </span>
                  )}
                </p>
                <p className={cn('mt-0.5 text-xs', row.isExpired ? 'text-red-400' : 'text-slate-400')}>
                  {formatDate(row.issuedOn)}
                  {row.expiresOn ? ` — ${formatDate(row.expiresOn)}` : ''}
                  {row.subjectNames.length > 0 ? ` · ${row.subjectNames.join(', ')}` : ''}
                  {row.teacherName ? ` · ${row.teacherName}` : ''}
                </p>
                {row.comment && <p className="mt-1 text-xs text-slate-500">{row.comment}</p>}
              </div>

              <div className="flex gap-1">
                {row.fileUrl && (
                  <a
                    href={row.fileUrl}
                    target="_blank"
                    rel="noreferrer"
                    title="Yuklab olish"
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-brand-600"
                  >
                    <Download className="h-4 w-4" />
                  </a>
                )}
                <button
                  type="button"
                  title="Tahrirlash"
                  onClick={() => {
                    setEditing(row)
                    setFormOpen(true)
                  }}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                >
                  <Pencil className="h-4 w-4" />
                </button>
                <button
                  type="button"
                  title="O'chirish"
                  onClick={() => remove(row)}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {formOpen && (
        <CertificateFormModal
          editing={editing}
          types={types}
          // Faqat shu o'quvchi: kartochkadan boshqasiga hujjat yozib bo'lmaydi.
          students={[student]}
          subjects={subjects}
          teachers={teachers}
          defaultStudentId={student.id}
          onClose={() => setFormOpen(false)}
          onSaved={(saved) => {
            setFormOpen(false)
            setRows((prev) => {
              const rest = prev.filter((r) => r.id !== saved.id)
              return [saved, ...rest].sort((a, b) => b.issuedOn.localeCompare(a.issuedOn))
            })
          }}
        />
      )}
    </ProfileSection>
  )
}
