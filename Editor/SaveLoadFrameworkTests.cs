using UnityEditor;
using UnityEngine;
using System.IO;
using EyE.Serialization;
using System.Collections.Generic;

// Example 1: Class using ISaveLoad Interface
public class TestData : ISaveLoad
{
    public int intValue;
    public string stringValue;

    public void Serialize(IDataWriter writer)
    {
        writer.Write(intValue, nameof(intValue));
        writer.Write(stringValue, nameof(stringValue));
    }

    public static TestData ReadAndCreate(IDataReader reader)
    {
        var t = new TestData();
        t.intValue = reader.Read<int>(nameof(intValue));
        t.stringValue = reader.Read<string>(nameof(stringValue));
        return t;
    }
}

// Example 2: Attribute-based SaveLoadHandler for Vector3
[SaveLoadHandler(typeof(Vector3))]
public static class Vector3SaveLoad
{
    public static void Save(IDataWriter writer, Vector3 obj)
    {
        Vector3 v = (Vector3)obj;
        writer.Write(v.x, "x");
        writer.Write(v.y, "y");
        writer.Write(v.z, "z");
    }

    public static Vector3 Load(IDataReader reader)
    {
        float x = reader.Read<float>("x");
        float y = reader.Read<float>("y");
        float z = reader.Read<float>("z");
        return new Vector3(x, y, z);
    }
}

public static class SaveLoadFrameworkTests
{
    [MenuItem("Tools/Run SaveLoadFramework Unit Test (ISaveLoad + Attribute)")]
    public static void RunTest()
    {
        Debug.Log("=== SaveLoadFramework Unit Test ===");

        bool allPass = true;

        // --- Test 1: ISaveLoad interface ---
        TestData data = new TestData { intValue = 42, stringValue = "Hello World" };
        string path1 = Path.Combine(Application.dataPath, "testdata.json");

        // Serialize
        using (var sw = new StreamWriter(path1))
        {
            var writer = new JsonDataWriter(sw);
            data.Serialize(writer);
            writer.Flush();
        }

        // Deserialize
        TestData loaded;
        using (var sr = new StreamReader(path1))
        {
            var reader = new JsonDataReader(sr);
            loaded = TestData.ReadAndCreate(reader);
        }

        bool pass1 = (loaded.intValue == data.intValue) && (loaded.stringValue == data.stringValue);
        Debug.Log($"ISaveLoad Test: intValue={loaded.intValue}, stringValue={loaded.stringValue}");

        if (pass1)
            Debug.Log("<color=green>ISaveLoad PASSED</color>");
        else
        {
            Debug.LogError("<color=red>ISaveLoad FAILED</color>");
            allPass = false;
        }

        // --- Test 2: Attribute-based handler (Vector3) ---
        Vector3 vec = new Vector3(1.1f, 2.2f, 3.3f);
        string path2 = Path.Combine(Application.dataPath, "vector3.json");

        // Serialize
        using (var sw = new StreamWriter(path2))
        {
            var writer = new JsonDataWriter(sw);
            writer.Write(vec, "vector"); // Attribute-based handler should be invoked
            writer.Flush();
        }

        // Deserialize
        Vector3 loadedVec;
        using (var sr = new StreamReader(path2))
        {
            var reader = new JsonDataReader(sr);
            loadedVec = reader.Read<Vector3>("vector"); // Attribute-based handler should be invoked
        }

        bool pass2 = Mathf.Approximately(loadedVec.x, vec.x) &&
                     Mathf.Approximately(loadedVec.y, vec.y) &&
                     Mathf.Approximately(loadedVec.z, vec.z);
        Debug.Log($"Attribute Test: Vector3=({loadedVec.x}, {loadedVec.y}, {loadedVec.z})");

        if (pass2)
            Debug.Log("<color=green>Attribute-based Vector3 PASSED</color>");
        else
        {
            Debug.LogError("<color=red>Attribute-based Vector3 FAILED</color>");
            allPass = false;
        }

        if (allPass)
            Debug.Log("<color=green>ALL SaveLoadFramework tests PASSED</color>");
        else
            Debug.LogError("<color=red>One or more SaveLoadFramework tests FAILED</color>");

        // Clean up
        if (File.Exists(path1)) File.Delete(path1);
        if (File.Exists(path2)) File.Delete(path2);
    }
}