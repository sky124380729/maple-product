import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StationaryAttackPage } from './StationaryAttackPage'

describe('StationaryAttackPage', () => {
  it('shows the unavailable recognition mode without allowing selection', () => {
    render(<StationaryAttackPage />)

    expect(screen.getByText('识别怪物后攻击')).toBeVisible()
    expect(screen.getByText('后续版本开放')).toBeVisible()
    expect(screen.getByRole('radio', { name: /识别怪物后攻击/ })).toBeDisabled()
  })

  it('keeps advanced debugging parameters collapsed by default', () => {
    render(<StationaryAttackPage />)

    expect(screen.getByText('高级调试参数')).toBeVisible()
    expect(screen.queryByLabelText('每侧最大累计偏移')).not.toBeInTheDocument()
  })
})
