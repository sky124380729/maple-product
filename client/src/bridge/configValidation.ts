import type { StationaryAttackConfig } from './types'

export type ConfigFieldPath = Array<string | number>

export interface ConfigValidationError {
  name: ConfigFieldPath
  code: string
  message: string
}

export interface ConfigValidationResult {
  valid: boolean
  errors: ConfigValidationError[]
}

export function validateStationaryConfig(config: StationaryAttackConfig): ConfigValidationResult {
  const errors: ConfigValidationError[] = []
  const add = (name: ConfigFieldPath, code: string, message: string) => errors.push({ name, code, message })

  if (config.attackTriggerMode !== 'always') {
    add(['attackTriggerMode'], 'ATTACK_TRIGGER_DISABLED', '识别怪物后攻击尚未开放')
  }
  if (!Array.isArray(config.attackBands) || config.attackBands.length !== 4) {
    add(['attackBands'], 'ATTACK_BANDS_REQUIRED', '请保留四个攻击时长分段')
  }

  const bands = Array.isArray(config.attackBands) ? config.attackBands : []
  let weightTotal = 0
  bands.forEach((band, index) => {
    const min = numberOrNaN(band?.minMs)
    const max = numberOrNaN(band?.maxMs)
    const weight = numberOrNaN(band?.weight)
    if (!Number.isFinite(min) || min <= 0) add(['attackBands', index, 'minMs'], 'ATTACK_BAND_MIN_INVALID', '必须为正数')
    if (!Number.isFinite(max) || max <= 0) add(['attackBands', index, 'maxMs'], 'ATTACK_BAND_MAX_INVALID', '必须为正数')
    if (Number.isFinite(min) && Number.isFinite(max) && min > max) {
      add(['attackBands', index, 'minMs'], 'ATTACK_BAND_RANGE_INVALID', '最小值不能大于最大值')
      add(['attackBands', index, 'maxMs'], 'ATTACK_BAND_RANGE_INVALID', '最大值不能小于最小值')
    }
    if (Number.isFinite(max) && max > 60_000) add(['attackBands', index, 'maxMs'], 'ATTACK_DURATION_LIMIT', '不能超过 60000 ms')
    if (!Number.isFinite(weight) || weight <= 0) add(['attackBands', index, 'weight'], 'ATTACK_WEIGHT_INVALID', '必须为正数')
    if (Number.isFinite(weight)) weightTotal += weight
  })
  if (weightTotal !== 100) add(['attackBands'], 'ATTACK_WEIGHT_TOTAL', '四段权重总和必须为 100%')

  validatePositiveRange(config.moveHoldMinMs, config.moveHoldMaxMs, ['moveHoldMinMs', 'moveHoldMaxMs'], '移动按压')
  validatePositiveRange(config.moveGapMinMs, config.moveGapMaxMs, ['moveGapMinMs', 'moveGapMaxMs'], '无按键间隔')
  validatePositiveRange(config.stabilizeMinMs, config.stabilizeMaxMs, ['stabilizeMinMs', 'stabilizeMaxMs'], '稳定等待')
  validatePositiveRange(config.restMinMs, config.restMaxMs, ['restMinMs', 'restMaxMs'], '休息')

  const lateral = numberOrNaN(config.maxLateralMoveMs)
  const holdMin = numberOrNaN(config.moveHoldMinMs)
  if (!Number.isFinite(lateral) || lateral <= 0) add(['maxLateralMoveMs'], 'MAX_LATERAL_MOVE_INVALID', '必须为正数')
  if (Number.isFinite(lateral) && Number.isFinite(holdMin) && lateral < holdMin) {
    add(['maxLateralMoveMs'], 'MOVE_BUDGET_TOO_SMALL', '每侧最大累计偏移不能小于移动按压最小值')
    add(['moveHoldMinMs'], 'MOVE_BUDGET_TOO_SMALL', '不能大于每侧最大累计偏移')
  }
  const probability = numberOrNaN(config.restProbabilityPercent)
  if (!Number.isFinite(probability) || probability < 0 || probability > 100) {
    add(['restProbabilityPercent'], 'REST_PROBABILITY_INVALID', '请输入 0 到 100 之间的百分比')
  }

  return { valid: errors.length === 0, errors }

  function validatePositiveRange(minimumValue: number, maximumValue: number, fields: [string, string], label: string) {
    const minimum = numberOrNaN(minimumValue)
    const maximum = numberOrNaN(maximumValue)
    if (!Number.isFinite(minimum) || minimum <= 0) add([fields[0]], 'RANGE_INVALID', `${label}最小值必须为正数`)
    if (!Number.isFinite(maximum) || maximum <= 0) add([fields[1]], 'RANGE_INVALID', `${label}最大值必须为正数`)
    if (Number.isFinite(minimum) && Number.isFinite(maximum) && minimum > maximum) {
      add([fields[0]], 'RANGE_INVALID', `${label}最小值不能大于最大值`)
      add([fields[1]], 'RANGE_INVALID', `${label}最大值不能小于最小值`)
    }
  }
}

function numberOrNaN(value: unknown): number {
  return typeof value === 'number' ? value : Number.NaN
}

export function hostErrorMessage(code: string): string {
  if (code.startsWith('BROKER_START_FAILED:')) {
    return '输入服务启动失败，请确认已允许管理员授权后重试'
  }
  if (code.startsWith('FOCUS_LOST:')) {
    return '游戏窗口失去前台，已安全停止输入；请保持游戏窗口为当前前台窗口后重新开始'
  }
  const messages: Record<string, string> = {
    CONFIG_INVALID: '配置未通过校验，请检查标红字段',
    TARGET_NOT_FOUND: '未检测到正在运行的冒险岛怀旧服客户端',
    TARGET_MULTIPLE: '检测到异常的多个客户端窗口，请关闭多余窗口后重试',
    FOREGROUND_SWITCH_FAILED: '无法将游戏窗口切换到前台',
    FOREGROUND_VERIFY_FAILED: '游戏窗口未处于前台或仍处于最小化状态',
    FOCUS_LOST: '游戏窗口失去前台，已安全停止输入；请保持游戏窗口为当前前台窗口后重新开始',
    WINDOW_IDENTITY_CHANGED: '游戏窗口身份发生变化，已安全停止输入；请重新开始',
    BROKER_HEARTBEAT_IO: '输入服务心跳失败，已安全停止输入；请重新开始',
    ATTACK_WEIGHT_TOTAL: '四段攻击权重总和必须为 100%',
    ATTACK_TRIGGER_DISABLED: '识别怪物后攻击尚未开放',
    MOVE_BUDGET_TOO_SMALL: '每侧最大累计偏移不能小于移动按压最小值',
  }
  return messages[code] ?? code
}
