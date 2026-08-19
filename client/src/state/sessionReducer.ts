import type { StationaryRhythmState } from '../bridge/types'

export type SessionStatus = 'idle' | 'locating' | 'arming' | 'running' | 'stopped' | 'error'

export interface SessionState {
  status: SessionStatus
  rhythm: StationaryRhythmState | null
  stopReason: string | null
  error: string | null
}

export type SessionAction =
  | { type: 'starting' }
  | { type: 'arming' }
  | { type: 'rhythmUpdated'; payload: StationaryRhythmState }
  | { type: 'stopped'; reason: string }
  | { type: 'failed'; error: string }
  | { type: 'reset' }

export const initialSessionState: SessionState = {
  status: 'idle',
  rhythm: null,
  stopReason: null,
  error: null,
}

export function sessionReducer(state: SessionState, action: SessionAction): SessionState {
  switch (action.type) {
    case 'starting':
      return { status: 'locating', rhythm: null, stopReason: null, error: null }
    case 'arming':
      return { ...state, status: 'arming' }
    case 'rhythmUpdated':
      return { status: 'running', rhythm: action.payload, stopReason: null, error: null }
    case 'stopped':
      return { status: 'stopped', rhythm: null, stopReason: action.reason, error: null }
    case 'failed':
      return { status: 'error', rhythm: null, stopReason: null, error: action.error }
    case 'reset':
      return initialSessionState
  }
}
