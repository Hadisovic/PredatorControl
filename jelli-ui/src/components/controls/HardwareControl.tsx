import { useEffect, useState } from 'react'
import type { Control } from '../../bridge/types'
import { request, reportError } from '../../bridge/client'

export function HardwareControl({control: c}: {control: Control}) {
  const [busy, setBusy] = useState(false)
  const [value, setValue] = useState(c.value)
  const [isPrompting, setIsPrompting] = useState(false)
  const [profileName, setProfileName] = useState('')
  const [savedFeedback, setSavedFeedback] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => setValue(c.value), [c.value])

  const commit = async (v: unknown) => {
    setBusy(true)
    try {
      await request('control', {id: c.id, value: v})
      if (c.id === 'rgb.profile.save') {
        setSavedFeedback(true)
        setTimeout(() => setSavedFeedback(false), 1500)
      }
    } catch (e) {
      reportError(e)
      setValue(c.value)
    } finally {
      setBusy(false)
    }
  }

  const disabled = !c.enabled || busy

  // Custom inline prompt for + New Profile in RGB zones
  if (c.id === 'rgb.profile.new') {
    if (isPrompting) {
      return (
        <form
          className="new-profile-prompt"
          onSubmit={e => {
            e.preventDefault()
            const trimmed = profileName.trim()
            if (trimmed) {
              void commit(trimmed)
              setIsPrompting(false)
              setProfileName('')
            }
          }}
          style={{display: 'flex', gap: '6px', width: '100%', alignItems: 'center', margin: '4px 0'}}
        >
          <input
            autoFocus
            type="text"
            placeholder="Profile name..."
            value={profileName}
            onChange={e => setProfileName(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Escape') {
                setIsPrompting(false)
                setProfileName('')
              }
            }}
            style={{
              flex: 1,
              background: '#101a21',
              border: '1px solid var(--jelli-accent)',
              borderRadius: '8px',
              color: '#e6ecef',
              padding: '7px 10px',
              fontSize: '11px',
              outline: 'none'
            }}
          />
          <button
            type="submit"
            disabled={!profileName.trim() || busy}
            style={{
              background: 'var(--jelli-accent)',
              color: '#0b1018',
              fontWeight: 600,
              borderRadius: '8px',
              padding: '7px 12px',
              border: 'none',
              fontSize: '11px',
              cursor: 'pointer'
            }}
          >
            Save
          </button>
          <button
            type="button"
            onClick={() => {
              setIsPrompting(false)
              setProfileName('')
            }}
            style={{
              background: '#ffffff10',
              color: '#a5b6bd',
              borderRadius: '8px',
              padding: '7px 10px',
              border: '1px solid #ffffff14',
              fontSize: '11px',
              cursor: 'pointer'
            }}
          >
            {'\u2715'}
          </button>
        </form>
      )
    }

    return (
      <button
        className="mode"
        disabled={disabled}
        onClick={() => {
          setIsPrompting(true)
          setProfileName('')
        }}
        style={{
          borderColor: 'var(--jelli-accent)',
          color: 'var(--jelli-accent)',
          fontWeight: 500
        }}
      >
        + New Profile
      </button>
    )
  }

  if (c.id === 'rgb.profile.delete') {
    if (confirmDelete) {
      return (
        <div style={{display: 'flex', gap: '6px', alignItems: 'center'}}>
          <button
            className="mode"
            disabled={disabled}
            onClick={() => {
              void commit(null)
              setConfirmDelete(false)
            }}
            style={{
              borderColor: '#ff4d4d',
              background: '#ff4d4d22',
              color: '#ff4d4d',
              fontWeight: 600
            }}
          >
            Confirm Delete?
          </button>
          <button
            type="button"
            onClick={() => setConfirmDelete(false)}
            style={{
              background: '#ffffff10',
              color: '#a5b6bd',
              borderRadius: '8px',
              padding: '7px 10px',
              border: '1px solid #ffffff14',
              fontSize: '11px',
              cursor: 'pointer'
            }}
          >
            ✕
          </button>
        </div>
      )
    }

    return (
      <button
        className="mode"
        disabled={disabled}
        onClick={() => {
          setConfirmDelete(true)
          setTimeout(() => setConfirmDelete(false), 4000)
        }}
        style={{
          borderColor: '#ff4d4d55',
          color: '#ff6b6b'
        }}
        title="Delete the currently selected profile"
      >
        Delete Profile
      </button>
    )
  }

  if (c.kind === 'button') {
    const label = (c.id === 'rgb.profile.save' && savedFeedback) ? 'Saved \u2713' : c.label
    return (
      <button
        className={`mode ${c.value ? 'selected' : ''}`}
        disabled={disabled}
        aria-pressed={!!c.value}
        onClick={() => void commit(null)}
      >
        {c.accent && <i className="swatch" style={{backgroundColor:c.accent}}/>}
        {label}
      </button>
    )
  }

  if (c.kind === 'toggle') return <button className="toggle-row" role="switch" aria-checked={!!c.value} disabled={disabled} onClick={() => void commit(!c.value)}><span>{c.label}</span><span className={`switch ${c.value ? 'on' : ''}`}><i /></span></button>
  if (c.kind === 'select') return <label className="field"><span>{c.label}</span><select disabled={disabled} value={Number(c.value)} onChange={e => void commit(Number(e.target.value))}>{c.choices?.map(o => <option key={o.value} value={o.value} disabled={!o.enabled}>{o.label}</option>)}</select></label>
  if (c.kind === 'color') return <label className="field"><span>{c.label}</span><input aria-label={c.label} type="color" value={String(value)} disabled={disabled} onChange={e => {setValue(e.target.value); void commit(e.target.value)}} /></label>
  return <label className={`dial-field ${disabled ? 'disabled' : ''}`}><span>{c.label}<strong>{Number(value)}<small>%</small></strong></span><input type="range" min={c.min} max={c.max} value={Number(value)} disabled={disabled} onChange={e => setValue(Number(e.target.value))} onPointerUp={e => void commit(Number(e.currentTarget.value))} onKeyUp={e => {if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown'].includes(e.key)) void commit(Number(e.currentTarget.value))}} /></label>
}
