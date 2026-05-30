import { useEffect, useState } from 'react'
import type { Credentials, Student } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { getStudentCredentials, resetStudentPassword } from '@/api/services/students'
import { genderLabels } from '@/config/constants'
import { formatDate, formatMoney } from '@/lib/utils'

interface Props {
  student: Student | null
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

export function StudentViewModal({ student, onClose }: Props) {
  const [credentials, setCredentials] = useState<Credentials | null>(null)

  useEffect(() => {
    if (!student) {
      setCredentials(null)
      return
    }
    let active = true
    setCredentials(null)
    getStudentCredentials(student.id)
      .then((c) => active && setCredentials(c))
      .catch(() => active && setCredentials(null))
    return () => {
      active = false
    }
  }, [student])

  return (
    <Modal
      open={!!student}
      onClose={onClose}
      title="O'quvchi ma'lumotlari"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      {student && (
        <div className="space-y-0">
          <Row label="F.I.SH" value={student.fullName} />
          <Row label="Tug'ilgan kun" value={formatDate(student.birthDate)} />
          <Row label="Jinsi" value={genderLabels[student.gender]} />
          <Row label="Manzil" value={student.address} />
          <Row label="Sinf" value={student.className} />
          <Row label="Ota-onasi" value={student.parentFullName} />
          <Row label="Ota-onasi raqami" value={student.parentPhone} />
          <Row label="Balans" value={formatMoney(student.balance)} />
          {(student.discountPct > 0 || student.discountAmount > 0) && (
            <Row
              label="Chegirma"
              value={
                [
                  student.discountPct > 0 ? `${student.discountPct}%` : null,
                  student.discountAmount > 0 ? `${formatMoney(student.discountAmount)}` : null,
                ]
                  .filter(Boolean)
                  .join(' + ') + (student.discountNote ? ` — ${student.discountNote}` : '')
              }
            />
          )}
          <CredentialsBox
            credentials={credentials}
            onReset={async () => {
              const c = await resetStudentPassword(student.id)
              setCredentials(c)
            }}
          />
        </div>
      )}
    </Modal>
  )
}
