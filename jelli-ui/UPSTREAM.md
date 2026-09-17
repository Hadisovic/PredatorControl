# Renderer provenance

Source: https://github.com/Hadisovic/Jelli

Revision: `c493a7933cd41ae3feaf08f9136d5cc112e0ccc4` (`master`, fetched 2026-09-17).

Imported file: `jelli-companion/src/components/BlobCanvas.tsx`.

The source repository is a read-only reference and was not modified. The user requested integration into PredatorControl. No standalone Jelli build output, AI code, provider configuration, chat UI, stores, Tauri shell, assets or dependency tree was copied. This directory's production runtime dependencies are React and React DOM only.

Adaptations: remove chat/processing states; substitute the native host bridge for window/cursor calls; change click/context-menu dispatch; add drag listener cleanup; suspend while hidden; cap high-refresh drawing at 60 Hz; publish HSL through CSS variables and a rate-limited hardware-sync channel. All non-chat creature choreography is retained. See `docs/jelli-architecture.md` for validation and limitations.
