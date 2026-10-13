import type { State, Preferences, Surface } from './types'
type Actions = {
  connect: {data: null; result: State}; surface: {data: Surface; result: void};
  move: {data: {x: number; y: number}; result: void}; savePosition: {data: null; result: void};
  control: {data: {id: string; value: unknown}; result: void}; settings: {data: Preferences; result: void};
  color: {data: {r: number; g: number; b: number}; result: void}; overlay: {data: boolean; result: void};
  legacy: {data: null; result: void}; exit: {data: null; result: void};
  'overlay.settings': {data: null; result: void}
}
type Message = {version: number; type: string; id?: string; data?: unknown; error?: string}
interface WebView { postMessage: (message: unknown) => void; addEventListener: (type: 'message', handler: (e: MessageEvent<Message>) => void) => void }
declare global { interface Window { chrome?: {webview?: WebView} } }
const listeners = new Set<(message: Message) => void>()
let sequence = 0
const pending = new Map<string, {resolve: (v: unknown) => void; reject: (e: Error) => void; timer: ReturnType<typeof setTimeout>}>()
const webview = window.chrome?.webview
webview?.addEventListener('message', ({data: m}) => {
  if (m.version !== 1) return
  if (m.type === 'response' && m.id) {
    const p = pending.get(m.id)
    if (p) { clearTimeout(p.timer); pending.delete(m.id); if (m.error) p.reject(new Error(m.error)); else p.resolve(m.data) }
  } else listeners.forEach(fn => fn(m))
})
export function subscribe(fn: (message: Message) => void) { listeners.add(fn); return () => {listeners.delete(fn)} }
export function request<K extends keyof Actions>(action: K, data: Actions[K]['data'] = null as Actions[K]['data']): Promise<Actions[K]['result']> {
  if (!webview) return Promise.reject(new Error('Open Predator Control to connect to your hardware.'))
  const id = String(++sequence)
  return new Promise<Actions[K]['result']>((resolve, reject) => {
    const timer = setTimeout(() => {pending.delete(id); reject(new Error('The backend did not respond. Reconnect to refresh the current state.'))}, action === 'control' ? 600000 : 15000)
    pending.set(id, {resolve: v => resolve(v as Actions[K]['result']), reject, timer})
    webview.postMessage({version: 1, id, action, data})
  })
}
export const connect = () => request('connect')
export function reportError(error: unknown) { window.dispatchEvent(new CustomEvent('jelli-error', {detail: error instanceof Error ? error.message : String(error)})) }
export function send<K extends keyof Actions>(action: K, data: Actions[K]['data'] = null as Actions[K]['data']) { void request(action, data).catch(reportError) }
