import { Tag, Typography } from 'antd'
import type { VisualStationaryStateView } from '../bridge/types'

export function VisualSafetyStatus({ state }: { state: VisualStationaryStateView | null }) {
  const label = state ? statusLabel(state.status) : '无数据'
  const color = state?.status.toLowerCase() === 'safe'
    ? 'success'
    : state && (state.status.toLowerCase().includes('guard') || state.status.toLowerCase().includes('fallback'))
      ? 'warning'
      : state ? 'error' : 'default'
  return (
    <section className="visual-safety-status" aria-label="视觉平台保护">
      <div className="visual-safety-heading">
        <Typography.Text strong>视觉平台保护</Typography.Text>
        <Tag color={color}>{label}</Tag>
      </div>
      <div className="visual-safety-facts">
        <SafetyFact label="识别目标" value={state ? state.identityKind === 'CharacterAppearance' ? '人物外观' : '名字模板' : '-'} />
        <SafetyFact label={state?.identityKind === 'CharacterAppearance' ? '人物匹配' : '名字匹配'} value={state ? `${Math.round(state.bestScore * 100)}%` : '-'} />
        <SafetyFact label="保护带" value={state ? `${state.guardWidthPx} px` : '-'} />
      </div>
    </section>
  )
}

function SafetyFact({ label, value }: { label: string; value: string }) {
  return <span><Typography.Text type="secondary">{label}</Typography.Text><Typography.Text>{value}</Typography.Text></span>
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
