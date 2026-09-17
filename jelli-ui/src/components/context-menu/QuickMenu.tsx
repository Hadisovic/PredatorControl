import { useState } from 'react'
import type { MenuItem } from '../../bridge/types'
import { send } from '../../bridge/client'
export function QuickMenu({items}: {items: MenuItem[]}) {
  const [open, setOpen] = useState<string | null>(null)
  return <div className="quick-menu" role="menu" aria-label="Predator quick controls" onKeyDown={e => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const buttons = [...e.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]
      const i = buttons.indexOf(document.activeElement as HTMLButtonElement)
      buttons[(i + (e.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length]?.focus()
    }
    if (e.key === 'ArrowLeft') setOpen(null)
  }}>{items.map(item => <div className="menu-group" key={item.id} onMouseEnter={() => setOpen(item.id)}>
    <button role="menuitem" disabled={!item.enabled} aria-haspopup={item.children.length ? 'menu' : undefined} aria-expanded={item.children.length ? open === item.id : undefined} onFocus={() => setOpen(item.id)} onClick={() => item.children.length ? setOpen(item.id) : send('control', {id: item.id, value: null})}>
      <span>{item.checked ? '✓ ' : ''}{item.label}</span><span>{item.children.length ? '›' : ''}</span>
    </button>
    {!!item.children.length && open === item.id && <div className="submenu" role="menu">{item.children.map(child => <button role="menuitemradio" aria-checked={child.checked} disabled={!child.enabled} key={child.id} onClick={() => {send('control', {id: child.id, value: null}); send('surface', 'compact')}}>{child.checked ? '✓ ' : ''}{child.label}</button>)}</div>}
  </div>)}</div>
}
