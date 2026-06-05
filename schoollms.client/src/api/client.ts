import axios from 'axios'

/**
 * Markaziy axios klienti.
 * Base URL .env faylidagi VITE_API_BASE_URL dan olinadi.
 */
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  headers: { 'Content-Type': 'application/json' },
})

// Har bir so'rovga auth tokenni qo'shamiz
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Token tugagan/yaroqsiz bo'lsa (401) — sessiyani tozalab login sahifasiga qaytaramiz.
// Login so'rovining o'zidagi 401 (parol noto'g'ri) bundan mustasno — uni LoginPage ko'rsatadi.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response?.status
    const url: string = error.config?.url ?? ''
    const isLoginCall = url.includes('/auth/login')
    if (status === 401 && !isLoginCall) {
      localStorage.removeItem('token')
      localStorage.removeItem('user')
      if (window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    return Promise.reject(error)
  },
)

/**
 * Backend hali tayyor bo'lmagani uchun vaqtinchalik mock rejim.
 * .env da VITE_USE_MOCK=false qilinsa, haqiqiy API ishlatiladi.
 */
export const USE_MOCK = import.meta.env.VITE_USE_MOCK !== 'false'
