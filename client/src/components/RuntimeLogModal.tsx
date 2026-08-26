import { Alert, Button, Empty, Modal, Table, Tag } from 'antd'
import { ReloadOutlined } from '@ant-design/icons'
import type { ColumnsType } from 'antd/es/table'
import type { SessionLogEntryView } from '../bridge/sessionLogTypes'

const columns: ColumnsType<SessionLogEntryView> = [
  { title: '时间', dataIndex: 'timestampUtc', width: 154, render: formatTimestamp },
  { title: '周期', dataIndex: 'cycleId', width: 64 },
  { title: '阶段', dataIndex: 'phase', width: 116 },
  { title: '事件', dataIndex: 'event', width: 110 },
  { title: '结果', dataIndex: 'resultCode', width: 150, render: (value: string) => <Tag color={value === 'OK' ? 'success' : 'default'}>{value}</Tag> },
  { title: '方向', dataIndex: 'direction', width: 74, render: (value: string | null) => directionLabel(value) },
  { title: '偏移', dataIndex: 'offsetAfterMs', width: 86, align: 'right', render: formatOffset },
]

export function RuntimeLogModal({
  open,
  loading,
  entries,
  error,
  onClose,
  onRefresh,
}: {
  open: boolean
  loading: boolean
  entries: SessionLogEntryView[]
  error: string | null
  onClose: () => void
  onRefresh: () => void
}) {
  return (
    <Modal
      className="runtime-log-modal"
      title="运行日志"
      open={open}
      width={920}
      onCancel={onClose}
      footer={<Button onClick={onClose}>关闭</Button>}
      centered
    >
      <div className="log-modal-toolbar">
        <span>最近 {entries.length} 条结构化记录</span>
        <Button aria-label="刷新运行日志" icon={<ReloadOutlined />} loading={loading} onClick={onRefresh} />
      </div>
      {error && <Alert type="error" showIcon message={error} />}
      <Table<SessionLogEntryView>
        rowKey={(entry) => `${entry.timestampUtc}-${entry.sessionId}-${entry.cycleId}-${entry.event}-${entry.brokerSequence}`}
        columns={columns}
        dataSource={entries}
        loading={loading}
        size="small"
        pagination={false}
        scroll={{ x: 754, y: 360 }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无运行日志" /> }}
      />
    </Modal>
  )
}

function formatTimestamp(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}

function directionLabel(value: string | null): string {
  if (value === 'Left') return '左'
  if (value === 'Right') return '右'
  return '-'
}

function formatOffset(value: number | null): string {
  if (value == null) return '-'
  return `${value > 0 ? '+' : ''}${value} ms`
}
