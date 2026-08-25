import type { StationaryAttackConfig } from './types'

type BridgeCommand =
  | { command: 'loadConfig' }
  | { command: 'saveConfig'; config: StationaryAttackConfig }
  | { command: 'startStationary'; config: StationaryAttackConfig; initialFacing: 'left' | 'right' }
  | { command: 'stopStationary' }
  | { command: 'openPreview'; recognitionEnabled: boolean }
  | { command: 'openVisualStationarySetup' }
  | { command: 'clearVisualStationaryProfile' }
  | { command: 'startMapRecording'; recognitionEnabled: boolean }
  | { command: 'stopMapRecording' }
  | { command: 'loadNavigationCatalog' }
  | { command: 'chooseMapDirectory' }
  | { command: 'startNavigation'; packagePath: string }
  | { command: 'stopNavigation' }

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: BridgeCommand): void
        addEventListener(type: 'message', listener: (event: MessageEvent) => void): void
        removeEventListener(type: 'message', listener: (event: MessageEvent) => void): void
      }
    }
  }
}

export function postBridgeCommand(message: BridgeCommand) {
  window.chrome?.webview?.postMessage(message)
}

export function subscribeBridgeMessages(listener: (message: unknown) => void) {
  const handler = (event: MessageEvent) => listener(event.data)
  window.chrome?.webview?.addEventListener('message', handler)
  return () => window.chrome?.webview?.removeEventListener('message', handler)
}
