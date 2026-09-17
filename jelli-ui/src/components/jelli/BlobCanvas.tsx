// Adapted from Hadisovic/Jelli master c493a7933cd41ae3feaf08f9136d5cc112e0ccc4.
// Anatomy, physics and autonomous expression choreography remain upstream code.
import { useEffect, useRef, useCallback } from 'react'
import { getWindowPosition, getAnchorPosition, setWindowPosition, getCursorPosition, isSuspended, publishColor } from '../../bridge/creature'
import { send } from '../../bridge/client'
const BLOB = { SIZE: 90, BREATH_PERIOD_MS: 3800, HUE_SPEED: 15 } as const
const DRAG_THRESHOLD = 6
const FRAME_SECONDS = 1 / 60
const MAX_FRAME_SECONDS = 1 / 30
const RAGE_HOLD_MS = 2500
const RAGE_DIZZY_SECONDS = 1.6
const RAGE_MORPH_SECONDS = 0.4
const RAGE_LOOP_MIN_SECONDS = 5.6
const RAGE_LOOP_VARIANCE_SECONDS = 1.4
const RAGE_OUTRO_SECONDS = 0.8
const SLEEP_IDLE_MS = 8000
const SLEEP_INTRO_SECONDS = 1.5
const SLEEP_OUTRO_SECONDS = 0.9
const MODE_TRANSITION_SECONDS = 0.12

type HslColor = { h: number; s: number; l: number }

// ── Expressions ──────────────────────────────────────────────────────────
type Expression = 'idle' | 'annoyed' | 'dizzy' | 'sleepy' | 'happy' | 'surprised' | 'shy' | 'mad' | 'sad'

// ── Jellyfish anatomy ────────────────────────────────────────────────────
const BELL_SEGS = 80
const BELL_W = 32
const BELL_H = 24
const BELL_BASE_Y = 38
const TENT_COUNT = 4
const TENT_SEGS = 12
const TENT_LEN = 32

// ── Tentacle ─────────────────────────────────────────────────────────────
interface TPoint { x: number; y: number; vx: number; vy: number }
interface Tentacle {
  pts: TPoint[]
  sx: number
  phase: number
  speed: number
  amp: number
  damp: number
  width: number
}

function makeTentacles(): Tentacle[] {
  return Array.from({ length: TENT_COUNT }, (_, i) => {
    const spread = 22
    const sx = -spread / 2 + (i / (TENT_COUNT - 1)) * spread
    const pts: TPoint[] = Array.from({ length: TENT_SEGS }, (_, j) => ({
      x: sx,
      y: BELL_BASE_Y + (j / TENT_SEGS) * TENT_LEN,
      vx: 0,
      vy: 0,
    }))
    return {
      pts, sx,
      phase: i * 1.3 + Math.random() * 0.5,
      speed: 0.5 + Math.random() * 0.4,
      amp: 1.5 + Math.random() * 1.2,
      damp: 0.90 + Math.random() * 0.05,
      width: 3.5 + Math.random() * 2.0,
    }
  })
}

// ── Bioluminescent organs ────────────────────────────────────────────────
interface Organ {
  bx: number; by: number
  size: number; bright: number
  phase: number; speed: number
}

function makeOrgans(): Organ[] {
  return [
    { bx: -8, by: -5, size: 4.5, bright: 1, phase: 0, speed: 1.1 },
    { bx: 6, by: -3, size: 3.5, bright: 0.8, phase: 1.8, speed: 0.9 },
    { bx: -2, by: 2, size: 3.0, bright: 0.7, phase: 3.2, speed: 1.0 },
    { bx: 10, by: -1, size: 2.2, bright: 0.55, phase: 4.5, speed: 0.8 },
    { bx: -11, by: 0, size: 2.0, bright: 0.45, phase: 2.5, speed: 1.2 },
  ]
}

// ── Bell path ────────────────────────────────────────────────────────────
function traceBell(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  sx: number,
  sy: number,
  time: number,
) {
  ctx.beginPath()
  for (let i = 0; i <= BELL_SEGS; i++) {
    const a = (i / BELL_SEGS) * Math.PI * 2
    const ca = Math.cos(a)
    const sa = Math.sin(a)
    let bx: number, by: number

    if (sa <= 0) {
      const wobble = Math.sin(a * 3 + time * 0.5) * 0.6
      bx = cx + ca * (BELL_W + wobble) * sx
      by = cy + sa * BELL_H * sy
    } else {
      const rimTuck = 1 - Math.pow(ca, 2) * 0.1
      const rimFlare = Math.pow(Math.abs(ca), 0.5) * 3
      bx = cx + ca * BELL_W * rimTuck * sx
      by = cy + sa * BELL_H * sy + rimFlare
    }

    if (i === 0) ctx.moveTo(bx, by)
    else ctx.lineTo(bx, by)
  }
  ctx.closePath()
}

// ── Color Interpolation ──────────────────────────────────────────────────
function lerpAngle(current: number, target: number, speed: number) {
  let diff = (target - current) % 360
  if (diff > 180) diff -= 360
  if (diff < -180) diff += 360
  return (current + diff * speed + 360) % 360
}

function lerpValue(current: number, target: number, speed: number) {
  return current + (target - current) * speed
}

function clamp(value: number, min: number, max: number) {
  return Math.max(min, Math.min(max, value))
}

function clamp01(value: number) {
  return clamp(value, 0, 1)
}

function frameLerp(speedAt60Fps: number, dt: number) {
  return 1 - Math.pow(1 - speedAt60Fps, dt / FRAME_SECONDS)
}

function tickTimer(seconds: number, dt: number) {
  return Math.max(0, seconds - dt)
}

function easeOutCubic(t: number) {
  const p = 1 - clamp01(t)
  return 1 - p * p * p
}

function mixHue(from: number, to: number, amount: number) {
  let diff = (to - from) % 360
  if (diff > 180) diff -= 360
  if (diff < -180) diff += 360
  return (from + diff * clamp01(amount) + 360) % 360
}

function mixHsl(from: HslColor, to: HslColor, amount: number): HslColor {
  const t = clamp01(amount)
  return {
    h: mixHue(from.h, to.h, t),
    s: lerpValue(from.s, to.s, t),
    l: lerpValue(from.l, to.l, t),
  }
}

// ── Component ───────────────────────────────────────────────────────────
export function BlobCanvas({onActivate, onContext, onDrag}: {onActivate: () => void; onContext: () => void; onDrag: () => void}) {
  const callbacks = useRef({onActivate, onContext, onDrag})
  callbacks.current = {onActivate, onContext, onDrag}
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const draggingStateRef = useRef(false)
  const dragCleanup = useRef<(() => void) | null>(null)
  useEffect(() => () => dragCleanup.current?.(), [])
  const isPointerDownRef = useRef(false)
  const isDraggingRef = useRef(false)
  const didDragRef = useRef(false)
  const startScreenRef = useRef<{ x: number; y: number } | null>(null)
  const startWindowRef = useRef<{ x: number; y: number } | null>(null)

  // Eye state
  const eyeTargetRef = useRef({ x: 0, y: 0 })
  const eyePosRef = useRef({ x: 0, y: 0 })
  const blinkTimerRef = useRef(0)
  const nextBlinkThresholdRef = useRef(12)
  const isBlinkingRef = useRef(false)
  const blinkPhaseRef = useRef(0)
  // Drag velocity for ocean jellyfish lean
  const dragVelRef = useRef({ x: 0, y: 0 })
  // Smooth drag blend: 0 = normal wave, 1 = full jellyfish lean
  const dragBlendRef = useRef(0)
  // Smoothed squint
  const squintCurrentRef = useRef(0)
  // Staged post-drag transitions
  const postDragDizzyRef = useRef(0)   // 1.6s dizzy buffer after drag release
  // Mad cooldown
  const madCooldownRef = useRef(0)
  const prevDraggingRef = useRef(false)
  // Happy: ref so it persists across effect re-runs (drag state changes re-run the effect)
  const happyCooldownRef = useRef(0)
  const happyTotalRef = useRef(0)
  // Blocks happy from triggering for 30s after a rage phase ends
  const madRecoveryRef = useRef(0)
  // Petting detection: horizontal wiggles near the blob
  const petScoreRef = useRef(0)
  const lastSignRef = useRef(0)
  const lastPetTimeRef = useRef(0)
  // Color transition states (lerping color gradient pairs)
  const color1Ref = useRef({ h: 180, s: 70, l: 65 })
  const color2Ref = useRef({ h: 230, s: 60, l: 50 })
  // Custom spin angle and smooth rage transitions
  const dizzySpinAngleRef = useRef(0)
  const madAlphaRef = useRef(0)
  const happyAlphaRef = useRef(0)
  // Transition choreography
  const prevExpressionRef = useRef<Expression>('idle')
  const transitionTimerRef = useRef(1)  // start at 1 (no transition pending)
  const popRef = useRef(0)
  // Sleep choreography: 5-second intro + 3-second outro
  const sleepTransitionRef = useRef(0)
  // Sleep outro: 3-second wake-up
  const sleepOutroRef = useRef(0)
  // Rage sequence lock: true from drag-start until mad outro finishes
  const ragSequenceActiveRef = useRef(false)
  // Dizzy→Mad transition: 1.0s cross-fade
  const dizzyToMadRef = useRef(0)
  // Mad outro timer: 3.0s after mad loop ends
  const madOutroTimerRef = useRef(0)
  // ── Sad Lockout Mode state refs ──────────────────────────────────
  const clickTimesRef = useRef<number[]>([])
  const sadLockoutTimerRef = useRef(0)
  const sadModeActiveRef = useRef(false)
  const sadLeanAlphaRef = useRef(0)

  // Morphable eye geometry states
  const eyeMorphRxRef = useRef(5.5)
  const eyeMorphRyTopRef = useRef(5.5)
  const eyeMorphRyBotRef = useRef(5.5)
  const eyeMorphRotRef = useRef(0)
  const eyeMorphIsStrokeRef = useRef(0)
  const eyeMorphYOffRef = useRef(0)
  const eyeMorphPXRef = useRef(0)
  const eyeMorphPYRef = useRef(0)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    let raf: number
    let lastFrameTime = 0
    let nextDrawTime = 0
    let hueSyncAccumulator = 0
    let cursorPollInFlight = false
    const w = 140
    const h = 160
    const cx = w / 2
    const cy = 50

    const tents = makeTentacles()
    const organs = makeOrgans()
    nextBlinkThresholdRef.current = 8 + Math.random() * 8

    // ── Expression tracking ────────────────────────────────────────
    let dragStartTime = 0
    let lastInteractionTime = Date.now()
    let lastMouseTime = Date.now()
    let mouseSpeed = 0
    let mouseNearBlob = false
    // happyCooldown is a ref (not a local let) so it survives effect re-runs
    let dizzyStars: { angle: number; dist: number; speed: number }[] = []
    let dizzyWobble = 0

    // Track system cursor for eye direction (works even outside window)
    let blobScreenX = 0
    let blobScreenY = 0
    let lastCursorX = 0
    let lastCursorY = 0

    // Get initial blob position
    getWindowPosition().then((wp) => {
      blobScreenX = wp.x + cx
      blobScreenY = wp.y + cy
    }).catch(() => {})

    // Poll system cursor at ~60fps
    const cursorInterval = setInterval(() => {
      if (cursorPollInFlight || isSuspended()) return
      cursorPollInFlight = true
      Promise.all([getCursorPosition(), getWindowPosition()]).then(([cursor, wp]) => {
        const dragging = draggingStateRef.current
        blobScreenX = wp.x + cx
        blobScreenY = wp.y + cy

        const dx = cursor.x - blobScreenX
        const dy = cursor.y - blobScreenY
        const dist = Math.sqrt(dx * dx + dy * dy)
        const maxShift = 3.5
        if (dist > 0) {
          eyeTargetRef.current = {
            x: (dx / dist) * Math.min(maxShift, dist * 0.05),
            y: (dy / dist) * Math.min(maxShift, dist * 0.05),
          }
        }

        // Mouse speed tracking
        const now = Date.now()
        const dt = (now - lastMouseTime) / 1000
        let moved = false
        if (dt > 0) {
          const dmx = cursor.x - lastCursorX
          const dmy = cursor.y - lastCursorY
          mouseSpeed = Math.sqrt(dmx * dmx + dmy * dmy) / dt
          if (Math.abs(dmx) > 0.5 || Math.abs(dmy) > 0.5) {
            moved = true
          }

          // Smooth drag velocity for broom-trail (only care when dragging)
          if (dragging) {
            dragVelRef.current.x = dragVelRef.current.x * 0.6 + dmx * 0.4
            dragVelRef.current.y = dragVelRef.current.y * 0.6 + dmy * 0.4
          } else {
            // Decay back to zero when not dragging
            dragVelRef.current.x *= 0.8
            dragVelRef.current.y *= 0.8
          }

          // Petting detection: hover over it left and right wiggles
          // Only checks if not currently dragging, close to the blob, and rage sequence is inactive
          if (!dragging && dist < 85 && !ragSequenceActiveRef.current && madRecoveryRef.current <= 0) {
            if (Math.abs(dmx) > 1.0) {
              const currentSign = dmx > 0 ? 1 : -1
              if (lastSignRef.current !== currentSign) {
                petScoreRef.current += 1
                lastPetTimeRef.current = now
                lastSignRef.current = currentSign
              }
            }
          }
        }

        // Decay petting score if no patting happens for 1.2s
        if (now - lastPetTimeRef.current > 1200) {
          petScoreRef.current = 0
          lastSignRef.current = 0
        }

        // Trigger happy mode if enough alternating pats are detected
        if (petScoreRef.current >= 4) {
          petScoreRef.current = 0
          lastSignRef.current = 0
          const happyDuration = 60 + Math.random() * 60
          happyCooldownRef.current = happyDuration
          happyTotalRef.current = happyDuration
        }

        lastCursorX = cursor.x
        lastCursorY = cursor.y
        lastMouseTime = now
        // Sleep mode activation fix: only update lastInteractionTime on actual mouse movement or drag
        if (moved || dragging) {
          lastInteractionTime = now
        }

        // Is mouse near blob?
        mouseNearBlob = dist < 120
      }).catch(() => {}).finally(() => {
        cursorPollInFlight = false
      })
    }, 32)

    const draw = (time: number) => {
      if (isSuspended()) { lastFrameTime = 0; nextDrawTime = 0; raf = requestAnimationFrame(draw); return }
      // High-refresh displays otherwise render this small canvas at 165/240 Hz.
      // Keep upstream time-delta physics while limiting actual drawing to 60 Hz.
      if (time < nextDrawTime) { raf = requestAnimationFrame(draw); return }
      nextDrawTime = nextDrawTime === 0 ? time + 1000 / 60 : Math.max(nextDrawTime + 1000 / 60, time)

      ctx.setTransform(1, 0, 0, 1, 0, 0)
      ctx.clearRect(0, 0, w, h)
      const t = time / 1000
      const dt = lastFrameTime === 0
        ? FRAME_SECONDS
        : clamp((time - lastFrameTime) / 1000, 0.001, MAX_FRAME_SECONDS)
      lastFrameTime = time
      const dragging = draggingStateRef.current

      // ── Expression determination ─────────────────────────────────
      const now = Date.now()
      const idleMs = now - lastInteractionTime

      // Calculate how long we've been dragging up to this moment
      // (do not clear until after checking drag-release staged transitions)
      const dragMs = dragStartTime === 0 ? 0 : now - dragStartTime

      // Detect drag-release → locked rage sequence
      if (prevDraggingRef.current && !dragging) {
        if (dragMs > RAGE_HOLD_MS || ragSequenceActiveRef.current) {
          postDragDizzyRef.current = RAGE_DIZZY_SECONDS
          dizzyToMadRef.current = 0
          madCooldownRef.current = 0
          madOutroTimerRef.current = 0
          ragSequenceActiveRef.current = true
        } else {
          postDragDizzyRef.current = 0
          dizzyToMadRef.current = 0
          madCooldownRef.current = 0
          madOutroTimerRef.current = 0
          ragSequenceActiveRef.current = false  // short drag: no rage
        }
        happyCooldownRef.current = 0       // cancel happy on drag release
        happyTotalRef.current = 0
      }
      prevDraggingRef.current = dragging

      // Start or stop drag timer
      if (dragging) {
        if (dragStartTime === 0) {
          dragStartTime = now
          happyCooldownRef.current = 0 // cancel happy if dragged
          happyTotalRef.current = 0
        }
      } else {
        dragStartTime = 0
      }

      // ── Tick down sad lockout timer ──────────────────────────────
      if (sadLockoutTimerRef.current > 0) {
        sadLockoutTimerRef.current = tickTimer(sadLockoutTimerRef.current, dt)
      }

      // ── Expression state machine ─────────────────────────────────
      let expression: Expression = 'idle'
      let ragePhase: 'none' | 'dizzyBuffer' | 'dizzyToMad' | 'madLoop' | 'madOutro' = 'none'

      if (sadModeActiveRef.current) {
        // Sad Lockout Mode is active!
        if (sadLockoutTimerRef.current > 0) {
          // STRICT 5-SECOND LOCKOUT: un-interruptible sad mode
          expression = 'sad'
          ragSequenceActiveRef.current = false
          happyCooldownRef.current = 0
          happyTotalRef.current = 0
          petScoreRef.current = 0

        } else {
          // Post-5s: still sad, but now responsive to patting!
          expression = 'sad'
          if (petScoreRef.current >= 4) {
            // User patted Jelli after the 5s cooldown!
            sadModeActiveRef.current = false
            petScoreRef.current = 0
            lastSignRef.current = 0
            happyCooldownRef.current = 4.0  // Trigger 4s happy mode
            happyTotalRef.current = 4.0
          }
        }
      } else if (dragging) {
        if (dragMs > RAGE_HOLD_MS) {
          expression = 'dizzy'
          ragSequenceActiveRef.current = true  // LOCK the rage sequence
        } else {
          expression = 'annoyed'
        }
      } else if (ragSequenceActiveRef.current) {
        if (postDragDizzyRef.current > 0) {
          ragePhase = 'dizzyBuffer'
          expression = 'dizzy'
          postDragDizzyRef.current = tickTimer(postDragDizzyRef.current, dt)
          if (postDragDizzyRef.current <= 0) {
            postDragDizzyRef.current = 0
            dizzyToMadRef.current = RAGE_MORPH_SECONDS
          }
        } else if (dizzyToMadRef.current > 0) {
          ragePhase = 'dizzyToMad'
          expression = 'mad'
          dizzyToMadRef.current = tickTimer(dizzyToMadRef.current, dt)
          if (dizzyToMadRef.current <= 0) {
            dizzyToMadRef.current = 0
            const dur = RAGE_LOOP_MIN_SECONDS + Math.random() * RAGE_LOOP_VARIANCE_SECONDS
            madCooldownRef.current = dur
          }
        } else if (madCooldownRef.current > 0) {
          ragePhase = 'madLoop'
          expression = 'mad'
          madCooldownRef.current = tickTimer(madCooldownRef.current, dt)
          if (madCooldownRef.current <= 0) {
            madCooldownRef.current = 0
            madOutroTimerRef.current = RAGE_OUTRO_SECONDS
          }
        } else if (madOutroTimerRef.current > 0) {
          ragePhase = 'madOutro'
          expression = 'mad'
          madOutroTimerRef.current = tickTimer(madOutroTimerRef.current, dt)
          if (madOutroTimerRef.current <= 0) {
            madOutroTimerRef.current = 0
            ragSequenceActiveRef.current = false  // UNLOCK — normal mode resumes
            madRecoveryRef.current = 30
          }
        } else {
          // Safety: should not reach here, but unlock just in case
          ragSequenceActiveRef.current = false
        }
      } else if (happyCooldownRef.current > 0) {
        happyCooldownRef.current = tickTimer(happyCooldownRef.current, dt)
        if (happyCooldownRef.current <= 0) {
          happyTotalRef.current = 0
        }
        expression = 'happy'
      } else if (idleMs > SLEEP_IDLE_MS) {
        expression = 'sleepy'
      } else if (mouseSpeed > 800 && mouseNearBlob) {
        expression = 'surprised'
      } else if (mouseNearBlob) {
        expression = 'shy'
      }

      // ── Sleep choreography: 5-second intro + 3-second outro ────
      let sleepProgress = 0
      const canSleep = !dragging
        && !ragSequenceActiveRef.current
        && happyCooldownRef.current <= 0
        && !mouseNearBlob
      const sleepBlockedByPriority = dragging
        || ragSequenceActiveRef.current
        || happyCooldownRef.current > 0
      if (sleepBlockedByPriority) {
        sleepTransitionRef.current = 0
        sleepOutroRef.current = 0
      }
      if (canSleep && idleMs > SLEEP_IDLE_MS && sleepTransitionRef.current < SLEEP_INTRO_SECONDS) {
        sleepTransitionRef.current = Math.min(SLEEP_INTRO_SECONDS, sleepTransitionRef.current + dt)
        expression = 'idle'  // keep idle visuals during intro choreography
      }
      if (sleepTransitionRef.current >= SLEEP_INTRO_SECONDS) {
        expression = 'sleepy'
        sleepProgress = 1
      } else if (sleepTransitionRef.current > 0) {
        sleepProgress = sleepTransitionRef.current / SLEEP_INTRO_SECONDS
      }
      // Reset sleep transition on any user interaction → start 3s outro
      if ((!canSleep || idleMs < SLEEP_IDLE_MS) && sleepTransitionRef.current > 0) {
        sleepTransitionRef.current = Math.max(0, sleepTransitionRef.current - dt * (SLEEP_INTRO_SECONDS / SLEEP_OUTRO_SECONDS))
        sleepOutroRef.current = Math.min(SLEEP_OUTRO_SECONDS, sleepOutroRef.current + dt)
        expression = 'sleepy'  // keep sleepy visuals during outro choreography
      } else if (sleepOutroRef.current > 0 && sleepTransitionRef.current <= 0) {
        sleepOutroRef.current = Math.max(0, sleepOutroRef.current - dt)
      }
      const sleepOutro = sleepOutroRef.current / SLEEP_OUTRO_SECONDS

      const dizzyBuffer = ragePhase === 'dizzyBuffer'
      const inTransition = ragePhase === 'dizzyToMad'
      const inMadOutro = ragePhase === 'madOutro'
      const dizzyToMadProgress = inTransition
        ? 1 - dizzyToMadRef.current / RAGE_MORPH_SECONDS
        : (ragePhase === 'madLoop' || ragePhase === 'madOutro' ? 1 : 0)
      const madVisualStrength = inTransition
        ? easeOutCubic(dizzyToMadProgress)
        : ragePhase === 'madLoop'
          ? 1
          : inMadOutro
            ? madOutroTimerRef.current / RAGE_OUTRO_SECONDS
            : 0

      // Update happyAlpha for smooth mouth transitions
      const happyAlphaTarget = expression === 'happy' ? 1 : 0
      happyAlphaRef.current += (happyAlphaTarget - happyAlphaRef.current) * frameLerp(0.35, dt)
      const happyAlpha = happyAlphaRef.current

      // Decay the post-mad recovery timer
      if (madRecoveryRef.current > 0) madRecoveryRef.current = tickTimer(madRecoveryRef.current, dt)

      if (expression === 'idle'
        && !dragging
        && !ragSequenceActiveRef.current
        && madRecoveryRef.current <= 0
        && happyCooldownRef.current <= 0
        && Math.random() < 0.00001) {
        const happyDuration = 60 + Math.random() * 60
        happyCooldownRef.current = happyDuration
        happyTotalRef.current = happyDuration
        expression = 'happy'
      }

      // ── Transition choreography ─────────────────────────────────
      const prevExpressionSaved = prevExpressionRef.current  // save before overwriting
      if (expression !== prevExpressionSaved) {
        transitionTimerRef.current = 0
        popRef.current = 0.12  // small scale pulse on mode change

        // SFX on expression change — disabled, re-enable when SFX system is active
      }
      prevExpressionRef.current = expression
      if (transitionTimerRef.current < 1) {
        transitionTimerRef.current = Math.min(1, transitionTimerRef.current + dt / MODE_TRANSITION_SECONDS)
      }
      // Pop decay
      if (popRef.current > 0.001) {
        popRef.current *= Math.pow(0.85, dt / FRAME_SECONDS)
      } else {
        popRef.current = 0
      }
      const transitionBlend = transitionTimerRef.current  // 0→1 over ~0.4s

      // ── Target Colors for each mode (blended by intro/outro) ───
      const idle1 = { h: 180, s: 70, l: 65 }
      const idle2 = { h: 230, s: 60, l: 50 }
      let target1: HslColor
      let target2: HslColor

      if (expression === 'sad') {
        // Sad mode: subdued slate-indigo & deep blue-violet
        target1 = { h: 220, s: 45, l: 52 }
        target2 = { h: 250, s: 40, l: 38 }
      } else if (dragging || dizzyBuffer) {
        // Dragged/dizzy-buffer mode — LOCKED, no flash on release
        target1 = { h: 195, s: 75, l: 65 }
        target2 = { h: 270, s: 70, l: 50 }
      } else if (inTransition) {
        // Dizzy→Mad transition: morph purple→red over 1s (using hue 360 to force purple transition path)
        target1 = mixHsl({ h: 195, s: 75, l: 65 }, { h: 360, s: 85, l: 58 }, madVisualStrength)
        target2 = mixHsl({ h: 270, s: 70, l: 50 }, { h: 335, s: 82, l: 42 }, madVisualStrength)
      } else if (expression === 'mad') {
        // Mad loop + outro — blend with intro/outro (using hue 360 to force purple transition path)
        const madT1 = { h: 360, s: 85, l: 58 }
        const madT2 = { h: 335, s: 82, l: 42 }
        target1 = mixHsl(idle1, madT1, madVisualStrength)
        target2 = mixHsl(idle2, madT2, madVisualStrength)
      } else if (expression === 'happy') {
        // Happy mode — blend with happyAlpha
        const hapT1 = { h: 145, s: 75, l: 55 }
        const hapT2 = { h: 160, s: 65, l: 40 }
        target1 = mixHsl(idle1, hapT1, happyAlpha)
        target2 = mixHsl(idle2, hapT2, happyAlpha)
      } else if (expression === 'sleepy') {
        // Sleep mode — blend with intro/outro
        const sleepColorBlend = sleepProgress > 0 ? sleepProgress : (1 - sleepOutro)
        const slpT1 = { h: 245, s: 50, l: 45 }
        const slpT2 = { h: 285, s: 45, l: 35 }
        target1 = mixHsl(idle1, slpT1, sleepColorBlend)
        target2 = mixHsl(idle2, slpT2, sleepColorBlend)
      } else {
        // Normal mode
        target1 = idle1
        target2 = idle2
      }

      // Sleep intro choreography: blend colors from idle → purple during intro
      if (sleepProgress > 0 && sleepProgress < 1 && expression !== 'sleepy') {
        const sleepyTarget1 = { h: 245, s: 50, l: 45 }
        const sleepyTarget2 = { h: 285, s: 45, l: 35 }
        target1 = mixHsl(target1, sleepyTarget1, sleepProgress)
        target2 = mixHsl(target2, sleepyTarget2, sleepProgress)
      }

      // Add dynamic breathing to saturation and lightness
      const pulseSpeed = expression === 'happy' ? 4.5 : expression === 'sleepy' ? 1.5 : 3.0
      const pulseAmpS = expression === 'sleepy' ? 3 : 6
      const pulseAmpL = expression === 'sleepy' ? 5 : 4

      const satOffset = Math.sin(t * pulseSpeed) * pulseAmpS
      const litOffset = Math.cos(t * (pulseSpeed * 1.1)) * pulseAmpL

      target1.s = Math.max(10, Math.min(100, target1.s + satOffset))
      target1.l = Math.max(10, Math.min(95, target1.l + litOffset))
      target2.s = Math.max(10, Math.min(100, target2.s - satOffset)) // out of phase
      target2.l = Math.max(10, Math.min(95, target2.l - litOffset))

      const c1 = color1Ref.current
      const c2 = color2Ref.current

      const lerpSpd = frameLerp(0.12, dt)
      c1.h = lerpAngle(c1.h, target1.h, lerpSpd)
      c1.s = lerpValue(c1.s, target1.s, lerpSpd)
      c1.l = lerpValue(c1.l, target1.l, lerpSpd)

      c2.h = lerpAngle(c2.h, target2.h, lerpSpd)
      c2.s = lerpValue(c2.s, target2.s, lerpSpd)
      c2.l = lerpValue(c2.l, target2.l, lerpSpd)

      // hue is computed dynamically from c1.h
      const hue = c1.h

      // Dizzy stars stay full after release, then fade through the rage morph.
      const dizzyAlpha = dragging || dizzyBuffer
        ? 1
        : inTransition
          ? 1 - easeOutCubic(dizzyToMadProgress)
          : 0

      // Compute spin velocity and progress
      let spinVel = 0
      let dizzyProgress = 0
      if (expression === 'dizzy' || inTransition) {
        if (dragging) {
          spinVel = 4.0
          dizzyProgress = 1.0
        } else if (postDragDizzyRef.current > 0) {
          dizzyProgress = 1.0
          spinVel = 3.2
        } else if (inTransition) {
          dizzyProgress = 1 - dizzyToMadProgress
          spinVel = 3.2 * dizzyProgress
        }
      }
      dizzySpinAngleRef.current += spinVel * dt

      const madAlphaTarget = expression === 'mad' ? madVisualStrength : 0
      madAlphaRef.current += (madAlphaTarget - madAlphaRef.current) * frameLerp(0.35, dt)
      const madAlpha = madAlphaRef.current

      if (expression === 'dizzy' || inTransition) {
        dizzyWobble = Math.sin(t * 6) * 3 * Math.max(0.25, dizzyAlpha)
        // Only spawn new stars during active drag dizziness, not during fade-out
        if (dragging && dizzyStars.length < 5 && Math.random() < 0.05) {
          dizzyStars.push({
            angle: Math.random() * Math.PI * 2,
            dist: 14 + Math.random() * 6,
            speed: 1.5 + Math.random() * 1,
          })
        }
        for (const star of dizzyStars) {
          // Stars decelerate orbit during slow-mo
          star.angle += star.speed * dt * dizzyProgress
        }
        // Gradually drain stars during post-drag fade so they disappear naturally
        if (!dragging && Math.random() < 2.4 * dt && dizzyStars.length > 0) {
          dizzyStars.pop()
        }
      } else {
        dizzyStars = []
        dizzyWobble *= 0.9
      }

      // ── Pulse ─────────────────────────────────────────────────────
      const pp = t * (Math.PI * 2 / (BLOB.BREATH_PERIOD_MS / 1000))
      const pulse = Math.sin(pp)
      const sx = 1 + pulse * 0.06 + dizzyWobble * 0.01 + popRef.current
      const sy = 1 - pulse * 0.08 - popRef.current * 0.5
      const pm = 1.0

      // ── Eye tracking ──────────────────────────────────────────────
      const ep = eyePosRef.current
      const et = eyeTargetRef.current

      // Dizzy eyes spin instead of tracking (decelerates to stop during slow-mo + transition)
      if (expression === 'dizzy') {
        const spinAngle = dizzySpinAngleRef.current
        const orbitProgress = dragging || dizzyBuffer ? 1 : (inTransition ? 1 - dizzyToMadProgress : 0)
        const orbitRadius = 2.5 * orbitProgress
        eyeTargetRef.current = {
          x: Math.cos(spinAngle) * orbitRadius,
          y: Math.sin(spinAngle) * orbitRadius,
        }
      }

      // Shy eyes look toward mouse (but offset slightly)
      if (expression === 'shy') {
        // Keep normal tracking but减弱
        et.x *= 0.6
        et.y *= 0.6
      }

      const eyeFollow = frameLerp(0.08, dt)
      ep.x += (et.x - ep.x) * eyeFollow
      ep.y += (et.y - ep.y) * eyeFollow

      // Blink: 8-16s intervals, 0.4s animation (slow and natural)
      if (!dragging) {
        blinkTimerRef.current += dt
        if (!isBlinkingRef.current && blinkTimerRef.current >= nextBlinkThresholdRef.current) {
          isBlinkingRef.current = true
          blinkPhaseRef.current = 0
          blinkTimerRef.current = 0
          nextBlinkThresholdRef.current = 8 + Math.random() * 8  // next blink in 8-16s
        }
      } else {
        isBlinkingRef.current = false
        blinkPhaseRef.current = 0
        blinkTimerRef.current = 0
      }
      if (isBlinkingRef.current) {
        blinkPhaseRef.current += dt
        if (blinkPhaseRef.current > 0.4) {  // 0.4s duration — noticeably slow
          isBlinkingRef.current = false
          blinkPhaseRef.current = 0
        }
      }
      const blinkAmount = isBlinkingRef.current
        ? Math.sin((blinkPhaseRef.current / 0.4) * Math.PI)
        : 0

      // Eye squint — compute target then lerp current toward it (smooth, not instant)
      let squintTarget = 0
      if (expression === 'annoyed') squintTarget = 0.12
      else if (expression === 'mad') squintTarget = 0.18 + 0.27 * madVisualStrength
      else if (expression === 'sleepy') squintTarget = 0.35
      else if (expression === 'shy') squintTarget = 0.05
      // Sleep choreography: staged squint override (5s intro)
      if (sleepProgress > 0 && sleepProgress < 1) {
        // Stage 1 (0-0.33): eyes half-closed; Stage 2+ (0.33-1): fully closed
        const stageSquint = sleepProgress < 0.33
          ? sleepProgress / 0.33 * 0.65    // 0 → 0.65
          : 0.65 + (sleepProgress - 0.33) / 0.67 * 0.35  // 0.65 → 1.0
        squintTarget = Math.max(squintTarget, stageSquint)
      } else if (sleepProgress >= 1) {
        squintTarget = Math.max(squintTarget, 1.0)  // fully closed when asleep
      } else if (sleepOutro > 0) {
        // Sleep outro: eyes gradually reopen (3s wake-up)
        const reopenSquint = sleepOutro  // 1→0: starts closed, opens fully
        squintTarget = Math.max(squintTarget, reopenSquint)
      }
      squintCurrentRef.current += (squintTarget - squintCurrentRef.current) * frameLerp(0.15, dt)
      const squint = squintCurrentRef.current

      // ── Target eye morph parameters based on expression ────────────────
      const eyeRadius = 5.5
      let targetRx = eyeRadius
      let targetRyTop = eyeRadius * (1 - squint) * (1 - blinkAmount)
      let targetRyBot = eyeRadius * (1 - squint) * (1 - blinkAmount)
      let targetRot = 0
      let targetIsStroke = 0
      let targetYOff = 0
      let targetPX = ep.x * 1.2
      let targetPY = ep.y * 1.2

      if (expression === 'mad') {
        const madIntensity = madAlpha
        const madTilt = 0.35 * madIntensity
        targetRx = eyeRadius * 0.85
        targetRyTop = 0
        targetRyBot = eyeRadius * 0.85 * madIntensity
        targetRot = madTilt
        targetIsStroke = 0
        targetYOff = 0
        targetPX = 0
        targetPY = 0
      } else if (expression === 'sad') {
        targetRx = eyeRadius * 0.85
        targetRyTop = eyeRadius * 0.55
        targetRyBot = -eyeRadius * 0.45
        targetRot = 0.22  // Droopy sad eye arches
        targetIsStroke = 1
        targetYOff = 2.5
        targetPX = 0
        targetPY = 0
      } else if (expression === 'happy') {
        targetRx = eyeRadius * 0.7
        targetRyTop = -eyeRadius * 0.45
        targetRyBot = eyeRadius * 0.45
        targetRot = 0
        targetIsStroke = 1
        targetYOff = 1
        targetPX = 0
        targetPY = 0
      } else if (expression === 'sleepy') {
        const sleepyIntensity = (sleepProgress > 0 ? sleepProgress : (1 - sleepOutro))
        targetRx = eyeRadius * 0.75
        targetRyTop = eyeRadius * 0.45 * sleepyIntensity
        targetRyBot = -eyeRadius * 0.45 * sleepyIntensity
        targetRot = 0
        targetIsStroke = 1
        targetYOff = -1
        targetPX = 0
        targetPY = 0
      } else if (expression === 'surprised') {
        targetRx = eyeRadius * 1.2
        targetRyTop = eyeRadius * 1.2 * (1 - squint) * (1 - blinkAmount)
        targetRyBot = eyeRadius * 1.2 * (1 - squint) * (1 - blinkAmount)
        targetRot = 0
        targetIsStroke = 0
        targetYOff = 0
      }

      // Smoothly morph values
      const morphSpd = frameLerp(0.18, dt)
      eyeMorphRxRef.current += (targetRx - eyeMorphRxRef.current) * morphSpd
      eyeMorphRyTopRef.current += (targetRyTop - eyeMorphRyTopRef.current) * morphSpd
      eyeMorphRyBotRef.current += (targetRyBot - eyeMorphRyBotRef.current) * morphSpd
      eyeMorphRotRef.current += (targetRot - eyeMorphRotRef.current) * morphSpd
      eyeMorphIsStrokeRef.current += (targetIsStroke - eyeMorphIsStrokeRef.current) * morphSpd
      eyeMorphYOffRef.current += (targetYOff - eyeMorphYOffRef.current) * morphSpd
      eyeMorphPXRef.current += (targetPX - eyeMorphPXRef.current) * morphSpd
      eyeMorphPYRef.current += (targetPY - eyeMorphPYRef.current) * morphSpd

      const mRx = eyeMorphRxRef.current
      const mRyTop = eyeMorphRyTopRef.current
      const mRyBot = eyeMorphRyBotRef.current
      const mRot = eyeMorphRotRef.current
      const mIsStroke = eyeMorphIsStrokeRef.current
      const mYOff = eyeMorphYOffRef.current
      const mPX = eyeMorphPXRef.current
      const mPY = eyeMorphPYRef.current

      // ── Tentacle physics: ocean jellyfish drag ───────────────────────────────
      // dragBlend eases in/out so there's never an instant physics switch
      const blendTarget = dragging ? 1 : 0
      dragBlendRef.current += (blendTarget - dragBlendRef.current)
        * frameLerp(blendTarget > dragBlendRef.current ? 0.18 : 0.12, dt)
      const dragBlend = dragBlendRef.current
      const releaseBlend = Math.abs(blendTarget - dragBlend)

      // Compute unified lean direction: ALL tentacles lean TOGETHER in the
      // direction OPPOSITE to motion, max 15° — like a jellyfish in the ocean
      const dVx = dragVelRef.current.x
      const dVy = dragVelRef.current.y
      const rawSpeed = Math.sqrt(dVx * dVx + dVy * dVy)
      const MAX_LEAN_PX = Math.tan(15 * Math.PI / 180) * TENT_LEN  // ~8.6px at tip
      const leanScale = rawSpeed > 0.5 ? Math.min(MAX_LEAN_PX / rawSpeed, 1) : 0
      // Trailing unit vector (opposite to motion)
      const leanUx = rawSpeed > 0.5 ? (-dVx / rawSpeed) * leanScale * dragBlend : 0
      const leanUy = rawSpeed > 0.5 ? (-dVy / rawSpeed) * leanScale * dragBlend * 0.4 : 0

      for (const ten of tents) {
        const ax = cx + ten.sx * sx
        const ay = cy + BELL_BASE_Y * sy
        ten.pts[0].x = ax
        ten.pts[0].y = ay

        // During drag: reduce wave amplitude so legs don't flail
        const rageMotion = expression === 'dizzy' || inTransition
        const tentAmp = ten.amp * (1 - dragBlend * 0.62) + (rageMotion ? 0.35 * dizzyAlpha : 0)
        const tentSpeed = ten.speed * (rageMotion ? 1.25 : 1)

        for (let i = 1; i < ten.pts.length; i++) {
          const p = ten.pts[i]
          const f = i / TENT_SEGS  // 0=root, 1=tip

          // Gentle wave (always present, attenuated during drag)
          const wave = Math.sin(t * tentSpeed * pm + ten.phase + f * 2.8) * tentAmp * f

          // Normal rest position
          const normalTx = ax + wave
          const normalTy = ay + f * TENT_LEN

          // Jellyfish lean: uniform angular sweep, linear with depth
          // All legs lean the same direction (no per-tentacle chaos)
          const dragTx = ax + wave * 0.25 + leanUx * f * TENT_LEN
          const dragTy = ay + f * TENT_LEN + leanUy * f * TENT_LEN

          // Smooth blend between rest and lean
          const tx = normalTx + (dragTx - normalTx) * dragBlend
          const ty = normalTy + (dragTy - normalTy) * dragBlend

          // Blended spring: stiffer at rest, softer during drag for fluid lag
          const springK = 0.04 - 0.018 * dragBlend

          p.vx += (tx - p.x) * springK
          p.vy += (ty - p.y) * springK
          const damping = clamp(ten.damp - releaseBlend * 0.08, 0.82, 0.96)
          p.vx = clamp(p.vx * damping, -3.2, 3.2)
          p.vy = clamp(p.vy * damping, -3.2, 3.2)
          p.x += p.vx
          p.y += p.vy
        }
      }

      // ── Organ positions ───────────────────────────────────────────
      const organPos = organs.map((o) => ({
        x: cx + o.bx * sx + Math.sin(t * o.speed * 0.4 + o.phase) * 1.2,
        y: cy + o.by * sy + Math.cos(t * o.speed * 0.3 + o.phase) * 0.8,
      }))

      // ══════════════════════════════════════════════════════════════
      // LAYER 1 — Ambient glow (strong, wide)
      // ══════════════════════════════════════════════════════════════
      const glowR = 58
      const gG = ctx.createRadialGradient(cx, cy, 6, cx, cy, glowR)
      gG.addColorStop(0, `hsla(${c1.h}, ${c1.s}%, ${c1.l}%, 0.28)`)
      gG.addColorStop(0.35, `hsla(${(c1.h + c2.h) / 2}, ${(c1.s + c2.s) / 2}%, ${(c1.l + c2.l) / 2}%, 0.14)`)
      gG.addColorStop(0.7, `hsla(${c2.h}, ${c2.s}%, ${c2.l}%, 0.05)`)
      gG.addColorStop(1, `hsla(${c2.h}, ${c2.s}%, ${c2.l}%, 0)`)
      ctx.beginPath()
      ctx.arc(cx, cy, glowR, 0, Math.PI * 2)
      ctx.fillStyle = gG
      ctx.fill()

      // ══════════════════════════════════════════════════════════════
      // LAYER 2 — Tentacles (tapering, flowing)
      // ══════════════════════════════════════════════════════════════
      for (const ten of tents) {
        for (let i = 0; i < ten.pts.length - 1; i++) {
          const f0 = i / (ten.pts.length - 1)
          const f1 = (i + 1) / (ten.pts.length - 1)
          const w0 = ten.width * (1 - f0 * 0.8)
          const a0 = 0.35 * (1 - f0 * 0.75)
          const a1 = 0.35 * (1 - f1 * 0.75)

          ctx.beginPath()
          ctx.moveTo(ten.pts[i].x, ten.pts[i].y)
          ctx.lineTo(ten.pts[i + 1].x, ten.pts[i + 1].y)

          const segG = ctx.createLinearGradient(
            ten.pts[i].x, ten.pts[i].y,
            ten.pts[i + 1].x, ten.pts[i + 1].y,
          )
          const hSeg = c1.h + (c2.h - c1.h) * f0
          const sSeg = c1.s + (c2.s - c1.s) * f0
          const lSeg = c1.l + (c2.l - c1.l) * f0

          const hSegNext = c1.h + (c2.h - c1.h) * f1
          const sSegNext = c1.s + (c2.s - c1.s) * f1
          const lSegNext = c1.l + (c2.l - c1.l) * f1

          segG.addColorStop(0, `hsla(${hSeg}, ${sSeg}%, ${lSeg}%, ${a0})`)
          segG.addColorStop(1, `hsla(${hSegNext}, ${sSegNext}%, ${lSegNext}%, ${a1})`)
          ctx.strokeStyle = segG
          ctx.lineWidth = w0
          ctx.lineCap = 'round'
          ctx.stroke()
        }
      }

      // ── Smooth alpha for sideways phase-shift slump during sad mode ─────
      const sadLeanTarget = expression === 'sad' ? 1 : 0
      sadLeanAlphaRef.current += (sadLeanTarget - sadLeanAlphaRef.current) * frameLerp(0.35, dt)
      const sadLeanAlpha = sadLeanAlphaRef.current

      // Sideways slump offset & tilt angle
      const sadShiftX = (Math.sin(t * 1.5) * 2 + 9) * sadLeanAlpha
      const sadShiftY = (Math.cos(t * 1.2) * 1.5 + 4) * sadLeanAlpha
      const sadTiltAngle = (0.16 + Math.sin(t * 1.0) * 0.02) * sadLeanAlpha

      if (sadLeanAlpha > 0.001) {
        ctx.save()
        ctx.translate(cx, cy)
        ctx.rotate(sadTiltAngle)
        ctx.translate(-cx + sadShiftX, -cy + sadShiftY)
      }

      // ══════════════════════════════════════════════════════════════
      // LAYER 3 — Bell body (transparent with strong glow center)
      // ══════════════════════════════════════════════════════════════
      traceBell(ctx, cx, cy, sx, sy, t)
      const bG = ctx.createRadialGradient(cx, cy - 2, 0, cx, cy + 2, BELL_H + 6)
      bG.addColorStop(0, `hsla(${c1.h}, ${c1.s}%, 88%, ${0.22 + pulse * 0.06})`)
      bG.addColorStop(0.2, `hsla(${c1.h}, ${c1.s}%, ${c1.l}%, 0.28)`)
      bG.addColorStop(0.5, `hsla(${(c1.h + c2.h) / 2}, ${(c1.s + c2.s) / 2}%, ${(c1.l + c2.l) / 2}%, 0.28)`)
      bG.addColorStop(0.8, `hsla(${c2.h}, ${c2.s}%, ${c2.l}%, 0.24)`)
      bG.addColorStop(1, `hsla(${c2.h}, ${c2.s}%, ${c2.l - 5}%, 0.20)`)
      ctx.fillStyle = bG
      ctx.fill()

      // Bright center glow (drawn inside clipped bell)
      ctx.save()
      traceBell(ctx, cx, cy, sx, sy, t)
      ctx.clip()
      const coreG = ctx.createRadialGradient(cx, cy - 3, 0, cx, cy - 3, BELL_H * 0.65)
      coreG.addColorStop(0, `hsla(${c1.h}, ${c1.s}%, 95%, ${0.40 + pulse * 0.12})`)
      coreG.addColorStop(0.4, `hsla(${c1.h}, ${c1.s}%, 80%, 0.22)`)
      coreG.addColorStop(1, `hsla(${c1.h}, ${c1.s}%, ${c1.l}%, 0)`)
      ctx.fillStyle = coreG
      ctx.fillRect(cx - BELL_W - 5, cy - BELL_H - 5, (BELL_W + 5) * 2, (BELL_H + 5) * 2)
      ctx.restore()

      // ══════════════════════════════════════════════════════════════
      // LAYER 4 — Inner glow + organs (clipped to bell)
      // ══════════════════════════════════════════════════════════════
      ctx.save()
      traceBell(ctx, cx, cy, sx, sy, t)
      ctx.clip()

      // Inner radial glow
      const iG = ctx.createRadialGradient(cx, cy - 2, 0, cx, cy, BELL_H * 0.8)
      iG.addColorStop(0, `hsla(${c1.h}, ${c1.s}%, 80%, ${0.18 + (pulse + 1) * 0.06})`)
      iG.addColorStop(0.5, `hsla(${c2.h}, ${c2.s}%, 65%, ${0.08})`)
      iG.addColorStop(1, `hsla(${c2.h}, ${c2.s}%, ${c2.l}%, 0)`)
      ctx.fillStyle = iG
      ctx.fillRect(0, 0, w, h)

      // Organs with halos
      for (let i = 0; i < organs.length; i++) {
        const o = organs[i]
        const pos = organPos[i]
        const op = Math.sin(t * o.speed + o.phase) * 0.3 + 0.7
        const ob = o.bright * op * 1.0

        // Halo (stronger)
        const ohG = ctx.createRadialGradient(pos.x, pos.y, 0, pos.x, pos.y, o.size * 2.8)
        ohG.addColorStop(0, `hsla(${c1.h + 20}, ${c1.s}%, 78%, ${0.16 * ob})`)
        ohG.addColorStop(0.4, `hsla(${c1.h + 10}, ${c1.s}%, 62%, ${0.06 * ob})`)
        ohG.addColorStop(1, `hsla(${c1.h}, ${c1.s}%, 48%, 0)`)
        ctx.beginPath()
        ctx.arc(pos.x, pos.y, o.size * 2.8, 0, Math.PI * 2)
        ctx.fillStyle = ohG
        ctx.fill()

        // Core (brighter)
        const oG = ctx.createRadialGradient(pos.x, pos.y, 0, pos.x, pos.y, o.size)
        oG.addColorStop(0, `hsla(${c1.h + 15}, ${c1.s}%, 85%, ${0.45 * ob})`)
        oG.addColorStop(0.4, `hsla(${c1.h + 5}, ${c1.s}%, 70%, ${0.20 * ob})`)
        oG.addColorStop(1, `hsla(${c1.h}, ${c1.s}%, 50%, 0)`)
        ctx.beginPath()
        ctx.arc(pos.x, pos.y, o.size, 0, Math.PI * 2)
        ctx.fillStyle = oG
        ctx.fill()
      }

      ctx.restore()

      // ══════════════════════════════════════════════════════════════
      // LAYER 5 — Bell membrane (subtle edge stroke)
      // ══════════════════════════════════════════════════════════════
      traceBell(ctx, cx, cy, sx, sy, t)
      const mG = ctx.createLinearGradient(cx - BELL_W, cy - BELL_H, cx + BELL_W, cy + BELL_H * 0.5)
      mG.addColorStop(0, `hsla(${c1.h}, ${c1.s}%, 68%, 0.12)`)
      mG.addColorStop(0.5, `hsla(${(c1.h + c2.h) / 2}, ${(c1.s + c2.s) / 2}%, 55%, 0.08)`)
      mG.addColorStop(1, `hsla(${c2.h}, ${c2.s}%, 50%, 0.04)`)
      ctx.strokeStyle = mG
      ctx.lineWidth = 1.0
      ctx.stroke()

      // ══════════════════════════════════════════════════════════════
      // LAYER 5.5 — Thin glowing edge (neon outline)
      // ══════════════════════════════════════════════════════════════
      // Bright core stroke
      traceBell(ctx, cx, cy, sx, sy, t)
      const edgeG = ctx.createLinearGradient(cx - BELL_W, cy - BELL_H, cx + BELL_W, cy + BELL_H * 0.3)
      edgeG.addColorStop(0, `hsla(${c1.h}, ${c1.s - 10}%, ${c1.l + 20}%, 0.45)`)
      edgeG.addColorStop(0.5, `hsla(${(c1.h + c2.h) / 2}, ${c1.s}%, ${c1.l + 15}%, 0.35)`)
      edgeG.addColorStop(1, `hsla(${c2.h}, ${c2.s - 10}%, ${c2.l + 15}%, 0.25)`)
      ctx.strokeStyle = edgeG
      ctx.lineWidth = 0.8
      ctx.stroke()

      // Outer glow aura (wider, fainter)
      traceBell(ctx, cx, cy, sx, sy, t)
      ctx.strokeStyle = `hsla(${hue}, ${c1.s}%, ${c1.l + 10}%, 0.08)`
      ctx.lineWidth = 3.5
      ctx.stroke()

      // ══════════════════════════════════════════════════════════════
      // LAYER 6 — EYES (morphing between expressions)
      // ══════════════════════════════════════════════════════════════
      const eyeSpacing = 9
      const eyeY = cy - 2

      for (let side = -1; side <= 1; side += 2) {
        const ex = cx + side * eyeSpacing * sx
        const ey = eyeY + mYOff

        ctx.save()
        ctx.translate(ex + mPX, ey + mPY)
        ctx.rotate(-side * mRot)

        ctx.beginPath()
        ctx.moveTo(-mRx, 0)
        ctx.quadraticCurveTo(0, -2 * mRyTop, mRx, 0)
        ctx.quadraticCurveTo(0, 2 * mRyBot, -mRx, 0)

        // Draw filled base (normal, mad, surprised) — fades out during drag
        if (mIsStroke < 0.99) {
          const baseAlpha = (1 - mIsStroke) * (1 - dragBlend)
          if (baseAlpha > 0.001) {
            ctx.fillStyle = `hsla(0, 0%, 5%, ${0.95 * baseAlpha})`
            ctx.fill()
          }

          // Draw the pupil details & shines ONLY for the filled part (normal eyes)
          const normalAlpha = (1 - madAlpha) * baseAlpha
          if (normalAlpha > 0.01) {
            // Draw iris stroke
            const irisR = mRx * 0.65
            ctx.beginPath()
            ctx.arc(0, 0, irisR, 0, Math.PI * 2)
            ctx.strokeStyle = `hsla(${hue + 20}, 30%, 18%, ${0.35 * normalAlpha})`
            ctx.lineWidth = 1.0
            ctx.stroke()

            // Highlight shines
            const shX = 2
            const shY = -2
            const shG = ctx.createRadialGradient(shX, shY, 0, shX, shY, 2.2)
            shG.addColorStop(0, `hsla(0, 0%, 100%, ${0.70 * (1 - blinkAmount) * normalAlpha})`)
            shG.addColorStop(0.5, `hsla(0, 0%, 100%, ${0.25 * (1 - blinkAmount) * normalAlpha})`)
            shG.addColorStop(1, `hsla(0, 0%, 100%, 0)`)
            ctx.beginPath()
            ctx.arc(shX, shY, 2.2, 0, Math.PI * 2)
            ctx.fillStyle = shG
            ctx.fill()

            const sh2X = -1.5
            const sh2Y = 1.5
            ctx.beginPath()
            ctx.arc(sh2X, sh2Y, 1.0, 0, Math.PI * 2)
            ctx.fillStyle = `hsla(0, 0%, 100%, ${0.25 * (1 - blinkAmount) * normalAlpha})`
            ctx.fill()


          }
        }

        // Draw stroke line (happy, sleepy) — fades out during drag
        if (mIsStroke > 0.01 && dragBlend < 0.99) {
          ctx.beginPath()
          ctx.moveTo(-mRx, 0)
          ctx.quadraticCurveTo(0, -2 * mRyTop, mRx, 0)
          ctx.quadraticCurveTo(0, 2 * mRyBot, -mRx, 0)
          ctx.strokeStyle = `hsla(0, 0%, 5%, ${0.90 * mIsStroke * (1 - dragBlend)})`
          ctx.lineWidth = 2.2
          ctx.lineCap = 'round'
          ctx.stroke()
        }

        // Draw spiral morphing into a line (dizzy -> mad)
        if (expression === 'dizzy' || inTransition) {
          const morphT = inTransition ? dizzyToMadProgress : 0

          ctx.save()
          // Spin the spiral, but damp the spin velocity down to 0 during transition
          ctx.rotate(dizzySpinAngleRef.current * (1 - morphT))

          ctx.beginPath()
          for (let s = 0; s < 20; s++) {
            const sa = (s / 20) * Math.PI * 4
            const sr = (s / 20) * 3.5
            const spiralX = Math.cos(sa) * sr
            const spiralY = Math.sin(sa) * sr

            // Unravel into a horizontal line that matches the current morphed eye radius
            const lineX = -mRx + (s / 20) * (2 * mRx)
            const lineY = 0

            const sx2 = spiralX + (lineX - spiralX) * morphT
            const sy2 = spiralY + (lineY - spiralY) * morphT

            if (s === 0) ctx.moveTo(sx2, sy2)
            else ctx.lineTo(sx2, sy2)
          }
          ctx.strokeStyle = `hsla(0, 0%, 95%, ${0.7 * dizzyAlpha * (1 - dragBlend)})`
          ctx.lineWidth = 1.2
          ctx.stroke()
          ctx.restore()
        }

        ctx.restore()

        // >< overlay fades in with dragBlend (only if not dizzy or mad)
        if (dragBlend > 0.01 && expression !== 'dizzy' && expression !== 'mad') {
          const arm = eyeRadius * 0.85
          ctx.save()
          ctx.translate(ex, ey)
          const tipX = -side * arm
          ctx.strokeStyle = `hsla(0, 0%, 5%, ${0.92 * dragBlend})`
          ctx.lineWidth = 2.4
          ctx.lineCap = 'round'
          ctx.lineJoin = 'round'
          ctx.beginPath()
          ctx.moveTo(side * arm, -arm * 0.65)
          ctx.lineTo(tipX, 0)
          ctx.lineTo(side * arm, arm * 0.65)
          ctx.stroke()
          ctx.restore()
        }
      }

      // ── Expression overlays ─────────────────────────────────────

      // Mad: red glow + anger vein — intensity modulated by intro/outro (3s each)
      const madOverlayAlpha = madAlpha
      if (madOverlayAlpha > 0.01) {
        const madPulse = Math.sin(t * 8) * 0.15 + 0.85

        // Red tint glow over the bell
        ctx.save()
        traceBell(ctx, cx, cy, sx, sy, t)
        ctx.clip()
        const madG = ctx.createRadialGradient(cx, cy, 0, cx, cy, BELL_W)
        madG.addColorStop(0, `hsla(0, 80%, 50%, ${0.18 * madOverlayAlpha * madPulse})`)
        madG.addColorStop(1, `hsla(0, 70%, 40%, 0)`)
        ctx.fillStyle = madG
        ctx.fillRect(0, 0, w, h)
        ctx.restore()

        // Anger vein 💢 — 4-bracket cross, top-right of head
        const vx = cx + 14
        const vy = cy - BELL_H * 0.85
        const vs = 5.5 * madPulse * madOverlayAlpha
        const va = madOverlayAlpha * (0.7 + Math.sin(t * 12) * 0.3)
        ctx.strokeStyle = `hsla(0, 90%, 55%, ${va})`
        ctx.lineWidth = 1.8
        ctx.lineCap = 'round'
        ctx.lineJoin = 'round'
        const corners: [number, number][] = [[-1, -1], [1, -1], [-1, 1], [1, 1]]
        for (const [dx, dy] of corners) {
          ctx.beginPath()
          ctx.moveTo(vx + dx * vs, vy + dy * vs * 0.45)
          ctx.lineTo(vx + dx * vs * 0.45, vy + dy * vs * 0.45)
          ctx.lineTo(vx + dx * vs * 0.45, vy + dy * vs)
          ctx.stroke()
        }
      }

      // Dizzy: orbiting stars (with dizzyAlpha fade during post-drag and transition)
      if (expression === 'dizzy' || inTransition) {
        for (const star of dizzyStars) {
          const starX = cx + Math.cos(star.angle) * star.dist * sx
          const starY = cy - 6 + Math.sin(star.angle) * star.dist * 0.4
          const starSize = 1.5 + Math.sin(t * 8 + star.angle) * 0.5
          ctx.fillStyle = `hsla(50, 90%, 75%, ${0.8 * dizzyAlpha})`
          ctx.beginPath()
          for (let p = 0; p < 4; p++) {
            const pa = (p / 4) * Math.PI * 2 - Math.PI / 2
            const outerX = starX + Math.cos(pa) * starSize
            const outerY = starY + Math.sin(pa) * starSize
            const innerPa = pa + Math.PI / 4
            const innerX = starX + Math.cos(innerPa) * starSize * 0.3
            const innerY = starY + Math.sin(innerPa) * starSize * 0.3
            if (p === 0) ctx.moveTo(outerX, outerY)
            else ctx.lineTo(outerX, outerY)
            ctx.lineTo(innerX, innerY)
          }
          ctx.closePath()
          ctx.fill()
        }
      }

      // Sleepy: floating zzz — fade with intro/outro
      if (expression === 'sleepy') {
        const zzzAlpha = sleepProgress > 0 ? sleepProgress : (1 - sleepOutro)
        const zzz = ['z', 'Z', 'z']
        for (let i = 0; i < 3; i++) {
          const zt = (t * 0.8 + i * 0.7) % 3
          const zx = cx + 16 + i * 4 + Math.sin(zt * 2) * 2
          const zy = cy - 10 - zt * 8
          const za = Math.max(0, 1 - zt / 3)
          const zSize = 5 + i * 1.5
          ctx.font = `bold ${zSize}px sans-serif`
          ctx.fillStyle = `hsla(${hue}, 40%, 80%, ${za * 0.6 * zzzAlpha})`
          ctx.fillText(zzz[i], zx, zy)
        }
      }

      // Surprised: small "o" mouth
      if (expression === 'surprised') {
        const surprisedAlpha = Math.min(1, transitionBlend * 2.5) * 0.6
        ctx.beginPath()
        ctx.arc(cx, cy + 8, 2.5, 0, Math.PI * 2)
        ctx.fillStyle = `hsla(0, 0%, 5%, ${surprisedAlpha})`
        ctx.fill()
      }

      // Shy: slight blush marks
      if (expression === 'shy') {
        for (let side = -1; side <= 1; side += 2) {
          const bx = cx + side * (eyeSpacing + 5) * sx
          const by = eyeY + 4
          const blushG = ctx.createRadialGradient(bx, by, 0, bx, by, 5.2)
          blushG.addColorStop(0, 'hsla(340, 92%, 72%, 0.46)')
          blushG.addColorStop(0.55, 'hsla(348, 90%, 68%, 0.18)')
          blushG.addColorStop(1, 'hsla(340, 90%, 70%, 0)')
          ctx.beginPath()
          ctx.ellipse(bx, by, 5.2, 3.2, side * 0.15, 0, Math.PI * 2)
          ctx.fillStyle = blushG
          ctx.fill()
        }
        // Shy mouth: tiny subtle smile
        const shyAlpha = Math.min(1, transitionBlend * 2.5) * 0.35
        ctx.beginPath()
        ctx.arc(cx, cy + 7, 2.5, 0.2, Math.PI - 0.2)
        ctx.strokeStyle = `hsla(0, 0%, 5%, ${shyAlpha})`
        ctx.lineWidth = 1.2
        ctx.lineCap = 'round'
        ctx.stroke()
      }

      // Sleepy mouth: small yawning oval (intro + outro)
      const showYawn = expression === 'sleepy' || sleepProgress > 0.35 || sleepOutro > 0.1
      if (showYawn) {
        // During intro: yawn fades in from 0.35→0.65 progress
        // During outro: yawn reappears during first third of wake-up
        const yawnAlpha = expression === 'sleepy' && sleepProgress >= 1
          ? Math.min(1, transitionBlend * 2) * 0.45
          : sleepOutro > 0.5
            ? (sleepOutro - 0.5) / 0.5 * 0.45  // outro: yawn fades 1→0
            : Math.min(1, (sleepProgress - 0.35) / 0.3) * 0.45
        const yawnOpen = 1.5 + Math.sin(t * 1.2) * 0.3  // gentle pulse
        ctx.beginPath()
        ctx.ellipse(cx, cy + 8, 2.2, yawnOpen, 0, 0, Math.PI * 2)
        ctx.fillStyle = `hsla(0, 0%, 8%, ${yawnAlpha})`
        ctx.fill()
        // Inner mouth highlight
        ctx.beginPath()
        ctx.ellipse(cx, cy + 7.5, 1.2, yawnOpen * 0.5, 0, 0, Math.PI * 2)
        ctx.fillStyle = `hsla(340, 55%, 45%, ${yawnAlpha * 0.3})`
        ctx.fill()
      }

      // Happy mouth: curved smile — fades smoothly with happyAlpha
      if (happyAlpha > 0.01) {
        const mouthAlpha = Math.min(1, transitionBlend * 2.5) * 0.7 * happyAlpha
        ctx.beginPath()
        ctx.arc(cx, cy + 5, 4, 0.3, Math.PI - 0.3)
        ctx.strokeStyle = `hsla(0, 0%, 5%, ${mouthAlpha})`
        ctx.lineWidth = 1.8
        ctx.lineCap = 'round'
        ctx.stroke()
      }

      // Mad mouth: angry frown — fades with intro/outro
      const madMouthAlpha = madAlpha
      if (madMouthAlpha > 0.01) {
        ctx.beginPath()
        ctx.arc(cx, cy + 10, 3.5, Math.PI + 0.3, -0.3)
        ctx.strokeStyle = `hsla(0, 0%, 5%, ${0.5 * madMouthAlpha})`
        ctx.lineWidth = 1.6
        ctx.lineCap = 'round'
        ctx.stroke()
      }

      // ══════════════════════════════════════════════════════════════
      // LAYER 7 — Specular highlight on bell
      // ══════════════════════════════════════════════════════════════
      const spX = cx - 8
      const spY = cy - BELL_H * 0.55
      const spG = ctx.createRadialGradient(spX, spY, 0, spX, spY, 9)
      spG.addColorStop(0, `hsla(${hue}, 30%, 98%, 0.50)`)
      spG.addColorStop(0.3, `hsla(${hue}, 40%, 92%, 0.22)`)
      spG.addColorStop(0.6, `hsla(${hue}, 45%, 85%, 0.08)`)
      spG.addColorStop(1, `hsla(${hue}, 50%, 80%, 0)`)
      ctx.beginPath()
      ctx.ellipse(spX, spY, 9, 4.5, -0.2, 0, Math.PI * 2)
      ctx.fillStyle = spG
      ctx.fill()

      // Sad: teardrop accent on cheek
      if (expression === 'sad') {
        const tearY = eyeY + 7 + Math.sin(t * 2.5) * 1.2
        const tearX = cx + 9 * sx
        const tearG = ctx.createRadialGradient(tearX, tearY, 0, tearX, tearY, 2.5)
        tearG.addColorStop(0, `hsla(195, 90%, 88%, ${0.9 * sadLeanAlpha})`)
        tearG.addColorStop(0.6, `hsla(205, 80%, 72%, ${0.5 * sadLeanAlpha})`)
        tearG.addColorStop(1, `hsla(215, 70%, 60%, 0)`)
        ctx.beginPath()
        ctx.arc(tearX, tearY, 2.5, 0, Math.PI * 2)
        ctx.fillStyle = tearG
        ctx.fill()
      }

      if (sadLeanAlpha > 0.001) {
        ctx.restore()
      }

      // ══════════════════════════════════════════════════════════════
      // ══════════════════════════════════════════════════════════════


      // ── Hue sync ──────────────────────────────────────────────────
      hueSyncAccumulator += dt
      if (hueSyncAccumulator >= 0.2) {
        hueSyncAccumulator = 0
        publishColor(c1.h, c1.s, c1.l)
      }

      raf = requestAnimationFrame(draw)
    }

    raf = requestAnimationFrame(draw)
    return () => {
      cancelAnimationFrame(raf)
      clearInterval(cursorInterval)
    }
  }, [])

  // ── Interaction ──────────────────────────────────────────────────────
  const handleMouseDown = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    if (e.button !== 0) return
    e.preventDefault()
    e.stopPropagation()

    isPointerDownRef.current = true
    isDraggingRef.current = false
    didDragRef.current = false
    draggingStateRef.current = false
    startScreenRef.current = { x: e.screenX, y: e.screenY }

    startWindowRef.current = getAnchorPosition()

    const onMove = (me: MouseEvent) => {
      if (!isPointerDownRef.current || !startScreenRef.current || !startWindowRef.current) return
      const dx = me.screenX - startScreenRef.current.x
      const dy = me.screenY - startScreenRef.current.y
      if (!isDraggingRef.current) {
        if (Math.sqrt(dx * dx + dy * dy) <= DRAG_THRESHOLD) return
        isDraggingRef.current = true
        didDragRef.current = true
        callbacks.current.onDrag()
        draggingStateRef.current = true
      }
      const nx = startWindowRef.current.x + dx
      const ny = startWindowRef.current.y + dy
      setWindowPosition(nx, ny).catch(() => {})


    }

    const onUp = () => {
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
      if (!isPointerDownRef.current) return
      isPointerDownRef.current = false

      if (!didDragRef.current) {
        const now = Date.now()
        // Count clicks in a rolling 2.5 second window
        const recentClicks = clickTimesRef.current.filter((t) => now - t < 2500)
        recentClicks.push(now)
        clickTimesRef.current = recentClicks

        if (recentClicks.length >= 7) {
          // Spam clicked 7 times -> trigger Sad Lockout Mode!
          clickTimesRef.current = []
          sadLockoutTimerRef.current = 5.0
          sadModeActiveRef.current = true

        } else if (!sadModeActiveRef.current) {
          callbacks.current.onActivate()
        }
      } else {
        send('savePosition')
      }

      isDraggingRef.current = false
      didDragRef.current = false
      draggingStateRef.current = false
      startScreenRef.current = null
      startWindowRef.current = null
    }

    dragCleanup.current = () => {
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
      isPointerDownRef.current = false
    }
    document.addEventListener('mousemove', onMove)
    document.addEventListener('mouseup', onUp)
  }, [])

  const handleContextMenu = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    e.preventDefault()
    e.stopPropagation()
    callbacks.current.onContext()
  }, [])

  return (
    <canvas
      ref={canvasRef}
      width={140}
      height={160}
      className="creature-canvas"
      aria-label="Jelli. Click for summary, double click for dashboard, right click for controls."
      style={{
        touchAction: 'none',
        backgroundColor: 'transparent',
      }}
      onMouseDown={handleMouseDown}
      onContextMenu={handleContextMenu}
    />
  )
}
