using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using EyE.Serialization;
using System;

// Test class with various nested/complex types
public class ComplexData : ISaveLoad
{
    public int id;
    public string name;
    public List<Dictionary<string, List<float>>> listOfDicts= new List<Dictionary<string, List<float>>>();
    public Vector3 position; // attribute-based handler
    public UnityEngine.Object refPrefab; // Resource reference

    public void Serialize(IDataWriter writer)
    {
        writer.Write(id, nameof(id));
        writer.Write(name, nameof(name));
        writer.Write(listOfDicts, nameof(listOfDicts));
        writer.Write(position, nameof(position));
        writer.Write(refPrefab, nameof(refPrefab));
    }

    public static ComplexData ReadAndCreate(IDataReader reader)
    {
        var data = new ComplexData();
        data.id = reader.Read<int>(nameof(id));
        data.name = reader.Read<string>(nameof(name));
        data.listOfDicts = reader.Read<List<Dictionary<string, List<float>>>>(nameof(listOfDicts));
        data.position = reader.Read<Vector3>(nameof(position));
        data.refPrefab = reader.Read<UnityEngine.Object>(nameof(refPrefab));
        return data;
    }
}

public static class SaveLoadFrameworkAdvancedTests
{
    [MenuItem("Tools/Run SaveLoadFramework Advanced JSON Tests")]
    public static void RunAdvancedJsonTests()
    {
        RunAdvancedTests(
            writerFactory: sw => new JsonDataWriter(sw),
            readerFactory: sr => new JsonDataReader(sr),
            fileExtension: "json"
        );
    }

    [MenuItem("Tools/Run SaveLoadFramework Advanced Binary Tests")]
    public static void RunAdvancedBinaryTests()
    {
        RunAdvancedTests(
            writerFactory: sw => new BinaryDataWriter(new BinaryWriter(sw.BaseStream)),
            readerFactory: sr => new BinaryDataReader(new BinaryReader(sr.BaseStream)),
            fileExtension: "bin"
        );
    }

    private static void RunAdvancedTests(
    Func<StreamWriter, IDataWriter> writerFactory,
    Func<StreamReader, IDataReader> readerFactory,
    string fileExtension)
    {
        Debug.Log("=== SaveLoadFramework Advanced/Edge Case Tests ===");

        // Step 1: Ensure Prefabs Exist in Resources
        string resourcesPath = "Assets/Resources/";
        string[] prefabNames = { "TestPrefabA", "TestPrefabB", "TestPrefabC" };
        List<UnityEngine.Object> testPrefabs = new List<UnityEngine.Object>();
        foreach (var n in prefabNames)
        {
            var loaded1 = Resources.Load<GameObject>(n);
            if (loaded1 == null)
            {
                // Create a cube in the scene, name it uniquely, and save as prefab
                GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tempCube.name = n;
                string prefabAssetPath = resourcesPath + n + ".prefab";
                Directory.CreateDirectory(resourcesPath);
                PrefabUtility.SaveAsPrefabAsset(tempCube, prefabAssetPath);
                GameObject.DestroyImmediate(tempCube);

                Debug.Log($"Created and saved prefab '{n}' in Resources.");
            }

        }
        //rebuild ResourceReferenceManager
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ResourceReferenceManager.BuildNew();
        
        //load test prefabs from disk
        foreach (var n in prefabNames)
        {
            GameObject loaded1 = Resources.Load<GameObject>(n);
            if (loaded1 != null)
                testPrefabs.Add(loaded1);
            else
                Debug.LogError("Unable to load resource: " + n);
        }

        // --- Test 1: Deeply Nested Structures ---
        var complex = new ComplexData
        {
            id = 123,
            name = "TestComplex",
            listOfDicts = new List<Dictionary<string, List<float>>>
            {
                new Dictionary<string, List<float>>
                {
                    { "alpha", new List<float>{ 1.1f, 2.2f } },
                    { "beta", new List<float>{ 3.3f } }
                },
                new Dictionary<string, List<float>>
                {
                    { "gamma", new List<float>{ 4.4f, 5.5f, 6.6f } },
                    { "delta", new List<float>() }
                },
                new Dictionary<string, List<float>>()
            },
            position = new Vector3(7.7f, 8.8f, 9.9f),
            refPrefab = testPrefabs.Count > 0 ? testPrefabs[0] : null
        };

        string jsonPath = Path.Combine(Application.dataPath, "advancedtest.json");

        // Serialize
        using (var sw = new StreamWriter(jsonPath))
        {
            var writer = writerFactory(sw);
            writer.Write(complex, "Complex1");
            writer.Close();
        }

        // Deserialize
        ComplexData loaded;
        using (var sr = new StreamReader(jsonPath))
        {
            var reader = readerFactory(sr);
            loaded= reader.Read<ComplexData>(null);
        }

        // Check values
        bool pass = true;
        if (loaded.id != complex.id || loaded.name != complex.name)
        {
            Debug.LogError("Basic fields mismatch");
            pass = false;
        }
        if (loaded.listOfDicts == null || loaded.listOfDicts.Count != complex.listOfDicts.Count)
        {
            Debug.LogError("List<Dictionary<string,List<float>>> count mismatch");
            pass = false;
        }
        else if (loaded.listOfDicts.Count > 0)
        {
            for (int i = 0; i < loaded.listOfDicts.Count; i++)
            {
                var origDict = complex.listOfDicts[i];
                var loadedDict = loaded.listOfDicts[i];
                if (origDict.Count != loadedDict.Count)
                {
                    Debug.LogError($"Dictionary #{i} count mismatch");
                    pass = false;
                }
                foreach (var k in origDict.Keys)
                {
                    if (!loadedDict.ContainsKey(k))
                    {
                        Debug.LogError($"Key '{k}' missing in loaded dictionary #{i}");
                        pass = false;
                    }
                    else
                    {
                        var origList = origDict[k];
                        var loadedList = loadedDict[k];
                        if (origList.Count != loadedList.Count)
                        {
                            Debug.LogError($"List count mismatch for key '{k}'  originalList.Count:"+ origList.Count+" loadedcountr:"+ loadedList.Count);
                            pass = false;
                        }
                        for (int j = 0; j < origList.Count; j++)
                        {
                            if (!Mathf.Approximately(origList[j], loadedList[j]))
                            {
                                Debug.LogError($"Float mismatch at dict #{i}, key '{k}', index {j}");
                                pass = false;
                            }
                        }
                    }
                }
            }
        }
        if (!Mathf.Approximately(loaded.position.x, complex.position.x) ||
            !Mathf.Approximately(loaded.position.y, complex.position.y) ||
            !Mathf.Approximately(loaded.position.z, complex.position.z))
        {
            Debug.LogError("Vector3 position mismatch");
            pass = false;
        }

        // --- Test 2: ResourceReferenceManager and UnityEngine.Object serialization ---
        if (testPrefabs.Count > 0)
        {
            ResourceReferenceManager.BuildNew();
            string refPath = ResourceReferenceManager.GetPathOfObject(testPrefabs[0]);
            var expectedObj = ResourceReferenceManager.GetObjectByPath(refPath);
            if (refPath == string.Empty || expectedObj == null)
            {
                Debug.LogWarning("ResourceReferenceManager could not resolve prefab reference correctly (maybe ResourceManager asset not built or prefab missing).");
                pass = false;
            }
            else if (loaded.refPrefab == null || loaded.refPrefab.name != testPrefabs[0].name)
            {
                Debug.LogError($"Prefab reference not deserialized correctly: expected '{testPrefabs[0].name}', got '{(loaded.refPrefab == null ? "null" : loaded.refPrefab.name)}'");
                pass = false;
            }
        }
        else
        {
            Debug.LogWarning("Prefab reference test skipped (no prefabs in Resources).");
        }

        // --- Test 3: Edge cases ---
        // Empty list/dict, null values, etc.
        var edge = new ComplexData
        {
            id = 0,
            name = null,
            listOfDicts = new List<Dictionary<string, List<float>>>(),
            position = Vector3.zero,
            refPrefab = null
        };

        string emptyJsonPath = Path.Combine(Application.dataPath, "advancedtest_empty.json");
        using (var sw = new StreamWriter(emptyJsonPath))
        {
            var writer = writerFactory(sw);
            writer.Write(edge, "Complex Data");
            writer.Close();
        }
        ComplexData loadedEdge;
        using (var sr = new StreamReader(emptyJsonPath))
        {
            var reader = readerFactory(sr);
            loadedEdge = reader.Read<ComplexData>(null);
        }
        bool pass_id = loadedEdge.id == edge.id;
        bool pass_name = loadedEdge.name == edge.name;
        bool pass_list = loadedEdge.listOfDicts != null;
        bool pass_listCount = loadedEdge.listOfDicts != null && loadedEdge.listOfDicts.Count == 0;
        bool pass_prefab = loadedEdge.refPrefab == null;
        bool pass_position = loadedEdge.position == Vector3.zero;

        bool testPassed = pass_id && pass_name && pass_list && pass_listCount && pass_prefab && pass_position;

        if (!testPassed)
        {
            Debug.LogError($"Edge case test FAILED:\n" +
                $"- ID Match:         {pass_id}\n" +
                $"- Name Match:       {pass_name}\n" +
                $"- List Not Null:    {pass_list}\n" +
                $"- List Is  Empty:   {pass_listCount}\n" +
                $"- Prefab Null:      {pass_prefab}\n" +
                $"- Position Zero:    {pass_position}");
            pass = false;
        }
        else
        {
            Debug.Log("Edge case test PASSED");
        }

        if (pass)
            Debug.Log("<color=green>Advanced/Edge SaveLoadFramework tests PASSED</color>");
        else
            Debug.LogError("<color=red>One or more Advanced/Edge SaveLoadFramework tests FAILED</color>");

        // Clean up
     //   if (File.Exists(jsonPath)) File.Delete(jsonPath);
       // if (File.Exists(emptyJsonPath)) File.Delete(emptyJsonPath);
    }
}