import type {
  AuditEntry,
  CopilotResult,
  CountryPackage,
  EvidenceManifestInput,
  PackageSummary,
  Persona,
  ReviewContext,
  ReviewerTask,
} from './types'

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code?: string,
  ) {
    super(message)
  }
}

async function request<T>(path: string, userId?: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (userId) headers.set('X-User-Id', userId)
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  const response = await fetch(path, { ...init, headers })
  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new ApiError(problem?.detail ?? problem?.title ?? `Request failed (${response.status})`, response.status, problem?.code)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

const commandHeaders = () => ({ 'Idempotency-Key': crypto.randomUUID() })

export const api = {
  personas: () => request<Persona[]>('/api/dev/personas'),
  packages: (userId: string) => request<PackageSummary[]>('/api/packages', userId),
  package: (userId: string, id: string) => request<CountryPackage>(`/api/packages/${id}`, userId),
  createPackage: (userId: string, countryCode: string, title: string) =>
    request<CountryPackage>('/api/packages', userId, {
      method: 'POST', headers: commandHeaders(), body: JSON.stringify({ countryCode, title }),
    }),
  upload: (userId: string, packageId: string, order: number, file: Blob, fileName: string, manifest?: EvidenceManifestInput) => {
    const body = new FormData()
    body.append('file', file, fileName)
    if (manifest) body.append('evidenceManifest', JSON.stringify(manifest))
    return request(`/api/packages/${packageId}/steps/${order}/document`, userId, {
      method: 'POST', headers: commandHeaders(), body,
    })
  },
  submit: (userId: string, packageId: string, order: number, assignment: { reviewerUserId?: string; recipientUserId?: string }) =>
    request(`/api/packages/${packageId}/steps/${order}/submit`, userId, {
      method: 'POST', headers: commandHeaders(), body: JSON.stringify(assignment),
    }),
  audit: (userId: string, packageId: string, order: number) =>
    request<AuditEntry[]>(`/api/packages/${packageId}/steps/${order}/audit`, userId),
  download: async (userId: string, packageId: string, order: number) => {
    const response = await fetch(`/api/packages/${packageId}/steps/${order}/document`, { headers: { 'X-User-Id': userId } })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      throw new ApiError(problem?.title ?? 'Could not download the document.', response.status, problem?.code)
    }
    const disposition = response.headers.get('Content-Disposition') ?? ''
    const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1]
    const plain = /filename="?([^";]+)"?/i.exec(disposition)?.[1]
    return { blob: await response.blob(), fileName: encoded ? decodeURIComponent(encoded) : plain ?? 'country-package-document' }
  },
  tasks: (userId: string) => request<ReviewerTask[]>('/api/reviewer/tasks', userId),
  reviewContext: (userId: string, packageId: string, order: number) =>
    request<ReviewContext>(`/api/reviewer/tasks/${packageId}/steps/${order}`, userId),
  review: (userId: string, packageId: string, order: number, decision: 'Approve' | 'Return', comment?: string) =>
    request(`/api/packages/${packageId}/steps/${order}/review`, userId, {
      method: 'POST', headers: commandHeaders(), body: JSON.stringify({ decision, comment }),
    }),
  prepare: (userId: string, countryCode: string, instructions: string, existingDraft?: string, reviewComment?: string) =>
    request<CopilotResult>('/api/copilot/prepare', userId, {
      method: 'POST', body: JSON.stringify({ countryCode, instructions, existingDraft, reviewComment }),
    }),
  exportDraft: async (userId: string, title: string, draft: string) => {
    const response = await fetch('/api/copilot/export', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-User-Id': userId },
      body: JSON.stringify({ title, draft }),
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null)
      throw new ApiError(problem?.title ?? 'Could not export the draft.', response.status, problem?.code)
    }
    return response.blob()
  },
}
