import { describe, expect, it } from 'vitest'
import { hostErrorMessage, validateStationaryConfig } from './configValidation'
import { safeDefaults } from './types'

const validConfig = { ...safeDefaults }

describe('validateStationaryConfig', () => {
  it('accepts a complete configuration with four weighted attack bands', () => {
    expect(validateStationaryConfig(validConfig)).toEqual({ valid: true, errors: [] })
  })

  it('does not require a target executable path because the Host discovers the running client', () => {
    expect(validateStationaryConfig({ ...validConfig, targetExecutablePath: '' })).toEqual({ valid: true, errors: [] })
  })

  it('maps attack band range, positive value and total weight errors to fields', () => {
    const result = validateStationaryConfig({
      ...validConfig,
      attackBands: [
        { minMs: 0, maxMs: 60_001, weight: 1 },
        { minMs: 200, maxMs: 100, weight: 1 },
        { minMs: 1, maxMs: 2, weight: 1 },
        { minMs: 1, maxMs: 2, weight: 1 },
      ],
    })

    expect(result.valid).toBe(false)
    expect(result.errors).toEqual(expect.arrayContaining([
      expect.objectContaining({ name: ['attackBands', 0, 'minMs'] }),
      expect.objectContaining({ name: ['attackBands', 0, 'maxMs'] }),
      expect.objectContaining({ name: ['attackBands', 1, 'minMs'] }),
      expect.objectContaining({ name: ['attackBands'] }),
    ]))
  })

  it('rejects disabled mode and invalid movement relationships', () => {
    const result = validateStationaryConfig({
      ...validConfig,
      attackTriggerMode: 'monsterInRange' as never,
      maxLateralMoveMs: 10,
      moveHoldMinMs: 20,
      moveHoldMaxMs: 10,
      moveGapMinMs: 30,
      moveGapMaxMs: 20,
    })

    expect(result.valid).toBe(false)
    expect(result.errors).toEqual(expect.arrayContaining([
      expect.objectContaining({ name: ['attackTriggerMode'] }),
      expect.objectContaining({ name: ['maxLateralMoveMs'] }),
      expect.objectContaining({ name: ['moveHoldMinMs'] }),
      expect.objectContaining({ name: ['moveGapMinMs'] }),
    ]))
  })

  it('accepts the independent visual-safe continuous mode', () => {
    expect(validateStationaryConfig({
      ...validConfig,
      attackTriggerMode: 'visualSafeContinuous' as never,
    })).toEqual({ valid: true, errors: [] })
  })

  it('rejects a movement hold above the Broker limit', () => {
    const result = validateStationaryConfig({
      ...validConfig,
      moveHoldMaxMs: 5_001,
    })

    expect(result.errors).toContainEqual(expect.objectContaining({
      name: ['moveHoldMaxMs'],
      code: 'MOVE_HOLD_LIMIT',
    }))
  })

  it('rejects a lateral budget smaller than the minimum hold plus release margin', () => {
    const result = validateStationaryConfig({
      ...validConfig,
      moveHoldMinMs: 30,
      maxLateralMoveMs: 49,
    })

    expect(result.errors).toContainEqual(expect.objectContaining({
      name: ['maxLateralMoveMs'],
      code: 'MOVE_BUDGET_TOO_SMALL',
    }))
  })
})

describe('hostErrorMessage', () => {
  it('maps broker startup exception details to an operator-facing message', () => {
    expect(hostErrorMessage('BROKER_START_FAILED:UnauthorizedAccessException'))
      .toBe('输入服务启动失败，请确认已允许管理员授权后重试')
  })

  it('maps focus diagnostics with a foreground handle to the focus-loss message', () => {
    expect(hostErrorMessage('FOCUS_LOST:foreground=1182182'))
      .toBe('游戏窗口失去前台，已安全停止输入；请保持游戏窗口为当前前台窗口后重新开始')
  })

  it('maps stationary movement safety validation errors', () => {
    expect(hostErrorMessage('MOVE_HOLD_LIMIT')).toBe('移动按压最大值不能超过 5000 ms')
    expect(hostErrorMessage('MOVE_BUDGET_TOO_SMALL')).toBe('每侧最大累计偏移至少要比移动按压最小值多 20 ms')
  })

  it('maps visual appearance startup diagnostics without referring to character names', () => {
    expect(hostErrorMessage('VISUAL_SELF_NOT_TRUSTED'))
      .toBe('未能稳定锁定自己的人物外观，请检查人物框选或遮挡后重试')
    expect(hostErrorMessage('VISUAL_NAME_SCORE_LOW'))
      .toBe('人物外观暂时低于锁定阈值，已保留原视觉配置并继续尝试识别')
    expect(hostErrorMessage('VISUAL_NAME_AMBIGUOUS'))
      .toBe('人物外观候选位置不唯一，请等待遮挡减少后重试')
  })
})
