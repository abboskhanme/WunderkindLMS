import { useEffect, useState } from 'react'
import type { Credentials, Subject, Teacher } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { getTeacherCredentials } from '@/api/services/teachers'
import { genderLabels, formatMonth } from '@/config/constants'
import { formatDate, formatMoney } from '@/lib/utils'

interface Props {
  teacher: Teacher | null
  subjects: Subject[]
  onClose: () => void
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-sm text-slate-400">{label}</span>
      <span className="text-right text-sm font-medium text-slate-800">{value}</span>
    </div>
  )
}

export function TeacherViewModal({ teacher, subjects, onClose }: Props) {
  const [credentials, setCredentials] = useState<Credentials | null>(null)

  useEffect(() => {
    if (!teacher) {
      setCredentials(null)
      return
    }
    let active = true
    setCredentials(null)
    getTeacherCredentials(teacher.id)
      .then((c) => active && setCredentials(c))
      .catch(() => active && setCredentials(null))
    return () => {
      active = false
    }
  }, [teacher])

  const subjectNames = teacher
    ? teacher.subjectIds
        .map((id) => subjects.find((s) => s.id === id)?.name)
        .filter(Boolean)
        .join(', ')
    : ''

  return (
    <Modal
      open={!!teacher}
      onClose={onClose}
      title="O'qituvchi ma'lumotlari"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      {teacher && (
        <div>
          <Row label="F.I.SH" value={teacher.fullName} />
          <Row label="Jinsi" value={genderLabels[teacher.gender]} />
          <Row label="Tug'ilgan kun" value={formatDate(teacher.birthDate)} />
          <Row label="Manzil" value={teacher.address || '—'} />
          <Row label="Sinf rahbarligi" value={teacher.homeroomClass || '—'} />
          <Row label="Oylik ish haqi" value={teacher.salary ? formatMoney(teacher.salary) : '—'} />
          <Row
            label="Oylik hisoblanadi"
            value={teacher.salaryStartMonth ? `${formatMonth(teacher.salaryStartMonth)} dan` : '—'}
          />
          <Row label="Fanlar" value={subjectNames || '—'} />
          <CredentialsBox credentials={credentials} />
        </div>
      )}
    </Modal>
  )
}
