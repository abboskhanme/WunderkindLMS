import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import { AuthProvider } from '@/context/AuthProvider'
import { detectTenant } from '@/lib/tenant'
import { PlatformApp } from '@/platform/PlatformApp'

// Asosiy domen → Control Plane (loyiha boshlig'i). Subdomen → o'sha maktab LMS'i.
const tenant = detectTenant()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {tenant.isPlatform ? (
      <PlatformApp />
    ) : (
      <BrowserRouter>
        <AuthProvider>
          <App />
        </AuthProvider>
      </BrowserRouter>
    )}
  </StrictMode>,
)
