import { useEffect, useState } from 'react'
import type { StationaryRhythmState } from '../bridge/types'

export function calculateRemainingMs(deadlineMonoMs: number, nowMonoMs: number) {
  return Math.max(0, Math.round(deadlineMonoMs - nowMonoMs))
}

export function formatDurationSeconds(milliseconds: number) {
  return `${(Math.max(0, milliseconds) / 1000).toFixed(3)} 秒`
}

export function useRhythmCountdown(rhythm: StationaryRhythmState | null) {
  const [remainingMs, setRemainingMs] = useState(rhythm?.remainingMs ?? 0)

  useEffect(() => {
    if (!rhythm) {
      setRemainingMs(0)
      return
    }

    const localReceivedAt = performance.now()
    const backendNowAtReceipt = rhythm.updatedAtMonoMs
    const update = () => {
      const estimatedBackendNow = backendNowAtReceipt + performance.now() - localReceivedAt
      setRemainingMs(calculateRemainingMs(rhythm.phaseDeadlineMonoMs, estimatedBackendNow))
    }
    update()
    const timer = window.setInterval(update, 33)
    return () => window.clearInterval(timer)
  }, [rhythm?.sessionId, rhythm?.cycleId, rhythm?.phase, rhythm?.phaseDeadlineMonoMs, rhythm?.updatedAtMonoMs])

  return remainingMs
}
