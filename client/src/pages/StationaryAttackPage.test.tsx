import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { safeDefaults } from '../bridge/types'
import { StationaryAttackPage } from './StationaryAttackPage'

describe('StationaryAttackPage', () => {
  let bridgeListener: ((event: MessageEvent) => void) | undefined

  beforeEach(() => {
    bridgeListener = undefined
    window.chrome = {
      webview: {
        postMessage: vi.fn(),
        addEventListener: (_type, listener) => { bridgeListener = listener },
        removeEventListener: () => undefined,
      },
    }
  })

  it('shows the unavailable recognition mode without allowing selection', () => {
    render(<StationaryAttackPage />)

    expect(screen.getByText('识别怪物后攻击')).toBeVisible()
    expect(screen.getByText('后续版本开放')).toBeVisible()
    expect(screen.getByRole('radio', { name: /识别怪物后攻击/ })).toBeDisabled()
  })

  it('keeps advanced debugging parameters collapsed by default', () => {
    render(<StationaryAttackPage />)

    expect(screen.getByText('高级调试参数')).toBeVisible()
    expect(screen.queryByLabelText('每侧最大累计偏移')).not.toBeInTheDocument()
  })

  it('exposes all four attack duration bands as editable fields', () => {
    render(<StationaryAttackPage />)

    expect(screen.getByText('攻击时长分段')).toBeVisible()
    expect(screen.getByLabelText('分段 1 最小值')).toHaveValue('1000')
    expect(screen.getByLabelText('分段 2 最大值')).toHaveValue('20000')
    expect(screen.getByLabelText('分段 3 权重')).toHaveValue('1')
    expect(screen.getByLabelText('分段 4 最大值')).toHaveValue('60000')
  })

  it('hydrates the saved config and shows its startup warning', () => {
    render(<StationaryAttackPage />)
    const loaded = {
      ...safeDefaults,
      targetExecutablePath: String.raw`C:\Games\SavedMaple.exe`,
      attackKey: 'Space',
    }

    act(() => bridgeListener?.(new MessageEvent('message', {
      data: { type: 'config.loaded', config: loaded, warning: 'CONFIG_FILE_CORRUPT' },
    })))

    expect(screen.getByText('保存的配置文件已损坏，已恢复安全默认值')).toBeVisible()
    expect(screen.queryByLabelText('目标游戏程序')).not.toBeInTheDocument()
  })

  it('does not ask the operator to choose a game executable', () => {
    render(<StationaryAttackPage />)

    expect(screen.queryByText('选择')).not.toBeInTheDocument()
    expect(screen.getByText('自动检测正在运行的冒险岛怀旧服客户端')).toBeVisible()
  })

  it('maps Host error codes to a visible operator message', () => {
    render(<StationaryAttackPage />)

    act(() => bridgeListener?.(new MessageEvent('message', {
      data: { type: 'stationary.error', error: 'ATTACK_WEIGHT_TOTAL' },
    })))

    expect(screen.getByText('四段攻击权重总和必须为 100%')).toBeVisible()
  })

  it('shows the previous abnormal termination reported at startup', () => {
    render(<StationaryAttackPage />)

    act(() => bridgeListener?.(new MessageEvent('message', {
      data: {
        type: 'stationary.abnormalTermination',
        record: { reason: 'SESSION_IN_PROGRESS', stoppedAtUtc: '2026-08-19T06:00:00Z' },
      },
    })))

    expect(screen.getByText('上次运行未正常结束')).toBeVisible()
  })

  it('shows the Host countdown after a rhythm bridge message', () => {
    const { container } = render(<StationaryAttackPage />)

    act(() => bridgeListener?.(new MessageEvent('message', {
      data: {
        type: 'stationary.rhythm.updated',
        state: {
          schemaVersion: 1,
          sessionId: 'session-live',
          cycleId: 7,
          phase: 'attackHolding',
          sampledDurationMs: 27_438,
          phaseStartedMonoMs: 1_000,
          phaseDeadlineMonoMs: 28_438,
          remainingMs: 27_438,
          updatedAtMonoMs: 1_000,
          earlyReleaseReason: null,
        },
      },
    })))

    expect(screen.getByText('运行中')).toBeVisible()
    expect(screen.getAllByText('持续攻击')).toHaveLength(2)
    expect(container.querySelector('.ant-statistic-content-value')).toHaveTextContent('27.438')
  })

  it('asks for the current facing before starting and cancel sends no start intent', async () => {
    const user = userEvent.setup()
    render(<StationaryAttackPage />)

    await user.click(screen.getByRole('button', { name: /开始/ }))
    expect(await screen.findByRole('dialog', { name: '人物当前朝向' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /取\s*消/ }))

    const postMessage = vi.mocked(window.chrome!.webview!.postMessage)
    expect(postMessage).not.toHaveBeenCalledWith(expect.objectContaining({ command: 'startStationary' }))
  })

  it.each([
    ['人物当前朝向左', 'left'],
    ['人物当前朝向右', 'right'],
  ] as const)('starts with the selected facing from %s', async (buttonName, initialFacing) => {
    const user = userEvent.setup()
    render(<StationaryAttackPage />)

    await user.click(screen.getByRole('button', { name: /开始/ }))
    await user.click(await screen.findByRole('button', { name: buttonName }))

    const postMessage = vi.mocked(window.chrome!.webview!.postMessage)
    expect(postMessage).toHaveBeenCalledWith(expect.objectContaining({
      command: 'startStationary',
      initialFacing,
    }))
  })
})
