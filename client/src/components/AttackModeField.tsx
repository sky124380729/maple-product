import { Form, Radio, Tag, Typography } from 'antd'

export function AttackModeField() {
  return (
    <Form.Item label="定点攻击模式" name="attackTriggerMode">
      <Radio.Group className="mode-options">
        <Radio value="always">
          <span className="mode-copy">
            <Typography.Text strong>持续攻击</Typography.Text>
            <Typography.Text type="secondary">随机长按攻击，并执行受限左右移动</Typography.Text>
          </span>
        </Radio>
        <Radio value="monsterInRange" disabled aria-label="识别怪物后攻击">
          <span className="mode-copy">
            <span className="mode-title-row">
              <Typography.Text>识别怪物后攻击</Typography.Text>
              <Tag>后续版本开放</Tag>
            </span>
            <Typography.Text type="secondary">一期不可保存或启动此模式</Typography.Text>
          </span>
        </Radio>
      </Radio.Group>
    </Form.Item>
  )
}
