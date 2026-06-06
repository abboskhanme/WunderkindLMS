import { useEffect } from 'react'

/**
 * O'qituvchi endi alohida PWA'da ishlaydi (`/teacher/`, wwwroot/teacher — statik ilova).
 * Bu yerda SPA ichidan to'liq sahifa yuklash bilan o'sha ilovaga o'tamiz (react-router emas).
 * Token localStorage'da bir xil kalitda ('token') — PWA uni o'qib, qayta login so'ramaydi.
 */
export function TeacherAppRedirect() {
  useEffect(() => {
    window.location.replace('/teacher/')
  }, [])
  return null
}
