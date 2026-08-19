import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { StationaryAttackPage } from './pages/StationaryAttackPage'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <StationaryAttackPage />
  </StrictMode>,
)
