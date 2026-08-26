import { useCallback, useEffect, useReducer, useState } from 'react'
import {
  Button,
  ConfigProvider,
  Form,
  InputNumber,
  message,
  Modal,
  Popconfirm,
  Select,
  Space,
  Tabs,
  Tag,
  Typography,
  theme,
} from 'antd'
import {
  ArrowLeftOutlined,
  ArrowRightOutlined,
  DeleteOutlined,
  EyeOutlined,
  FileTextOutlined,
  PlayCircleOutlined,
  SafetyCertificateOutlined,
  SaveOutlined,
  StopOutlined,
} from '@ant-design/icons'
import { AttackModeField } from '../components/AttackModeField'
import { AttackBandsEditor } from '../components/AttackBandsEditor'
import { AdvancedParametersCollapse } from '../components/AdvancedParametersCollapse'
import { SessionStatusPanel, type RuntimeNotice } from '../components/SessionStatusPanel'
import { RecognitionToggle } from '../components/RecognitionToggle'
import { CharacterStatusPanel } from '../components/CharacterStatusPanel'
import { MapManagementPanel } from '../components/MapManagementPanel'
import { RuntimeLogModal } from '../components/RuntimeLogModal'
import { postBridgeCommand, subscribeBridgeMessages } from '../bridge/bridge'
import { safeDefaults, type RecognitionSnapshotView, type StationaryAttackConfig, type VisualStationaryStateView } from '../bridge/types'
import type { NavigationCatalogEntry, NavigationStateView } from '../bridge/navigationTypes'
import type { SessionLogEntryView } from '../bridge/sessionLogTypes'
import { hostErrorMessage, validateStationaryConfig, type ConfigValidationError } from '../bridge/configValidation'
import { initialSessionState, sessionReducer } from '../state/sessionReducer'
import '../styles/app.css'

const attackKeys = ['Ctrl', 'Shift', 'Space', 'A', 'S', 'D', 'F', 'Z', 'X', 'C', 'V']

export function StationaryAttackPage() {
  const [form] = Form.useForm<StationaryAttackConfig>()
  const [messageApi, messageContext] = message.useMessage()
  const [session, dispatch] = useReducer(sessionReducer, initialSessionState)
  const [saving, setSaving] = useState(false)
  const [configWarning, setConfigWarning] = useState<string | null>(null)
  const [configError, setConfigError] = useState<string | null>(null)
  const [abnormalTermination, setAbnormalTermination] = useState<string | null>(null)
  const [pendingStartConfig, setPendingStartConfig] = useState<StationaryAttackConfig | null>(null)
  const [recognition, setRecognition] = useState<RecognitionSnapshotView | null>(null)
  const [visualSafety, setVisualSafety] = useState<VisualStationaryStateView | null>(null)
  const [visualConfigStatus, setVisualConfigStatus] = useState<string>('notConfigured')
  const [activeTab, setActiveTab] = useState<'attack' | 'maps'>('attack')
  const [mapDirectory, setMapDirectory] = useState<string | null>(null)
  const [mapEntries, setMapEntries] = useState<NavigationCatalogEntry[]>([])
  const [selectedMap, setSelectedMap] = useState<string | null>(null)
  const [navigationRunning, setNavigationRunning] = useState(false)
  const [navigationState, setNavigationState] = useState<NavigationStateView | null>(null)
  const [navigationError, setNavigationError] = useState<string | null>(null)
  const [logOpen, setLogOpen] = useState(false)
  const [logLoading, setLogLoading] = useState(false)
  const [logError, setLogError] = useState<string | null>(null)
  const [logEntries, setLogEntries] = useState<SessionLogEntryView[]>([])
  const running = session.status === 'running' || session.status === 'locating' || session.status === 'arming'

  const applyHostValidationErrors = useCallback((errors: Array<{ field: string; code: string }>) => {
    if (errors.length === 0) return
    form.setFields(errors.map(({ field, code }) => ({
      name: hostFieldPath(field),
      errors: [hostErrorMessage(code)],
    })) as Parameters<typeof form.setFields>[0])
  }, [form])

  useEffect(() => {
    const unsubscribe = subscribeBridgeMessages((bridgeMessage) => {
      if (!bridgeMessage || typeof bridgeMessage !== 'object') return
      const data = bridgeMessage as {
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
        entries?: NavigationCatalogEntry[] | SessionLogEntryView[]
      }
      if (data.type === 'config.loaded' && data.config) {
        form.setFieldsValue({ ...data.config, attackBands: data.config.attackBands.map((band) => ({ ...band })) })
        setConfigWarning(data.warning ? configWarningMessage(data.warning) : null)
      }
      if (data.type === 'config.saved') {
        setSaving(false)
        setConfigError(null)
        void messageApi.success('配置已保存')
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
        applyHostValidationErrors(data.validationErrors ?? [])
        if (isConfigurationError(data.error)) {
          setConfigError(messageText)
        } else {
          setConfigError(null)
          dispatch({ type: 'failed', error: messageText })
        }
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
        const entries = (data.entries ?? []) as NavigationCatalogEntry[]
        setMapEntries(entries)
        setSelectedMap((current) => current && entries.some((entry) => entry.packagePath === current && entry.canRun) ? current : null)
      }
      if (data.type === 'navigation.started') { setNavigationRunning(true); setNavigationError(null) }
      if (data.type === 'navigation.state.updated' && data.state) setNavigationState(data.state as NavigationStateView)
      if (data.type === 'navigation.stopped') setNavigationRunning(false)
      if (data.type === 'navigation.error') { setNavigationRunning(false); setNavigationError(data.error ?? 'NAVIGATION_FAILED') }
      if (data.type === 'session.log.loaded') {
        setLogEntries((data.entries ?? []) as SessionLogEntryView[])
        setLogLoading(false)
        setLogError(null)
      }
      if (data.type === 'session.log.error') {
        setLogLoading(false)
        setLogError('运行日志读取失败')
      }
    })
    postBridgeCommand({ command: 'loadConfig' })
    postBridgeCommand({ command: 'loadNavigationCatalog' })
    return unsubscribe
  }, [applyHostValidationErrors, form, messageApi])

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

  const openPreview = () => postBridgeCommand({
    command: 'openPreview',
    recognitionEnabled: Boolean(form.getFieldValue('recognitionEnabled')),
  })

  const loadLogs = () => {
    setLogLoading(true)
    setLogError(null)
    postBridgeCommand({ command: 'loadSessionLog' })
  }

  const openLogs = () => {
    setLogOpen(true)
    loadLogs()
  }

  const runtimeNotices: RuntimeNotice[] = [
    ...(configError ? [{ level: 'error' as const, title: '配置错误', message: configError }] : []),
    ...(configWarning ? [{ level: 'warning' as const, title: '配置提示', message: configWarning }] : []),
    ...(abnormalTermination ? [{ level: 'warning' as const, title: '异常终止', message: abnormalTermination }] : []),
  ]
  const relativeOffsetMs = session.status === 'locating' || session.status === 'arming'
    ? 0
    : session.rhythm?.relativeOffsetMs ?? null

  return (
    <ConfigProvider
      theme={{
        algorithm: theme.defaultAlgorithm,
        token: {
          colorPrimary: '#14786f',
          colorInfo: '#14786f',
          colorText: '#18211f',
          colorTextSecondary: '#68726f',
          colorBgLayout: '#f1f4f3',
          colorBorder: '#d7dddb',
          borderRadius: 6,
          borderRadiusLG: 8,
          controlHeight: 34,
          fontFamily: '"Segoe UI Variable", "Segoe UI", system-ui, sans-serif',
        },
      }}
    >
      {messageContext}
      <main className="app-shell">
        <header className="app-header">
          <div className="brand-lockup">
            <div className="brand-mark" aria-hidden="true">M</div>
            <div>
              <Typography.Title level={1}>Maple Product</Typography.Title>
              <Typography.Text type="secondary">Windows x64 控制台</Typography.Text>
            </div>
          </div>
          <Tabs
            className="primary-tabs"
            activeKey={activeTab}
            onChange={(key) => setActiveTab(key as 'attack' | 'maps')}
            items={[
              { key: 'attack', label: '定点攻击', disabled: navigationRunning },
              { key: 'maps', label: '地图管理', disabled: running },
            ]}
          />
          <Space className="header-actions" size="small">
            <Button aria-label="运行日志" title="运行日志" icon={<FileTextOutlined />} onClick={openLogs}>日志</Button>
            <Button aria-label="打开实时预览" title="打开实时预览" icon={<EyeOutlined />} onClick={openPreview}>预览</Button>
            {activeTab === 'attack' && (
              <>
                <Button title="保存配置" icon={<SaveOutlined />} loading={saving} onClick={save}>保存配置</Button>
                {running
                  ? <Button title="停止" danger type="primary" icon={<StopOutlined />} onClick={stop}>停止</Button>
                  : <Button title="开始" type="primary" icon={<PlayCircleOutlined />} onClick={start}>开始</Button>}
              </>
            )}
          </Space>
        </header>

        {activeTab === 'attack' ? (
          <div className="attack-workspace">
            <div className="status-grid">
              <CharacterStatusPanel
                recognition={recognition}
                visualSafety={visualSafety}
                relativeOffsetMs={relativeOffsetMs}
              />
              <SessionStatusPanel state={session} notices={runtimeNotices} visualSafety={visualSafety} />
            </div>

            <Form
              form={form}
              layout="vertical"
              initialValues={safeDefaults}
              requiredMark={false}
              className="configuration-panel"
            >
              <div className="configuration-heading">
                <div>
                  <Typography.Title level={2}>攻击设置</Typography.Title>
                  <Typography.Text type="secondary">基础选项常显，详细参数按需展开</Typography.Text>
                </div>
                <Space className="visual-profile-actions" size="small" wrap>
                  <Button
                    icon={<SafetyCertificateOutlined />}
                    aria-label="配置平台安全区"
                    title={visualConfigStatus === 'ready' ? '平台安全区已配置，人物模板继续复用' : '配置平台安全区'}
                    onClick={() => postBridgeCommand({ command: 'openVisualStationarySetup' })}
                  >配置平台</Button>
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
                    />
                  </Popconfirm>
                </Space>
              </div>

              <div className="base-configuration-grid">
                <AttackModeField />
                <div className="essential-settings">
                  <div className="target-status-line">
                    <span className="target-status-dot" />
                    <div>
                      <Typography.Text strong>客户端</Typography.Text>
                      <Typography.Text type="secondary">自动检测正在运行的冒险岛怀旧服客户端</Typography.Text>
                    </div>
                  </div>
                  <div className="basic-grid">
                    <Form.Item label="攻击键" name="attackKey" rules={[{ required: true }]}>
                      <Select options={attackKeys.map((value) => ({ value, label: value }))} />
                    </Form.Item>
                    <Form.Item label="攻击硬上限">
                      <InputNumber value={60000} disabled suffix="ms" />
                    </Form.Item>
                  </div>
                  <RecognitionToggle />
                </div>
              </div>

              <div className="parameter-collapse-grid">
                <AttackBandsEditor />
                <AdvancedParametersCollapse />
              </div>

              <div className="form-footer">
                <Button onClick={() => { form.setFieldsValue(safeDefaults); form.setFields([]); setConfigError(null) }}>恢复安全默认值</Button>
                <Typography.Text type="secondary">运行中保存的节奏参数从下一完整周期生效</Typography.Text>
              </div>
            </Form>
          </div>
        ) : (
          <MapManagementPanel
            recognitionEnabled={Boolean(form.getFieldValue('recognitionEnabled'))}
            directory={mapDirectory}
            entries={mapEntries}
            selected={selectedMap}
            running={navigationRunning}
            error={navigationError}
            navigationState={navigationState}
            onRecord={() => postBridgeCommand({
              command: 'startMapRecording',
              recognitionEnabled: Boolean(form.getFieldValue('recognitionEnabled')),
            })}
            onStopRecording={() => postBridgeCommand({ command: 'stopMapRecording' })}
            onChooseDirectory={() => postBridgeCommand({ command: 'chooseMapDirectory' })}
            onSelect={setSelectedMap}
            onStart={startNavigation}
            onStop={stopNavigation}
          />
        )}
      </main>

      <RuntimeLogModal
        open={logOpen}
        loading={logLoading}
        entries={logEntries}
        error={logError}
        onClose={() => setLogOpen(false)}
        onRefresh={loadLogs}
      />

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
  if (status === 'ready') return '视觉可用'
  if (status === 'viewportMismatch') return '画面已变化'
  if (status === 'invalid') return '视觉无效'
  return '视觉未配置'
}

function isConfigurationError(code: string | undefined): boolean {
  return code === 'CONFIG_INVALID' ||
    code === 'ATTACK_WEIGHT_TOTAL' || code === 'ATTACK_TRIGGER_DISABLED' || code === 'MOVE_BUDGET_TOO_SMALL'
}
