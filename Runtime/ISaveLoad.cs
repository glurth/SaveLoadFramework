using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;

namespace EyE.Serialization
{
    /// <summary>
    /// Abstraction of a writer - implemented by binary and JSON writers.
    /// </summary>
    public interface IDataWriter
    {
        /// <summary>
        /// Writes a value of type T to the output.
        /// </summary>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        /// <param name="value">The value to write.</param>
        void Write<T>(T value, string fieldName);

    }

    /// <summary>
    /// Abstraction of a reader - implemented by binary and JSON readers.
    /// </summary>
    /// <remarks>
    /// PATTERN: Implementations should check if type T implements ISaveLoad, and if so, use reflection to find a static function of the form
    /// public static T ReadAndCreate(IDataReader reader), which should be used rather than Activator.CreateInstance.
    /// </remarks>
    public interface IDataReader
    {
        /// <summary>
        /// Reads a value of type T from the input.
        /// </summary>
        /// <typeparam name="T">The type of the value to read.</typeparam>
        /// <returns>The read value of type T.</returns>
        T Read<T>(string fieldName);
        
    }

    /// <summary>
    /// Defines a contract for serialization and deserialization of objects.
    /// </summary>
    public interface ISaveLoad
    {
        /// <summary>
        /// Serializes the current instance of the object into a BinaryWriter stream.
        /// </summary>
        /// <param name="writer">The BinaryWriter used to write the object's state.</param>
        /// <remarks>
        /// Implementers should write all necessary fields and properties to fully represent the object's state.
        /// </remarks>
        void Serialize(IDataWriter writer);

        // Deserialization is via pattern only since there are no virtual static functions in Unity-compatible C# version.
        // So, they are implemented via reflection in IDataReader. Alas, exceptions are thrown rather than compiler errors should the user fail to follow the pattern.

        // Classes that implement ISaveLoad must define a static function of the form and name
        // public static T ReadAndCreate(IDataReader reader) 
        // Where T is the derived class itself.
        // This function must instantiate and populate the deserialized object from the stream.
        // It should follow normal deserialization procedures and match the Serialize function both in order and content.
    }

    /// <summary>
    /// This class has a single function which will use reflection to see if a given type implements a ReadAndCreate method.  
    /// Results are cached for faster, reflection free, subsequent lookups.
    /// </summary>
    static class ReaderExtension
    {

        private static readonly Dictionary<Type, MethodInfo> readAndCreateByTypeCache = new();

        /// <summary>
        /// Attempts to invoke the static ReadAndCreate method on type T if it exists.
        /// </summary>
        /// <typeparam name="T">The type to attempt deserialization for.</typeparam>
        /// <param name="reader">The data reader instance.</param>
        /// <param name="value">The deserialized value if successful.</param>
        /// <returns>True if ReadAndCreate was found and invoked; otherwise false.</returns>
        public static bool TryStaticReadAndCreate<T>(this IDataReader reader, out T value)
        {
            value = default;
            MethodInfo func;
            if (!readAndCreateByTypeCache.TryGetValue(typeof(T), out func))
            {
                func = typeof(T).GetMethod(
                    "ReadAndCreate",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(IDataReader) },
                    null);
                readAndCreateByTypeCache[typeof(T)] = func;
            }
            if (func != null && func.ReturnType == typeof(T))
            {
                value = (T)func.Invoke(null, new object[] { reader });
                return true;
            }

            return false;
        }

    }


   
}