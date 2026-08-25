import { Descriptions, Tag, Typography } from 'antd'
import type { VisualStationaryStateView } from '../bridge/types'

export function VisualSafetyStatus({ state }: { state: VisualStationaryStateView | null }) {
  if (!state) return null
  const label = statusLabel(state.status)
  const color = state.status.toLowerCase() === 'safe'
    ? 'success'
    : state.status.toLowerCase().includes('guard') || state.status.toLowerCase().includes('fallback')
      ? 'warning'
      : 'error'
  return (
    <section className="visual-safety-status" aria-labelledby="visual-safety-title">
      <div className="visual-safety-heading">
        <Typography.Title level={4} id="visual-safety-title">视觉平台保护</Typography.Title>
        <Tag color={color}>{label}</Tag>
      </div>
      <Descriptions column={1} size="small">
        <Descriptions.Item label="识别目标">
          {state.identityKind === 'CharacterAppearance' ? '人物外观' : '名字模板'}
        </Descriptions.Item>
        <Descriptions.Item label={state.status.toLowerCase() === 'fallbackcontinuous' ? '预测位置' : '实际位置'}>
          <Typography.Text strong data-testid="visual-offset">{formatPixelOffset(state.visualOffsetPx)}</Typography.Text>
        </Descriptions.Item>
        <Descriptions.Item label={state.identityKind === 'CharacterAppearance' ? '人物匹配' : '名字匹配'}>
          {Math.round(state.bestScore * 100)}%
        </Descriptions.Item>
        <Descriptions.Item label="保护带">{state.guardWidthPx} px</Descriptions.Item>
      </Descriptions>
    </section>
  )
}

function statusLabel(status: string): string {
  const normalized = status.toLowerCase()
  if (normalized === 'safe') return '安全区'
  if (normalized === 'guardleft') return '左侧保护区'
  if (normalized === 'guardright') return '右侧保护区'
  if (normalized === 'outside') return '越界冻结'
  if (normalized === 'fallbackcontinuous') return '持续攻击回退'
  if (normalized === 'untrusted') return '识别暂失，移动冻结'
  return '正在锁定'
}

function formatPixelOffset(value: number | null): string {
  if (value == null) return '-'
  if (value < 0) return `${value} px（左）`
  if (value > 0) return `+${value} px（右）`
  return '0 px（中心）'
}
