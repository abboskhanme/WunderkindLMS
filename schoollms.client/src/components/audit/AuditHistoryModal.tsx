import type { AuditFilters } from '@/api/services/audit'
import { Modal } from '@/components/ui/Modal'
import { AuditHistoryList } from './AuditHistoryList'

interface Props {
  open: boolean
  onClose: () => void
  title?: string
  filters: AuditFilters
  emptyLabel?: string
}

/** O'zgarishlar tarixini modal oynada ko'rsatadi (FinancePage uchun) */
export function AuditHistoryModal({ open, onClose, title = "O'zgarishlar tarixi", filters, emptyLabel }: Props) {
  return (
    <Modal open={open} onClose={onClose} size="lg" title={title}>
      {open && <AuditHistoryList filters={filters} emptyLabel={emptyLabel} />}
    </Modal>
  )
}
