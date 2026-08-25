import type { StationaryRhythmState } from '../bridge/types'

export type SessionStatus = 'idle' | 'locating' | 'arming' | 'running' | 'stopped' | 'error'

export interface SessionState {
  status: SessionStatus
  rhythm: StationaryRhythmState | null
  retiredSessionId: string | null
  stopReason: string | null
  error: string | null
}

export type SessionAction =
  | { type: 'starting' }
  | { type: 'arming' }
  | { type: 'rhythmUpdated'; payload: StationaryRhythmState }
  | { type: 'stopped'; reason: string; payload?: StationaryRhythmState }
  | { type: 'failed'; error: string }
  | { type: 'reset' }

export const initialSessionState: SessionState = {
  status: 'idle',
  rhythm: null,
  retiredSessionId: null,
  stopReason: null,
  error: null,
}

export function sessionReducer(state: SessionState, action: SessionAction): SessionState {
  switch (action.type) {
    case 'starting':
      return {
        status: 'locating',
        rhythm: null,
        retiredSessionId: state.rhythm?.sessionId ?? state.retiredSessionId,
        stopReason: null,
        error: null,
      }
    case 'arming':
      return { ...state, status: 'arming' }
    case 'rhythmUpdated':
      if (action.payload.sessionId === state.retiredSessionId) return state
      return { ...state, status: 'running', rhythm: action.payload, stopReason: null, error: null }
    case 'stopped': {
      if (action.payload?.sessionId === state.retiredSessionId) return state
      const finalRhythm = action.payload ?? state.rhythm
      return {
        ...state,
        status: 'stopped',
        rhythm: finalRhythm == null ? null : {
          ...finalRhythm,
          phase: 'stopped',
          phaseDeadlineMonoMs: finalRhythm.updatedAtMonoMs,
          remainingMs: 0,
          earlyReleaseReason: null,
        },
        stopReason: action.reason,
        error: null,
      }
    }
    case 'failed':
      return {
        status: 'error',
        rhythm: null,
        retiredSessionId: state.rhythm?.sessionId ?? state.retiredSessionId,
        stopReason: null,
        error: action.error,
      }
    case 'reset':
      return initialSessionState
  }
}
