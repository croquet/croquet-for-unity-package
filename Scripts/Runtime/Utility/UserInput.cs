using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace Croquet.Adapters
{
    public class UserInput : MonoBehaviour
    {
        public bool SendAllKeysAsEvents = true;
        public bool SendPointerEvents = true;

        private UserInputActions inputActions;
        private InputAction keyboard;
        private InputAction pointer;
        private InputAction pointerValue;

        void Awake()
        {
            inputActions = new UserInputActions();

            if (SendAllKeysAsEvents)
            {
                // Fire an Event for all Keys Up and Down
                InputSystem.onEvent.ForDevice<Keyboard>().SelectMany(GetControlsDown).Call(SendKeyDown);
                InputSystem.onEvent.ForDevice<Keyboard>().SelectMany(GetControlsUp).Call(SendKeyUp);
            }
        }

        private IEnumerable<InputControl> GetControlsDown(InputEventPtr eventPtr)
        {
            if (eventPtr.type != StateEvent.Type && eventPtr.type != DeltaStateEvent.Type)
                yield break;

            foreach (var control in eventPtr.EnumerateControls(InputControlExtensions.Enumerate.IgnoreControlsInCurrentState))
            {
                if (control.IsPressed())
                    continue;

                yield return control;
            }
        }

        private IEnumerable<InputControl> GetControlsUp(InputEventPtr eventPtr)
        {
            if (eventPtr.type != StateEvent.Type && eventPtr.type != DeltaStateEvent.Type)
                yield break;

            foreach (var control in eventPtr.EnumerateControls(InputControlExtensions.Enumerate.IgnoreControlsInCurrentState))
            {
                if (!control.IsPressed())
                    continue;

                yield return control;
            }
        }

        void OnEnable()
        {
            inputActions.Enable();

            // KEYBOARD
            keyboard = inputActions.User.Keyboard;

            // POINTER - Touch and Mouse
            pointer = inputActions.User.PointerEvent;
            pointerValue = inputActions.User.PointerValue;

            if (SendPointerEvents)
            {
                pointer.started += SendPointerDown;

                pointer.canceled += SendPointerUp;
                pointer.Enable();
            }
        }


        void SendKeyDown(InputControl control)
        {
            // Debug.Log($"[INPUT] KEYDOWN: " + control.name);

            CroquetBridge.Instance.SendToCroquet("event", "keyDown", control.name);
        }

        void SendKeyUp(InputControl control)
        {
            //Debug.Log($"[INPUT] KEYUP: " + control.name);

            CroquetBridge.Instance.SendToCroquet("event", "keyUp", control.name);
        }

        void SendPointerDown(InputAction.CallbackContext callbackContext)
        {
            Debug.Log("[INPUT] Pointer Down");
            //CroquetBridge.Instance.SendToCroquet("event", "pointerDown");
        }

        void SendPointerUp(InputAction.CallbackContext callbackContext)
        {
            Debug.Log("[INPUT] Pointer Up");
            //CroquetBridge.Instance.SendToCroquet("event", "pointerUp");
        }
    }
}
