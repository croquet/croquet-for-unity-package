using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

[InitializeOnLoad]
public class CroquetDependencyAdder
{
    static CroquetDependencyAdder()
    {
        EditorApplication.delayCall += AddDependency;
    }

    static void AddDependency()
    {
        EditorApplication.delayCall -= AddDependency;

        string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");

        if (File.Exists(manifestPath))
        {
            string manifestJson = File.ReadAllText(manifestPath);
            var manifestDict = (Dictionary<string, object>)MiniJSON.Json.Deserialize(manifestJson);

            if (manifestDict.TryGetValue("dependencies", out object dependenciesObj))
            {
                var dependencies = (Dictionary<string, object>)dependenciesObj;

                string dependencyKey = "net.gree.unity-webview";
                string dependencyValue = "https://github.com/gree/unity-webview.git?path=/dist/package-nofragment";

                if (!dependencies.ContainsKey(dependencyKey))
                {
                    dependencies[dependencyKey] = dependencyValue;
                    string newManifestJson = MiniJSON.Json.Serialize(manifestDict);
                    File.WriteAllText(manifestPath, newManifestJson);

                    AssetDatabase.Refresh();

                    Debug.Log(dependencyKey + " dependency added to manifest.json");
                }
            }
        }
        else
        {
            Debug.LogError("Could not find the manifest.json file.");
        }
    }
}
