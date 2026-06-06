// Ported from AppColors.dart — helpers that pick a color from a value.

export const GRADE_COLORS = {
  5: '#059669',
  4: '#0D9488',
  3: '#D97706',
  2: '#EA580C',
  1: '#E11D48',
}

export function gradeColor(grade) {
  return GRADE_COLORS[grade] ?? '#94A09B'
}

const AVATAR_PALETTE = [
  '#0D9488', // teal600
  '#3B82F6', // info
  '#7C3AED',
  '#DB2777',
  '#F59E0B', // warning
  '#10B981', // success
  '#2563EB',
]

export function avatarColor(name) {
  const hash = [...(name || '')].reduce((a, c) => a + c.charCodeAt(0), 0)
  return AVATAR_PALETTE[hash % AVATAR_PALETTE.length]
}

export function initials(name) {
  return (name || '')
    .trim()
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((p) => p[0].toUpperCase())
    .join('')
}
