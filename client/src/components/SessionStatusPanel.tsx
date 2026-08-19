import { Alert, Button, Descriptions, Space, Statistic, Tag, Typography } from 'antd'
import { EyeOutlined } from '@ant-design/icons'
import type { SessionState } from '../state/sessionReducer'
import { formatDurationSeconds, useRhythmCountdown } from '../hooks/useRhythmCountdown'
import { postBridgeCommand } from '../bridge/bridge'

const phaseLabels: Record<string, string> = {
  idle: '等待开始',
  attackHolding: '持续攻击',
  moveFirst: '第一方向移动',
  moveGap: '无按键间隔',
  moveSecond: '反向移动',
  stabilizing: '稳定等待',
  resting: '休息',
  stopped: '已停止',
}

const nextPhaseLabels: Record<string, string> = {
  attackHolding: '第一方向移动',
  moveFirst: '无按键间隔',
  moveGap: '反向移动',
  moveSecond: '稳定等待',
  stabilizing: '下一轮攻击',
  resting: '下一轮攻击',
}

export function SessionStatusPanel({ state }: { state: SessionState }) {
  const remainingMs = useRhythmCountdown(state.rhythm)
  const phase = state.rhythm?.phase ?? 'idle'
  const active = state.status === 'running'

  return (
    <section className="status-panel" aria-labelledby="session-status-title">
      <div className="section-heading compact">
        <div>
          <Typography.Title level={3} id="session-status-title">运行状态</Typography.Title>
          <Typography.Text type="secondary">Host 发布的权威会话与节奏状态</Typography.Text>
        </div>
        <Tag color={active ? 'success' : state.status === 'error' ? 'error' : 'default'}>
          {active ? '运行中' : state.status === 'error' ? '异常' : state.status === 'stopped' ? '已停止' : '待机'}
        </Tag>
      </div>

      {state.error && <Alert type="error" showIcon title="运行异常" description={state.error} />}
      {state.stopReason && <Alert type="info" showIcon title="会话已停止" description={stopReasonMessage(state.stopReason)} />}

      <div className="countdown-block" aria-live="polite">
        <Typography.Text className="countdown-label">本阶段剩余</Typography.Text>
        <Statistic value={remainingMs / 1000} precision={3} suffix="秒" />
        <Typography.Text type="secondary">
          本轮攻击总时长 {formatDurationSeconds(state.rhythm?.sampledDurationMs ?? 0)}
        </Typography.Text>
      </div>

      <Descriptions column={1} size="small" className="session-details">
        <Descriptions.Item label="Cycle ID">{state.rhythm?.cycleId ?? '-'}</Descriptions.Item>
        <Descriptions.Item label="当前阶段">{phaseLabels[phase]}</Descriptions.Item>
        <Descriptions.Item label="下一阶段">{nextPhaseLabels[phase] ?? '-'}</Descriptions.Item>
        <Descriptions.Item label="输入状态">{active ? 'Broker 已租约保护' : '未发送输入'}</Descriptions.Item>
      </Descriptions>

      <Space wrap>
        <Button icon={<EyeOutlined />} onClick={() => postBridgeCommand({ command: 'openPreview' })}>
          打开实时预览
        </Button>
        <Typography.Text type="secondary">预览在独立原生窗口打开</Typography.Text>
      </Space>
    </section>
  )
}

function stopReasonMessage(reason: string): string {
  if (reason.startsWith('FOCUS_LOST:')) {
    return '游戏窗口失去前台，已安全停止输入。请保持游戏窗口为当前前台窗口后重新开始。'
  }
  const messages: Record<string, string> = {
    FOCUS_LOST: '游戏窗口失去前台，已安全停止输入。请保持游戏窗口为当前前台窗口后重新开始。',
    WINDOW_IDENTITY_CHANGED: '游戏窗口身份发生变化，已安全停止输入。请重新开始。',
    BROKER_HEARTBEAT_IO: '输入服务心跳失败，已安全停止输入。请重新开始。',
  }
  return messages[reason] ?? reason
}
