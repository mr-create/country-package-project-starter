import { useCallback, useEffect, useState } from 'react'
import { ApiError, api } from './api'
import type {
  ApprovalStep,
  AuditEntry,
  CopilotResult,
  CountryPackage,
  PackageSummary,
  Persona,
  ReviewContext,
  ReviewerTask,
} from './types'
import { isActionable, statusLabel, stepNames } from './workflow'
import './styles.css'

type Notify = (message: string, kind?: 'success' | 'error') => void

export default function App() {
  const [personas, setPersonas] = useState<Persona[]>([])
  const [user, setUser] = useState<Persona | null>(() => {
    const saved = sessionStorage.getItem('country-package-user')
    return saved ? JSON.parse(saved) : null
  })
  const [notice, setNotice] = useState<{ message: string; kind: 'success' | 'error' } | null>(null)

  const notify: Notify = useCallback((message, kind = 'success') => {
    setNotice({ message, kind })
    window.setTimeout(() => setNotice(null), 4500)
  }, [])

  useEffect(() => {
    api.personas().then(setPersonas).catch((error) => notify(errorMessage(error), 'error'))
  }, [notify])

  const login = (persona: Persona) => {
    sessionStorage.setItem('country-package-user', JSON.stringify(persona))
    setUser(persona)
  }

  const logout = () => {
    sessionStorage.removeItem('country-package-user')
    setUser(null)
  }

  if (!user) return <Login personas={personas} onLogin={login} />

  return (
    <div className="app-shell">
      <header className="topbar">
        <div>
          <span className="eyebrow">Country operations</span>
          <h1>Country Package Workspace</h1>
        </div>
        <div className="identity">
          <div><strong>{user.displayName.split(' — ')[0]}</strong><span>{user.role === 'CountryEditor' ? 'Editor' : `${user.clearance} reviewer`}</span></div>
          <button className="button ghost" onClick={logout}>Switch persona</button>
        </div>
      </header>
      {notice && <div className={`notice ${notice.kind}`}>{notice.message}</div>}
      {user.role === 'CountryEditor'
        ? <EditorDashboard user={user} personas={personas} notify={notify} />
        : <ReviewerDashboard user={user} notify={notify} />}
    </div>
  )
}

function Login({ personas, onLogin }: { personas: Persona[]; onLogin: (persona: Persona) => void }) {
  return (
    <main className="login-page">
      <section className="login-card">
        <span className="eyebrow">Fictional data · Development access</span>
        <h1>Choose a working persona</h1>
        <p>This selector demonstrates country scope, organizational clearance, and assignment rules. Production uses enterprise sign-in.</p>
        <div className="persona-grid">
          {personas.map((persona) => (
            <button className="persona" key={persona.userId} onClick={() => onLogin(persona)}>
              <span className={`avatar ${persona.role === 'CountryEditor' ? 'editor' : 'reviewer'}`}>{persona.displayName[0]}</span>
              <span><strong>{persona.displayName.split(' — ')[0]}</strong><small>{persona.displayName.split(' — ')[1]}</small><small>{persona.countryScopes.join(' · ')}</small></span>
            </button>
          ))}
        </div>
      </section>
    </main>
  )
}

function EditorDashboard({ user, personas, notify }: { user: Persona; personas: Persona[]; notify: Notify }) {
  const [packages, setPackages] = useState<PackageSummary[]>([])
  const [selected, setSelected] = useState<CountryPackage | null>(null)
  const [creating, setCreating] = useState(false)

  const loadPackages = useCallback(() => api.packages(user.userId).then(setPackages).catch((e) => notify(errorMessage(e), 'error')), [notify, user.userId])
  const loadPackage = useCallback((id: string) => api.package(user.userId, id).then(setSelected).catch((e) => notify(errorMessage(e), 'error')), [notify, user.userId])
  useEffect(() => { void loadPackages() }, [loadPackages])

  const refresh = async () => {
    if (selected) await loadPackage(selected.id)
    await loadPackages()
  }

  return (
    <main className="workspace">
      <aside className="sidebar">
        <div className="sidebar-heading"><div><span className="eyebrow">Editor view</span><h2>Packages</h2></div><button className="icon-button" onClick={() => setCreating(true)} aria-label="Create package">+</button></div>
        {packages.length === 0 && <p className="empty-copy">No packages yet. Create one to initialize its roadmap.</p>}
        <div className="package-list">
          {packages.map((item) => (
            <button className={selected?.id === item.id ? 'package-item active' : 'package-item'} key={item.id} onClick={() => loadPackage(item.id)}>
              <span className="country-code">{item.countryCode}</span>
              <span><strong>{item.title}</strong><small>Step {item.currentStepOrder} · {statusLabel[item.currentStepStatus]}</small></span>
            </button>
          ))}
        </div>
      </aside>
      <section className="content">
        {creating && <CreatePackage user={user} onClose={() => setCreating(false)} onCreated={(pkg) => { setCreating(false); setSelected(pkg); loadPackages(); notify('Package roadmap created.') }} />}
        {!selected && !creating && <WelcomePanel onCreate={() => setCreating(true)} />}
        {selected && !creating && <PackageWorkspace key={selected.id} package={selected} user={user} personas={personas} refresh={refresh} notify={notify} />}
      </section>
    </main>
  )
}

function CreatePackage({ user, onClose, onCreated }: { user: Persona; onClose: () => void; onCreated: (pkg: CountryPackage) => void }) {
  const [country, setCountry] = useState(user.countryScopes[0] ?? '')
  const [title, setTitle] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const submit = async (event: React.FormEvent) => {
    event.preventDefault(); setBusy(true); setError('')
    try { onCreated(await api.createPackage(user.userId, country, title)) }
    catch (e) { setError(errorMessage(e)); setBusy(false) }
  }
  return (
    <section className="panel form-panel"><span className="eyebrow">New workflow</span><h2>Create Country Package</h2>
      <form onSubmit={submit}>
        <label>Country<select value={country} onChange={(e) => setCountry(e.target.value)}>{user.countryScopes.map((scope) => <option key={scope}>{scope}</option>)}</select></label>
        <label>Package title<input value={title} maxLength={200} required onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Bangladesh Country Package 2027" /></label>
        {error && <p className="field-error">{error}</p>}
        <div className="button-row"><button className="button ghost" type="button" onClick={onClose}>Cancel</button><button className="button primary" disabled={busy}>{busy ? 'Creating…' : 'Create roadmap'}</button></div>
      </form>
    </section>
  )
}

function WelcomePanel({ onCreate }: { onCreate: () => void }) {
  return <section className="welcome"><div className="welcome-mark">CP</div><span className="eyebrow">Approval workspace</span><h2>Start with a package roadmap</h2><p>Create a country-scoped package, prepare its evidence, and follow the four ordered management steps.</p><button className="button primary" onClick={onCreate}>Create package</button></section>
}

function PackageWorkspace({ package: pkg, user, personas, refresh, notify }: { package: CountryPackage; user: Persona; personas: Persona[]; refresh: () => Promise<void>; notify: Notify }) {
  const [openStep, setOpenStep] = useState(pkg.steps.find((x) => x.status !== 'Completed')?.order ?? 4)
  return (
    <>
      <div className="package-header"><div><span className="eyebrow">{pkg.countryCode} · {pkg.status === 'Completed' ? 'Complete' : 'In progress'}</span><h2>{pkg.title}</h2></div><span className={`status-pill ${pkg.status.toLowerCase()}`}>{pkg.status === 'Completed' ? 'Roadmap complete' : 'Active roadmap'}</span></div>
      <div className="roadmap" aria-label="Package approval roadmap">
        {pkg.steps.map((step) => <button key={step.id} className={`roadmap-step ${step.status.toLowerCase()} ${openStep === step.order ? 'selected' : ''}`} onClick={() => setOpenStep(step.order)}><span className="step-number">{step.status === 'Completed' ? '✓' : step.order}</span><span><strong>{stepNames[step.order - 1]}</strong><small>{statusLabel[step.status]}</small></span></button>)}
      </div>
      {pkg.steps.map((step) => openStep === step.order && <StepWorkspace key={step.id} pkg={pkg} step={step} user={user} personas={personas} refresh={refresh} notify={notify} />)}
    </>
  )
}

function StepWorkspace({ pkg, step, user, personas, refresh, notify }: { pkg: CountryPackage; step: ApprovalStep; user: Persona; personas: Persona[]; refresh: () => Promise<void>; notify: Notify }) {
  const [file, setFile] = useState<File | null>(null)
  const [stakeholder, setStakeholder] = useState(step.reviewerUserId ?? step.recipientUserId ?? '')
  const [audit, setAudit] = useState<AuditEntry[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [copilot, setCopilot] = useState<CopilotResult | null>(null)
  const [instructions, setInstructions] = useState('Summarize the latest country context, priorities, and evidence gaps for management review.')
  const previousComplete = pkg.steps.filter((x) => x.order < step.order).every((x) => x.status === 'Completed')
  const candidates = personas.filter((x) => x.role === 'CountryReviewer' && x.clearance === step.requiredClearance && x.countryScopes.includes(pkg.countryCode))

  const run = async (work: () => Promise<unknown>, success: string) => {
    setBusy(true)
    try { await work(); await refresh(); notify(success) }
    catch (e) { notify(errorMessage(e), 'error') }
    finally { setBusy(false) }
  }

  const upload = () => file && run(() => api.upload(user.userId, pkg.id, step.order, file, file.name), 'Immutable document version uploaded.')
  const submit = () => run(() => api.submit(user.userId, pkg.id, step.order, step.kind === 'Decision' ? { reviewerUserId: stakeholder } : { recipientUserId: stakeholder }), step.kind === 'Decision' ? 'Submitted for review.' : 'Distribution recorded.')
  const prepare = () => run(async () => setCopilot(await api.prepare(user.userId, pkg.countryCode, instructions, copilot?.draft, step.reviewComment ?? undefined)), 'Evidence-grounded draft prepared.')
  const acceptCopilot = () => copilot && run(async () => {
    const blob = await api.exportDraft(user.userId, pkg.title, copilot.draft)
    await api.upload(user.userId, pkg.id, step.order, blob, `${pkg.countryCode}-country-package.docx`, copilot.evidenceManifest)
  }, 'Copilot draft accepted and uploaded as an immutable DOCX version.')
  const loadAudit = async () => { try { setAudit(await api.audit(user.userId, pkg.id, step.order)) } catch (e) { notify(errorMessage(e), 'error') } }
  const download = () => run(async () => saveDownload(await api.download(user.userId, pkg.id, step.order)), 'Document downloaded.')

  return (
    <section className="step-workspace panel">
      <div className="section-heading"><div><span className="eyebrow">Step {step.order} · {step.requiredClearance} level</span><h3>{stepNames[step.order - 1]}</h3></div><span className={`status-pill ${step.status.toLowerCase()}`}>{statusLabel[step.status]}</span></div>
      {!previousComplete && <div className="callout">Complete the preceding roadmap step before working here.</div>}
      {step.reviewComment && <div className="return-comment"><strong>Returned comment</strong><p>{step.reviewComment}</p></div>}
      {previousComplete && step.kind === 'Decision' && isActionable(step) && <div className="action-grid">
        <div className="action-card"><span className="card-index">01</span><h4>Prepare evidence</h4><p>Upload an existing PDF/DOCX, or use the bounded Copilot workspace.</p><input type="file" accept=".pdf,.docx" onChange={(e) => setFile(e.target.files?.[0] ?? null)} /><button className="button secondary" disabled={!file || busy} onClick={upload}>Upload document</button></div>
        <div className="action-card copilot-card"><span className="card-index">AI</span><h4>Evidence Copilot</h4><p>Retrieves only fictional sources authorized for {pkg.countryCode}. You remain responsible for the result.</p><textarea rows={3} value={instructions} onChange={(e) => setInstructions(e.target.value)} /><button className="button secondary" disabled={busy} onClick={prepare}>{copilot ? 'Refine draft' : 'Prepare draft'}</button></div>
      </div>}
      {copilot && <div className="copilot-result"><div className="section-heading"><div><span className="eyebrow">Human review required</span><h4>Grounded draft</h4></div><span className="model-tag">{copilot.evidenceManifest.modelIdentifier}</span></div>
        <p className="copilot-disclosure">{copilot.evidenceManifest.modelIdentifier === 'deterministic-local-poc'
          ? 'Demo mode: with an approved model API configured, AI would generate this editable draft from authorized country evidence and Reviewer feedback. A human must review and accept it before it becomes a document version.'
          : 'This AI-assisted draft was generated from authorized country evidence and Reviewer feedback. A human must review and accept it before it becomes a document version.'}</p>
        <textarea rows={18} value={copilot.draft} onChange={(e) => setCopilot({ ...copilot, draft: e.target.value })} />
        <div className="evidence-strip"><strong>Evidence</strong>{copilot.citations.map((x) => <span key={x}>{x}</span>)}{copilot.warnings.map((x) => <span className="warning" key={x}>{x}</span>)}</div>
        <button className="button primary" disabled={busy} onClick={acceptCopilot}>Accept and upload DOCX</button>
      </div>}
      {previousComplete && isActionable(step) && ((step.kind === 'Decision' && step.draftDocumentVersionId) || step.kind === 'Distribution') && <div className="assignment"><div><span className="eyebrow">{step.kind === 'Decision' ? 'Review assignment' : 'Distribution recipient'}</span><h4>{step.kind === 'Decision' ? 'Select the required reviewer' : 'Select the management recipient'}</h4></div><select value={stakeholder} onChange={(e) => setStakeholder(e.target.value)}><option value="">Select a fictional user…</option>{candidates.map((x) => <option value={x.userId} key={x.userId}>{x.displayName}</option>)}</select><button className="button primary" disabled={!stakeholder || busy} onClick={submit}>{step.kind === 'Decision' ? 'Submit for review' : 'Record distribution'}</button></div>}
      {step.status === 'PendingReview' && <div className="callout">The pinned document snapshot is awaiting {step.reviewerUserId}. It cannot be changed while review is pending.</div>}
      {step.status === 'Completed' && <div className="completion"><strong>Step completed</strong><span>{step.completedAt ? formatDate(step.completedAt) : ''}</span><button className="button ghost" onClick={download}>Download recorded document</button></div>}
      <div className="audit-section"><button className="text-button" onClick={loadAudit}>{audit ? 'Refresh audit history' : 'View audit history'}</button>{audit && <AuditList entries={audit} />}</div>
    </section>
  )
}

function ReviewerDashboard({ user, notify }: { user: Persona; notify: Notify }) {
  const [tasks, setTasks] = useState<ReviewerTask[]>([])
  const [context, setContext] = useState<ReviewContext | null>(null)
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const loadTasks = useCallback(() => api.tasks(user.userId).then(setTasks).catch((e) => notify(errorMessage(e), 'error')), [notify, user.userId])
  useEffect(() => { void loadTasks() }, [loadTasks])
  const open = async (task: ReviewerTask) => { try { setContext(await api.reviewContext(user.userId, task.packageId, task.stepOrder)) } catch (e) { notify(errorMessage(e), 'error') } }
  const decide = async (decision: 'Approve' | 'Return') => {
    if (!context) return
    setBusy(true)
    try { await api.review(user.userId, context.packageId, context.step.order, decision, comment); setContext(null); setComment(''); await loadTasks(); notify(decision === 'Approve' ? 'Decision approved.' : 'Package returned to the Editor.') }
    catch (e) { notify(errorMessage(e), 'error') }
    finally { setBusy(false) }
  }
  const download = async () => {
    if (!context) return
    try { saveDownload(await api.download(user.userId, context.packageId, context.step.order)) }
    catch (e) { notify(errorMessage(e), 'error') }
  }
  return (
    <main className="reviewer-layout"><section className="review-list panel"><span className="eyebrow">Reviewer view</span><h2>My Reviews</h2><p>Only assigned pending decisions are visible.</p>{tasks.length === 0 && <div className="empty-state">No decisions are waiting for you.</div>}{tasks.map((task) => <button className="review-task" key={`${task.packageId}-${task.stepOrder}`} onClick={() => open(task)}><span className="country-code">{task.countryCode}</span><span><strong>{task.title}</strong><small>{stepNames[task.stepOrder - 1]} · submitted {formatDate(task.submittedAt)}</small></span><span>→</span></button>)}</section>
      <section className="review-detail">{!context ? <div className="welcome compact"><div className="welcome-mark">✓</div><h2>Select an assigned decision</h2><p>The exact pinned snapshot and its provenance will appear here.</p></div> : <div className="panel"><div className="section-heading"><div><span className="eyebrow">{context.countryCode} · Step {context.step.order}</span><h2>{context.title}</h2></div><span className="status-pill pendingreview">Pending review</span></div><div className="snapshot"><div><strong>{context.snapshot.fileName}</strong><small>SHA-256 {context.snapshot.sha256.slice(0, 16)}…</small><small>Uploaded {formatDate(context.snapshot.uploadedAt)}</small></div><button className="button secondary" onClick={download}>Download snapshot</button></div>{context.snapshot.evidenceManifest && <div className="manifest"><span className="eyebrow">AI provenance</span><strong>Accepted by {context.snapshot.evidenceManifest.acceptedBy}</strong><p>{context.snapshot.evidenceManifest.sourceReferences.join(' · ')}</p></div>}<label>Return comment<textarea rows={4} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Required only when returning the package" /></label><div className="button-row"><button className="button danger" disabled={busy || !comment.trim()} onClick={() => decide('Return')}>Return for revision</button><button className="button primary" disabled={busy} onClick={() => decide('Approve')}>Approve snapshot</button></div><div className="audit-section"><h4>Step history</h4><AuditList entries={context.audit} /></div></div>}</section></main>
  )
}

function AuditList({ entries }: { entries: AuditEntry[] }) {
  return <ol className="audit-list">{entries.map((entry) => {
    const detail = typeof entry.details.comment === 'string' ? entry.details.comment
      : typeof entry.details.fileName === 'string' ? entry.details.fileName
      : typeof entry.details.reviewerUserId === 'string' ? `Assigned to ${entry.details.reviewerUserId}`
      : typeof entry.details.recipientUserId === 'string' ? `Sent to ${entry.details.recipientUserId}`
      : null
    return <li key={entry.id}><span className="audit-dot" /><div><strong>{humanize(entry.action)}</strong><small>{entry.actorUserId} · {formatDate(entry.occurredAt)}</small>{detail && <p>{detail}</p>}</div></li>
  })}</ol>
}

const humanize = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2')
const formatDate = (value: string) => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
const errorMessage = (error: unknown) => error instanceof ApiError || error instanceof Error ? error.message : 'An unexpected error occurred.'
const saveDownload = ({ blob, fileName }: { blob: Blob; fileName: string }) => {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
