export interface SessionLogEntryView {
  timestampUtc: string
  sessionId: string
  cycleId: number
  phase: string
  event: string
  resultCode: string
  brokerSequence: number
  direction: string | null
  offsetAfterMs: number | null
}
