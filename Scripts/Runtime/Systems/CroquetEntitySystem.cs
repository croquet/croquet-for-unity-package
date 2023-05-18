using System;
using System.Collections;
using System.Collections.Generic;
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
    private Dictionary<string, GameObject> addressableAssets;
    private bool addressablesReady = false; // make public read or emit event to inform other systems that the assets are loaded
    
    // Create Singleton Reference
    public static CroquetEntitySystem Instance { get; private set; }

    public override List<string> KnownCommands { get; } = new List<string>()
    {
        "makeObject",
        "destroyObject"
    };

    protected override Dictionary<string, CroquetComponent> components { get; set; } =
        new Dictionary<string, CroquetComponent>();

    /// <summary>
    /// Find GameObject with a specific Croquet ID
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public GameObject FindObject(string id)
    {
        CroquetComponent croquetComponent;

        if (components.TryGetValue(id, out croquetComponent))
        {
            return croquetComponent.gameObject;
        }
        Debug.Log($"Failed to find object {id}");
        return null;
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
    }
    
    void MakeObject(string[] args)
    {
        ObjectSpec spec = JsonUtility.FromJson<ObjectSpec>(args[0]);
        Debug.Log($"making object {spec.id}");

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

        CroquetEntityComponent entity = gameObjectToMake.GetComponent<CroquetEntityComponent>();
        entity.croquetGameHandle = spec.id;
        components.Add(spec.id, entity);
        if (spec.cN != "") entity.croquetActorId = spec.cN;

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

        gameObjectToMake.SetActive(!spec.wTA);

        components[spec.id] = entity;

        // gameObjectToMake.transform.localScale = new Vector3(spec.s[0], spec.s[1], spec.s[2]);
        // // normalise the quaternion because it's potentially being sent with reduced precision
        // gameObjectToMake.transform.localRotation = Quaternion.Normalize(new Quaternion(spec.r[0], spec.r[1], spec.r[2], spec.r[3]));
        // gameObjectToMake.transform.localPosition = new Vector3(spec.t[0], spec.t[1], spec.t[2]);

        if (spec.cC)
        {
            CroquetBridge.Instance.SendToCroquet("objectCreated", spec.id.ToString(), DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());
        }
    }
    
    void DestroyObject(string id)
    {
        Debug.Log( "destroying object " + id.ToString());
        // if (cameraOwnerId == id)
        // {
        //     cameraOwnerId = "";
        //     GameObject camera = GameObject.FindWithTag("MainCamera");
        //     camera.transform.parent = null;
        // }
        
        if (components.ContainsKey(id))
        {
            GameObject obj = components[id].gameObject;
            
            //TODO: CALL ALL SYSTEMS ONDESTROY for this ID
            
            Destroy(obj);
            components.Remove(id);
        }
        else
        {
            // asking to destroy a pawn for which there's no view can happen just because of
            // creation/destruction timing in worldcore.  not necessarily a problem.
            Debug.Log($"attempt to destroy absent object {id}");
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
    public string id; // currently an integer, but no point converting all the time
    public string cN; // Croquet name (generally, the model id)
    public bool cC; // confirmCreation: whether Croquet is waiting for a confirmCreation message for this 
    public bool wTA; // waitToActivate:  whether to make visible immediately, or only on first posn update
    public string type;
    public string cs; // comma-separated list of extra components
}
