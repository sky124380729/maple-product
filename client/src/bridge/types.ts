export type StationaryPhase =
  | 'idle'
  | 'attackHolding'
  | 'attackReleased'
  | 'moveFirst'
  | 'moveGap'
  | 'moveSecond'
  | 'stabilizing'
  | 'resting'
  | 'stopped'

export interface StationaryRhythmState {
  schemaVersion: number
  sessionId: string
  cycleId: number
  phase: StationaryPhase
  sampledDurationMs: number
  phaseStartedMonoMs: number
  phaseDeadlineMonoMs: number
  remainingMs: number
  updatedAtMonoMs: number
  relativeOffsetMs: number
  earlyReleaseReason: string | null
}

export interface AttackBand {
  minMs: number
  maxMs: number
  weight: number
}

export interface StationaryAttackConfig {
  schemaVersion: 1
  source: string
  updatedAtUtc: string
  targetExecutablePath: string
  attackKey: string
  attackBands: AttackBand[]
  maxLateralMoveMs: number
  moveHoldMinMs: number
  moveHoldMaxMs: number
  moveGapMinMs: number
  moveGapMaxMs: number
  stabilizeMinMs: number
  stabilizeMaxMs: number
  restEnabled: boolean
  restProbabilityPercent: number
  restMinMs: number
  restMaxMs: number
  attackTriggerMode: 'always' | 'visualSafeContinuous' | 'monsterInRange'
  recognitionEnabled: boolean
}

export const safeDefaults: StationaryAttackConfig = {
  schemaVersion: 1,
  source: 'safe-default',
  updatedAtUtc: '1970-01-01T00:00:00.000Z',
  targetExecutablePath: '',
  attackKey: 'Ctrl',
  attackBands: [
    { minMs: 1000, maxMs: 10000, weight: 97 },
    { minMs: 10000, maxMs: 20000, weight: 1 },
    { minMs: 20000, maxMs: 40000, weight: 1 },
    { minMs: 40000, maxMs: 60000, weight: 1 },
  ],
  maxLateralMoveMs: 80,
  moveHoldMinMs: 30,
  moveHoldMaxMs: 50,
  moveGapMinMs: 30,
  moveGapMaxMs: 120,
  stabilizeMinMs: 80,
  stabilizeMaxMs: 150,
  restEnabled: true,
  restProbabilityPercent: 50,
  restMinMs: 2000,
  restMaxMs: 5000,
  attackTriggerMode: 'always',
  recognitionEnabled: false,
}

export interface RecognitionHudSnapshot {
  characterName: string | null
  level: number | null
  job: string | null
  hpCurrent: number | null
  hpMax: number | null
  mpCurrent: number | null
  mpMax: number | null
  hpPercent: number | null
  mpPercent: number | null
  expPercent: number | null
  confidence: number
}

export interface RecognitionSnapshotView {
  health: 'disabled' | 'starting' | 'running' | 'stale' | 'faulted' | 'targetLost'
  frameAgeMs: number
  faultCode: string | null
  hud: RecognitionHudSnapshot
}

export interface VisualStationaryStateView {
  schemaVersion: number
  sessionId: string
  cycleId: number
  status: string
  frameSequence: number
  bestScore: number
  visualOffsetPx: number | null
  guardWidthPx: number
  code: string
  updatedAtMonoMs: number
  identityKind: 'NameTemplate' | 'CharacterAppearance'
}
