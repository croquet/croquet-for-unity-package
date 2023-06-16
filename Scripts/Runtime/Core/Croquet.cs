using System;
using UnityEngine;
using System.Collections.Generic;

public static class Croquet
{
    #region Say and Listen Functions

    /// <summary>
    /// Send an event directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    public static void Say(GameObject gameObject, string eventName)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }
    
    /// <summary>
    /// Send an event directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argString"></param>
    public static void Say(GameObject gameObject, string eventName, string argString)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, argString);
    }
    
    /// <summary>
    /// Send an event directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argStrings"></param>
    public static void Say(GameObject gameObject, string eventName, string[] argStrings)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, string.Join('\x03', argStrings));    
    }
    
    /// <summary>
    /// Send an event directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloat"></param>
    public static void Say(GameObject gameObject, string eventName, float argFloat)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, argFloat.ToString());
    }
    
    /// <summary>
    /// Send an event directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloats"></param>
    public static void Say(GameObject gameObject, string eventName, float[] argFloats)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, string.Join<float>('\x03', argFloats));
    }

    /// <summary>
    /// Listen for events sent directly from the corresponding actor.
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
    /// Listen for events sent directly from the corresponding actor.
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
    /// Listen for events sent directly from the corresponding actor.
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
    /// Listen for events sent directly from the corresponding actor.
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
    /// Listen for events sent directly from the corresponding actor.
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
    /// Cancel listen for events sent directly from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="forwarder"></param>
    public static void Ignore(GameObject gameObject, string eventName, Action<string> forwarder)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.UnsubscribeFromCroquetEvent(gameObject,scope, eventName, forwarder);
    }

    #endregion
    
    #region Publish and Subscribe Functions

    /// <summary>
    /// Publish an event with explicit scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    public static void Publish(string scope, string eventName)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }
    
    /// <summary>
    /// Publish an event with explicit scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argString"></param>
    public static void Publish(string scope, string eventName, string argString)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, argString);
    }
    
    /// <summary>
    /// Publish an event with explicit scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argStrings"></param>
    public static void Publish(string scope, string eventName, string[] argStrings)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, string.Join('\x03', argStrings));    
    }
    
    /// <summary>
    /// Publish an event with explicit scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloat"></param>
    public static void Publish(string scope, string eventName, float argFloat)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, argFloat.ToString());
    }
    
    /// <summary>
    /// Publish an event with explicit scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloats"></param>
    public static void Publish(string scope, string eventName, float[] argFloats)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, string.Join<float>('\x03', argFloats));
    }

    /// <summary>
    /// Listen for events sent with explicit scope.
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
    /// Listen for events sent with explicit scope.
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
    /// Listen for events sent with explicit scope.
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
    /// Listen for events sent with explicit scope.
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
    /// Listen for events sent with explicit scope.
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
    
}

