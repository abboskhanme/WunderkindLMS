import { useEffect, useState } from 'react'
import { Send } from 'lucide-react'
import type { Student } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'

interface Props {
  open: boolean
  onClose: () => void
  recipients: Student[]
}

export function SmsModal({ open, onClose, recipients }: Props) {
  const [message, setMessage] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda xabar maydonini tozalaymiz (maqsadli)
    if (open) setMessage('')
  }, [open])

  const handleSend = () => {
    if (!message.trim()) return
    // TODO: API — SMS yuborish
    alert(`${recipients.length} ta ota-onaga SMS yuborildi.`)
    onClose()
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="SMS yuborish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={handleSend} disabled={!message.trim()}>
            <Send className="h-4 w-4" /> Yuborish
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
          Qabul qiluvchilar: <b>{recipients.length} ta</b> ota-ona
        </div>
        <Textarea
          label="Xabar matni"
          rows={5}
          placeholder="Hurmatli ota-onalar, ..."
          value={message}
          onChange={(e) => setMessage(e.target.value)}
        />
      </div>
    </Modal>
  )
}
