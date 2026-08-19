import { Form, InputNumber, Typography } from 'antd'

const bandNames = ['短时', '中短时', '中长时', '长时']

export function AttackBandsEditor() {
  return (
    <section className="attack-bands-section" aria-labelledby="attack-bands-title">
      <div className="section-heading compact">
        <div>
          <Typography.Title level={3} id="attack-bands-title">攻击时长分段</Typography.Title>
          <Typography.Text type="secondary">四段权重总和必须为 100%，每段最长 60000 ms</Typography.Text>
        </div>
      </div>
      <div className="attack-band-list">
        {bandNames.map((bandName, index) => (
          <fieldset className="attack-band-row" key={bandName}>
            <legend>分段 {index + 1} / {bandName}</legend>
            <Form.Item label={`分段 ${index + 1} 最小值`} name={['attackBands', index, 'minMs']}>
              <InputNumber aria-label={`分段 ${index + 1} 最小值`} min={1} max={60000} step={1} suffix="ms" />
            </Form.Item>
            <Form.Item label={`分段 ${index + 1} 最大值`} name={['attackBands', index, 'maxMs']}>
              <InputNumber aria-label={`分段 ${index + 1} 最大值`} min={1} max={60000} step={1} suffix="ms" />
            </Form.Item>
            <Form.Item label={`分段 ${index + 1} 权重`} name={['attackBands', index, 'weight']}>
              <InputNumber aria-label={`分段 ${index + 1} 权重`} min={1} max={100} step={1} suffix="%" />
            </Form.Item>
          </fieldset>
        ))}
      </div>
    </section>
  )
}
