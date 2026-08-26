import { Collapse, Form, InputNumber, Typography } from 'antd'

const bandNames = ['短时', '中短时', '中长时', '长时']

export function AttackBandsEditor() {
  return (
    <Collapse
      className="parameter-collapse"
      items={[{
        key: 'attack-bands',
        label: <span className="collapse-title"><Typography.Text strong>攻击时长分段</Typography.Text><Typography.Text type="secondary">4 组随机权重</Typography.Text></span>,
        children: (
          <div className="attack-band-list">
            {bandNames.map((bandName, index) => (
              <fieldset className="attack-band-row" key={bandName}>
                <legend>分段 {index + 1} / {bandName}</legend>
                <Form.Item label="最小值" name={['attackBands', index, 'minMs']}>
                  <InputNumber aria-label={`分段 ${index + 1} 最小值`} min={1} max={60000} step={1} suffix="ms" />
                </Form.Item>
                <Form.Item label="最大值" name={['attackBands', index, 'maxMs']}>
                  <InputNumber aria-label={`分段 ${index + 1} 最大值`} min={1} max={60000} step={1} suffix="ms" />
                </Form.Item>
                <Form.Item label="权重" name={['attackBands', index, 'weight']}>
                  <InputNumber aria-label={`分段 ${index + 1} 权重`} min={1} max={100} step={1} suffix="%" />
                </Form.Item>
              </fieldset>
            ))}
          </div>
        ),
      }]}
    />
  )
}
