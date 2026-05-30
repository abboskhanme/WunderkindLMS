import { useEffect, useState } from 'react'
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { Plus } from 'lucide-react'
import type { Lead, Stage } from '@/types'
import { getLeads, createLead, updateLead, updateLeadStage, deleteLead } from '@/api/services/leads'
import {
  getStages,
  createStage,
  updateStage,
  deleteStage,
  reorderStages,
  type StagePayload,
} from '@/api/services/stages'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { LeadColumn } from './LeadColumn'
import { LeadCardContent } from './LeadCard'
import { LeadFormModal, type LeadFormValues } from './LeadFormModal'
import { LeadDetailModal } from './LeadDetailModal'
import { StageFormModal } from './StageFormModal'

export function LeadsPage() {
  const [leads, setLeads] = useState<Lead[]>([])
  const [stages, setStages] = useState<Stage[]>([])
  const [loading, setLoading] = useState(true)
  const [activeId, setActiveId] = useState<string | null>(null)

  // modallar
  const [detailLead, setDetailLead] = useState<Lead | null>(null)
  const [leadFormOpen, setLeadFormOpen] = useState(false)
  const [editingLead, setEditingLead] = useState<Lead | null>(null)
  const [stageFormOpen, setStageFormOpen] = useState(false)
  const [editingStage, setEditingStage] = useState<Stage | null>(null)

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  useEffect(() => {
    Promise.all([getLeads(), getStages()])
      .then(([l, s]) => {
        setLeads(l)
        setStages(s)
      })
      .finally(() => setLoading(false))
  }, [])

  /* ---------- Karta drag-and-drop ---------- */
  const handleDragStart = (e: DragStartEvent) => setActiveId(String(e.active.id))

  const handleDragEnd = (e: DragEndEvent) => {
    setActiveId(null)
    const { active, over } = e
    if (!over) return
    const leadId = String(active.id)
    const newStage = String(over.id)
    const lead = leads.find((l) => l.id === leadId)
    if (!lead || lead.stage === newStage) return

    setLeads((prev) => prev.map((l) => (l.id === leadId ? { ...l, stage: newStage } : l)))
    updateLeadStage(leadId, newStage).catch(() => {
      setLeads((prev) => prev.map((l) => (l.id === leadId ? { ...l, stage: lead.stage } : l)))
    })
  }

  /* ---------- Lid CRUD ---------- */
  const handleLeadSubmit = (values: LeadFormValues) => {
    if (editingLead) {
      const id = editingLead.id
      setLeads((prev) => prev.map((l) => (l.id === id ? { ...l, ...values } : l)))
      updateLead(id, values)
    } else {
      const stageId = stages[0]?.id ?? 'new'
      createLead(values, stageId).then((lead) => setLeads((prev) => [lead, ...prev]))
    }
    setLeadFormOpen(false)
    setEditingLead(null)
  }

  const handleLeadEdit = (lead: Lead) => {
    setDetailLead(null)
    setEditingLead(lead)
    setLeadFormOpen(true)
  }

  const handleLeadDelete = (lead: Lead) => {
    if (!confirm(`"${lead.fullName}" lidini o'chirishni tasdiqlaysizmi?`)) return
    deleteLead(lead.id).then(() => {
      setLeads((prev) => prev.filter((l) => l.id !== lead.id))
      setDetailLead(null)
    })
  }

  /* ---------- Ustun CRUD ---------- */
  const handleStageSubmit = (values: StagePayload) => {
    if (editingStage) {
      const id = editingStage.id
      setStages((prev) => prev.map((s) => (s.id === id ? { ...s, ...values } : s)))
      updateStage(id, values)
    } else {
      createStage(values).then((stage) => setStages((prev) => [...prev, stage]))
    }
    setStageFormOpen(false)
    setEditingStage(null)
  }

  const handleStageDelete = (stage: Stage) => {
    const count = leads.filter((l) => l.stage === stage.id).length
    if (count > 0) {
      alert("Bu ustunda lidlar bor. Avval ularni boshqa ustunga ko'chiring.")
      return
    }
    if (stages.length <= 1) {
      alert('Kamida bitta ustun bo\'lishi kerak.')
      return
    }
    if (!confirm(`"${stage.title}" ustunini o'chirasizmi?`)) return
    deleteStage(stage.id).then(() => setStages((prev) => prev.filter((s) => s.id !== stage.id)))
  }

  const handleStageMove = (id: string, dir: -1 | 1) => {
    setStages((prev) => {
      const idx = prev.findIndex((s) => s.id === id)
      const j = idx + dir
      if (idx < 0 || j < 0 || j >= prev.length) return prev
      const next = [...prev]
      ;[next[idx], next[j]] = [next[j], next[idx]]
      reorderStages(next.map((s) => s.id))
      return next
    })
  }

  const activeLead = activeId ? leads.find((l) => l.id === activeId) : null

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Lidlar</h1>
          <p className="text-sm text-slate-400">
            Maktabga qiziqqanlar — kartani sudrang yoki ustiga bosing
          </p>
        </div>
        <Button
          onClick={() => {
            setEditingLead(null)
            setLeadFormOpen(true)
          }}
        >
          <Plus className="h-4 w-4" /> Yangi lid
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <DndContext sensors={sensors} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
          <div className="flex gap-4 overflow-x-auto pb-2">
            {stages.map((stage, i) => (
              <LeadColumn
                key={stage.id}
                stage={stage}
                leads={leads.filter((l) => l.stage === stage.id)}
                isFirst={i === 0}
                isLast={i === stages.length - 1}
                onCardClick={setDetailLead}
                onEdit={(s) => {
                  setEditingStage(s)
                  setStageFormOpen(true)
                }}
                onDelete={handleStageDelete}
                onMove={handleStageMove}
              />
            ))}

            <button
              onClick={() => {
                setEditingStage(null)
                setStageFormOpen(true)
              }}
              className="flex min-h-[200px] w-64 shrink-0 flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-slate-200 text-sm text-slate-400 transition-colors hover:border-brand-300 hover:text-brand-600"
            >
              <Plus className="h-5 w-5" /> Ustun qo'shish
            </button>
          </div>

          <DragOverlay>
            {activeLead ? <LeadCardContent lead={activeLead} dragging /> : null}
          </DragOverlay>
        </DndContext>
      )}

      {/* Modallar */}
      <LeadDetailModal
        lead={detailLead}
        onClose={() => setDetailLead(null)}
        onEdit={handleLeadEdit}
        onDelete={handleLeadDelete}
      />
      <LeadFormModal
        open={leadFormOpen}
        onClose={() => {
          setLeadFormOpen(false)
          setEditingLead(null)
        }}
        onSubmit={handleLeadSubmit}
        initial={editingLead}
      />
      <StageFormModal
        open={stageFormOpen}
        onClose={() => {
          setStageFormOpen(false)
          setEditingStage(null)
        }}
        onSubmit={handleStageSubmit}
        initial={editingStage}
      />
    </div>
  )
}
