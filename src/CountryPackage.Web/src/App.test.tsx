import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'

const personas = [
  {
    userId: 'editor-bgd',
    displayName: 'Amina Rahman — Bangladesh Editor',
    role: 'CountryEditor',
    countryScopes: ['BGD'],
    clearance: null,
  },
]

describe('development persona experience', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = String(input)
      const body = path.includes('/api/dev/personas') ? personas : []
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }))
  })

  afterEach(() => vi.unstubAllGlobals())

  it('switches from the persona selector to the Editor workspace', async () => {
    render(<App />)
    fireEvent.click(await screen.findByRole('button', { name: /Amina Rahman/i }))
    expect(await screen.findByText('Packages')).toBeInTheDocument()
    expect(screen.getByText('No packages yet. Create one to initialize its roadmap.')).toBeInTheDocument()
  })
})
