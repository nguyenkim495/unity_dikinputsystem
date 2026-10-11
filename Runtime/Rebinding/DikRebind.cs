using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    public enum RebindResult
    {
        Completed,
        Canceled
    }

    /// <summary>
    /// A running interactive rebind. For composites (e.g. WASD) it walks through every part in turn.
    /// </summary>
    public sealed class RebindSession : IDisposable
    {
        internal InputActionRebindingExtensions.RebindingOperation Operation;
        internal Action<RebindResult> OnFinished;
        internal bool WasEnabled;

        public InputAction Action { get; internal set; }

        /// <summary>The binding currently waiting for input (a composite part while rebinding a composite).</summary>
        public int CurrentBindingIndex { get; internal set; }

        public bool IsRunning { get; internal set; }

        /// <summary>Fired when moving to the next composite part, so the UI can show "Press key for: Up".</summary>
        public event Action<RebindSession> PartChanged;

        public void Cancel() => Operation?.Cancel();

        public void Dispose()
        {
            if (IsRunning)
                Cancel();
        }

        internal void RaisePartChanged() => PartChanged?.Invoke(this);
    }

    /// <summary>
    /// Interactive rebinding with the safety measures the original sample lacked: cancel input, composite support,
    /// restricting candidates to the current control scheme, and restoring the action's enabled state.
    /// Prefer DikInputPlayer.StartRebind, which also re-applies contexts and saves overrides.
    /// </summary>
    public static class DikRebind
    {
        public static readonly string[] DefaultExcludedControls =
        {
            "<Pointer>/position",
            "<Pointer>/delta",
            "<Mouse>/scroll",
            "<Touchscreen>/touch*/position",
            "<Touchscreen>/touch*/delta"
        };

        /// <param name="bindingIndex">A composite index rebinds all of its parts; a part index rebinds only that part.</param>
        /// <param name="controlScheme">Restricts candidates to this scheme's devices and picks the matching cancel control.</param>
        public static RebindSession Start(InputAction action, int bindingIndex, Action<RebindResult> onFinished = null,
            InputControlScheme? controlScheme = null, IEnumerable<string> excludedControls = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                throw new ArgumentOutOfRangeException(nameof(bindingIndex));

            var session = new RebindSession
            {
                Action = action,
                OnFinished = onFinished,
                WasEnabled = action.enabled,
                IsRunning = true
            };

            // The Input System requires the action to be disabled while rebinding.
            if (session.WasEnabled)
                action.Disable();

            var isComposite = action.bindings[bindingIndex].isComposite;
            var first = isComposite ? bindingIndex + 1 : bindingIndex;
            StartPart(session, first, isComposite, controlScheme, excludedControls ?? DefaultExcludedControls);
            return session;
        }

        static void StartPart(RebindSession session, int bindingIndex, bool walkComposite,
            InputControlScheme? scheme, IEnumerable<string> excluded)
        {
            var action = session.Action;
            session.CurrentBindingIndex = bindingIndex;

            var operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough(GetCancelPath(scheme))
                .OnMatchWaitForAnother(0.1f);

            foreach (var path in excluded)
                operation.WithControlsExcluding(path);

            if (scheme.HasValue)
            {
                foreach (var requirement in scheme.Value.deviceRequirements)
                    operation.WithControlsHavingToMatchPath(requirement.controlPath);
            }

            operation.OnComplete(op =>
            {
                op.Dispose();
                var next = bindingIndex + 1;
                if (walkComposite && next < action.bindings.Count && action.bindings[next].isPartOfComposite)
                {
                    StartPart(session, next, true, scheme, excluded);
                    session.RaisePartChanged();
                }
                else
                {
                    Finish(session, RebindResult.Completed);
                }
            });

            operation.OnCancel(op =>
            {
                op.Dispose();
                Finish(session, RebindResult.Canceled);
            });

            session.Operation = operation;
            operation.Start();
        }

        static void Finish(RebindSession session, RebindResult result)
        {
            session.Operation = null;
            session.IsRunning = false;
            if (session.WasEnabled)
                session.Action.Enable();
            session.OnFinished?.Invoke(result);
        }

        static string GetCancelPath(InputControlScheme? scheme)
        {
            if (scheme.HasValue)
            {
                foreach (var requirement in scheme.Value.deviceRequirements)
                {
                    if (requirement.controlPath != null && requirement.controlPath.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "<Gamepad>/start";
                }
            }
            return "<Keyboard>/escape";
        }

        /// <summary>
        /// The binding of <paramref name="action"/> used by <paramref name="controlScheme"/>.
        /// Returns the composite index if the match is a composite part. Returns -1 if none is found.
        /// </summary>
        public static int FindBindingIndex(InputAction action, string controlScheme)
        {
            if (action == null)
                return -1;

            var bindings = action.bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.isComposite)
                    continue;
                if (!string.IsNullOrEmpty(controlScheme) && !InputBinding.MaskByGroup(controlScheme).Matches(binding))
                    continue;

                if (!binding.isPartOfComposite)
                    return i;

                var composite = i;
                while (composite > 0 && !bindings[composite].isComposite)
                    composite--;
                return composite;
            }
            return -1;
        }

        /// <summary>Readable text for a binding, e.g. "Space", "W/A/S/D", "Button South".</summary>
        public static string GetDisplayString(InputAction action, int bindingIndex,
            InputBinding.DisplayStringOptions options = default)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return string.Empty;
            return action.GetBindingDisplayString(bindingIndex, options);
        }

        public static void ResetBinding(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return;

            if (action.bindings[bindingIndex].isComposite)
            {
                for (var i = bindingIndex + 1; i < action.bindings.Count && action.bindings[i].isPartOfComposite; i++)
                    action.RemoveBindingOverride(i);
            }
            else
            {
                action.RemoveBindingOverride(bindingIndex);
            }
        }
    }
}
