using System;
using UnityEngine;
using System.Collections.Generic;

public static class Croquet
{
    #region Say and Listen Functions

    /// <summary>
    /// Send a message directly to the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    public static void Say(GameObject gameObject, string eventName)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }
    
    /// <summary>
    /// Send a message directly to the corresponding actor.
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
    /// Send a message directly to the corresponding actor.
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
    /// Send a message directly to the corresponding actor.
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
    /// Send a message directly to the corresponding actor.
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
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Listen(GameObject gameObject, string eventName, Action handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler();
        CroquetBridge.Instance.SubscribeToCroquetEvent(gameObject, scope, eventName, forwarder);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Listen(GameObject gameObject, string eventName, Action<string> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        CroquetBridge.Instance.SubscribeToCroquetEvent(gameObject, scope, eventName, handler);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Listen(GameObject gameObject, string eventName, Action<string[]> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler(s.Split('\x03'));
        CroquetBridge.Instance.SubscribeToCroquetEvent(gameObject, scope, eventName, forwarder);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Listen(GameObject gameObject, string eventName, Action<float> handler)
    {
        string scope = gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
        Action<string> forwarder = s => handler(float.Parse(s));
        CroquetBridge.Instance.SubscribeToCroquetEvent(gameObject, scope, eventName, forwarder);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="gameObject"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Listen(GameObject gameObject, string eventName, Action<float[]> handler)
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
        CroquetBridge.Instance.SubscribeToCroquetEvent(gameObject, scope, eventName, forwarder);
    }
    #endregion
    
    #region Publish and Subscribe Functions

    /// <summary>
    /// Send a message directly to the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    public static void Publish(string scope, string eventName)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }
    
    /// <summary>
    /// Send a message directly to the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argString"></param>
    public static void Publish(string scope, string eventName, string argString)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, argString);
    }
    
    /// <summary>
    /// Send a message directly to the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argStrings"></param>
    public static void Publish(string scope, string eventName, string[] argStrings)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, string.Join('\x03', argStrings));    
    }
    
    /// <summary>
    /// Send a message directly to the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloat"></param>
    public static void Publish(string scope, string eventName, float argFloat)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, argFloat.ToString());
    }
    
    /// <summary>
    /// Send a message directly to the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="argFloats"></param>
    public static void Publish(string scope, string eventName, float[] argFloats)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, string.Join<float>('\x03', argFloats));
    }
#if false // needs more work
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Subscribe(string scope, string eventName, Action handler)
    {
        Action<string> forwarder = s => handler();
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Subscribe(string scope, string eventName, Action<string> handler)
    {
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, handler);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Subscribe(string scope, string eventName, Action<string[]> handler)
    {
        Action<string> forwarder = s => handler(s.Split('\x03'));
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Subscribe(string scope, string eventName, Action<float> handler)
    {
        Action<string> forwarder = s => handler(float.Parse(s));
        CroquetBridge.Instance.SubscribeToCroquetEvent(scope, eventName, forwarder);
    }
    
    /// <summary>
    /// Listen to messages sent directly from the corresponding actor.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="eventName"></param>
    /// <param name="handler"></param>
    public static void Subscribe(string scope, string eventName, Action<float[]> handler)
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
    }
#endif
    #endregion
    
}