using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace EyE.Serialization
{
    /// <summary>
    /// Class to hold filePath to object information.  Created at editor time, allows getting the path of given assets, **in final build runtime**.
    /// for completeness provides the opposite(object from path), which is already available via the unity Resources class.
    /// </summary>
    public class ResourceReferenceManager : ScriptableObject,ISerializationCallbackReceiver
    {
        /// <summary>
        /// primary function of class.  does not display error or throw exceptions if object passed in, is not found in the collection.
        /// </summary>
        /// <param name="obj">a UnityEngine Object</param>
        /// <returns>the path of the object, if stored in the collection.  string.Empty otherwise</returns>
        public static string GetPathOfObject(UnityEngine.Object obj)
        {
            string path;
            if (Instance != null && Instance.objectToPathDict.TryGetValue(obj, out path))
            {
                return path;
            }
            return string.Empty;
        }
        //primary- convinience/consistency function of class
        public static UnityEngine.Object GetObjectByPath(string path)
        {
            return Resources.Load<UnityEngine.Object>(path);
        }
        //primary- convinience/consistency function of class
        public static T GetObjectByPath<T>(string path) where T:UnityEngine.Object
        {
            return Resources.Load<T>(path);
        }


        #region creation, serialization and singleton stuff
        [System.Serializable]
        public class Entry
        {
            public UnityEngine.Object asset;
            public string path;
        }

        [SerializeField]
        private List<Entry> entries = new List<Entry>();

        static private ResourceReferenceManager Create(List<Entry> entries)
        {
            ResourceReferenceManager manager = ScriptableObject.CreateInstance<ResourceReferenceManager>();
            manager.entries = entries;
            manager.BuildLookup();
            return manager;
        }

        private Dictionary<UnityEngine.Object, string> objectToPathDict = new Dictionary<Object, string>();
        private static ResourceReferenceManager instance = null;

        private static string OutputPath => storagePath + "/" + filenameWithExtension;
        private const string storagePath = "Assets/Resources";
        private const string filename = "ResourceManager";
        private static string filenameWithExtension => filename + ".asset";

        /// <summary>
        /// rebuild dictionary from serializable list
        /// </summary>
        private void BuildLookup()
        {
            objectToPathDict = new Dictionary<UnityEngine.Object, string>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.asset != null && !objectToPathDict.ContainsKey(entry.asset))
                {
                    objectToPathDict.Add(entry.asset, entry.path);
                }
            }
        }

        /// <summary>
        /// Resource singleton instance-  will use the resource named "ResourceManager", or if it does not exist, build one( and use that
        /// </summary>
        public static ResourceReferenceManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<ResourceReferenceManager>(filename);
                    if (instance != null && instance.objectToPathDict == null)
                    {
                        instance.BuildLookup();
                    }
                    else
                    {
#if UNITY_EDITOR
                        ResourceReferenceManager.BuildNew();
#endif
                    }
                }
                return instance;
            }
        }

        #endregion

        static string AssetPathToResourcesPath(string assetPath)
        {
            const string resourcesFolder = "/Resources/";
            int index = assetPath.IndexOf(resourcesFolder, System.StringComparison.OrdinalIgnoreCase);
            if (index == -1)
                throw new System.ArgumentException("Asset path is not inside a Resources folder: " + assetPath);

            int start = index + resourcesFolder.Length;
            string path = assetPath.Substring(start);
            int lastDot = path.LastIndexOf('.');
            if (lastDot != -1)
                path = path.Substring(0, lastDot);
            return path.Replace('\\', '/');
        }

#if UNITY_EDITOR
        /// <summary>
        /// Creates a new asset of this type in the resources folder and populates it with current assets found in there.  
        /// Saves the result as an asset- overwriting existing, and assigns it as THE instance.
        /// </summary>
        [UnityEditor.MenuItem("Tools/Build ResourceManager Asset")]
        public static void BuildNew()
        {
            Debug.Log("starting rebuild of ResourceManager Asset");
            UnityEngine.Object[] allAssets = Resources.LoadAll<UnityEngine.Object>("");

            List<ResourceReferenceManager.Entry> entries = new List<ResourceReferenceManager.Entry>();

            for (int i = 0; i < allAssets.Length; i++)
            {
                UnityEngine.Object asset = allAssets[i];
                if (asset == instance) continue;

                string fullPath = UnityEditor.AssetDatabase.GetAssetPath(asset); //this function is our limiter- only available in unity editor- which is the reason this class exists.

                ResourceReferenceManager.Entry entry = new ResourceReferenceManager.Entry();
                entry.asset = asset;
                entry.path = AssetPathToResourcesPath(fullPath);
                entries.Add(entry);
            }

            ResourceReferenceManager manager = ResourceReferenceManager.Create(entries);

            Directory.CreateDirectory(storagePath);
            UnityEditor.AssetDatabase.DeleteAsset(OutputPath);
            UnityEditor.AssetDatabase.CreateAsset(manager, OutputPath);
            UnityEditor.AssetDatabase.SaveAssets();
            instance = manager;
            Debug.Log("ResourceManager built with " + entries.Count + " entries.");
        }
#endif
        public void OnBeforeSerialize()
        {
            //it's fine- serializable list is always built
        }
        public void OnAfterDeserialize()
        {
            BuildLookup();
        }

    }
   
}