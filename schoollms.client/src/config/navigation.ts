import type { LucideIcon } from 'lucide-react'
import {
  LayoutDashboard,
  UserPlus,
  Users,
  GraduationCap,
  School,
  NotebookText,
  CalendarRange,
  ClipboardList,
  CalendarCheck,
  Wallet,
  MessageSquare,
  ClipboardCheck,
  Settings,
  Smartphone,
  BarChart3,
  FileSignature,
  Building2,
  BookOpen,
} from 'lucide-react'
import type { Role } from '@/types'

export interface NavChild {
  label: string
  to: string
  /** NavLink exact match (faqat shu manzilda faol) */
  end?: boolean
  /** Faqat shu rollarga ko'rinadi (yo'q = barcha rollarga) */
  roles?: Role[]
  /** Ruxsat kaliti — xodim (staff) shu bo'limga ega bo'lsagina ko'rinadi */
  perm?: string
}

export interface NavItem {
  label: string
  to: string
  icon: LucideIcon
  children?: NavChild[]
  /** Bo'lim ruxsat kaliti (o'qituvchi/xodim filtri uchun; yo'q = har doim ko'rinadi) */
  perm?: string
  /** Faqat shu rollarga ko'rinadi (yo'q = barcha rollarga) */
  roles?: Role[]
}

/** Har bir rol uchun yon menyu (sidebar) elementlari */
export const navByRole: Record<Role, NavItem[]> = {
  admin: [
    { label: 'Bosh sahifa', to: '/admin', icon: LayoutDashboard },
    { label: 'Lidlar', to: '/admin/leads', icon: UserPlus, perm: 'leads' },
    { label: "O'quvchilar", to: '/admin/students', icon: Users, perm: 'students' },
    { label: "O'qituvchilar", to: '/admin/teachers', icon: GraduationCap, perm: 'teachers' },
    { label: 'Davomat', to: '/admin/attendance', icon: CalendarCheck, perm: 'attendance' },
    {
      label: 'Dars jadvali',
      to: '/admin/schedule',
      icon: CalendarRange,
      perm: 'schedule',
      children: [
        { label: 'Sinf jadvali', to: '/admin/schedule', end: true },
        { label: "O'qituvchi jadvali", to: '/admin/schedule/teachers' },
        { label: 'Dars jadvali yaratish', to: '/admin/schedule/manage' },
        { label: 'Fanlar', to: '/admin/subjects' },
        { label: 'Choraklar', to: '/admin/settings/quarters' },
        { label: 'Dars vaqtlari', to: '/admin/settings/lesson-times' },
        { label: 'Davomat sabablari', to: '/admin/settings/reasons' },
      ],
    },
    {
      label: 'Sinflar',
      to: '/admin/classes',
      icon: School,
      perm: 'classes',
      children: [
        { label: "Sinflar ro'yxati", to: '/admin/classes', end: true },
        { label: 'Reyting', to: '/admin/classes/rating' },
      ],
    },
    { label: 'Jurnal', to: '/admin/journal', icon: NotebookText, perm: 'journal' },
    { label: 'Xabarlar', to: '/admin/messages', icon: MessageSquare, perm: 'messages' },
    {
      label: 'Ilova',
      to: '/admin/assignments',
      icon: Smartphone,
      perm: 'app',
      children: [
        { label: 'Topshiriqlar', to: '/admin/assignments' },
        { label: 'Topshiriqlar bali', to: '/admin/assignment-scores' },
        { label: "Ta'lim (LMS)", to: '/admin/lms' },
        { label: 'Joylashuv', to: '/admin/locations' },
        { label: 'Oshxona', to: '/admin/canteen' },
        { label: 'Ota-onalar', to: '/admin/parents' },
        // Topshiriq turlari sozlamadan ko'chirildi; route hamon settings — perm 'settings'
        { label: 'Topshiriq turlari', to: '/admin/settings/assignment-types', perm: 'settings' },
      ],
    },
    {
      label: 'Baholar hisoboti',
      to: '/admin/grades-report',
      icon: ClipboardList,
      perm: 'gradesReport',
      children: [
        { label: "Maktab bo'yicha", to: '/admin/grades-report/school' },
        { label: "Sinf bo'yicha", to: '/admin/grades-report/class' },
        { label: "O'quvchi bo'yicha", to: '/admin/grades-report/student' },
      ],
    },
    { label: "O'qituvchilar hisoboti", to: '/admin/teacher-reports', icon: BarChart3, perm: 'teacherReports' },
    { label: 'Shartnomalar', to: '/admin/contracts', icon: FileSignature, perm: 'contracts' },
    { label: 'Moliya', to: '/admin/finance', icon: Wallet, perm: 'finance' },
    {
      label: 'Boshqaruv',
      to: '/admin/boshqaruv/staff',
      icon: Building2,
      children: [
        { label: 'Filiallar', to: '/admin/boshqaruv/branches', roles: ['superadmin'] },
        { label: 'Rollar', to: '/admin/boshqaruv/roles', roles: ['superadmin'] },
        { label: 'Xodimlar', to: '/admin/boshqaruv/staff', perm: 'staff' },
        { label: 'Taklif va shikoyatlar', to: '/admin/boshqaruv/feedback', perm: 'feedback' },
      ],
    },
    {
      label: 'Sozlamalar',
      to: '/admin/settings/school',
      icon: Settings,
      perm: 'settings',
      children: [
        { label: "Maktab ma'lumotlari", to: '/admin/settings/school' },
        { label: 'Telegram bot', to: '/admin/settings/telegram' },
        { label: "Yangi o'quv yiliga o'tish", to: '/admin/academic-year', perm: 'academicYear' },
      ],
    },
  ],
  teacher: [
    { label: 'Bosh sahifa', to: '/teacher', icon: LayoutDashboard },
    { label: 'Jurnal', to: '/teacher/journal', icon: NotebookText, perm: 'journal' },
    { label: 'Topshiriqlar', to: '/teacher/assignments', icon: ClipboardCheck, perm: 'assignments' },
    { label: "Ta'lim (LMS)", to: '/teacher/lms', icon: BookOpen },
    { label: 'Dars jadvali', to: '/teacher/schedule', icon: CalendarRange, perm: 'schedule' },
    { label: 'Xabarlar', to: '/teacher/messages', icon: MessageSquare, perm: 'messages' },
    { label: 'Maosh', to: '/teacher/salary', icon: Wallet, perm: 'salary' },
  ],
  student: [{ label: 'Bosh sahifa', to: '/student', icon: LayoutDashboard }],
  parent: [{ label: 'Bosh sahifa', to: '/parent', icon: LayoutDashboard }],
  // Superadmin admin bilan bir xil nav'ni ishlatadi (qo'shimcha menyusiz, faqat ruxsat farqli)
  superadmin: [],
  // Xodim ham admin nav'ini ishlatadi — Sidebar uni permissions bo'yicha filtrlaydi
  staff: [],
}

// Superadmin va xodim admin nav'ini qayta ishlatadi (Sidebar rol/ruxsat bo'yicha filtrlaydi).
navByRole.superadmin = navByRole.admin
navByRole.staff = navByRole.admin

/** Rol bo'yicha asosiy sahifa manzili */
export const homeByRole: Record<Role, string> = {
  superadmin: '/admin',
  admin: '/admin',
  teacher: '/teacher',
  student: '/student',
  parent: '/parent',
  staff: '/admin',
}

export const roleLabels: Record<Role, string> = {
  superadmin: 'Tizim egasi',
  admin: 'Administrator',
  teacher: "O'qituvchi",
  student: "O'quvchi",
  parent: 'Ota-ona',
  staff: 'Xodim',
}
