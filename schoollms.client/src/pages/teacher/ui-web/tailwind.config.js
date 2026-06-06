/**
 * Teacher App design tokens — ported 1:1 from lib/core/theme/app_colors.dart.
 *
 * Two kinds of colors:
 *  1. Fixed palette (teal scale, grade colors, semantic) — identical in light & dark.
 *  2. Theme-aware tokens (surface, bg, border, text…) — driven by CSS variables in
 *     index.css and flipped by the `.dark` class on <html>. This mirrors the
 *     Flutter `AppColors.getXxx(context)` pattern.
 */
/** @type {import('tailwindcss').Config} */
export default {
  darkMode: 'class',
  content: ['./index.html', './src/**/*.{js,jsx}'],
  theme: {
    extend: {
      colors: {
        // ── Fixed teal scale (Tailwind-aligned, same as app) ──
        teal: {
          50: '#F0FDFA',
          100: '#CCFBF1',
          300: '#5EEAD4',
          400: '#2DD4BF',
          500: '#14B8A6',
          600: '#0D9488',
          700: '#0F766E',
          800: '#115E59',
          900: '#134E4A',
          950: '#042F2E',
        },
        // ── Grade colors (5→1) ──
        grade: {
          5: '#059669',
          4: '#0D9488',
          3: '#D97706',
          2: '#EA580C',
          1: '#E11D48',
        },
        // ── Semantic ──
        success: '#10B981',
        warning: '#F59E0B',
        danger: '#EF4444',
        info: '#3B82F6',

        // ── Theme-aware (CSS variables) ──
        bg: 'var(--bg)',
        'bg-alt': 'var(--bg-alt)',
        surface: 'var(--surface)',
        surface2: 'var(--surface2)',
        surface3: 'var(--surface3)',
        border: 'var(--border)',
        'border-soft': 'var(--border-soft)',
        text: 'var(--text)',
        muted: 'var(--muted)',
        faint: 'var(--faint)',
        primary: 'var(--primary)',
        'primary-soft': 'var(--primary-soft)',
        chip: 'var(--chip)',
      },
      fontFamily: {
        sans: ['"Plus Jakarta Sans"', 'system-ui', 'sans-serif'],
        mono: ['"JetBrains Mono"', 'ui-monospace', 'monospace'],
      },
      borderRadius: {
        // App uses these specific radii heavily
        xl: '14px',
        '2xl': '16px',
        '3xl': '18px',
        '4xl': '20px',
        '5xl': '22px',
        '6xl': '24px',
      },
      boxShadow: {
        card: '0 2px 8px rgba(15,26,23,0.04)',
        soft: '0 1px 2px rgba(15,26,23,0.04)',
        sheet: '0 -8px 32px rgba(0,0,0,0.12)',
        glow: '0 12px 28px rgba(13,148,136,0.30)',
        fab: '0 8px 24px rgba(13,148,136,0.40)',
      },
      letterSpacing: {
        tightest: '-0.03em',
        tighter2: '-0.025em',
      },
    },
  },
  plugins: [],
}
