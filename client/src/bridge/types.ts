export type StationaryPhase =
  | 'idle'
  | 'attackHolding'
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
  attackTriggerMode: 'always' | 'monsterInRange'
}

export const safeDefaults: StationaryAttackConfig = {
  schemaVersion: 1,
  source: 'safe-default',
  updatedAtUtc: '1970-01-01T00:00:00.000Z',
  targetExecutablePath: '',
  attackKey: 'Ctrl',
  attackBands: [
    { minMs: 1000, maxMs: 10000, weight: 5 },
    { minMs: 10000, maxMs: 20000, weight: 10 },
    { minMs: 20000, maxMs: 40000, weight: 60 },
    { minMs: 40000, maxMs: 60000, weight: 25 },
  ],
  maxLateralMoveMs: 250,
  moveHoldMinMs: 80,
  moveHoldMaxMs: 125,
  moveGapMinMs: 30,
  moveGapMaxMs: 120,
  stabilizeMinMs: 80,
  stabilizeMaxMs: 150,
  restEnabled: true,
  restProbabilityPercent: 25,
  restMinMs: 2000,
  restMaxMs: 5000,
  attackTriggerMode: 'always',
}
