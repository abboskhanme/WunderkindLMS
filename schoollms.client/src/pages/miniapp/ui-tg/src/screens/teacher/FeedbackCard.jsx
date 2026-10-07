/**
 * TAKLIF VA SHIKOYATLAR — the teacher's way in (client, 2026-10-07: "oldin jo'natgan shikoyat
 * taklif qayerda saqlanadi"). One row on the Bugun screen opens the shared sheet: the teacher's
 * own items with status and answer, and the form for a new one.
 */
import { useState } from 'react'
import { ChevronRight, MessageSquareText } from 'lucide-react'
import { Card, Row } from '../../components/ui'
import { FeedbackSheet } from '../../components/FeedbackSheet'
import { haptic } from '../../lib/telegram'
import { teacherApi } from '../../lib/teacherApi'

const API = {
  list: () => teacherApi.myFeedback(),
  departments: () => teacherApi.feedbackDepartments(),
  submit: (formData) => teacherApi.sendFeedback(formData),
}

export function FeedbackCard() {
  const [open, setOpen] = useState(false)
  return (
    <>
      <Card>
        <Row
          lead={
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
              <MessageSquareText className="h-5 w-5" />
            </div>
          }
          title="Taklif va shikoyatlar"
          subtitle="Murojaat yuborish va holatini ko'rish"
          right={<ChevronRight className="h-5 w-5 text-slate-400" />}
          onClick={() => {
            haptic('light')
            setOpen(true)
          }}
        />
      </Card>
      {open && <FeedbackSheet api={API} onClose={() => setOpen(false)} />}
    </>
  )
}
