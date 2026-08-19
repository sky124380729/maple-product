import { useCallback, useEffect, useReducer, useState } from 'react'
import {
  Alert,
  Button,
  ConfigProvider,
  Form,
  InputNumber,
  Modal,
  Select,
  Space,
  Typography,
  theme,
} from 'antd'
import { ArrowLeftOutlined, ArrowRightOutlined, PlayCircleOutlined, SaveOutlined, StopOutlined } from '@ant-design/icons'
import { AttackModeField } from '../components/AttackModeField'
import { AttackBandsEditor } from '../components/AttackBandsEditor'
import { AdvancedParametersCollapse } from '../components/AdvancedParametersCollapse'
import { SessionStatusPanel } from '../components/SessionStatusPanel'
import { postBridgeCommand, subscribeBridgeMessages } from '../bridge/bridge'
import { safeDefaults, type StationaryAttackConfig } from '../bridge/types'
import { hostErrorMessage, validateStationaryConfig, type ConfigValidationError } from '../bridge/configValidation'
import { initialSessionState, sessionReducer } from '../state/sessionReducer'
import '../styles/app.css'

const attackKeys = ['Ctrl', 'Shift', 'Space', 'A', 'S', 'D', 'F', 'Z', 'X', 'C', 'V']

export function StationaryAttackPage() {
  const [form] = Form.useForm<StationaryAttackConfig>()
  const [session, dispatch] = useReducer(sessionReducer, initialSessionState)
  const [saving, setSaving] = useState(false)
  const [configWarning, setConfigWarning] = useState<string | null>(null)
  const [configError, setConfigError] = useState<string | null>(null)
  const [abnormalTermination, setAbnormalTermination] = useState<string | null>(null)
  const [pendingStartConfig, setPendingStartConfig] = useState<StationaryAttackConfig | null>(null)
  const running = session.status === 'running' || session.status === 'locating' || session.status === 'arming'

  const applyHostValidationErrors = useCallback((errors: Array<{ field: string; code: string }>) => {
    if (errors.length === 0) return
    form.setFields(errors.map(({ field, code }) => ({
      name: hostFieldPath(field),
      errors: [hostErrorMessage(code)],
    })) as Parameters<typeof form.setFields>[0])
  }, [form])

  useEffect(() => {
    const unsubscribe = subscribeBridgeMessages((message) => {
      if (!message || typeof message !== 'object') return
      const data = message as {
        type?: string
        state?: typeof session.rhythm
        reason?: string
        error?: string
        warning?: string | null
        config?: StationaryAttackConfig
        validationErrors?: Array<{ field: string; code: string }>
        record?: { reason?: string; stoppedAtUtc?: string }
      }
      if (data.type === 'config.loaded' && data.config) {
        form.setFieldsValue({ ...data.config, attackBands: data.config.attackBands.map((band) => ({ ...band })) })
        setConfigWarning(data.warning ? configWarningMessage(data.warning) : null)
      }
      if (data.type === 'config.saved') {
        setSaving(false)
        setConfigError(null)
      }
      if (data.type === 'stationary.rhythm.updated' && data.state) dispatch({ type: 'rhythmUpdated', payload: data.state })
      if (data.type === 'stationary.stopped') dispatch({ type: 'stopped', reason: data.reason ?? 'HOST_STOPPED' })
      if (data.type === 'stationary.error') {
        const messageText = hostErrorMessage(data.error ?? '运行异常')
        setSaving(false)
        setConfigError(messageText)
        applyHostValidationErrors(data.validationErrors ?? [])
        if (!isConfigurationError(data.error)) dispatch({ type: 'failed', error: messageText })
      }
      if (data.type === 'stationary.abnormalTermination' && data.record?.reason) {
        setAbnormalTermination(data.record.reason === 'SESSION_IN_PROGRESS'
          ? '上次运行未正常结束'
          : `上次运行异常停止：${data.record.reason}`)
      }
    })
    postBridgeCommand({ command: 'loadConfig' })
    return unsubscribe
  }, [applyHostValidationErrors, form])

  const validatedConfig = async (): Promise<StationaryAttackConfig | null> => {
    try {
      await form.validateFields()
    } catch {
      setConfigError('配置未通过校验，请检查标红字段')
      return null
    }
    const config = { ...safeDefaults, ...form.getFieldsValue(true) } as StationaryAttackConfig
    const result = validateStationaryConfig(config)
    if (!result.valid) {
      form.setFields(result.errors.map(toFormFieldError) as Parameters<typeof form.setFields>[0])
      setConfigError('配置未通过校验，请检查标红字段')
      return null
    }
    setConfigError(null)
    return config
  }

  const save = async () => {
    const config = await validatedConfig()
    if (!config) return
    setSaving(true)
    postBridgeCommand({ command: 'saveConfig', config })
  }

  const start = async () => {
    const config = await validatedConfig()
    if (!config) return
    setPendingStartConfig(config)
  }

  const startWithFacing = (initialFacing: 'left' | 'right') => {
    const config = pendingStartConfig
    if (!config) return
    setPendingStartConfig(null)
    dispatch({ type: 'starting' })
    postBridgeCommand({ command: 'startStationary', config, initialFacing })
  }

  const stop = () => {
    postBridgeCommand({ command: 'stopStationary' })
    dispatch({ type: 'stopped', reason: '用户已停止' })
  }

  return (
    <ConfigProvider
      theme={{
        algorithm: theme.defaultAlgorithm,
        token: {
          colorPrimary: '#167e72',
          colorInfo: '#167e72',
          borderRadius: 8,
          borderRadiusLG: 12,
          fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif',
        },
      }}
    >
      <main className="app-shell">
        <header className="app-header">
          <div>
            <Typography.Title level={1}>Maple Product</Typography.Title>
            <Typography.Text type="secondary">Windows x64 定点持续攻击配置</Typography.Text>
          </div>
          <Space wrap>
            <Button icon={<SaveOutlined />} loading={saving} onClick={save}>保存配置</Button>
            {running ? (
              <Button danger type="primary" icon={<StopOutlined />} onClick={stop}>停止</Button>
            ) : (
              <Button type="primary" icon={<PlayCircleOutlined />} onClick={start}>开始</Button>
            )}
          </Space>
        </header>

        <Alert
          className="safety-notice"
          type="info"
          showIcon
          title="输入安全边界"
          description="开始后会自动定位并校验目标窗口。失焦会立即停止且不会自动恢复。"
        />

        {configWarning && <Alert className="config-message" type="warning" showIcon title="配置加载提示" description={configWarning} />}
        {configError && <Alert className="config-message" type="error" showIcon title="配置错误" description={configError} />}
        {abnormalTermination && <Alert className="config-message" type="warning" showIcon title="异常终止记录" description={abnormalTermination} />}

        <div className="workspace-grid">
          <Form
            form={form}
            layout="vertical"
            initialValues={safeDefaults}
            requiredMark="optional"
            className="configuration-panel"
          >
            <section className="config-section">
              <div className="section-heading">
                <div>
                  <Typography.Title level={2}>基础配置</Typography.Title>
                  <Typography.Text type="secondary">设置攻击模式和输入按键</Typography.Text>
                </div>
              </div>

              <Alert
                type="info"
                showIcon
                title="自动检测客户端"
                description="自动检测正在运行的冒险岛怀旧服客户端"
              />

              <AttackModeField />

              <div className="basic-grid">
                <Form.Item label="攻击键" name="attackKey" rules={[{ required: true }]}>
                  <Select options={attackKeys.map((value) => ({ value, label: value }))} />
                </Form.Item>
                <Form.Item label="攻击硬上限">
                  <InputNumber value={60000} disabled suffix="ms" />
                </Form.Item>
              </div>
            </section>

            <AttackBandsEditor />

            <AdvancedParametersCollapse />

            <Space className="form-footer" wrap>
              <Button onClick={() => { form.setFieldsValue(safeDefaults); form.setFields([]); setConfigError(null) }}>恢复安全默认值</Button>
              <Typography.Text type="secondary">运行中保存的节奏参数从下一完整周期生效</Typography.Text>
            </Space>
          </Form>

          <SessionStatusPanel state={session} />
        </div>
      </main>
      <Modal
        title="人物当前朝向"
        open={pendingStartConfig !== null}
        onCancel={() => setPendingStartConfig(null)}
        footer={(_, { CancelBtn }) => <CancelBtn />}
        okButtonProps={{ style: { display: 'none' } }}
        cancelText="取消"
        centered
      >
        <Typography.Paragraph type="secondary">
          请选择人物此刻面向的方向。本次会话每轮移动结束后会恢复到该朝向。
        </Typography.Paragraph>
        <div className="facing-options">
          <Button
            className="facing-option"
            aria-label="人物当前朝向左"
            icon={<ArrowLeftOutlined />}
            onClick={() => startWithFacing('left')}
          />
          <Button
            className="facing-option"
            aria-label="人物当前朝向右"
            icon={<ArrowRightOutlined />}
            onClick={() => startWithFacing('right')}
          />
        </div>
      </Modal>
    </ConfigProvider>
  )
}

function toFormFieldError(error: ConfigValidationError) {
  return { name: error.name, errors: [error.message] }
}

function hostFieldPath(field: string): Array<string | number> {
  const paths: Record<string, Array<string | number>> = {
    moveHold: ['moveHoldMinMs'],
    moveGap: ['moveGapMinMs'],
    stabilize: ['stabilizeMinMs'],
    rest: ['restMinMs'],
  }
  return paths[field] ?? [field]
}

function configWarningMessage(code: string): string {
  const warnings: Record<string, string> = {
    CONFIG_FILE_CORRUPT: '保存的配置文件已损坏，已恢复安全默认值',
    CONFIG_FILE_INVALID: '保存的配置未通过校验，已恢复安全默认值',
  }
  return warnings[code] ?? code
}

function isConfigurationError(code: string | undefined): boolean {
  return code === 'CONFIG_INVALID' ||
    code === 'ATTACK_WEIGHT_TOTAL' || code === 'ATTACK_TRIGGER_DISABLED' || code === 'MOVE_BUDGET_TOO_SMALL'
}
