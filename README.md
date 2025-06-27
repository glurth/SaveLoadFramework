# Save Load Framework

A Unity-compatible serialization framework designed for **explicit, granular control** of serialization.  
Supports both interface-based and attribute-based serialization, with special support for Unity objects.

---

## Features

- **Explicit Serialization**: Users define exactly how objects are serialized—no hidden magic.
- **Granular Control**: Serialize only what you want, how you want.
- **Multiple Backends**: Binary and JSON serialization provided.
- **Unity Runtime Compatible**: Handles `UnityEngine.Object` references (via `ResourceReferenceManager`).
- **Extensible**: Register custom handlers via `[SaveLoadHandler]` attribute.

---

## Getting Started

### 1. Implementing Serialization

You have two main options:

#### Option A: Implement `ISaveLoad`

Implement the `ISaveLoad` interface for types you want to control:

```csharp
using EyE.Serialization;

public class PlayerData : ISaveLoad
{
    public int Level;
    public string Name;

    public void Serialize(IDataWriter writer)
    {
        writer.Write(Level, nameof(Level));
        writer.Write(Name, nameof(Name));
    }

    // Required static method for deserialization
    public static PlayerData ReadAndCreate(IDataReader reader)
    {
        var data = new PlayerData();
        data.Level = reader.Read<int>(nameof(Level));
        data.Name = reader.Read<string>(nameof(Name));
        return data;
    }
}
```

#### Option B: Use `[SaveLoadHandler]` Attribute

You can create a static class with this attribute to handle serialization for types you cannot modify.  This attribute-based method does NOT provide compile-time type and signature checking of the functions in such a class, such errors will only be exposed at runtime via thrown exceptions. (So, if you can, use option A and implement ISaveLoad)

```csharp
using EyE.Serialization;

[SaveLoadHandler(typeof(Vector3))]
public static class Vector3SaveLoad
{
    public static void Save(IDataWriter writer, Vector3 v)
    {
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
```

---

### 2. Serializing and Deserializing

Choose a backend (`BinaryDataWriter/BinaryDataReader` or `JsonDataWriter/JsonDataReader`):

```csharp
// JSON Example
using StreamWriter sw = new StreamWriter("player.json"))
{
    var writer = new JsonDataWriter(sw);
    writer.Write(playerData,"PlayerData")
    writer.Close();
}

using StreamReader sr = new StreamReader("player.json"))
{
    JsonDataReader reader = new JsonDataReader(sr);
    PlayerData loadedPlayerData = reader.Read<PlayerData>("PlayerData")
}
```

---

### 3. Unity Object Support

References to `UnityEngine.Object` fields (e.g., prefabs, materials) are saved as resource paths by default.  
The framework uses `ResourceReferenceManager` to map assets <-> paths.  
To update the asset mapping, run the menu command:

**Tools ? Build ResourceManager Asset**

---

## Example: Saving a List

```csharp
List<PlayerData> players = ...;
writer.Write(players, "Players");
```

---

## Advanced: Registering Custom Handlers

For 3rd-party or built-in types, register global handlers with `[SaveLoadHandler]`.


---

    
## License

All rights reserved.

No license is granted for use, modification, distribution, or any other purpose without prior written permission.

If you're an independent developer and would like to use this software, email glurth at gmail.com to request a license. I usually approve such requests for free.  Businesses may contact me for pricing.

## Contributions

While contributions are welcome, they cannot be used without your explicit written permission, as this project will remain proprietary software.