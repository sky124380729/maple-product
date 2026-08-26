import { Tag, Typography } from 'antd'
import type { RecognitionSnapshotView, VisualStationaryStateView } from '../bridge/types'
import { RecognitionStatus } from './RecognitionStatus'

export function CharacterStatusPanel({
  recognition,
  visualSafety,
  relativeOffsetMs,
}: {
  recognition: RecognitionSnapshotView | null
  visualSafety: VisualStationaryStateView | null
  relativeOffsetMs: number | null
}) {
  return (
    <section className="status-surface character-status-panel" aria-labelledby="character-status-title">
      <div className="panel-heading">
        <div>
          <Typography.Title level={2} id="character-status-title">角色状态</Typography.Title>
          <Typography.Text type="secondary">识别资源与站位诊断</Typography.Text>
        </div>
        <Tag color={recognition?.health === 'running' ? 'success' : recognition?.health === 'faulted' ? 'error' : 'default'}>
          {recognition?.health === 'running' ? '实时' : recognition ? '待恢复' : '无数据'}
        </Tag>
      </div>
      <div className="character-status-content">
        <RecognitionStatus
          snapshot={recognition}
          relativeOffsetMs={relativeOffsetMs}
          visualOffsetPx={visualSafety?.visualOffsetPx ?? null}
        />
      </div>
    </section>
  )
}
