using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;

using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>
/// Handles Creation and Destruction of Objects.
/// Maintains the mapping between the model and the view objects.
/// </summary>
public class CroquetEntitySystem : CroquetSystem
{
    // manages preloading the addressableAssets
    private Dictionary<string, GameObject> addressableAssets;
    public bool addressablesReady = false; // make public read or emit event to inform other systems that the assets are loaded
    
    // Create Singleton Reference
    public static CroquetEntitySystem Instance { get; private set; }

    public override List<string> KnownCommands { get; } = new List<string>()
    {
        "makeObject",
        "destroyObject",
        "tearDownSession"
    };
    
    protected override Dictionary<int, CroquetComponent> components { get; set; } =
        new Dictionary<int, CroquetComponent>();

    private Dictionary<string, int> CroquetHandleToInstanceID = new Dictionary<string, int>();

    private void AssociateCroquetHandleToInstanceID(string croquetHandle, int id)
    {
        CroquetHandleToInstanceID.Add(croquetHandle, id);
    }
    
    private void DisassociateCroquetHandleToInstanceID(string croquetHandle)
    {
        CroquetHandleToInstanceID.Remove(croquetHandle);
    }
    
    /// <summary>
    /// Get GameObject with a specific Croquet Handle
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public GameObject GetGameObjectByCroquetHandle(string croquetHandle)
    {
        CroquetComponent croquetComponent;

        if (CroquetHandleToInstanceID.ContainsKey(croquetHandle))
        {
            int instanceID = CroquetHandleToInstanceID[croquetHandle];
            if (components.TryGetValue(instanceID, out croquetComponent))
            {
                return croquetComponent.gameObject;
            }
        }

        Debug.Log($"Failed to find object {croquetHandle}");
        return null;
    }

    public static int GetInstanceIDByCroquetHandle(string croquetHandle)
    {
        GameObject go = Instance.GetGameObjectByCroquetHandle(croquetHandle);
        if (go != null)
        {
            return go.GetInstanceID();
        }

        return 0; // TODO: remove sentinel in favor of unwrapping optional
    }

    private void Awake()
    {
        // Create Singleton Accessor
        // If there is an instance, and it's not me, delete myself.
        if (Instance != null && Instance != this) 
        { 
            Destroy(this);
        }
        else
        { 
            Instance = this; 
        }
        
        addressableAssets = new Dictionary<string, GameObject>();
    }
    
    private void Start()
    {
        CroquetBridge.Instance.RegisterSystem(this);
        StartCoroutine(LoadAddressableAssetsWithLabel(CroquetBridge.Instance.appName));
    }
    
    IEnumerator LoadAddressableAssetsWithLabel(string label)
    {
        // @@ LoadAssetsAsync throws an error - asynchronously - if there are
        // no assets that match the key.  One way to avoid that error is to run
        // the following code to get a list of locations matching the key.
        // If the list is empty, don't run the LoadAssetsAsync.
        // Presumably there are more efficient ways to do this (in particular, when
        // there *are* matches).  Maybe by using the list?

        //Returns any IResourceLocations that are mapped to the supplied label
        AsyncOperationHandle<IList<IResourceLocation>> handle = Addressables.LoadResourceLocationsAsync(label);
        yield return handle;

        IList<IResourceLocation> result = handle.Result;
        int prefabs = 0;
        foreach (var loc in result) {
            if (loc.ToString().EndsWith(".prefab")) prefabs++;
        }
        // int count = result.Count;
        // Debug.Log($"Found {prefabs} addressable prefabs");
        Addressables.Release(handle);

        if (prefabs != 0)
        {
            // Load any assets labelled with this appName from the Addressable Assets
            Addressables.LoadAssetsAsync<GameObject>(label, null).Completed += objects =>
            {
                foreach (var go in objects.Result)
                {
                    Debug.Log($"Addressable Loaded: {go.name}");
                    addressableAssets.Add(go.name.ToLower(), go); // @@ remove case-sensitivity
                }
                addressablesReady = true;
            };
        }
        else
        {
            Debug.Log($"No addressable assets are tagged '{label}'");
            addressablesReady = true;
        }
    }

    public override void ProcessCommand(string command, string[] args)
    {
        if (command.Equals("makeObject"))
        {
            MakeObject(args);
        }
        else if (command.Equals("destroyObject"))
        {
            DestroyObject(args[0]);
        }
        else if (command.Equals("tearDownSession"))
        {
            TearDownSession();
        }
    }
    
    void MakeObject(string[] args)
    {
        ObjectSpec spec = JsonUtility.FromJson<ObjectSpec>(args[0]);
        // Debug.Log($"making object {spec.cH}");

        // try to find a prefab with the given name
        GameObject gameObjectToMake;
        if (spec.type.StartsWith("primitive"))
        {
            PrimitiveType primType = PrimitiveType.Cube;
            if (spec.type == "primitiveSphere") primType = PrimitiveType.Sphere;
            else if (spec.type == "primitiveCapsule") primType = PrimitiveType.Capsule;
            else if (spec.type == "primitiveCylinder") primType = PrimitiveType.Cylinder;
            else if (spec.type == "primitivePlane") primType = PrimitiveType.Plane;

            gameObjectToMake = CreateCroquetPrimitive(primType, Color.blue);
        }
        else
        {
            if (addressableAssets.ContainsKey(spec.type.ToLower()))
            {
                gameObjectToMake = Instantiate(addressableAssets[spec.type.ToLower()]); // @@ remove case-sensitivity
            }
            else
            {
                Debug.Log( $"Specified spec.type ({spec.type}) is not found as a prefab! Creating Cube as Fallback Object");
                gameObjectToMake = CreateCroquetPrimitive(PrimitiveType.Cube, Color.magenta);
            }
        }

        if (gameObjectToMake.GetComponent<CroquetEntityComponent>() == null){
            gameObjectToMake.AddComponent<CroquetEntityComponent>();
        }
        
        CroquetEntityComponent entity = gameObjectToMake.GetComponent<CroquetEntityComponent>();
        entity.croquetHandle = spec.cH;
        int instanceID = gameObjectToMake.GetInstanceID();
        AssociateCroquetHandleToInstanceID(spec.cH, instanceID);

        if (spec.cN != "")
        {
            entity.croquetActorId = spec.cN;
            CroquetBridge.Instance.FixUpEarlyEventActions(gameObjectToMake, entity.croquetActorId);
        }
        
        if (spec.cs != "")
        {
            string[] comps = spec.cs.Split(',');
            foreach (string compName in comps)
            {
                try
                {
                    Type typeToAdd = Type.GetType(compName);
                    if (typeToAdd == null)
                    {
                        string assemblyQualifiedName =
                            System.Reflection.Assembly.CreateQualifiedName("Assembly-CSharp", compName);
                        typeToAdd = Type.GetType(assemblyQualifiedName);
                    }
                    if (typeToAdd == null)
                    {
                        // blew it
                        Debug.LogError($"Unable to find component {compName} in package or main assembly");
                    }
                    else
                    {
                        if (gameObjectToMake.GetComponent(typeToAdd) == null)
                        {
                            gameObjectToMake.AddComponent(typeToAdd);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in adding component {compName}: {e}");
                }
            }
        }
        
        if (spec.wTP)
        {
            foreach (Renderer renderer in gameObjectToMake.GetComponentsInChildren<Renderer>())
            {
                renderer.enabled = false;
            }
        }
        
        if (spec.cC)
        {
            CroquetBridge.Instance.SendToCroquet("objectCreated", spec.cH, DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());
        }
    }
    
    void DestroyObject(string croquetHandle)
    {
        // Debug.Log( "Destroying Object " + croquetHandle.ToString());

        if (CroquetHandleToInstanceID.ContainsKey(croquetHandle))
        {
            int instanceID = CroquetHandleToInstanceID[croquetHandle];
            //components.Remove(instanceID);
            
            // INFORM OTHER COMPONENT'S SYSTEMS THEY ARE TO BE UNREGISTERED
            GameObject go = GetGameObjectByCroquetHandle(croquetHandle);
            CroquetComponent[] componentsToUnregister  = go.GetComponents<CroquetComponent>();
            foreach (var componentToUnregister in componentsToUnregister)
            {
                CroquetSystem system = componentToUnregister.croquetSystem;
                system.UnregisterComponent(componentToUnregister); //crosses fingers
            }
            
            
            CroquetBridge.Instance.RemoveCroquetSubscriptionsFor(go);
            
            
            DisassociateCroquetHandleToInstanceID(croquetHandle);

            Destroy(go);
        }
        else
        {
            // asking to destroy a pawn for which there's no view can happen just because of
            // creation/destruction timing in worldcore.  not necessarily a problem.
            Debug.Log($"attempt to destroy absent object {croquetHandle}");
        }
    }

    void TearDownSession()
    {
        // destroy everything in the scene for the purposes of rebuilding when the
        // connection is reestablished.
        
        List<CroquetComponent> componentsToDelete = components.Values.ToList();
        foreach (CroquetComponent component in componentsToDelete)
        {
            CroquetEntityComponent entityComponent = component as CroquetEntityComponent;
            if (entityComponent != null)
            {
                DestroyObject(entityComponent.croquetHandle);
            }
        }
    }
    
    GameObject CreateCroquetPrimitive(PrimitiveType type, Color color)
    {
        GameObject go = new GameObject();
        go.name = $"primitive{type.ToString()}";
        go.AddComponent<CroquetEntityComponent>();
        GameObject inner = GameObject.CreatePrimitive(type);
        inner.transform.parent = go.transform;
        return go;
    }
}

[System.Serializable]
public class ObjectSpec
{
    public string cH; // croquet handle: currently an integer, but no point converting all the time
    public string cN; // Croquet name (generally, the model id)
    public bool cC; // confirmCreation: whether Croquet is waiting for a confirmCreation message for this 
    public bool wTP; // waitToPresent:  whether to make visible immediately
    public string type;
    public string cs; // comma-separated list of extra components
}
