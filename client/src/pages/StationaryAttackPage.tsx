import { useCallback, useEffect, useReducer, useState } from 'react'
import {
  Alert,
  Button,
  ConfigProvider,
  Form,
  InputNumber,
  Modal,
  Popconfirm,
  Segmented,
  Select,
  Space,
  Tag,
  Typography,
  theme,
} from 'antd'
import { ArrowLeftOutlined, ArrowRightOutlined, DeleteOutlined, EyeOutlined, PlayCircleOutlined, SafetyCertificateOutlined, SaveOutlined, StopOutlined, VideoCameraOutlined } from '@ant-design/icons'
import { AttackModeField } from '../components/AttackModeField'
import { AttackBandsEditor } from '../components/AttackBandsEditor'
import { AdvancedParametersCollapse } from '../components/AdvancedParametersCollapse'
import { SessionStatusPanel } from '../components/SessionStatusPanel'
import { RecognitionToggle } from '../components/RecognitionToggle'
import { NavigationControls } from '../components/NavigationControls'
import { NavigationStatus } from '../components/NavigationStatus'
import { postBridgeCommand, subscribeBridgeMessages } from '../bridge/bridge'
import { safeDefaults, type RecognitionSnapshotView, type StationaryAttackConfig, type VisualStationaryStateView } from '../bridge/types'
import type { NavigationCatalogEntry, NavigationStateView } from '../bridge/navigationTypes'
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
  const [recognition, setRecognition] = useState<RecognitionSnapshotView | null>(null)
  const [visualSafety, setVisualSafety] = useState<VisualStationaryStateView | null>(null)
  const [visualConfigStatus, setVisualConfigStatus] = useState<string>('notConfigured')
  const [mode, setMode] = useState<'stationary' | 'navigation'>('stationary')
  const [mapDirectory, setMapDirectory] = useState<string | null>(null)
  const [mapEntries, setMapEntries] = useState<NavigationCatalogEntry[]>([])
  const [selectedMap, setSelectedMap] = useState<string | null>(null)
  const [navigationRunning, setNavigationRunning] = useState(false)
  const [navigationState, setNavigationState] = useState<NavigationStateView | null>(null)
  const [navigationError, setNavigationError] = useState<string | null>(null)
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
        state?: typeof session.rhythm | NavigationStateView | VisualStationaryStateView
        reason?: string
        error?: string
        warning?: string | null
        config?: StationaryAttackConfig
        validationErrors?: Array<{ field: string; code: string }>
        record?: { reason?: string; stoppedAtUtc?: string }
        snapshot?: RecognitionSnapshotView
        directory?: string | null
        entries?: NavigationCatalogEntry[]
      }
      if (data.type === 'config.loaded' && data.config) {
        form.setFieldsValue({ ...data.config, attackBands: data.config.attackBands.map((band) => ({ ...band })) })
        setConfigWarning(data.warning ? configWarningMessage(data.warning) : null)
      }
      if (data.type === 'config.saved') {
        setSaving(false)
        setConfigError(null)
      }
      if (data.type === 'stationary.rhythm.updated' && data.state) dispatch({ type: 'rhythmUpdated', payload: data.state as NonNullable<typeof session.rhythm> })
      if (data.type === 'stationary.stopped') {
        dispatch({
          type: 'stopped',
          reason: data.reason ?? 'HOST_STOPPED',
          payload: data.state as NonNullable<typeof session.rhythm> | undefined,
        })
      }
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
      if (data.type === 'recognition.snapshot' && data.snapshot) setRecognition(data.snapshot)
      if (data.type === 'visualStationary.state.updated' && data.state) setVisualSafety(data.state as VisualStationaryStateView)
      if (data.type === 'visualStationary.config.updated') {
        setVisualConfigStatus((data as { status?: string }).status ?? 'ready')
        setConfigError(null)
      }
      if (data.type === 'visualStationary.config.error') {
        setConfigError(hostErrorMessage(data.error ?? '清空视觉配置失败，请重试'))
      }
      if (data.type === 'navigation.catalog.loaded') {
        setMapDirectory(data.directory ?? null)
        const entries = data.entries ?? []
        setMapEntries(entries)
        setSelectedMap((current) => current && entries.some((entry) => entry.packagePath === current && entry.canRun) ? current : null)
      }
      if (data.type === 'navigation.started') { setNavigationRunning(true); setNavigationError(null) }
      if (data.type === 'navigation.state.updated' && data.state) setNavigationState(data.state as NavigationStateView)
      if (data.type === 'navigation.stopped') setNavigationRunning(false)
      if (data.type === 'navigation.error') { setNavigationRunning(false); setNavigationError(data.error ?? 'NAVIGATION_FAILED') }
    })
    postBridgeCommand({ command: 'loadConfig' })
    postBridgeCommand({ command: 'loadNavigationCatalog' })
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

  const startNavigation = () => {
    if (!selectedMap) return
    setNavigationError(null)
    postBridgeCommand({ command: 'startNavigation', packagePath: selectedMap })
  }

  const stopNavigation = () => {
    postBridgeCommand({ command: 'stopNavigation' })
    setNavigationRunning(false)
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
          <div className="header-controls">
            <Segmented
              value={mode}
              disabled={running || navigationRunning}
              onChange={(value) => setMode(value as 'stationary' | 'navigation')}
              options={[{ label: '定点攻击', value: 'stationary' }, { label: '自动寻路', value: 'navigation' }]}
            />
            {mode === 'stationary' && <>
            <Form form={form} component={false}>
              <RecognitionToggle />
            </Form>
            <Space wrap>
              <Button icon={<VideoCameraOutlined />} onClick={() => postBridgeCommand({
                command: 'startMapRecording',
                recognitionEnabled: Boolean(form.getFieldValue('recognitionEnabled')),
              })}>录制地图</Button>
              <Button danger icon={<StopOutlined />} onClick={() => postBridgeCommand({ command: 'stopMapRecording' })}>停止录制地图</Button>
              <Button icon={<EyeOutlined />} onClick={() => postBridgeCommand({
                command: 'openPreview',
                recognitionEnabled: Boolean(form.getFieldValue('recognitionEnabled')),
              })}>打开实时预览</Button>
              <Button
                icon={<SafetyCertificateOutlined />}
                aria-label="配置平台安全区"
                title={visualConfigStatus === 'ready' ? '平台安全区已配置，人物模板继续复用' : '配置平台安全区'}
                onClick={() => postBridgeCommand({ command: 'openVisualStationarySetup' })}
              >配置平台安全区</Button>
              <Tag color={visualConfigStatus === 'ready' ? 'success' : visualConfigStatus === 'viewportMismatch' ? 'warning' : 'default'}>
                {visualConfigStatusLabel(visualConfigStatus)}
              </Tag>
              <Popconfirm
                title="清空视觉配置？"
                description="清空后将删除平台和已采集人物模板。"
                okText="确定"
                cancelText="取消"
                disabled={visualConfigStatus !== 'ready' || running || navigationRunning}
                onConfirm={() => postBridgeCommand({ command: 'clearVisualStationaryProfile' })}
              >
                <Button
                  danger
                  aria-label="清空视觉配置"
                  icon={<DeleteOutlined />}
                  disabled={visualConfigStatus !== 'ready' || running || navigationRunning}
                >清空视觉配置</Button>
              </Popconfirm>
              <Button icon={<SaveOutlined />} loading={saving} onClick={save}>保存配置</Button>
              {running ? (
                <Button danger type="primary" icon={<StopOutlined />} onClick={stop}>停止</Button>
              ) : (
                <Button type="primary" icon={<PlayCircleOutlined />} onClick={start}>开始</Button>
              )}
            </Space>
            </>}
          </div>
        </header>

        <Alert
          className="safety-notice"
          type="info"
          showIcon
          title="输入安全边界"
          description="开始后会自动定位并校验目标窗口。运行期间请保持游戏窗口为当前前台窗口；失焦会立即停止且不会自动恢复。"
        />

        {configWarning && <Alert className="config-message" type="warning" showIcon title="配置加载提示" description={configWarning} />}
        {configError && <Alert className="config-message" type="error" showIcon title="配置错误" description={configError} />}
        {abnormalTermination && <Alert className="config-message" type="warning" showIcon title="异常终止记录" description={abnormalTermination} />}

        <div className="workspace-grid">
          {mode === 'stationary' ? <>
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

          <SessionStatusPanel state={session} recognition={recognition} visualSafety={visualSafety} />
          </> : <>
            <NavigationControls
              directory={mapDirectory}
              entries={mapEntries}
              selected={selectedMap}
              running={navigationRunning}
              error={navigationError}
              onChooseDirectory={() => postBridgeCommand({ command: 'chooseMapDirectory' })}
              onSelect={setSelectedMap}
              onStart={startNavigation}
              onStop={stopNavigation}
            />
            <NavigationStatus state={navigationState} running={navigationRunning} />
          </>}
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

function visualConfigStatusLabel(status: string): string {
  if (status === 'ready') return '视觉配置：可用'
  if (status === 'viewportMismatch') return '视觉配置：画面尺寸已变化'
  if (status === 'invalid') return '视觉配置：无效'
  return '视觉配置：未配置'
}

function isConfigurationError(code: string | undefined): boolean {
  return code === 'CONFIG_INVALID' ||
    code === 'ATTACK_WEIGHT_TOTAL' || code === 'ATTACK_TRIGGER_DISABLED' || code === 'MOVE_BUDGET_TOO_SMALL'
}
