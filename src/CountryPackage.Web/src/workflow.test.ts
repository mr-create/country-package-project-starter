import { describe, expect, it } from 'vitest'
import { isActionable, statusLabel, stepNames } from './workflow'

describe('workflow presentation', () => {
  it('uses the four roadmap steps in the required order', () => {
    expect(stepNames).toEqual(['Country decision', 'Country distribution', 'Regional decision', 'Regional distribution'])
  })

  it('prevents actions while a step is pending or complete', () => {
    const base = { id: '1', order: 1, kind: 'Decision', requiredClearance: 'Country' } as const
    expect(isActionable({ ...base, status: 'PendingReview' })).toBe(false)
    expect(isActionable({ ...base, status: 'Completed' })).toBe(false)
    expect(isActionable({ ...base, status: 'Returned' })).toBe(true)
    expect(statusLabel.Returned).toBe('Returned')
  })
})
