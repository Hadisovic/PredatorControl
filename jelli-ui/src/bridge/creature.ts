import { request, subscribe, reportError } from './client'
let cursor = {x: 0, y: 0, windowX: 0, windowY: 0, anchorX: 0, anchorY: 0}
let suspended = false
subscribe(m => { if (m.type === 'cursor') cursor = m.data as typeof cursor; if (m.type === 'suspend') suspended = !!m.data })
export const isSuspended = () => suspended || document.hidden
export const getCursorPosition = async () => ({x: cursor.x, y: cursor.y})
export const getWindowPosition = async () => ({x: cursor.windowX, y: cursor.windowY})
export const getAnchorPosition = () => ({x: cursor.anchorX, y: cursor.anchorY})
export const setWindowPosition = (x: number, y: number) => request('move', {x, y})
let lastColor = ''
export function publishColor(h: number, s: number, l: number) {
  const key = `${Math.round(h / 3)}:${Math.round(s / 3)}:${Math.round(l / 3)}`
  if (key === lastColor) return
  lastColor = key
  const css = document.documentElement.style
  css.setProperty('--jelli-hue', String(h)); css.setProperty('--jelli-accent', `hsl(${h} ${s}% ${l}%)`)
  css.setProperty('--jelli-accent-soft', `hsl(${h} ${s}% ${l}% / .12)`)
  const a = s * Math.min(l, 100-l) / 10000
  const f = (n: number) => { const k = (n+h/30)%12; return Math.round(255*(l/100 - a*Math.max(-1, Math.min(k-3, 9-k, 1)))) }
  void request('color', {r: f(0), g: f(8), b: f(4)}).catch(reportError)
}
