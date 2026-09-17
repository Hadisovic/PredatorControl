export type Surface = 'compact' | 'summary' | 'dashboard' | 'menu'
export interface Preferences {
  presentation: 'expand' | 'flyout'; metric1: string; metric2: string; overlayCorner: string;
  overlayMonitor: string; overlayGraphs: boolean; overlayPower: boolean; overlayUsage: boolean; syncRgb: boolean; x: number; y: number
}
export interface Control {
  id: string; label: string; kind: 'button' | 'toggle' | 'range' | 'select' | 'color'; value: string | number | boolean;
  enabled: boolean; choices: { label: string; value: number; enabled: boolean }[] | null; min: number; max: number; accent?: string | null
}
export interface MenuItem { id: string; label: string; enabled: boolean; checked: boolean; children: MenuItem[] }
export interface State {
  telemetry: Record<string, number | boolean | null> | null; powerMode: string; fanMode: string; gpuMode: string;
  gpuNotice: string; fps: number | null; overlay: boolean; sections: {id: string; label: string; controls: Control[]}[];
  menu: MenuItem[]; settings: Preferences; monitors: {id: string; label: string}[]; version: string; doubleClickMs: number; gameSyncStatus: string
}
export interface Layout {surface: Surface; creatureX: number; creatureY: number; flyout: boolean; panelLeft: number}
