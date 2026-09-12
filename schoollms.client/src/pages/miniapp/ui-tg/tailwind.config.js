/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,jsx}'],
  theme: {
    extend: {
      colors: {
        // Wunderkind brend ranglari. Mijoz bergan namunadan (o'zining KPI
        // botidan) olingan — sariq va oq asosiy, qora matn.
        brand: {
          DEFAULT: '#FFD006',
          dark: '#E8BD02',
          ink: '#0E1520',
        },
        ground: '#F0F0F0',
      },
      borderRadius: { card: '18px' },
      fontFamily: {
        sans: ['-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
