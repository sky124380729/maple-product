import { Collapse, Form, InputNumber, Switch } from 'antd'

const rangeRule = [{ required: true, message: '请输入有效值' }]

export function AdvancedParametersCollapse() {
  return (
    <Collapse
      className="advanced-collapse"
      items={[
        {
          key: 'advanced',
          label: '高级调试参数',
          children: (
            <div className="parameter-grid">
              <Form.Item label="每侧最大累计偏移" name="maxLateralMoveMs" rules={rangeRule}>
                <InputNumber min={1} max={5000} suffix="ms" />
              </Form.Item>
              <Form.Item label="移动按压最小值" name="moveHoldMinMs" rules={rangeRule}>
                <InputNumber min={1} max={5000} suffix="ms" />
              </Form.Item>
              <Form.Item label="移动按压最大值" name="moveHoldMaxMs" rules={rangeRule}>
                <InputNumber min={1} max={5000} suffix="ms" />
              </Form.Item>
              <Form.Item label="无按键间隔最小值" name="moveGapMinMs" rules={rangeRule}>
                <InputNumber min={1} max={5000} suffix="ms" />
              </Form.Item>
              <Form.Item label="无按键间隔最大值" name="moveGapMaxMs" rules={rangeRule}>
                <InputNumber min={1} max={5000} suffix="ms" />
              </Form.Item>
              <Form.Item label="稳定等待最小值" name="stabilizeMinMs" rules={rangeRule}>
                <InputNumber min={1} max={5000} suffix="ms" />
              </Form.Item>
              <Form.Item label="稳定等待最大值" name="stabilizeMaxMs" rules={rangeRule}>
                <InputNumber min={1} max={5000} suffix="ms" />
              </Form.Item>
              <Form.Item label="启用随机休息" name="restEnabled" valuePropName="checked">
                <Switch />
              </Form.Item>
              <Form.Item label="休息概率" name="restProbabilityPercent" rules={rangeRule}>
                <InputNumber min={0} max={100} suffix="%" />
              </Form.Item>
              <Form.Item label="休息最小值" name="restMinMs" rules={rangeRule}>
                <InputNumber min={1} max={60000} suffix="ms" />
              </Form.Item>
              <Form.Item label="休息最大值" name="restMaxMs" rules={rangeRule}>
                <InputNumber min={1} max={60000} suffix="ms" />
              </Form.Item>
            </div>
          ),
        },
      ]}
    />
  )
}
