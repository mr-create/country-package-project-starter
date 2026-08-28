export type Role = 'CountryEditor' | 'CountryReviewer'
export type Clearance = 'Country' | 'Regional'
export type StepStatus = 'NotStarted' | 'Draft' | 'PendingReview' | 'Returned' | 'Completed'

export interface Persona {
  userId: string
  displayName: string
  role: Role
  countryScopes: string[]
  clearance?: Clearance | null
}

export interface PackageSummary {
  id: string
  countryCode: string
  title: string
  status: 'InProgress' | 'Completed'
  currentStepOrder: number
  currentStepStatus: StepStatus
}

export interface ApprovalStep {
  id: string
  order: number
  kind: 'Decision' | 'Distribution'
  requiredClearance: Clearance
  status: StepStatus
  reviewerUserId?: string | null
  recipientUserId?: string | null
  draftDocumentVersionId?: string | null
  snapshotDocumentVersionId?: string | null
  distributedDocumentVersionId?: string | null
  reviewDecision?: 'Approve' | 'Return' | null
  reviewComment?: string | null
  submittedAt?: string | null
  completedAt?: string | null
}

export interface CountryPackage {
  id: string
  countryCode: string
  title: string
  status: 'InProgress' | 'Completed'
  createdBy: string
  createdAt: string
  steps: ApprovalStep[]
}

export interface EvidenceManifestInput {
  sourceReferences: string[]
  citations: string[]
  validationFindings: string[]
  workflowVersion: string
  modelIdentifier: string
  generatedAt: string
}

export interface CopilotResult {
  draft: string
  citations: string[]
  warnings: string[]
  evidenceManifest: EvidenceManifestInput
}

export interface AuditEntry {
  id: string
  stepOrder?: number | null
  actorUserId: string
  action: string
  details: Record<string, unknown>
  occurredAt: string
  traceId: string
}

export interface ReviewerTask {
  packageId: string
  countryCode: string
  title: string
  stepOrder: number
  requiredClearance: Clearance
  snapshotDocumentVersionId: string
  submittedAt: string
}

export interface ReviewContext {
  packageId: string
  countryCode: string
  title: string
  step: ApprovalStep
  snapshot: {
    id: string
    fileName: string
    sha256: string
    uploadedBy: string
    uploadedAt: string
    evidenceManifest?: (EvidenceManifestInput & { acceptedBy: string }) | null
  }
  audit: AuditEntry[]
}
