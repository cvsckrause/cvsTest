import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AuthGate } from './AuthGate.tsx'
import { PublicPageView } from './PublicPages.tsx'
import { publicPageFor } from './route.ts'

const publicPage = publicPageFor(window.location.pathname)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {publicPage ? (
      <PublicPageView page={publicPage} />
    ) : (
      <AuthGate>
        <App />
      </AuthGate>
    )}
  </StrictMode>,
)
