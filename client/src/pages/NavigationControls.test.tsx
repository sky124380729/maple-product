import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, expect, it, vi } from 'vitest'
import { StationaryAttackPage } from './StationaryAttackPage'

let bridgeListener: ((event: MessageEvent) => void) | undefined

beforeEach(() => {
  globalThis.ResizeObserver = class {
    observe() { }
    unobserve() { }
    disconnect() { }
  }
  bridgeListener = undefined
  window.chrome = { webview: {
    postMessage: vi.fn(),
    addEventListener: (_type, listener) => { bridgeListener = listener },
    removeEventListener: () => undefined,
  } }
})

it('loads generic map catalog and starts selected navigation package', async () => {
  const user = userEvent.setup()
  render(<StationaryAttackPage />)
  await user.click(screen.getByRole('tab', { name: '地图管理' }))
  act(() => bridgeListener?.(new MessageEvent('message', { data: {
    type: 'navigation.catalog.loaded', directory: 'C:\\maps', entries: [
      { packagePath: 'C:\\maps\\swamp.mapzip', fileName: 'swamp.mapzip', mapName: '沼泽地3', canRun: true, warningCode: null },
      { packagePath: 'C:\\maps\\bad.mapzip', fileName: 'bad.mapzip', mapName: '错标', canRun: false, warningCode: 'MAP_NAME_MISMATCH' },
    ], errors: [],
  } })))
  await user.click(screen.getByRole('combobox', { name: '选择地图' }))
  await user.click(screen.getByText('swamp.mapzip · 沼泽地3'))
  await user.click(screen.getByRole('button', { name: /开始自动寻路/ }))

  expect(vi.mocked(window.chrome!.webview!.postMessage)).toHaveBeenCalledWith({
    command: 'startNavigation', packagePath: 'C:\\maps\\swamp.mapzip',
  })
})

it('directory button submits only folder selection intent', async () => {
  const user = userEvent.setup()
  render(<StationaryAttackPage />)
  await user.click(screen.getByRole('tab', { name: '地图管理' }))
  await user.click(screen.getByRole('button', { name: /选择地图目录/ }))
  expect(vi.mocked(window.chrome!.webview!.postMessage)).toHaveBeenCalledWith({ command: 'chooseMapDirectory' })
})

it('clears a selected package when a refreshed catalog no longer contains it', async () => {
  const user = userEvent.setup()
  render(<StationaryAttackPage />)
  await user.click(screen.getByRole('tab', { name: '地图管理' }))
  act(() => bridgeListener?.(new MessageEvent('message', { data: {
    type: 'navigation.catalog.loaded', directory: 'C:\\maps', entries: [
      { packagePath: 'C:\\maps\\swamp.mapzip', fileName: 'swamp.mapzip', mapName: '沼泽地3', canRun: true, warningCode: null },
    ], errors: [],
  } })))
  await user.click(screen.getByRole('combobox', { name: '选择地图' }))
  await user.click(screen.getByText('swamp.mapzip · 沼泽地3'))

  act(() => bridgeListener?.(new MessageEvent('message', { data: {
    type: 'navigation.catalog.loaded', directory: 'C:\\other', entries: [], errors: [],
  } })))

  expect(screen.getByRole('button', { name: /开始自动寻路/ })).toBeDisabled()
})
