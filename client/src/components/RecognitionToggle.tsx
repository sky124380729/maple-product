import { Form, Switch, Typography } from 'antd'

export function RecognitionToggle() {
  return (
    <div className="recognition-toggle">
      <Form.Item label="启用实时识别" name="recognitionEnabled" valuePropName="checked" noStyle>
        <Switch />
      </Form.Item>
      <Typography.Text type="secondary">预览和运行期间持续识别角色、HP、MP、EXP及目标</Typography.Text>
    </div>
  )
}
