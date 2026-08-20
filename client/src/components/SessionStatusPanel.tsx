import { Alert, Descriptions, Statistic, Tag, Typography } from 'antd'
import type { SessionState } from '../state/sessionReducer'
import type { RecognitionSnapshotView } from '../bridge/types'
import { RecognitionStatus } from './RecognitionStatus'
import { formatDurationSeconds, useRhythmCountdown } from '../hooks/useRhythmCountdown'

const phaseLabels: Record<string, string> = {
  idle: '等待开始',
  attackHolding: '持续攻击',
  attackReleased: '攻击释放缓冲',
  moveFirst: '第一方向移动',
  moveGap: '无按键间隔',
  moveSecond: '反向移动',
  stabilizing: '稳定等待',
  resting: '休息',
  stopped: '已停止',
}

const nextPhaseLabels: Record<string, string> = {
  attackHolding: '攻击释放缓冲',
  attackReleased: '第一方向移动',
  moveFirst: '无按键间隔',
  moveGap: '反向移动',
  moveSecond: '稳定等待',
  stabilizing: '下一轮攻击',
  resting: '下一轮攻击',
}

export function SessionStatusPanel({
  state,
  recognition,
}: {
  state: SessionState
  recognition: RecognitionSnapshotView | null
}) {
  const remainingMs = useRhythmCountdown(state.rhythm)
  const phase = state.rhythm?.phase ?? 'idle'
  const active = state.status === 'running'
  const movementTransition = phase === 'attackReleased' || phase === 'moveFirst' || phase === 'moveGap' || phase === 'moveSecond' || phase === 'stabilizing' || phase === 'resting'

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
        <Typography.Text className="countdown-label">
          {movementTransition ? '下轮攻击前剩余' : '本阶段剩余'}
        </Typography.Text>
        <Statistic value={remainingMs / 1000} precision={3} suffix="秒" />
        <Typography.Text type="secondary">
          {movementTransition
            ? '完成左右移动和稳定等待后才会进入下一轮攻击'
            : `本轮攻击总时长 ${formatDurationSeconds(state.rhythm?.sampledDurationMs ?? 0)}`}
        </Typography.Text>
      </div>

      <Descriptions column={1} size="small" className="session-details">
        <Descriptions.Item label="Cycle ID">{state.rhythm?.cycleId ?? '-'}</Descriptions.Item>
        <Descriptions.Item label="当前阶段">{phaseLabels[phase]}</Descriptions.Item>
        <Descriptions.Item label="下一阶段">{nextPhaseLabels[phase] ?? '-'}</Descriptions.Item>
        <Descriptions.Item label="输入状态">{active ? 'Broker 已租约保护' : '未发送输入'}</Descriptions.Item>
      </Descriptions>

      <RecognitionStatus snapshot={recognition} />

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
