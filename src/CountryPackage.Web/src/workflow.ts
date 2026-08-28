import type { ApprovalStep, StepStatus } from './types'

export const stepNames = [
  'Country decision',
  'Country distribution',
  'Regional decision',
  'Regional distribution',
]

export const statusLabel: Record<StepStatus, string> = {
  NotStarted: 'Not started',
  Draft: 'Draft ready',
  PendingReview: 'Pending review',
  Returned: 'Returned',
  Completed: 'Completed',
}

export function isActionable(step: ApprovalStep): boolean {
  return step.status !== 'Completed' && step.status !== 'PendingReview'
}
