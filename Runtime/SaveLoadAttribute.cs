using System;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
namespace EyE.Serialization
{
    /// <summary>
    /// Objects assigned this attribute we be ASSUMED to have Load and Save functions that take/return an object of the specified typeand and take the appropriae IDataWriter/IDataReader as a param
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SaveLoadHandlerAttribute : Attribute
    {
        public Type[] TargetTypes { get; }

        public SaveLoadHandlerAttribute(params Type[] targetTypes)
        {
            TargetTypes = targetTypes;
        }
    }

    /// <summary>
    /// This class will, on first access, use reflection to find all classes with the SaveLoadHandlerAttribute
    /// It will further use reflection to find member functions that correspond to the following signatures
    ///             Action<IDataWriter, T> writer;
    ///             Func<IDataReader, T> reader;
    ///  where T is ALL the types listed in the attribute's  TargetTypes member.
    /// </summary>
    public static class SaveLoadRegistry
    {

        /// <summary>
        /// stores the references to the save and load functions for a partiuclar type of object
        /// </summary>
        private class SaveLoadHandlerFunctions
        {
            public Action<IDataWriter, object> writer;
            public Func<IDataReader, object> reader;
        }
        //stores registered the load save function, by type
        private static readonly Dictionary<Type, SaveLoadHandlerFunctions> handlers = new Dictionary<Type, SaveLoadHandlerFunctions>();

        private static bool initComplete = false;

        private static void ScanAndRegisterAll()
        {
            if (initComplete) return;
            initComplete = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try { types = assemblies[i].GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];
                    if (type == null) continue;

                    object[] attrs = type.GetCustomAttributes(typeof(SaveLoadHandlerAttribute), false);
                    if (attrs.Length == 0) continue;

                    SaveLoadHandlerAttribute attr = (SaveLoadHandlerAttribute)attrs[0];
                    for (int k = 0; k < attr.TargetTypes.Length; k++)
                    {
                        Type targetType = attr.TargetTypes[k];

                        // Match Save(IDataWriter, targetType)
                        MethodInfo save = type.GetMethod("Save", BindingFlags.Public | BindingFlags.Static, null,
                            new[] { typeof(IDataWriter), targetType }, null);

                        // Find Load method that returns targetType and takes (IDataReader)
                        MethodInfo load = null;
                        MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
                        for (int m = 0; m < methods.Length; m++)
                        {
                            MethodInfo candidate = methods[m];
                            if (candidate.ReturnType == targetType)
                            {
                                ParameterInfo[] pars = candidate.GetParameters();
                                if (pars.Length == 1 && pars[0].ParameterType == typeof(IDataReader))
                                {
                                    load = candidate;
                                    break;
                                }
                            }
                        }

                        if (save == null || load == null)
                            throw new InvalidOperationException($"Missing Save/Load methods for {targetType.FullName} in {type.FullName}");

                        handlers[targetType] = new SaveLoadHandlerFunctions
                        {
                            writer = (Action<IDataWriter, object>)Delegate.CreateDelegate(
                                typeof(Action<IDataWriter, object>), save),
                            reader = (Func<IDataReader, object>)Delegate.CreateDelegate(
                                typeof(Func<IDataReader, object>), load)
                        };
                    }
                }
            }
        }

        public static bool TryGetWriter(Type type, out Action<IDataWriter, object> writer)
        {
            ScanAndRegisterAll();// return immediately if already done
            if (handlers.TryGetValue(type, out var handler) && handler.writer != null)
            {
                writer = handler.writer;
                return true;
            }
            writer = null;
            return false;
        }

        public static bool TryGetReader(Type type, out Func<IDataReader, object> reader)
        {
            ScanAndRegisterAll();// return immediately if already done
            if (handlers.TryGetValue(type, out var handler) && handler.reader != null)
            {
                reader = handler.reader;
                return true;
            }
            reader = null;
            return false;
        }
    }



    /// <summary>
    /// Uses the SaveLoadHandler attribute to specify how UnityEngine.Object references should be saved/loaded
    /// </summary>
    [SaveLoadHandler(typeof(UnityEngine.Object))]
    public static class UnityObjectHandler
    {
        public static void Save(IDataWriter writer, object obj)
        {
            UnityEngine.Object unityObj = (UnityEngine.Object)obj;
            string path = ResourceReferenceManager.GetPathOfObject(unityObj);
            writer.Write(path,"UnityObject");
        }

        public static object Load(IDataReader reader)
        {
            string path = reader.Read<string>("UnityObject");
            return ResourceReferenceManager.GetObjectByPath(path);
        }
    }
}