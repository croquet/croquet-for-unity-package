using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class CroquetEventParticipant : MonoBehaviour
{
    public virtual void AddCroquetSubscriptions()
    {
        // by default, nothing
    }

    private void OnDestroy()
    {
        if (CroquetBridge.Instance == null) return; // at shutdown, might have gone already

        CroquetBridge.Instance.RemoveCroquetSubscriptionsFor(gameObject);
    }

    private string CroquetActorId()
    {
        return gameObject.GetComponent<CroquetEntityComponent>().croquetActorId;
    }

    private void GenericPublish(string scope, string eventName, [CanBeNull] string argString)
    {
        if (argString == null)
        {
            CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
        }
        else
        {
            CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, argString);
        }
    }
    
    protected void Publish(string scope, string eventName)
    {
        GenericPublish(scope, eventName, null);
    }

    protected void Publish(string scope, string eventName, float number)
    {
        GenericPublish(scope, eventName, number.ToString());
    }
    
    protected void Publish(string scope, string eventName, float[] numbers)
    {
        string argString = string.Join<float>('\x03', numbers);
        GenericPublish(scope, eventName, argString);
    }
    
    protected void Publish(string scope, string eventName, string argString)
    {
        GenericPublish(scope, eventName, argString);
    }

    protected void Publish(string scope, string eventName, string[] strings)
    {
        string argString = string.Join('\x03', strings);
        GenericPublish(scope, eventName, argString);
    }
    
    private void GenericSay(string eventName, [CanBeNull] string argString)
    {
        string scope = CroquetActorId();
        GenericPublish(scope, eventName, argString);
    }
    
    protected void Say(string eventName)
    {
        GenericSay(eventName, null);
    }

    protected void Say(string eventName, float number)
    {
        GenericSay(eventName, number.ToString());
    }

    protected void Say(string eventName, float[] numbers)
    {
        string argString = string.Join<float>('\x03', numbers);
        GenericSay(eventName, argString);
    }
    
    protected void Say(string eventName, string argString)
    {
        GenericSay(eventName, argString);
    }

    protected void Say(string eventName, string[] strings)
    {
        string argString = string.Join('\x03', strings);
        GenericSay(eventName, argString);
    }
    
    private void GenericListen(string eventName, Action<string> listenForwarder)
    {
        // tell the Croquet Bridge that this object wants to hear some event when sent
        // using say() from the Croquet actor that this pawn represents.

        // Debug.Log($"subscribing to event {eventName} in scope {CroquetActorId()}");
        CroquetBridge.Instance.SubscribeToCroquetEvent(gameObject, CroquetActorId(), eventName, listenForwarder);
    }

    protected void Listen(string eventName, Action handler)
    {
        Action<string> forwarder = s => handler();
        GenericListen(eventName, forwarder);
    }

    protected void Listen(string eventName, Action<float> handler)
    {
        Action<string> forwarder = s => handler(float.Parse(s));
        GenericListen(eventName, forwarder);
    }

    protected void Listen(string eventName, Action<float[]> handler)
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
        GenericListen(eventName, forwarder);
    }

    protected void Listen(string eventName, Action<string> handler)
    {
        Action<string> forwarder = s => handler(s);
        GenericListen(eventName, forwarder);
    }

    protected void Listen(string eventName, Action<string[]> handler)
    {
        Action<string> forwarder = s => handler(s.Split('\x03'));
        GenericListen(eventName, forwarder);
    }

    private void GenericSubscribe(string scope, string eventName, Action<string> listenForwarder)
    {
        // Debug.Log($"subscribing to event {eventName} in scope {scope}");
        CroquetBridge.Instance.SubscribeToCroquetEvent(gameObject, scope, eventName, listenForwarder);
    }

    protected void Subscribe(string scope, string eventName, Action handler)
    {
        Action<string> forwarder = s => handler();
        GenericSubscribe(scope, eventName, forwarder);
    }

    protected void Subscribe(string scope, string eventName, Action<float> handler)
    {
        Action<string> forwarder = s => handler(float.Parse(s));
        GenericSubscribe(scope, eventName, forwarder);
    }

    protected void Subscribe(string scope, string eventName, Action<float[]> handler)
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
        GenericSubscribe(scope, eventName, forwarder);
    }

    protected void Subscribe(string scope, string eventName, Action<string> handler)
    {
        Action<string> forwarder = s => handler(s);
        GenericSubscribe(scope, eventName, forwarder);
    }

    protected void Subscribe(string scope, string eventName, Action<string[]> handler)
    {
        Action<string> forwarder = s => handler(s.Split('\x03'));
        GenericSubscribe(scope, eventName, forwarder);
    }
    
}
