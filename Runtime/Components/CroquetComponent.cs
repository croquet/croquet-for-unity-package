using System.Collections;
using System.Collections.Generic;
using UnityEditor.PackageManager;
using UnityEngine;

public abstract class CroquetComponent : MonoBehaviour
{
    public abstract CroquetSystem croquetSystem { get; set; }

    void Awake()
    {
        if (croquetSystem == null)
        {
            Debug.Log($"Futile attempt to awaken {this} ");
            croquetSystem = FindObjectOfType<CroquetSystem>();
            try
            {
                RegisterInAllSystems(this);
            }
            catch
            {
                Debug.Log($"Failed to register {this} in {croquetSystem}");
            }
        }
        else
        {
            if (croquetSystem != null)
            {
                try
                {
                    RegisterInAllSystems(this);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to register {this} in {croquetSystem}: {e} with instanceID {this.GetInstanceID()}, trying to unregister and register again");
                    Debug.Log("Current component for this instanceID: " + croquetSystem.KnowsObject(this.gameObject) + " with instanceID " + this.GetInstanceID());
                    croquetSystem.GetComponentFromID(this.GetInstanceID());
                    croquetSystem.UnregisterComponent(this);
                    Debug.Log($"Unregistered {this} in {croquetSystem} with instanceID {this.GetInstanceID()}");
                    RegisterInAllSystems(this);
                }
            }
        }
    }
    bool IsObjectReadyForOperations(GameObject obj)
    {
        return croquetSystem.KnowsObject(obj) && CroquetSpatialSystem.Instance.KnowsObject(obj);
    }

    protected void RegisterInAllSystems(CroquetComponent component)
    {
        croquetSystem.RegisterComponent(component);

        if (component is CroquetEntityComponent)
        {
            CroquetEntitySystem.Instance.RegisterComponent(component);
        }
        else if (component is CroquetSpatialComponent)
        {
            CroquetSpatialSystem.Instance.RegisterComponent(component);
        }
        else if (component is CroquetDrivableComponent)
        {
            CroquetDrivableSystem.Instance.RegisterComponent(component);
        }
        else if (component is CroquetMaterialComponent)
        {
            CroquetMaterialSystem.Instance.RegisterComponent(component);
        }
    }


}
