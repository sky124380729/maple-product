import { describe, expect, it } from 'vitest'
import { calculateRemainingMs, formatDurationSeconds } from './useRhythmCountdown'

describe('deadline countdown', () => {
  it('calculates remaining time from the deadline instead of decrementing state', () => {
    expect(calculateRemainingMs(30_000, 2_562)).toBe(27_438)
    expect(calculateRemainingMs(30_000, 30_500)).toBe(0)
  })

  it('formats milliseconds with three decimal places', () => {
    expect(formatDurationSeconds(27_438)).toBe('27.438 秒')
  })
})
