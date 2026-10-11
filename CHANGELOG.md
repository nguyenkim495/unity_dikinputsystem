# Changelog

## [1.1.0] - 2026-10-11

### Added
- `DikInputBehaviour`: optional base class for gameplay scripts. Resolves the receiver (field, parents, or player by index,
  waiting for the player to register), disposes bindings on disable, exposes `Bind` / `ReadValue` / `IsPressed`.
- Editor: *GameObject ▸ Dik ▸ Input Player* creates PlayerInput + DikInputReceiver + DikInputPlayer, wired together.

## [1.0.0] - 2026-10-11

### Added
- `InputMappingContext` assets with layer, priority, consumption and network routing.
- `InputContextStack` resolving active actions (mode → priority → consumption).
- `DikInputPlayer`: contexts, modes, smooth device hot-swap, command routing, persistent rebinding, uGUI module hook.
- `DikInputReceiver`: event (`Bind`) and polling (`ReadValue`, `IsPressed`) API for gameplay, server-side validator.
- `InputCommand`, `InputCommandSerializer`, `InputCommandBatcher`, `IInputNetworkBridge` for any netcode.
- `DeviceAutoPairing`, `DeviceClassifier`, `DeviceDisplayProfile`.
- `DikRebind` with composite support, cancel, and scheme filtering. `IBindingOverrideStore` with a PlayerPrefs default.
- Editor: mapping context inspector, "Mapping Contexts From Action Asset" generator, runtime player debug view.
- Samples: Basic (character, pause flow, device display, rebind row) and a Netcode for GameObjects bridge.
