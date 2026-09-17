// Delay the single-click action by the OS double-click interval; never flash a summary.
export class ClickArbiter {
  private timer: ReturnType<typeof setTimeout> | null = null
  click(delay: number, single: () => void, double: () => void) {
    if (this.timer !== null) { this.cancel(); double(); return }
    this.timer = setTimeout(() => {this.timer = null; single()}, delay)
  }
  cancel() { if (this.timer !== null) clearTimeout(this.timer); this.timer = null }
}
