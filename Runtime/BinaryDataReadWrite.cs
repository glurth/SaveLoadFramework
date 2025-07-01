using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;

namespace EyE.Serialization
{
    /// <summary>
    /// Binary implementation of IDataWriter for serializing values to a BinaryWriter.
    /// </summary>
    public class BinaryDataWriter : IDataWriter
    {
        private readonly BinaryWriter writer;

        /// <summary>
        /// Initializes a new instance of the BinaryDataWriter class.
        /// </summary>
        /// <param name="writer">The BinaryWriter to use for writing.</param>
        public BinaryDataWriter(BinaryWriter writer) => this.writer = writer;
        public void Close() { }
        /// <inheritdoc/>
        public void Write<T>(T value, string fieldName = null)
        {
            if (value is null) writer.Write("null");
            else if (value is int i) writer.Write(i);
            else if (value is float f) writer.Write(f);
            else if (value is string s) writer.Write(s);
            else if (value is bool b) writer.Write(b);
            else if (value is long l) writer.Write(l);
            else if (value is double d) writer.Write(d);
            else if (typeof(T).IsEnum) writer.Write(Convert.ToInt32(value));
            else if (value is ISaveLoad custom) custom.Serialize(this);
            //else if (value is UnityEngine.Object so) writer.Write(ResourceReferenceManager.GetPathOfObject(so));
            else if (SaveLoadRegistry.TryGetWriter(value.GetType(), out Action<IDataWriter, object> writeFunction))
            {
                writeFunction(this, value);
                return;
            }
            else if (typeof(T).IsArray)
            {
                Type itemType = typeof(T).GetElementType();
                typeof(IDataBinaryCollectionExtensionFunctions)
                    .GetMethod("SerializeCollection")
                    .MakeGenericMethod(itemType)
                    .Invoke(null, new object[] { this, value, fieldName });
            }
            else if (value is System.Collections.IList list)
            {
                Type itemType = typeof(T).IsGenericType ? typeof(T).GetGenericArguments()[0] : typeof(object);
                typeof(IDataBinaryCollectionExtensionFunctions)
                    .GetMethod("SerializeCollection")
                    .MakeGenericMethod(itemType)
                    .Invoke(null, new object[] { this, value, fieldName });
            }
            else if (value is System.Collections.IDictionary dict)
            {
                var args = typeof(T).GetGenericArguments();
                typeof(IDataBinaryCollectionExtensionFunctions)
                    .GetMethod("SerializeDictionary")
                    .MakeGenericMethod(args[0], args[1])
                    .Invoke(null, new object[] { this, value, fieldName, "value" });
            }
           // else if (value == null) writer.Write("null");
            else throw new InvalidOperationException("BinaryDataWriter- Unsupported type<" + value.GetType() + ">");
        }
    }

    /// <summary>
    /// Binary implementation of IDataReader for deserializing values from a BinaryReader.
    /// </summary>
    public class BinaryDataReader : IDataReader
    {
        private readonly BinaryReader reader;

        /// <summary>
        /// Initializes a new instance of the BinaryDataReader class.
        /// </summary>
        /// <param name="reader">The BinaryReader to use for reading.</param>
        public BinaryDataReader(BinaryReader reader) => this.reader = reader;

        /// <inheritdoc/>
        public T Read<T>(string fieldname)
        {
            object result;

            if (typeof(T) == typeof(int)) result = reader.ReadInt32();
            else if (typeof(T) == typeof(float)) result = reader.ReadSingle();
            else if (typeof(T) == typeof(string))
            {
                result = reader.ReadString();
                bool isNull = (string)result == "null";
                if (isNull)
                    result = null;
            }
            else if (typeof(T) == typeof(bool)) result = reader.ReadBoolean();
            else if (typeof(T) == typeof(long)) result = reader.ReadInt64();
            else if (typeof(T) == typeof(double)) result = reader.ReadDouble();
            else if (typeof(T).IsEnum) result = (T)Enum.ToObject(typeof(T), reader.ReadInt32());
            else if (SaveLoadRegistry.TryGetReader(typeof(T), out Func<IDataReader, object> readFunction))
            {
                result = readFunction(this);
            }
            else if (typeof(T).IsArray)
            {
                Type elementType = typeof(T).GetElementType();
                var method = typeof(IDataBinaryCollectionExtensionFunctions)
                    .GetMethod("ReadAndCreateArray")
                    .MakeGenericMethod(elementType);  // Get/create the appropriate concrete-variant of the generic IDataCollectionExtensionFunctions.ReadAndCreateList method
                return (T)method.Invoke(null, new object[] { this, "Element" });
            }
            // else if (typeof(UnityEngine.Object).IsAssignableFrom(typeof(T))) result = ResourceReferenceManager.GetObjectByPath(reader.ReadString());
            else if (typeof(T).IsGenericType &&
                     typeof(T).GetGenericTypeDefinition() == typeof(List<>))
            {
                Type[] genericParams = typeof(T).GetGenericArguments();//what type are the elements os the list?
                                                                       //note: we ASSUME the correct number of generic parameters is returned in the array
                var method = typeof(IDataBinaryCollectionExtensionFunctions)
                    .GetMethod("ReadAndCreateList")
                    .MakeGenericMethod(genericParams[0]);  // Get/create the appropriate concrete-variant of the generic IDataCollectionExtensionFunctions.ReadAndCreateList method
                return (T)method.Invoke(null, new object[] { this, "Element" });// invoke the static function to deserialize into a new list
            }
            else if (typeof(T).IsGenericType &&
                     typeof(T).GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type[] genericParams = typeof(T).GetGenericArguments();//what types are the key and value of the dictionary?
                                                                       //note: we ASSUME the correct number of generic parameters is returned in the array
                MethodInfo method = typeof(IDataBinaryCollectionExtensionFunctions)
                    .GetMethod("ReadAndCreateDictionary")
                    .MakeGenericMethod(genericParams[0], genericParams[1]);  // Get/create the appropriate concrete-variant of the generic IDataCollectionExtensionFunctions.ReadAndCreateDictionary method
                return (T)method.Invoke(null, new object[] { this, "Key", "Value" });  // invoke the static function to deserialize into a new dictionary
            }
            else
            {
                T typedResult;
                if (this.TryStaticReadAndCreate<T>(out typedResult))
                    return typedResult; // found a function for this type, return read result

                if (typeof(ISaveLoad).IsAssignableFrom(typeof(T))) // objects of this type should not get this far.  if they do- the required static function is not defined by them
                {
                    throw new InvalidOperationException(
                        $"Type {typeof(T).FullName} implements ISaveLoad but does not define a public static {typeof(T).Name} ReadAndCreate(IDataReader reader).  Field: " + fieldname
                    );
                }

                throw new InvalidOperationException("BinaryDataReader- Field: " + fieldname + "  Unsupported type: " + typeof(T).FullName);
            }

            return (T)result;
        }

        public bool ReadFormat(string expected)
        {
            //ignored
            return true;

        }


    }
    static public class IDataBinaryCollectionExtensionFunctions
    {
        public static void SerializeDictionary<K, V>(IDataWriter writer, Dictionary<K, V> dic, string collectionName = "Collection", string valueName = "Value")
        {
            writer.Write<int>(dic.Count, "Count");
            foreach (var kvp in dic)// loop through all dic values
            {
                writer.Write<K>(kvp.Key, null);
                writer.Write<V>(kvp.Value, null);
            }
        }

        public static Dictionary<K, V> ReadAndCreateDictionary<K, V>(IDataReader reader, string collectionName = "Collection", string valueName = "Value")
        {
            Dictionary<K, V> retVal = new Dictionary<K, V>();
            int length = reader.Read<int>("Count");
            for (int i = 0; i < length; i++)
            {
                K keyVal = reader.Read<K>(collectionName);
                V val = reader.Read<V>(valueName);
                retVal.Add(keyVal, val);
            }
            return retVal;
        }

        public static void SerializeCollection<T>(IDataWriter writer, ICollection<T> collection, string valueName = "Element")
        {
            // Count the items first (required for binary format)
            // Avoid enumerating twice: if it's a collection, use .Count, else enumerate
            int count = collection.Count;
            writer.Write<int>(count, null);
            foreach (T val in collection)
                writer.Write<T>(val, valueName);
        }
        public static void SerializeList<T>(IDataWriter writer, List<T> lst, string valueName = "Element")
        {
            writer.Write<int>(lst.Count, null);
            foreach (T val in lst)// loop through all dic values
            {
                writer.Write<T>(val, valueName);
            }
        }

        public static List<T> ReadAndCreateList<T>(IDataReader reader, string valueName = "Element")
        {
            List<T> retVal = new List<T>();
            int length = reader.Read<int>("Count");

            for (int i = 0; i < length; i++)
            {
                T val = reader.Read<T>(valueName);
                retVal.Add(val);
            }
            return retVal;
        }
        public static T[] ReadAndCreateArray<T>(IDataReader reader, string valueName = "Element")
        {
            return ReadAndCreateList<T>(reader, valueName).ToArray();
        }
    }
}
