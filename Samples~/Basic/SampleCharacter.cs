using Dik.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dik.Input.Samples
{
    /// <summary>
    /// Gameplay script. It depends only on DikInputReceiver, never on devices, PlayerInput or networking,
    /// so the same code runs offline, on a host, or on a dedicated server fed by the network bridge.
    /// </summary>
    [RequireComponent(typeof(DikInputReceiver))]
    public sealed class SampleCharacter : MonoBehaviour
    {
        [SerializeField] InputActionReference m_Move;
        [SerializeField] InputActionReference m_Attack;
        [SerializeField] float m_Speed = 5f;
        [SerializeField] float m_Smoothing = 10f;

        DikInputReceiver m_Receiver;
        InputBindingHandle m_AttackBinding;
        Vector2 m_SmoothedMove;

        void Awake()
        {
            m_Receiver = GetComponent<DikInputReceiver>();

            // Server-side anti-cheat example: reject impossible stick values.
            m_Receiver.Validator = command => command.ReadVector2().sqrMagnitude <= 1.01f;
        }

        void OnEnable()
        {
            // Event style: like UE5 BindAction(Attack, ETriggerEvent::Started, this, &Attack).
            m_AttackBinding = m_Receiver.Bind(m_Attack, InputTrigger.Started, _ => Attack());
        }

        void OnDisable()
        {
            m_AttackBinding.Dispose();
        }

        void Update()
        {
            // Polling style: returns zero automatically once the action is released, disabled by a mode change,
            // or released because the device was unplugged.
            var raw = m_Receiver.ReadValue<Vector2>(m_Move);
            m_SmoothedMove = Vector2.Lerp(m_SmoothedMove, raw, Time.deltaTime * m_Smoothing);

            var move = new Vector3(m_SmoothedMove.x, 0f, m_SmoothedMove.y);
            transform.position += move * (m_Speed * Time.deltaTime);
            if (move.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(move);
        }

        void Attack()
        {
            Debug.Log($"{name} attacks");
        }
    }
}
