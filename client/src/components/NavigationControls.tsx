import { Alert, Button, Select, Space, Typography } from 'antd'
import { FolderOpenOutlined, PlayCircleOutlined, StopOutlined } from '@ant-design/icons'
import type { NavigationCatalogEntry } from '../bridge/navigationTypes'

interface Props {
  directory: string | null
  entries: NavigationCatalogEntry[]
  selected: string | null
  running: boolean
  error: string | null
  onChooseDirectory: () => void
  onSelect: (path: string) => void
  onStart: () => void
  onStop: () => void
}

export function NavigationControls(props: Props) {
  return <section className="configuration-panel navigation-controls">
    <div className="section-heading">
      <div>
        <Typography.Title level={2}>单地图自动寻路</Typography.Title>
        <Typography.Text type="secondary">地图包定位、梯子路径规划与自动攻击</Typography.Text>
      </div>
    </div>
    {props.error && <Alert type="error" showIcon title="自动寻路异常" description={props.error} />}
    <Space orientation="vertical" size="middle" className="navigation-fields">
      <Button icon={<FolderOpenOutlined />} onClick={props.onChooseDirectory}>选择地图目录</Button>
      <Typography.Text type="secondary" ellipsis>{props.directory ?? '尚未选择地图目录'}</Typography.Text>
      <Select
        aria-label="选择地图"
        placeholder="选择当前所在地图"
        value={props.selected}
        onChange={props.onSelect}
        options={props.entries.map(entry => ({
          value: entry.packagePath,
          label: `${entry.fileName} · ${entry.mapName}`,
          disabled: !entry.canRun,
          title: entry.warningCode ?? undefined,
        }))}
      />
      {props.running
        ? <Button danger type="primary" icon={<StopOutlined />} onClick={props.onStop}>停止自动寻路</Button>
        : <Button type="primary" icon={<PlayCircleOutlined />} disabled={!props.selected} onClick={props.onStart}>开始自动寻路</Button>}
    </Space>
  </section>
}
