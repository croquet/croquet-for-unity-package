using System;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement;


/// <summary>
/// Main Public API for Croquet
/// </summary>
public static class Croquet
{
    #region Say and Listen Functions

    /// <summary>
    /// Send an event with no arguments directly to the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the message scope for Say</param>
    /// <param name="eventName">a string representing the name of the event</param>
    public static void Say(GameObject gameObject, string eventName)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }

    /// <summary>
    /// Send an event with a string argument directly to the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the message scope for Say</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argString">a string argument passed through to the event</param>
    public static void Say(GameObject gameObject, string eventName, string argString)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "s", argString);
    }

    /// <summary>
    /// Send an event with a string-array argument directly to the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the message scope for Say</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argStrings">a string[] argument passed through to the event</param>
    public static void Say(GameObject gameObject, string eventName, string[] argStrings)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ss", string.Join('\x03', argStrings));
    }

    /// <summary>
    /// Send an event with a float argument directly to the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the message scope for Say</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argFloat">a float argument passed through to the event</param>
    public static void Say(GameObject gameObject, string eventName, float argFloat)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "f", argFloat.ToString());
    }

    /// <summary>
    /// Send an event with a float-array argument directly to the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the message scope for Say</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argFloats">a float[] argument passed through to the event</param>
    public static void Say(GameObject gameObject, string eventName, float[] argFloats)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ff", string.Join<float>('\x03', argFloats));
    }

    /// <summary>
    /// Send an event with a boolean argument directly to the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the message scope for Say</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argBool">a bool argument passed through to the event</param>
    public static void Say(GameObject gameObject, string eventName, bool argBool)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "b", argBool.ToString());
    }

    /// <summary>
    /// Listen for a say event with no argument from the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the listening scope</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the say events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Ignore().
    /// </returns>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action handler)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        Action<string> forwarder = s => handler();
        CroquetBridge.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for a say event with string argument from the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the listening scope</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the say events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Ignore().
    /// </returns>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<string> handler)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        Action<string> forwarder = s => handler(s); // same type, but we want to ensure a unique handler
        CroquetBridge.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for a say event with string-array argument from the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the listening scope</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the say events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Ignore().
    /// </returns>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<string[]> handler)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        Action<string> forwarder = s => handler(s.Split('\x03'));
        CroquetBridge.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for a say event with float argument from the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the listening scope</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the say events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Ignore().
    /// </returns>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<float> handler)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        Action<string> forwarder = s => handler(float.Parse(s));
        CroquetBridge.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for a say event with float-array argument from the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the listening scope</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the say events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Ignore().
    /// </returns>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<float[]> handler)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        Action<string> forwarder = s =>
        {
            List<float> floats = new List<float>();
            foreach (string str in s.Split('\x03'))
            {
                floats.Add(float.Parse(str));
            }
            handler(floats.ToArray());
        };
        CroquetBridge.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for a say event with boolean argument from the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the listening scope</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the say events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Ignore().
    /// </returns>
    public static Action<string> Listen(GameObject gameObject, string eventName, Action<bool> handler)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        Action<string> forwarder = s => handler(bool.Parse(s));
        CroquetBridge.ListenForCroquetEvent(gameObject, scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Stop listening for say events of the given name on this gameObject's actor.
    /// </summary>
    /// <param name="gameObject">the gameObject, serving as the listening scope</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="forwarder">The Action object that was returned by the Listen() call that you wish to cancel</param>
    public static void Ignore(GameObject gameObject, string eventName, Action<string> forwarder)
    {
        string scope = GetActorIdIfAvailable(gameObject);
        CroquetBridge.UnsubscribeFromCroquetEvent(gameObject, scope, eventName, forwarder);
    }

    /// <summary>
    /// Provides the ActorID for a given GameObject if it is available.
    /// </summary>
    /// <param name="gameObject">the gameobject for which to get the ID.</param>
    /// <returns>string ID or empty string</returns>
    private static string GetActorIdIfAvailable(GameObject gameObject)
    {
        CroquetEntityComponent entity = gameObject.GetComponent<CroquetEntityComponent>();
        return entity == null ? "" : entity.croquetActorId;
    }

    #endregion

    #region Publish and Subscribe Functions

    /// <summary>
    /// Publish an event with explicit scope and no argument.
    /// </summary>
    /// <param name="scope">The purview within which this message will be relayed.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    public static void Publish(string scope, string eventName)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName);
    }

    /// <summary>
    /// Publish an event with explicit scope and string argument.
    /// </summary>
    /// <param name="scope">The purview within which this message will be relayed.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argString">a string argument passed through to the event</param>
    public static void Publish(string scope, string eventName, string argString)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "s", argString);
    }

    /// <summary>
    /// Publish an event with explicit scope and string-array argument.
    /// </summary>
    /// <param name="scope">The purview within which this message will be relayed.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argStrings">a string[] argument passed through to the event</param>
    public static void Publish(string scope, string eventName, string[] argStrings)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ss", string.Join('\x03', argStrings));
    }

    /// <summary>
    /// Publish an event with explicit scope and float argument.
    /// </summary>
    /// <param name="scope">The purview within which this message will be relayed.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argFloat">a float argument passed through to the event</param>
    public static void Publish(string scope, string eventName, float argFloat)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "f", argFloat.ToString());
    }

    /// <summary>
    /// Publish an event with explicit scope and float-array argument.
    /// </summary>
    /// <param name="scope">The purview within which this message will be relayed.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argFloats">a float[] argument passed through to the event</param>
    public static void Publish(string scope, string eventName, float[] argFloats)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "ff", string.Join<float>('\x03', argFloats));
    }

    /// <summary>
    /// Publish an event with explicit scope and boolean argument.
    /// </summary>
    /// <param name="scope">The purview within which this message will be relayed.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="argBool">a bool argument passed through to the event</param>
    public static void Publish(string scope, string eventName, bool argBool)
    {
        CroquetBridge.Instance.SendToCroquetSync("publish", scope, eventName, "b", argBool.ToString());
    }

    /// <summary>
    /// Listen for events sent with explicit scope and no argument.
    /// </summary>
    /// <param name="scope">The purview within which this subscription exists.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler"></param>
    /// <returns>
    /// The Action object created to forward the events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Unsubscribe().
    /// </returns>
    public static Action<string> Subscribe(string scope, string eventName, Action handler)
    {
        Action<string> forwarder = s => handler();
        CroquetBridge.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for events sent with explicit scope and string argument.
    /// </summary>
    /// <param name="scope">The purview within which this subscription exists.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Unsubscribe().
    /// </returns>
    public static Action<string> Subscribe(string scope, string eventName, Action<string> handler)
    {
        Action<string> forwarder = s => handler(s); // same type, but we want to ensure a unique handler
        CroquetBridge.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for events sent with explicit scope and string-array argument.
    /// </summary>
    /// <param name="scope">The purview within which this subscription exists.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Unsubscribe().
    /// </returns>
    public static Action<string> Subscribe(string scope, string eventName, Action<string[]> handler)
    {
        Action<string> forwarder = s => handler(s.Split('\x03'));
        CroquetBridge.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for events sent with explicit scope and float argument.
    /// </summary>
    /// <param name="scope">The purview within which this subscription exists.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Unsubscribe().
    /// </returns>
    public static Action<string> Subscribe(string scope, string eventName, Action<float> handler)
    {
        Action<string> forwarder = s => handler(float.Parse(s));
        CroquetBridge.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for events sent with explicit scope and float-array argument.
    /// </summary>
    /// <param name="scope">The purview within which this subscription exists.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Unsubscribe().
    /// </returns>
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
        CroquetBridge.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Listen for events sent with explicit scope and boolean argument.
    /// </summary>
    /// <param name="scope">The purview within which this subscription exists.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="handler">the designated Action to call when the given event fires.</param>
    /// <returns>
    /// The Action object created to forward the events for this subscription.
    /// To cancel the subscription, this value is required as one of the arguments to Unsubscribe().
    /// </returns>
    public static Action<string> Subscribe(string scope, string eventName, Action<bool> handler)
    {
        Action<string> forwarder = s => handler(bool.Parse(s));
        CroquetBridge.SubscribeToCroquetEvent(scope, eventName, forwarder);
        return forwarder;
    }

    /// <summary>
    /// Unsubscribe from events sent with explicit scope.
    /// </summary>
    /// <param name="scope">The purview within which this subscription exists.</param>
    /// <param name="eventName">a string representing the name of the event</param>
    /// <param name="forwarder">The Action object that was returned by the Subscribe() call that you wish to cancel</param>
    public static void Unsubscribe(string scope, string eventName, Action<string> forwarder)
    {
        CroquetBridge.UnsubscribeFromCroquetEvent(scope, eventName, forwarder);
    }

    #endregion

    #region Actor Property Access

    /// <summary>
    /// Test if gameObject's corresponding actor has supplied a value (either static or watched) for the named property.
    /// </summary>
    /// <param name="gameObject">the gameObject of interest</param>
    /// <param name="propertyName">the property to check</param>
    /// <returns>True if a value is available</returns>
    public static bool HasActorSentProperty(GameObject gameObject, string propertyName)
    {
        return CroquetEntitySystem.Instance.HasActorSentProperty(gameObject, propertyName);
    }

    /// <summary>
    /// Read a string-valued property that has been supplied by the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject of interest</param>
    /// <param name="propertyName">the property to read</param>
    /// <returns>The string value</returns>
    public static string ReadActorString(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return stringVal;
    }

    /// <summary>
    /// Read a string-array-valued property that has been supplied by the gameObject's corresponding actor.
    /// </summary>

    /// <param name="gameObject">the gameObject of interest</param>
    /// <param name="propertyName">the property to read</param>
    /// <returns>The string-array value</returns>
    public static string[] ReadActorStringArray(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return stringVal.Split('\x03');
    }

    /// <summary>
    /// Read a float-valued property that has been supplied by the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject of interest</param>
    /// <param name="propertyName">the property to read</param>
    /// <returns>The float value</returns>
    public static float ReadActorFloat(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return float.Parse(stringVal);
    }

    /// <summary>
    /// Read a float-array-valued property that has been supplied by the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject of interest</param>
    /// <param name="propertyName">the property to read</param>
    /// <returns>The float-array value</returns>
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

    /// <summary>
    /// Read a boolean-valued property that has been supplied by the gameObject's corresponding actor.
    /// </summary>
    /// <param name="gameObject">the gameObject of interest</param>
    /// <param name="propertyName">the property to read</param>
    /// <returns>The boolean value</returns>
    public static bool ReadActorBool(GameObject gameObject, string propertyName)
    {
        string stringVal = CroquetEntitySystem.Instance.GetPropertyValueString(gameObject, propertyName);
        return bool.Parse(stringVal);
    }

    #endregion

    /// <summary>
    /// Provide the name to be used by the Croquet JavaScript session that will synchronize the running of this app.
    /// This call is for use by a session-selection mechanism such as a menu or lobby interface, once the session name has
    /// been decided. This call triggers the launch of the JavaScript session.
    /// </summary>
    /// <param name="sessionName">name (a non-spaced token), or an empty string to specify use of the defaultSessionName as set on the Croquet Bridge</param>
    public static void SetSessionName(string sessionName)
    {
        CroquetBridge.Instance.SetSessionName(sessionName);
    }

    /// <summary>
    /// A time source (in seconds) that should be within a few ms across all clients.
    /// This is NOT the session's Teatime (which increases monotonically across
    /// starts and stops over hours and days), but a measure of how long this
    /// particular session has been running on its assigned reflector.
    /// NB: during game startup (at least until the Croquet session has started) this
    /// will return the default value -1f.
    /// </summary>
    /// <returns>floating-point session time or -1f before the session has started.</returns>
    public static float SessionTime()
    {
        return CroquetBridge.Instance.CroquetSessionTime();
    }

    /// <summary>
    /// Ask to switch all users in the current session to the specified scene.
    /// </summary>
    /// <param name="sceneBuildIndex">The scene's index in the Unity Build Settings</param>
    /// <param name="forceReload">If the app turns out to be in the specified scene already, should the scene be re-initialized?</param>
    public static void RequestToLoadScene(int sceneBuildIndex, bool forceReload)
    {
        RequestToLoadScene(sceneBuildIndex, forceReload, false); // don't normally force a rebuild
    }

    /// <summary>
    /// Ask to switch all users in the current session to the specified scene.
    /// </summary>
    /// <param name="sceneBuildIndex">The scene's index in the Unity Build Settings</param>
    /// <param name="forceReload">If the app turns out to be in the specified scene already, should the scene be re-initialized?</param>
    /// <param name="forceRebuild">If the scene is being initialized, should it ignore any cached definition and ask Unity again?</param>
    public static void RequestToLoadScene(int sceneBuildIndex, bool forceReload, bool forceRebuild)
    {
        string path = SceneUtility.GetScenePathByBuildIndex(sceneBuildIndex);
        if (path == "")
        {
            Debug.LogError($"Failed to find scene with buildIndex {sceneBuildIndex}");
            return;
        }

        string filename = Path.GetFileNameWithoutExtension(path);
        if (filename == "")
        {
            Debug.LogError($"Failed to parse scene-file name for buildIndex {sceneBuildIndex} from {path}");
            return;
        }

        RequestToLoadScene(filename, forceReload, forceRebuild);
    }

    /// <summary>
    /// Ask to switch all users in the current session to the specified scene.
    /// </summary>
    /// <param name="sceneName">The scene's name</param>
    /// <param name="forceReload">If the app turns out to be in the specified scene already, should the scene be re-initialized?</param>
    public static void RequestToLoadScene(string sceneName, bool forceReload)
    {
        RequestToLoadScene(sceneName, forceReload, false); // don't normally force a rebuild
    }

    /// <summary>
    /// Ask to switch all users in the current session to the specified scene.
    /// </summary>
    /// <param name="sceneName">The scene's name</param>
    /// <param name="forceReload">If the app turns out to be in the specified scene already, should the scene be re-initialized?</param>
    /// <param name="forceRebuild">If the scene is being initialized, should it ignore any cached definition and ask Unity again?</param>
    public static void RequestToLoadScene(string sceneName, bool forceReload, bool forceRebuild)
    {
        CroquetBridge.Instance.RequestToLoadScene(sceneName, forceReload, forceRebuild);
    }
}

