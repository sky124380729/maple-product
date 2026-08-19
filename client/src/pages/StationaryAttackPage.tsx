import { useEffect, useReducer, useState } from 'react'
import {
  Alert,
  Button,
  ConfigProvider,
  Form,
  Input,
  InputNumber,
  Select,
  Space,
  Typography,
  theme,
} from 'antd'
import { FolderOpenOutlined, PlayCircleOutlined, SaveOutlined, StopOutlined } from '@ant-design/icons'
import { AttackModeField } from '../components/AttackModeField'
import { AdvancedParametersCollapse } from '../components/AdvancedParametersCollapse'
import { SessionStatusPanel } from '../components/SessionStatusPanel'
import { postBridgeCommand, subscribeBridgeMessages } from '../bridge/bridge'
import { safeDefaults, type StationaryAttackConfig } from '../bridge/types'
import { initialSessionState, sessionReducer } from '../state/sessionReducer'
import '../styles/app.css'

const attackKeys = ['Ctrl', 'Shift', 'Space', 'A', 'S', 'D', 'F', 'Z', 'X', 'C', 'V']

export function StationaryAttackPage() {
  const [form] = Form.useForm<StationaryAttackConfig>()
  const [session, dispatch] = useReducer(sessionReducer, initialSessionState)
  const [saving, setSaving] = useState(false)
  const running = session.status === 'running' || session.status === 'locating' || session.status === 'arming'

  useEffect(() => subscribeBridgeMessages((message) => {
    if (!message || typeof message !== 'object') return
    const data = message as { type?: string; path?: string; state?: typeof session.rhythm; reason?: string; error?: string }
    if (data.type === 'targetExecutableSelected' && data.path) form.setFieldValue('targetExecutablePath', data.path)
    if (data.type === 'stationary.rhythm.updated' && data.state) dispatch({ type: 'rhythmUpdated', payload: data.state })
    if (data.type === 'stationary.stopped') dispatch({ type: 'stopped', reason: data.reason ?? 'HOST_STOPPED' })
    if (data.type === 'stationary.error') dispatch({ type: 'failed', error: data.error ?? '运行异常' })
  }), [form])

  const save = async () => {
    const config = await form.validateFields()
    setSaving(true)
    postBridgeCommand({ command: 'saveConfig', config })
    window.setTimeout(() => setSaving(false), 180)
  }

  const start = async () => {
    const config = await form.validateFields()
    dispatch({ type: 'starting' })
    postBridgeCommand({ command: 'startStationary', config })
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
                  <Typography.Text type="secondary">设置目标窗口、攻击模式和输入按键</Typography.Text>
                </div>
              </div>

              <Form.Item
                label="目标游戏程序"
                name="targetExecutablePath"
                rules={[{ required: true, message: '请选择目标游戏 exe' }]}
                extra="后续只按规范化后的完整 exe 路径定位窗口"
              >
                <Space.Compact block>
                  <Input readOnly placeholder="请选择 Windows 游戏 exe" />
                  <Button
                    icon={<FolderOpenOutlined />}
                    onClick={() => postBridgeCommand({ command: 'chooseTargetExecutable' })}
                  >
                    选择
                  </Button>
                </Space.Compact>
              </Form.Item>

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

            <AdvancedParametersCollapse />

            <Space className="form-footer" wrap>
              <Button onClick={() => form.setFieldsValue(safeDefaults)}>恢复安全默认值</Button>
              <Typography.Text type="secondary">运行中保存的节奏参数从下一完整周期生效</Typography.Text>
            </Space>
          </Form>

          <SessionStatusPanel state={session} />
        </div>
      </main>
    </ConfigProvider>
  )
}
