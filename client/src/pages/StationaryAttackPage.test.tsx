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

  it('allows the lateral offset budget below the safe default', async () => {
    const user = userEvent.setup()
    render(<StationaryAttackPage />)

    await user.click(screen.getByText('高级调试参数'))

    expect(screen.getByLabelText('每侧最大累计偏移')).toHaveAttribute('aria-valuemin', '1')
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

  it('shows character and resource recognition from the Host snapshot', () => {
    render(<StationaryAttackPage />)

    act(() => bridgeListener?.(new MessageEvent('message', {
      data: {
        type: 'recognition.snapshot',
        snapshot: {
          health: 'running', frameAgeMs: 42, faultCode: null,
          hud: {
            characterName: 'Pink丶Bin', level: 43, job: '猎人',
            hpCurrent: 1586, hpMax: 1586, hpPercent: 1,
            mpCurrent: 914, mpMax: 991, mpPercent: 0.922,
            expPercent: 0.23, confidence: 0.88,
          },
        },
      },
    })))

    expect(screen.getByText('Pink丶Bin')).toBeVisible()
    expect(screen.getByText('Lv.43')).toBeVisible()
    expect(screen.getByText('猎人')).toBeVisible()
    expect(screen.getByText('1586 / 1586')).toBeVisible()
    expect(screen.getByText('914 / 991')).toBeVisible()
    expect(screen.getByText('100%')).toBeVisible()
    expect(screen.getByText('92%')).toBeVisible()
    expect(screen.getByText('0.23%')).toBeVisible()
    expect(screen.getByRole('progressbar', { name: /EXP/ })).toHaveClass('recognition-exp-progress')
    expect(screen.getByText('42 ms')).toBeVisible()
  })

  it('marks a frozen recognition snapshot stale as its local frame age grows', () => {
    vi.useFakeTimers()
    try {
      render(<StationaryAttackPage />)

      act(() => bridgeListener?.(new MessageEvent('message', {
        data: {
          type: 'recognition.snapshot',
          snapshot: {
            health: 'running', frameAgeMs: 20, faultCode: null,
            hud: {
              characterName: null, level: null, job: null,
              hpCurrent: null, hpMax: null, hpPercent: null,
              mpCurrent: null, mpMax: null, mpPercent: null,
              expPercent: null, confidence: 0,
            },
          },
        },
      })))

      act(() => vi.advanceTimersByTime(750))

      expect(screen.getByText('结果过期')).toBeVisible()
      expect(screen.getByText(/7\d\d ms/)).toBeVisible()
    } finally {
      vi.useRealTimers()
    }
  })

  it('opens preview with the current recognition switch without requiring save', async () => {
    const user = userEvent.setup()
    render(<StationaryAttackPage />)

    await user.click(screen.getByRole('switch'))
    await user.click(screen.getByRole('button', { name: /打开实时预览/ }))

    expect(vi.mocked(window.chrome!.webview!.postMessage)).toHaveBeenCalledWith({
      command: 'openPreview', recognitionEnabled: true,
    })
  })

  it('starts map recording from the main window', async () => {
    const user = userEvent.setup()
    render(<StationaryAttackPage />)

    await user.click(screen.getByRole('button', { name: /录制地图/ }))

    expect(vi.mocked(window.chrome!.webview!.postMessage)).toHaveBeenCalledWith({
      command: 'startMapRecording', recognitionEnabled: false,
    })
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

  it('makes the movement transition explicit after an attack phase', () => {
    render(<StationaryAttackPage />)

    act(() => bridgeListener?.(new MessageEvent('message', {
      data: {
        type: 'stationary.rhythm.updated',
        state: {
          schemaVersion: 1,
          sessionId: 'session-live',
          cycleId: 7,
          phase: 'moveFirst',
          sampledDurationMs: 27_438,
          phaseStartedMonoMs: 28_438,
          phaseDeadlineMonoMs: 28_478,
          remainingMs: 40,
          updatedAtMonoMs: 28_438,
          earlyReleaseReason: null,
        },
      },
    })))

    expect(screen.getByText('下轮攻击前剩余')).toBeVisible()
    expect(screen.getByText('完成左右移动和稳定等待后才会进入下一轮攻击')).toBeVisible()
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
