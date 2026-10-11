using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input
{
    /// <summary>
    /// Action phase a handler wants to receive. Matches Input System phases
    /// (UE5: Started / Triggered / Completed+Canceled).
    /// </summary>
    [Flags]
    public enum InputTrigger : byte
    {
        None = 0,
        Started = 1 << 0,
        Performed = 1 << 1,
        Canceled = 1 << 2,
        Any = Started | Performed | Canceled
    }

    public enum InputValueKind : byte
    {
        None,
        Float,
        Vector2,
        Vector3,
        Quaternion,
        Integer
    }

    /// <summary>
    /// One action event, independent of the device that produced it. This is the only input type gameplay code sees,
    /// and the payload the client sends to the server. Fields are plain values so any netcode can serialize them
    /// (see <see cref="InputCommandSerializer"/>).
    /// </summary>
    [Serializable]
    public struct InputCommand
    {
        /// <summary>Index of the action in <see cref="InputActionTable"/>. Same on client and server for the same asset.</summary>
        public ushort ActionIndex;
        public InputTrigger Trigger;
        public InputValueKind ValueKind;
        public Vector4 Value;
        public uint Tick;

        public InputCommand(ushort actionIndex, InputTrigger trigger, InputValueKind kind, Vector4 value, uint tick)
        {
            ActionIndex = actionIndex;
            Trigger = trigger;
            ValueKind = kind;
            Value = value;
            Tick = tick;
        }

        public bool IsStarted => Trigger == InputTrigger.Started;
        public bool IsPerformed => Trigger == InputTrigger.Performed;
        public bool IsCanceled => Trigger == InputTrigger.Canceled;

        public float ReadFloat() => Value.x;
        public Vector2 ReadVector2() => new Vector2(Value.x, Value.y);
        public Vector3 ReadVector3() => new Vector3(Value.x, Value.y, Value.z);
        public Quaternion ReadQuaternion() => new Quaternion(Value.x, Value.y, Value.z, Value.w);
        public bool ReadButton() => Trigger != InputTrigger.Canceled && Mathf.Abs(Value.x) >= 0.5f;

        /// <summary>Supports float, Vector2, Vector3, Vector4, Quaternion, int and bool.</summary>
        public T ReadValue<T>() where T : struct
        {
            if (typeof(T) == typeof(Vector2)) { var v = ReadVector2(); return UnsafeUtility.As<Vector2, T>(ref v); }
            if (typeof(T) == typeof(float)) { var v = ReadFloat(); return UnsafeUtility.As<float, T>(ref v); }
            if (typeof(T) == typeof(bool)) { var v = ReadButton(); return UnsafeUtility.As<bool, T>(ref v); }
            if (typeof(T) == typeof(Vector3)) { var v = ReadVector3(); return UnsafeUtility.As<Vector3, T>(ref v); }
            if (typeof(T) == typeof(Vector4)) { var v = Value; return UnsafeUtility.As<Vector4, T>(ref v); }
            if (typeof(T) == typeof(Quaternion)) { var v = ReadQuaternion(); return UnsafeUtility.As<Quaternion, T>(ref v); }
            if (typeof(T) == typeof(int)) { var v = Mathf.RoundToInt(Value.x); return UnsafeUtility.As<int, T>(ref v); }
            throw new NotSupportedException($"InputCommand cannot read values of type {typeof(T).Name}.");
        }

        /// <summary>A Canceled command with a zero value, used to release held actions.</summary>
        public static InputCommand CreateRelease(ushort actionIndex, InputValueKind kind, uint tick)
        {
            return new InputCommand(actionIndex, InputTrigger.Canceled, kind, Vector4.zero, tick);
        }

        public static InputTrigger ToTrigger(InputActionPhase phase)
        {
            switch (phase)
            {
                case InputActionPhase.Started: return InputTrigger.Started;
                case InputActionPhase.Performed: return InputTrigger.Performed;
                case InputActionPhase.Canceled: return InputTrigger.Canceled;
                default: return InputTrigger.None;
            }
        }

        public static InputCommand FromContext(ushort actionIndex, InputTrigger trigger, in InputAction.CallbackContext context, uint tick)
        {
            var kind = GetValueKind(context.action);
            var value = Vector4.zero;

            switch (kind)
            {
                case InputValueKind.Vector2:
                    var v2 = context.ReadValue<Vector2>();
                    value = new Vector4(v2.x, v2.y, 0f, 0f);
                    break;
                case InputValueKind.Vector3:
                    var v3 = context.ReadValue<Vector3>();
                    value = new Vector4(v3.x, v3.y, v3.z, 0f);
                    break;
                case InputValueKind.Quaternion:
                    var q = context.ReadValue<Quaternion>();
                    value = new Vector4(q.x, q.y, q.z, q.w);
                    break;
                case InputValueKind.Integer:
                    value.x = context.ReadValue<int>();
                    break;
                case InputValueKind.Float:
                    value.x = context.control != null && context.valueType == typeof(float)
                        ? context.ReadValue<float>()
                        : (context.ReadValueAsButton() ? 1f : 0f);
                    break;
            }

            return new InputCommand(actionIndex, trigger, kind, value, tick);
        }

        /// <summary>Infers the value shape from the action's expected control type.</summary>
        public static InputValueKind GetValueKind(InputAction action)
        {
            if (action == null)
                return InputValueKind.None;

            switch (action.expectedControlType)
            {
                case "Vector2":
                case "Stick":
                case "Dpad":
                    return InputValueKind.Vector2;
                case "Vector3":
                    return InputValueKind.Vector3;
                case "Quaternion":
                    return InputValueKind.Quaternion;
                case "Integer":
                    return InputValueKind.Integer;
                default:
                    return InputValueKind.Float;
            }
        }

        public override string ToString() => $"[{Tick}] action#{ActionIndex} {Trigger} {Value}";
    }
}
