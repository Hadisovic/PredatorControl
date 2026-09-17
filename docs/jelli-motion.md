# Desktop motion and visual direction

The September 2026 refresh uses a restrained deep-sea palette around the original Jelli canvas. Hardware actions, sensor data and the creature's personality remain unchanged.

- Desktop readouts have no box or border. Each follows a different 6.7/8.3-second drift with a small vertical range and offset phase. Text shadows preserve desktop legibility; a short accent mark ties each readout to the creature color. Text metrics use a smaller wrapping style.
- Panels open with a 420 ms transform/opacity reveal. Tab content settles over 360 ms with a short stagger. The close button waits for a 150 ms exit before requesting native collapse. Escape and focus-loss dismissal remain immediate.
- Selected tabs, pressed buttons, toggles and keyboard focus provide distinct feedback. No new animation dependency or telemetry polling is added.
- Reduced-motion preferences remove the decorative animation and close delay. Gaming suspension pauses animations.
- README images come from the actual packaged WebView in the hardware-free fixture, with unavailable readings explicitly disclosed.

Validation includes native mouse targeting, click/drag/wheel behavior, animated close completion and reduced-motion emulation. The host's DWM transparency/input fix is preserved.
