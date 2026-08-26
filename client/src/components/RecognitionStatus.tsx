import { useEffect, useState } from 'react'
import { Progress, Tag, Typography } from 'antd'
import type { RecognitionSnapshotView } from '../bridge/types'

export function RecognitionStatus({
  snapshot,
  relativeOffsetMs,
  visualOffsetPx,
}: {
  snapshot: RecognitionSnapshotView | null
  relativeOffsetMs: number | null
  visualOffsetPx: number | null
}) {
  const [elapsedMs, setElapsedMs] = useState(0)
  const snapshotKey = snapshot == null ? 'none' : [snapshot.health, snapshot.faultCode, snapshot.hud.characterName, snapshot.hud.level, snapshot.hud.job, snapshot.hud.hpCurrent, snapshot.hud.hpMax, snapshot.hud.mpCurrent, snapshot.hud.mpMax, snapshot.hud.expPercent].join('|')
  useEffect(() => {
    setElapsedMs(0)
    if (!snapshot || snapshot.health !== 'running') return
    const startedAt = performance.now()
    const timer = window.setInterval(() => setElapsedMs(performance.now() - startedAt), 250)
    return () => window.clearInterval(timer)
  }, [snapshotKey, snapshot?.health])
  const hud = snapshot?.hud
  const frameAgeMs = snapshot ? Math.round(snapshot.frameAgeMs + elapsedMs) : null
  const health = snapshot?.health === 'running' && frameAgeMs != null && frameAgeMs > 500
    ? 'stale'
    : snapshot?.health
  const fresh = health === 'running'
  return (
    <div className="recognition-status">
      <div className="identity-row">
        <div className="identity-copy">
          <Typography.Text strong>{hud?.characterName || '未识别角色'}</Typography.Text>
          <Typography.Text>{hud?.level == null ? 'Lv.-' : `Lv.${hud.level}`}</Typography.Text>
          <Typography.Text type="secondary">{hud?.job || '职业未识别'}</Typography.Text>
        </div>
        <Tag color={fresh ? 'success' : health === 'faulted' ? 'error' : 'default'}>
          {fresh ? '识别中' : health ? healthLabel(health) : '无数据'}
        </Tag>
      </div>
      <div className="recognition-offset-row">
        <Typography.Text type="secondary">计算偏移</Typography.Text>
        <Typography.Text strong data-testid="relative-offset">
          {formatRelativeOffset(relativeOffsetMs)}
        </Typography.Text>
      </div>
      <div className="recognition-offset-row">
        <Typography.Text type="secondary">视觉像素偏移</Typography.Text>
        <Typography.Text strong data-testid="visual-offset">
          {formatPixelOffset(visualOffsetPx)}
        </Typography.Text>
      </div>
      <ResourceRow label="HP" current={hud?.hpCurrent} maximum={hud?.hpMax} ratio={hud?.hpPercent} color="#d94a4a" />
      <ResourceRow label="MP" current={hud?.mpCurrent} maximum={hud?.mpMax} ratio={hud?.mpPercent} color="#3586d8" />
      <div className="recognition-meta">
        <span className="recognition-exp"><Typography.Text>EXP</Typography.Text><Typography.Text>{hud?.expPercent == null ? '-' : `${hud.expPercent.toFixed(2)}%`}</Typography.Text></span>
        <Typography.Text type="secondary">置信度 {hud ? `${Math.round(hud.confidence * 100)}%` : '-'}</Typography.Text>
        <span className="recognition-age"><Typography.Text type="secondary">帧龄</Typography.Text><Typography.Text type="secondary">{frameAgeMs == null ? '-' : `${frameAgeMs} ms`}</Typography.Text></span>
      </div>
      <Progress className="recognition-exp-progress" aria-label="EXP" percent={hud?.expPercent == null ? 0 : Math.round(Math.max(0, Math.min(100, hud.expPercent)))} showInfo={false} strokeColor="#d6ab2c" size="small" />
      {snapshot?.faultCode && <Typography.Text type="danger">{snapshot.faultCode}</Typography.Text>}
    </div>
  )
}

function formatRelativeOffset(relativeOffsetMs: number | null): string {
  if (relativeOffsetMs == null) return '-'
  if (relativeOffsetMs < 0) return `${relativeOffsetMs} ms（左）`
  if (relativeOffsetMs > 0) return `+${relativeOffsetMs} ms（右）`
  return '0 ms（中心）'
}

function formatPixelOffset(value: number | null): string {
  if (value == null) return '-'
  if (value < 0) return `${value} px（左）`
  if (value > 0) return `+${value} px（右）`
  return '0 px（中心）'
}

function ResourceRow({ label, current, maximum, ratio, color }: {
  label: string
  current?: number | null
  maximum?: number | null
  ratio?: number | null
  color: string
}) {
  const percent = ratio == null ? 0 : Math.round(Math.max(0, Math.min(100, ratio * 100)))
  return (
    <div className="resource-row">
      <div className="resource-copy">
        <Typography.Text strong>{label}</Typography.Text>
        <Typography.Text>{current == null || maximum == null ? '-' : `${current} / ${maximum}`}</Typography.Text>
      </div>
      <Progress percent={percent} status="normal" showInfo={ratio != null} size="small" strokeColor={color} />
    </div>
  )
}

function healthLabel(health: RecognitionSnapshotView['health']) {
  const labels: Record<RecognitionSnapshotView['health'], string> = {
    disabled: '未开启', starting: '启动中', running: '识别中', stale: '结果过期', faulted: '识别异常', targetLost: '目标丢失',
  }
  return labels[health]
}
