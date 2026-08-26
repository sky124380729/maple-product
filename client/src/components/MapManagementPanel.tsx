import { Button, Space, Typography } from 'antd'
import { StopOutlined, VideoCameraOutlined } from '@ant-design/icons'
import type { NavigationCatalogEntry, NavigationStateView } from '../bridge/navigationTypes'
import { NavigationControls } from './NavigationControls'
import { NavigationStatus } from './NavigationStatus'

export function MapManagementPanel({
  recognitionEnabled,
  directory,
  entries,
  selected,
  running,
  error,
  navigationState,
  onRecord,
  onStopRecording,
  onChooseDirectory,
  onSelect,
  onStart,
  onStop,
}: {
  recognitionEnabled: boolean
  directory: string | null
  entries: NavigationCatalogEntry[]
  selected: string | null
  running: boolean
  error: string | null
  navigationState: NavigationStateView | null
  onRecord: () => void
  onStopRecording: () => void
  onChooseDirectory: () => void
  onSelect: (path: string) => void
  onStart: () => void
  onStop: () => void
}) {
  return (
    <section className="map-management" aria-labelledby="map-management-title">
      <div className="map-command-band">
        <div>
          <Typography.Title level={2} id="map-management-title">地图管理</Typography.Title>
          <Typography.Text type="secondary">录制地图、维护地图包并运行单地图寻路</Typography.Text>
        </div>
        <Space wrap>
          <Button icon={<VideoCameraOutlined />} onClick={onRecord}>录制地图</Button>
          <Button danger icon={<StopOutlined />} onClick={onStopRecording}>停止录制地图</Button>
        </Space>
      </div>
      <div className="map-workspace-grid" data-recognition-enabled={recognitionEnabled}>
        <NavigationControls
          directory={directory}
          entries={entries}
          selected={selected}
          running={running}
          error={error}
          onChooseDirectory={onChooseDirectory}
          onSelect={onSelect}
          onStart={onStart}
          onStop={onStop}
        />
        <NavigationStatus state={navigationState} running={running} />
      </div>
    </section>
  )
}
