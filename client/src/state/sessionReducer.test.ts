import { describe, expect, it } from 'vitest'
import { initialSessionState, sessionReducer } from './sessionReducer'

describe('sessionReducer', () => {
  it('replaces the countdown when a new cycle arrives', () => {
    const first = sessionReducer(initialSessionState, {
      type: 'rhythmUpdated',
      payload: {
        schemaVersion: 1,
        sessionId: 'session-a',
        cycleId: 1,
        phase: 'attackHolding',
        sampledDurationMs: 27_438,
        phaseStartedMonoMs: 1_000,
        phaseDeadlineMonoMs: 28_438,
        remainingMs: 27_438,
        updatedAtMonoMs: 1_000,
        relativeOffsetMs: 0,
        earlyReleaseReason: null,
      },
    })
    const second = sessionReducer(first, {
      type: 'rhythmUpdated',
      payload: { ...first.rhythm!, cycleId: 2, sampledDurationMs: 42_007 },
    })

    expect(second.rhythm?.cycleId).toBe(2)
    expect(second.rhythm?.sampledDurationMs).toBe(42_007)
  })

  it('clears stale countdown and retains the final offset after stop', () => {
    const running = { ...initialSessionState, status: 'running' as const, rhythm: sampleRhythm() }

    const stopped = sessionReducer(running, { type: 'stopped', reason: 'FOCUS_LOST' })

    expect(stopped.rhythm).toMatchObject({
      phase: 'stopped',
      remainingMs: 0,
      relativeOffsetMs: -23,
    })
    expect(stopped.status).toBe('stopped')
  })

  it('ignores rhythm and stop messages from the retired session after restart', () => {
    const running = { ...initialSessionState, status: 'running' as const, rhythm: sampleRhythm() }
    const restarting = sessionReducer(running, { type: 'starting' })

    const staleRhythm = sessionReducer(restarting, {
      type: 'rhythmUpdated',
      payload: { ...sampleRhythm(), cycleId: 2 },
    })
    const staleStop = sessionReducer(staleRhythm, {
      type: 'stopped',
      reason: 'CANCELLED',
      payload: { ...sampleRhythm(), phase: 'stopped' },
    })

    expect(staleStop.status).toBe('locating')
    expect(staleStop.rhythm).toBeNull()
  })
})

function sampleRhythm() {
  return {
    schemaVersion: 1,
    sessionId: 'session-a',
    cycleId: 1,
    phase: 'attackHolding' as const,
    sampledDurationMs: 27_438,
    phaseStartedMonoMs: 1_000,
    phaseDeadlineMonoMs: 28_438,
    remainingMs: 27_438,
    updatedAtMonoMs: 1_000,
    relativeOffsetMs: -23,
    earlyReleaseReason: null,
  }
}
