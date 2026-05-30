import type { ScheduleLesson, ScheduleTemplate } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { templatesMock } from '../mock/scheduleTemplates'

export async function getTemplates(classId: string): Promise<ScheduleTemplate[]> {
  if (USE_MOCK) {
    await delay()
    return templatesMock[classId] ?? []
  }
  const { data } = await api.get<ScheduleTemplate[]>(`/admin/classes/${classId}/schedule-templates`)
  return data
}

export async function createTemplate(classId: string, name: string): Promise<ScheduleTemplate> {
  if (USE_MOCK) {
    await delay(200)
    const tpl: ScheduleTemplate = { id: uid(), classId, name, lessons: [] }
    ;(templatesMock[classId] ??= []).push(tpl)
    return tpl
  }
  const { data } = await api.post<ScheduleTemplate>(
    `/admin/classes/${classId}/schedule-templates`,
    { name },
  )
  return data
}

export async function renameTemplate(
  classId: string,
  templateId: string,
  name: string,
): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    const tpl = templatesMock[classId]?.find((t) => t.id === templateId)
    if (tpl) tpl.name = name
    return
  }
  await api.patch(`/admin/classes/${classId}/schedule-templates/${templateId}`, { name })
}

export async function deleteTemplate(classId: string, templateId: string): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    if (templatesMock[classId]) {
      templatesMock[classId] = templatesMock[classId].filter((t) => t.id !== templateId)
    }
    return
  }
  await api.delete(`/admin/classes/${classId}/schedule-templates/${templateId}`)
}

/** Template ichidagi bitta katakni belgilash */
export async function setTemplateSlot(
  classId: string,
  templateId: string,
  lesson: ScheduleLesson,
): Promise<void> {
  if (USE_MOCK) {
    await delay(120)
    const tpl = templatesMock[classId]?.find((t) => t.id === templateId)
    if (tpl) {
      tpl.lessons = [
        ...tpl.lessons.filter((l) => !(l.day === lesson.day && l.period === lesson.period)),
        lesson,
      ]
    }
    return
  }
  await api.put(
    `/admin/classes/${classId}/schedule-templates/${templateId}/${lesson.day}/${lesson.period}`,
    lesson,
  )
}

/** Template ichidagi katakni tozalash */
export async function clearTemplateSlot(
  classId: string,
  templateId: string,
  day: number,
  period: number,
): Promise<void> {
  if (USE_MOCK) {
    await delay(120)
    const tpl = templatesMock[classId]?.find((t) => t.id === templateId)
    if (tpl) {
      tpl.lessons = tpl.lessons.filter((l) => !(l.day === day && l.period === period))
    }
    return
  }
  await api.delete(`/admin/classes/${classId}/schedule-templates/${templateId}/${day}/${period}`)
}

/** O'qituvchining hozir tahrirlayotgan template'dan boshqa joylardagi band soatlari.
 *  Qaytadi: { [teacherId]: [{ day, period, className, templateName }] }
 *  `excludeTemplateId` — hozir tahrirlanayotgan template (o'zi bilan ziddiyat ko'rsatilmasin). */
export async function getOccupiedSlots(
  excludeTemplateId: string,
): Promise<Record<string, { day: number; period: number; className: string; templateName: string }[]>> {
  if (USE_MOCK) return {}
  const { data } = await api.get('/admin/schedule/occupied-slots', {
    params: { excludeTemplateId },
  })
  return data as Record<string, { day: number; period: number; className: string; templateName: string }[]>
}

/**
 * Bitta (Day,Period) katakning to'liq holatini almashtirish.
 * Lessons: [] = tozalash, [SubGroup=0] = butun sinf, [SubGroup=1, SubGroup=2] = bo'lingan.
 */
export async function setTemplateCell(
  classId: string,
  templateId: string,
  day: number,
  period: number,
  lessons: ScheduleLesson[],
): Promise<void> {
  if (USE_MOCK) {
    await delay(120)
    const tpl = templatesMock[classId]?.find((t) => t.id === templateId)
    if (tpl) {
      tpl.lessons = [
        ...tpl.lessons.filter((l) => !(l.day === day && l.period === period)),
        ...lessons,
      ]
    }
    return
  }
  await api.put(
    `/admin/classes/${classId}/schedule-templates/${templateId}/cell`,
    { day, period, lessons },
  )
}
