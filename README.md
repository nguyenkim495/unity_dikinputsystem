# Dik Input System

A reusable layer on top of Unity's Input System (`com.unity.inputsystem` 1.7+), modelled on Unreal Engine 5 **Enhanced Input**.

It keeps the structure of the *InputSystem Warriors* sample (action asset → `PlayerInput` per player → a single adapter → plain-data gameplay) and adds:

| Need | What the package gives you |
|---|---|
| UE5-style mapping | `InputMappingContext` assets, added to or removed from a player at runtime with **priority** and **consumption** |
| UI vs. Gameplay isolation | Layers (`Gameplay`, `UI`, `System`, `Custom1-4`) + modes (`GameOnly`, `UIOnly`, `GameAndUI`) like UE5 `SetInputMode` |
| Device plug/unplug | Smooth hot-swap (held input released, contexts re-applied, rebinds kept, events raised) and automatic pairing of a free device for a player who lost theirs |
| Client/server | `InputCommand` routing: offline/host go straight to gameplay; a client sends to the server through `IInputNetworkBridge` (works with any netcode) |
| Easy authoring | Create contexts in one click from an `.inputactions` asset; gameplay binds with `InputActionReference` (drag and drop) |
| Rebinding | Interactive rebinding with cancel, composite support and per-scheme filtering, saved with `IBindingOverrideStore` |

---

## Architecture

```
 Devices (Keyboard/Mouse, Gamepad, Touch...)
        │      DeviceAutoPairing: plug-in or first use of a free device → given to a player missing one
        ▼
 .inputactions asset           Bindings, Interactions (UE "Triggers"), Processors (UE "Modifiers"), Control Schemes
        │
        ▼
 PlayerInput (Unity)           Device pairing, control-scheme auto-switch, per-player copy of the actions
        │
        ▼
 DikInputPlayer  ─────────────  InputContextStack  ◄── InputMappingContext assets (priority, layer, consume, routing)
   │  produces InputCommand        └ decides which actions are enabled
   │                                  (mode/layers → priority → consumption)
   │
   ├── Offline / Host, or LocalOnly routing ──────────────────────────────┐
   │                                                                      ▼
   └── Client + Replicated ─► IInputNetworkBridge ══ network ══► server: DikInputReceiver
                                                                          │  Bind(...) / ReadValue(...)
                                                                          ▼
                                                                  Gameplay scripts (no Input System code)
```

**Key rule:** gameplay scripts only talk to `DikInputReceiver`. They never touch `PlayerInput`, devices or networking, so the same script works offline, on a host, and on a dedicated server.

### UE5 → Dik mapping

| Unreal Engine 5 | Dik / Unity |
|---|---|
| `UInputAction` | Action inside the `.inputactions` asset (referenced with `InputActionReference`) |
| `UInputMappingContext` | `InputMappingContext` asset (wraps one or more action maps) |
| Mapping Modifiers | Input System **Processors** (deadzone, invert, scale...) on the binding |
| Mapping Triggers | Input System **Interactions** (hold, tap, multi-tap...) on the binding |
| `AddMappingContext(ctx, priority)` | `player.AddContext(ctx, priority)` |
| `RemoveMappingContext(ctx)` | `player.RemoveContext(ctx)` |
| `SetInputMode(GameOnly/UIOnly/GameAndUI)` | `player.SetMode(InputMode.GameOnly/UIOnly/GameAndUI)` |
| `BindAction(Action, ETriggerEvent::Started, ...)` | `receiver.Bind(actionRef, InputTrigger.Started, cmd => ...)` |
| `FInputActionValue` | `InputCommand.ReadValue<T>()` |

---

## Installation

* **Embedded:** copy the `dik.inputsystem` folder into another project's `Packages/` folder.
* **Local path:** Package Manager → *+* → *Add package from disk...* → `dik.inputsystem/package.json`.
* **Git:** push the folder to a repo and add `"dik.inputsystem": "https://<repo>.git"` (or `?path=/dik.inputsystem`) to `manifest.json`.

Requires Unity 2022.3+ and Input System 1.7+. uGUI integration turns on automatically when `com.unity.ugui` is installed.

---

## Quick start (5 minutes)

1. **Author actions** in an `.inputactions` asset, as usual. Put gameplay, menu and system actions in separate maps, e.g.:
   * `Gameplay`: Move, Look, Attack
   * `UI`: Navigate, Submit, Cancel, Point, Click
   * `System`: TogglePause
2. **Create contexts:** select the asset → *Assets ▸ Create ▸ Dik ▸ Input ▸ Mapping Contexts From Action Asset*.
   You get `IMC_<Map>.asset` files. Maps whose name contains *UI*/*Menu* become `UI` + `LocalOnly`. Then:
   * Set `IMC_System` to **Layer = System**, **Routing = LocalOnly** (pause works in every mode and never goes to the server).
3. **Player prefab:** *GameObject ▸ Dik ▸ Input Player* creates the three components below, already wired
   (select the `.inputactions` asset first to assign it too).
   * `PlayerInput`: assign the asset and leave *Default Map* empty. DikInputPlayer forces *Behavior = Invoke C# Events* and manages maps through contexts.
   * `DikInputPlayer`: add `IMC_Gameplay`, `IMC_UI`, `IMC_System` to *Default Contexts*. *Start Mode* = `GameOnly`.
   * `DikInputReceiver`: nothing to set when it's next to `PlayerInput`.
4. **Gameplay script:**

```csharp
using Dik.Input;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(DikInputReceiver))]
public class Hero : MonoBehaviour
{
    [SerializeField] InputActionReference move;     // drag from the asset
    [SerializeField] InputActionReference attack;

    DikInputReceiver input;
    InputBindingHandle attackBinding;

    void Awake()     => input = GetComponent<DikInputReceiver>();
    void OnEnable()  => attackBinding = input.Bind(attack, InputTrigger.Started, _ => Attack());
    void OnDisable() => attackBinding.Dispose();

    void Update()
    {
        Vector2 m = input.ReadValue<Vector2>(move);   // zero when released, disabled, or device lost
        transform.position += new Vector3(m.x, 0, m.y) * 5f * Time.deltaTime;
    }

    void Attack() { /* ... */ }
}
```

Or derive from `DikInputBehaviour`, which finds the receiver (parents, then the player with *Player Index*, waiting for
it to spawn if needed) and disposes bindings on disable. The script can then live on any object, e.g. a camera:

```csharp
public class Hero : DikInputBehaviour
{
    [SerializeField] InputActionReference move;
    [SerializeField] InputActionReference attack;

    protected override void BindInput() => Bind(attack, InputTrigger.Started, _ => Attack());

    void Update()
    {
        Vector2 m = ReadValue<Vector2>(move);
        transform.position += new Vector3(m.x, 0, m.y) * 5f * Time.deltaTime;
    }

    void Attack() { /* ... */ }
}
```

---

## UI and Gameplay isolation

```csharp
player.SetMode(InputMode.UIOnly);    // gameplay actions disabled; held movement is released (Canceled, value 0)
player.SetMode(InputMode.GameOnly);  // UI actions disabled
player.SetMode(InputMode.GameAndUI); // both (e.g. inventory over a live game). Use priority + consume to resolve conflicts
player.InputEnabled = false;         // everything off, System layer included

DikInput.SetInputEnabledForAll(false, except: focusedPlayer);   // the classic local-multiplayer pause
player.UIInputModule = sharedUiModule;                          // give the menu to the player who paused
```

* Contexts on the `System` layer (pause, chat toggle) stay active in `GameOnly` and `UIOnly`.
* For split-screen menus with one UI per player, use a `MultiplayerEventSystem` + `InputSystemUIInputModule` per player and assign it to that player's `UIInputModule`.

### Priority and consumption

When several contexts are active, they're evaluated from the highest priority down (ties: last added wins):

* `ConsumeBoundControls`: lower-priority actions that use **any of the same controls** are disabled. Example: a `Vehicle` context (priority 10) binds `South` to *Handbrake*, so on-foot *Jump* (priority 0, also `South`) is suppressed while driving, but *Move* still works.
* `BlockLowerPriority`: modal. Nothing below it gets input (cutscene skip, dialogue).
* `None`: lower contexts are not affected.

Consumption works per *action* (Unity can't disable a single binding at runtime). If an action shares only one key with a higher context, the whole action is suppressed. Split such actions if needed.

---

## Devices: plug, unplug, hot-swap

| Situation | Behaviour |
|---|---|
| Single player switches keyboard ↔ gamepad | Unity's `PlayerInput` switches scheme automatically. DikInputPlayer releases held input, re-applies contexts, **keeps rebinds**, raises `ControlsChanged` |
| Device unplugged | Held input is released (no running forever), `DeviceLost` is raised (show "Reconnect controller", pause...) |
| Same device plugged back | Unity re-pairs it, `DeviceRegained` |
| Different device plugged in, or a free device is used | `DeviceAutoPairing` gives it to the first player that `NeedsDevice` (scheme picked automatically, keyboard comes with a free mouse), `DeviceRegained` |
| Joining new players | Use Unity's `PlayerInputManager` (join on button press). Each spawned player with `DikInputPlayer` works out of the box |

```csharp
player.ControlsChanged += p => icons.Refresh(p.CurrentDeviceKind, p.CurrentGamepadFamily);
player.DeviceLost      += p => PauseAndShow("Reconnect controller");
DeviceAutoPairing.Policy = DeviceAutoPairPolicy.ReplaceLostDevices;   // default; Off to disable
```

`DeviceClassifier` uses device types and layout inheritance (Xbox / PlayStation / Switch / Generic) instead of name strings. `DeviceDisplayProfile` maps them to a name, color and icons keyed by control path (`buttonSouth`, `leftTrigger`, `space`...).

---

## Client-server

Each context has a **Routing**:

* `Replicated`: gameplay. On a client, it's sent to the server. Offline or host, it's processed locally.
* `LocalOnly`: UI, pause, camera, debug. Never sent.

Set the mode once from your session bootstrap:

```csharp
DikInput.NetworkMode = InputNetworkMode.Offline; // default: device → gameplay directly
DikInput.NetworkMode = InputNetworkMode.Host;    // server + local player: same as Offline
DikInput.NetworkMode = InputNetworkMode.Client;  // Replicated commands go to IInputNetworkBridge.SendToServer
DikInput.NetworkMode = InputNetworkMode.Server;  // dedicated: receivers are fed by your RPC handler
DikInput.TickSource  = () => (uint)myNetworkTick; // stamp commands with the network tick
```

Implement the bridge for your netcode (a full NGO example is in *Samples ▸ Netcode for GameObjects Bridge*):

```csharp
public class MyBridge : NetworkBehaviour, IInputNetworkBridge
{
    [SerializeField] DikInputReceiver receiver;
    readonly InputCommandBatcher batch = new InputCommandBatcher();

    public bool CanSend => IsOwner && IsSpawned;
    public void SendToServer(in InputCommand cmd) => batch.Add(cmd);    // client

    void LateUpdate()
    {
        if (CanSend && batch.TryFlush(out var packet))
            SubmitRpc(packet.ToArray());                                // one packet per frame
    }

    [Rpc(SendTo.Server)] void SubmitRpc(byte[] data)
        => InputNetworkBridgeUtility.Deliver(receiver, data);          // server → gameplay
}
```

* **Server authority:** gameplay runs on the server-side `DikInputReceiver`. Use `receiver.Validator` to reject bad commands (e.g. stick magnitude > 1).
* **Prediction:** enable `Predict Locally When Client` on `DikInputPlayer` to also feed the local receiver.
* **Disconnects:** call `receiver.ReleaseAll()` on the server when a client leaves.
* **Remote copies:** keep `PlayerInput` disabled on the networked prefab and enable it only for the owner, so remote copies don't grab local devices.
* **Wire format:** `InputCommandSerializer` packs a command into 8–24 bytes. `InputActionTable.Signature` lets client and server check that they use the same action asset.
* Commands are sent **on change** (Started/Performed/Canceled), so use a reliable channel. For unreliable channels, resend state per tick from `receiver.ReadValue`.

---

## Rebinding

```csharp
player.StartRebind(jumpRef, onFinished: result => RefreshUI());   // current scheme's binding, saved on success
player.ResetBindingOverrides();
DikRebind.GetDisplayString(player.ResolveAction(jumpRef), index); // "Space", "W/A/S/D", "Button South"
DikInput.BindingStore = new MyCloudStore();                       // default: PlayerPrefs, key "<key>.p<index>"
```

* Composites (WASD) are rebound part by part (`RebindSession.PartChanged`).
* Esc (keyboard) or Start (gamepad) cancels.
* Candidates are limited to the current scheme's devices.

---

## Creating a new mapping (checklist)

1. Add a map and its actions in the `.inputactions` asset (bindings for each control scheme).
2. *Create ▸ Dik ▸ Input ▸ Input Mapping Context*. Pick the asset, tick the map, set Layer / Priority / Consume / Routing.
   The inspector shows every action with its current bindings.
3. Add it at runtime where it makes sense:
   ```csharp
   player.AddContext(vehicleContext);     // enter vehicle
   player.RemoveContext(vehicleContext);  // exit vehicle
   ```
4. Bind in gameplay with `receiver.Bind(actionRef, InputTrigger.Started, ...)` or poll with `ReadValue`.

Contexts can also be created in code: `ScriptableObject.CreateInstance<InputMappingContext>().Configure(asset, maps, layer, ...)`.

---

## API overview

| Type | Role |
|---|---|
| `DikInput` | Global: `NetworkMode`, `TickSource`, `BindingStore`, `Players`, `SetInputEnabledForAll` |
| `DikInputPlayer` | Local player: contexts, mode, devices, routing, rebinding, events |
| `DikInputReceiver` | Gameplay API: `Bind`, `ReadValue`, `IsPressed`, `Was*ThisFrame`, `Receive`, `Validator`, `ReleaseAll` |
| `DikInputBehaviour` | Optional base for gameplay scripts: receiver lookup, auto-disposed `Bind`, `ReadValue`, `IsPressed` |
| `InputMappingContext` | Context asset |
| `InputContextStack` | Priority, layer and consumption resolution (accessed through `player.Contexts`) |
| `InputCommand` / `InputCommandSerializer` / `InputCommandBatcher` | Network-ready input data |
| `IInputNetworkBridge` / `InputNetworkBridgeUtility` | Netcode hook |
| `DeviceAutoPairing` / `DeviceClassifier` / `DeviceDisplayProfile` | Devices |
| `DikRebind` / `RebindSession` / `IBindingOverrideStore` | Rebinding |

## Known limitations

* Consumption is per action, not per binding (see above).
* If a player received a replacement device and the original one reconnects later, Unity also re-pairs the original, so the player ends up with both.
* If something outside the package enables or disables actions on the player's asset, call `player.Contexts.Apply()` to restore the context state.
