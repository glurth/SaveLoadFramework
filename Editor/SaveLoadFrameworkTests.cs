using UnityEditor;
using UnityEngine;
using System.IO;
using EyE.Serialization;
using System.Collections.Generic;
using System;
// Example 1: Class using ISaveLoad Interface
public class TestData : ISaveLoad
{
    public int intValue;
    public string stringValue;

    public void Serialize(IDataWriter writer)
    {
        writer.Write(intValue, "intValue");
        writer.Write(stringValue,"stringValue");
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
    [MenuItem("Tools/Run SaveLoadFramework Basic JSON Unit Test (ISaveLoad + Attribute)")]
    public static void RunJsonTests()
    {
        RunTest(
            writerFactory: sw => new JsonDataWriter(sw),
            readerFactory: sr => new JsonDataReader(sr),
            fileExtension: "json"
        );
    }

    [MenuItem("Tools/Run SaveLoadFramework Basic BINARY Unit Test (ISaveLoad + Attribute)")]
    public static void RunBinaryTests()
    {
        RunTest(
            writerFactory: sw => new BinaryDataWriter(new BinaryWriter(sw.BaseStream)),
            readerFactory: sr => new BinaryDataReader(new BinaryReader(sr.BaseStream)),
            fileExtension: "bin"
        );
    }
    
    public static void RunTest(
            Func<StreamWriter, IDataWriter> writerFactory,
            Func<StreamReader, IDataReader> readerFactory,
            string fileExtension)
    {
        Debug.Log("=== SaveLoadFramework Unit Test ===");

        bool allPass = true;

        // --- Test 1: ISaveLoad interface ---
        TestData data = new TestData { intValue = 42, stringValue = "Hello World" };
        string path1 = Path.Combine(Application.dataPath, "testdata.json");

        // Serialize
        using (var sw = new StreamWriter(path1))
        {
            var writer = writerFactory(sw);
            writer.Write<TestData>(data,"TestDataObject");
            writer.Close();
        }

        // Deserialize
        TestData loaded;
        using (var sr = new StreamReader(path1))
        {
            var reader = readerFactory(sr);
            loaded = reader.Read<TestData>("TestDataObject");
        }

        bool pass1 = (loaded.intValue == data.intValue) && (loaded.stringValue == data.stringValue);
        Debug.Log($"ISaveLoad Test:  orginal---intValue={data.intValue}, stringValue={data.stringValue}     Loaded--intValue={loaded.intValue}, stringValue={loaded.stringValue}");

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
        using (FileStream fs = File.Create(path2))
        {
            var writer = writerFactory(new StreamWriter(fs));
            writer.Write<Vector3>(vec, "vector");
            writer.Close(); // Needed to write closing }
        }

        // Deserialize
        Vector3 loadedVec;
        using (FileStream fs = File.OpenRead(path2))
        {
            var reader = readerFactory(new StreamReader(fs));
            loadedVec = reader.Read<Vector3>("vector");
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



        //test dictionary writing
        Dictionary<Vector3, bool[]> testBoolArrayDic = new Dictionary<Vector3, bool[]>
        {
            { new Vector3(1, 2, 3), new bool[] { true, false, true } },
            { new Vector3(0, 0, 0), new bool[] { false, false } },
            { new Vector3(-1, -2, -3), new bool[] { true } }
        };
        string path3 = Path.Combine(Application.dataPath, "boolArrayDictionary.json");

        // Serialize
        using (FileStream fs = File.Create(path3))
        {
            var writer = writerFactory(new StreamWriter(fs));
            writer.Write<Dictionary<Vector3, bool[]>>(testBoolArrayDic, "dictionary");
            writer.Close(); // Needed to write closing }
        }

        // Deserialize
        Dictionary<Vector3, bool[]> loadedTestBoolArrayDic;
        using (FileStream fs = File.OpenRead(path3))
        {
            var reader = readerFactory(new StreamReader(fs));
            loadedTestBoolArrayDic = reader.Read<Dictionary<Vector3, bool[]>>("dictionary");
        }
        bool testResult = true;

        if (testBoolArrayDic.Count != loadedTestBoolArrayDic.Count)
        {
            testResult = false;
        }
        else
        {
            foreach (KeyValuePair<Vector3, bool[]> pair in testBoolArrayDic)
            {
                if (!loadedTestBoolArrayDic.ContainsKey(pair.Key))
                {
                    testResult = false;
                    break;
                }

                bool[] originalArray = pair.Value;
                bool[] loadedArray = loadedTestBoolArrayDic[pair.Key];

                if (originalArray.Length != loadedArray.Length)
                {
                    testResult = false;
                    break;
                }

                for (int i = 0; i < originalArray.Length; i++)
                {
                    if (originalArray[i] != loadedArray[i])
                    {
                        testResult = false;
                        break;
                    }
                }

                if (!testResult)
                    break;
            }
        }
        if (testResult)
            Debug.Log("<color=green>Dictionary<Vector3, bool[]> PASSED</color>");
        else
        {
            Debug.LogError("<color=red>Dictionary<Vector3, bool[]> FAILED</color>");
            allPass = false;
        }

        /////////////FINAL RESULT ////////////
        if (allPass)
            Debug.Log("<color=green>ALL SaveLoadFramework tests PASSED</color>");
        else
            Debug.LogError("<color=red>One or more SaveLoadFramework tests FAILED</color>");

        // Clean up
        //   if (File.Exists(path1)) File.Delete(path1);
        //    if (File.Exists(path2)) File.Delete(path2);
    }
}