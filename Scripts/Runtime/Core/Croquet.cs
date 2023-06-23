using System;
using UnityEngine;
using System.Collections.Generic;

public static class Croquet
{
    #region Say and Listen Functions

    /// <summary>
    /// Send an event with no arguments directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    public static void Say(GameObject gameObject, string eventName)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }
    
    /// <summary>
    /// Send an event with a string argument directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argString"></param>
    public static void Say(GameObject gameObject, string eventName, string argString)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "s", argString);
    }
    
    /// <summary>
    /// Send an event with a string-array argument directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argStrings"></param>
    public static void Say(GameObject gameObject, string eventName, string[] argStrings)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ss", string.Join('\x03', argStrings));    
    }
    
    /// <summary>
    /// Send an event with a float argument directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloat"></param>
    public static void Say(GameObject gameObject, string eventName, float argFloat)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "f", argFloat.ToString());
    }
    
    /// <summary>
    /// Send an event with a float-array argument directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloats"></param>
    public static void Say(GameObject gameObject, string eventName, float[] argFloats)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ff", string.Join<float>('\x03', argFloats));
    }

    /// <summary>
    /// Send an event with a boolean argument directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argBool"></param>
    public static void Say(GameObject gameObject, string eventName, bool argBool)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "b", argBool.ToString());
    }

    /// <summary>
    /// Listen for a say event with no argument from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler();
        CroquetBridge.Instance.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for a say event with string argument from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<string> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler(s); // same type, but we want to ensure a unique handler
        CroquetBridge.Instance.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for a say event with string-array argument from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<string[]> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler(s.Split('\x03'));
        CroquetBridge.Instance.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for a say event with float argument from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<float> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler(float.Parse(s));
        CroquetBridge.Instance.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for a say event with float-array argument from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<float[]> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s =>
        {
            List<float> floats = new List<float>();
            foreach (string str in s.Split('\x03'))
            {
                floats.Add(float.Parse(str));
            }
            handler(floats.ToArray());
        };
        CroquetBridge.Instance.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for a say event with boolean argument from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<bool> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler(bool.Parse(s));
        CroquetBridge.Instance.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Cancel listen for say events
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="forwarder"></param>
    public static void Ignore(GameObject gameObject, string eventName, Action<string> forwarder)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.UnsubscribeFromCroquetEvent(gameObject, scope, eventName, forwarder);
    }

    #endregion
    
    #region Publish and Subscribe Functions

    /// <summary>
    /// Publish an event with explicit scope and no argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    public static void Publish(string scope, string eventName)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }
    
    /// <summary>
    /// Publish an event with explicit scope and string argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argString"></param>
    public static void Publish(string scope, string eventName, string argString)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "s", argString);
    }
    
    /// <summary>
    /// Publish an event with explicit scope and string-array argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argStrings"></param>
    public static void Publish(string scope, string eventName, string[] argStrings)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ss", string.Join('\x03', argStrings));    
    }
    
    /// <summary>
    /// Publish an event with explicit scope and float argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloat"></param>
    public static void Publish(string scope, string eventName, float argFloat)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "f", argFloat.ToString());
    }
    
    /// <summary>
    /// Publish an event with explicit scope and float-array argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloats"></param>
    public static void Publish(string scope, string eventName, float[] argFloats)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ff", string.Join<float>('\x03', argFloats));
    }

    /// <summary>
    /// Publish an event with explicit scope and boolean argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argBool"></param>
    public static void Publish(string scope, string eventName, bool argBool)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "b", argBool.ToString());
    }

    /// <summary>
    /// Listen for events sent with explicit scope and no argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Subscribe(string scope, string eventName, Action handler)
    {
        Action<string> forwarder = s => handler();
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for events sent with explicit scope and string argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Subscribe(string scope, string eventName, Action<string> handler)
    {
        Action<string> forwarder = s => handler(s); // same type, but we want to ensure a unique handler
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for events sent with explicit scope and string-array argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Subscribe(string scope, string eventName, Action<string[]> handler)
    {
        Action<string> forwarder = s => handler(s.Split('\x03'));
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for events sent with explicit scope and float argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Subscribe(string scope, string eventName, Action<float> handler)
    {
        Action<string> forwarder = s => handler(float.Parse(s));
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }
    
    /// <summary>
    /// Listen for events sent with explicit scope and float-array argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Subscribe(string scope, string eventName, Action<float[]> handler)
    {
        Action<string> forwarder = s =>
        {
            List<float> floats = new List<float>();
            foreach (string str in s.Split('\x03'))
            {
                floats.Add(float.Parse(str));
            }
            handler(floats.ToArray());
        };
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for events sent with explicit scope and boolean argument.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static Action<string> Subscribe(string scope, string eventName, Action<bool> handler)
    {
        Action<string> forwarder = s => handler(bool.Parse(s));
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Unsubscribe from events sent with explicit scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="forwarder"></param>
    public static void Unsubscribe(string scope, string eventName, Action<string> forwarder)
    {
        CroquetBridge.Instance.UnsubscribeFromCroquetEvent(scope, eventName, forwarder);
    }

    #endregion
    
    #region Actor Property Access

    public static string ReadActorString(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return stringVal;
    }
    
    public static string[] ReadActorStringArray(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return stringVal.Split('\x03');
    }

    public static float ReadActorFloat(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return float.Parse(stringVal);
    }
    
    public static float[] ReadActorFloatArray(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        List<float> floats = new List<float>();
        foreach (string str in stringVal.Split('\x03'))
        {
            floats.Add(float.Parse(str));
        }
        return floats.ToArray();
    }

    public static bool ReadActorBool(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return bool.Parse(stringVal);
    }

    #endregion
}

