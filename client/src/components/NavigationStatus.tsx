import { Descriptions, Tag, Typography } from 'antd'
import type { NavigationStateView } from '../bridge/navigationTypes'

export function NavigationStatus({ state, running }: { state: NavigationStateView | null, running: boolean }) {
  return <aside className="status-panel navigation-status">
    <div className="status-heading">
      <div><Typography.Title level={2}>寻路状态</Typography.Title></div>
      <Tag color={running ? 'green' : 'default'}>{running ? '运行中' : '待机'}</Tag>
    </div>
    <Descriptions column={1} size="small">
      <Descriptions.Item label="地图">{state?.mapName ?? '-'}</Descriptions.Item>
      <Descriptions.Item label="阶段">{state?.phase ?? '等待开始'}</Descriptions.Item>
      <Descriptions.Item label="当前平台">{state?.currentPlatformId ?? '-'}</Descriptions.Item>
      <Descriptions.Item label="目标平台">{state?.targetPlatformId ?? '-'}</Descriptions.Item>
      <Descriptions.Item label="路径">{state?.route.join(' → ') || '-'}</Descriptions.Item>
      <Descriptions.Item label="动作">{state?.action ?? '-'}</Descriptions.Item>
      <Descriptions.Item label="定位置信度">{state?.localizationConfidence != null ? `${Math.round(state.localizationConfidence * 100)}%` : '-'}</Descriptions.Item>
      <Descriptions.Item label="角色坐标">{state?.selfX != null && state.selfY != null ? `${state.selfX.toFixed(1)}, ${state.selfY.toFixed(1)}` : '-'}</Descriptions.Item>
      <Descriptions.Item label="停止原因">{state?.faultCode ?? '-'}</Descriptions.Item>
    </Descriptions>
  </aside>
}
