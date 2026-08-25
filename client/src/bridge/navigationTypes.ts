export interface NavigationCatalogEntry {
  packagePath: string
  fileName: string
  mapName: string
  canRun: boolean
  warningCode: string | null
}

export interface NavigationStateView {
  mapName: string
  phase: string
  currentPlatformId: number | null
  targetPlatformId: number | null
  route: number[]
  action: string | null
  faultCode: string | null
  localizationConfidence: number | null
  selfX: number | null
  selfY: number | null
}
