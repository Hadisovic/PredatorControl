export type Surface = 'compact' | 'summary' | 'dashboard' | 'menu'
export interface Preferences {
  presentation: 'expand' | 'flyout'; metric1: string; metric2: string; overlayCorner: string;
  overlayMonitor: string; overlayMode?: 'light' | 'default' | 'full' | 'complete'; overlayScale?: number;
  overlayGraphs: boolean; overlayPower: boolean; overlayUsage: boolean; syncRgb: boolean; jelliEnabled?: boolean; x: number; y: number
}
export interface Control {
  id: string; label: string; kind: 'button' | 'toggle' | 'range' | 'select' | 'color'; value: string | number | boolean | null;
  enabled: boolean; choices: { label: string; value: number; enabled: boolean }[] | null; min: number; max: number; accent?: string | null
}
export interface MenuItem { id: string; label: string; enabled: boolean; checked: boolean; children: MenuItem[] }
export interface OperationResult {status: 'confirmed' | 'failed' | 'unverified'; message: string}
export interface State {
  device?: string; theme?: 'PredatorTeal' | 'NitroCrimson' | 'OledBlack';
  keyboard?: {kind: 'unknown' | 'binary' | 'rgb' | 'none'; supportsRgb: boolean; fixedColor?: string | null; supportsBrightness: boolean; evidence: string; notice: string};
  telemetry: Record<string, number | boolean | null> | null; powerMode: string; fanMode: string; gpuMode: string;
  gpuNotice: string; fps: number | null; overlay: boolean; overlayMode?: 'light' | 'default' | 'full' | 'complete'; overlayScale?: number;
  sections: {id: string; label: string; controls: Control[]}[];
  menu: MenuItem[]; settings: Preferences; monitors: {id: string; label: string}[]; version: string; revision?: number; doubleClickMs: number; gameSyncStatus: string; batteryNotice?: string;
}
export interface Layout {surface: Surface; creatureX: number; creatureY: number; flyout: boolean; panelLeft: number}
