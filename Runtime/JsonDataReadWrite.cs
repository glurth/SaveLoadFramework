using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;

namespace EyE.Serialization
{

    public static class StringUtil
    {

        private const string singleQuote = "\"";
        private const string escapedQuote = "\\\"";

        public static string Quote(string rawString)
        {
            return singleQuote + rawString.Replace(singleQuote, escapedQuote) + singleQuote;
        }

        public static string UnQuote(string quotedString)
        {
            if (quotedString.Length >= 2 &&
                quotedString[0] == '"' &&
                quotedString[^1] == '"')
            {
                string inner = quotedString.Substring(1, quotedString.Length - 2);
                return inner.Replace(escapedQuote, singleQuote);
            }
            return quotedString;
        }
    }
    /// <summary>
    /// Json implementation of the IDataWriter interface
    /// </summary>
    public class JsonDataWriter : IDataWriter
    {

        private Stack<Context> contextStack = new Stack<Context>();
        private bool isRootWritten = false;

        private class Context
        {
            public enum ContextType { Object, Array }
            public ContextType Type;
            public bool IsFirst = true;

            public Context(ContextType type) => Type = type;
        }

        StreamWriter writer;
        public JsonDataWriter(StreamWriter writer) => this.writer = writer;
        bool isClosed = false;
        public void Close()
        {
            if (isClosed) return;

            if (isRootWritten && contextStack.Count == 1)
            {
                writer.WriteLine();
                writer.Write("}");
                contextStack.Pop();
            }

            writer.Flush();
            isClosed = true;
        }

        public void Dispose()//automatically called on using(){} close
        {
            Close(); // delegate to manual Close()
        }

        public void Flush()
        {
            writer.Flush();
        }

        enum LevelType { objectLevel, listElement, ListStart }

        /// <summary>
        /// Serializes the specified value into a JSON string, wrapping it with the given field name.
        /// </summary>
        /// <typeparam name="T">The type of the value to serialize.</typeparam>
        /// <param name="value">The object to serialize.</param>
        /// <param name="fieldName">The field name to use as the JSON property.</param>
        /// <returns>A JSON-formatted string containing the serialized object.</returns>
        public string WriteString<T>(T value, string fieldName)
        {
            MemoryStream memoryStream = new MemoryStream();
            StreamWriter streamWriter = new StreamWriter(memoryStream);
            JsonDataWriter jsonWriter = new JsonDataWriter(streamWriter);

            jsonWriter.Write<T>(value, fieldName);
            jsonWriter.Close();

            streamWriter.Flush();
            memoryStream.Position = 0;

            StreamReader streamReader = new StreamReader(memoryStream);
            string result = streamReader.ReadToEnd();

            streamReader.Dispose();
            streamWriter.Dispose();
            memoryStream.Dispose();

            return result;
        }
    

    //IDataWriter required function
        public void Write<T>(T value, string fieldName)
        {
            bool isInsideArray = contextStack.Count > 0 && contextStack.Peek().Type == Context.ContextType.Array;

            if (!isRootWritten)
            {
                writer.WriteLine("{");
                contextStack.Push(new Context(Context.ContextType.Object));
                isRootWritten = true;
            }

            if (contextStack.Count > 0 && !contextStack.Peek().IsFirst)
                writer.WriteLine(",");
            if (contextStack.Count > 0)
                contextStack.Peek().IsFirst = false;

            WriteIndent();
            if (!string.IsNullOrEmpty(fieldName))//write key
            {

                if (fieldName.Length >= 2 && fieldName[0] == '"' && fieldName[^1] == '"')
                    writer.Write($"{fieldName}: ");
                else
                    writer.Write($"\"{fieldName}\": ");
            }

            if (TrySerializeAtomicValueJsonString(value, out string jsonValue))
            {
                writer.Write(jsonValue);
            }
            else if (value is ISaveLoad custom)
            {
                BeginObject();
                custom.Serialize(this);
                EndObject();
            }
            else if (SaveLoadRegistry.TryGetWriter(value.GetType(), out Action<IDataWriter, object> writeFunction))
            {
                BeginObject();
                writeFunction(this, value);
                EndObject();
            }
            else if (typeof(T).IsGenericType &&
                     typeof(T).GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
               // BeginObject();
                Type[] types = typeof(T).GetGenericArguments();
                var method = typeof(JsonDataWriter).GetMethod("SerializeDictionary", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(types[0], types[1]);
                method.Invoke(this, new object[] { value });
               // EndObject();
            }
            else if( typeof(T).IsArray || 
                    (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(List<>)))
            {
                // BeginArray();
                Type elementType = typeof(T).GetGenericArguments()[0];
                var method = typeof(JsonDataWriter).GetMethod("SerializeEnumerable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(elementType);
                method.Invoke(this, new object[] { value });
                // EndArray();
            }
            else
            {
                throw new NotSupportedException($"Unsupported type: {typeof(T)}");
            }
            return;
        }

        void BeginObject()
        {
            //  WriteIndent();
            writer.Write("{");
            writer.WriteLine();
            contextStack.Push(new Context(Context.ContextType.Object));
        }

        void EndObject(bool emptyDictionary = false)
        {
            if (!emptyDictionary)
                writer.WriteLine();
            contextStack.Pop();
            WriteIndent();
            writer.Write("}");
        }

        void BeginArray()
        {
            //   WriteIndent();
            writer.Write("[");
            writer.WriteLine();
            contextStack.Push(new Context(Context.ContextType.Array));
        }

        void EndArray(bool emptyArray = false)
        {
            if (!emptyArray)
                writer.WriteLine();
            contextStack.Pop();
            WriteIndent();
            writer.Write("]");
        }

        void WriteIndent()
        {
            writer.Write(new string(' ', contextStack.Count * 2));
        }



        private bool TrySerializeAtomicValueJsonString<T>(T value, out string jsonString)
        {
            System.Text.StringBuilder str = new System.Text.StringBuilder();
            if (value == null)
            {
                jsonString = "null";
                return true;
            }
            if (value is string)
            {
                string valString = value as string;
                //valString = valString.Replace("\\", "\\\\").Replace("\"", "\\\"");//escape internal quotes- now done inside Quote func
                jsonString = StringUtil.Quote(valString);
                return true;
            }
            else if (value is int or float or bool or long or double)
            {
                jsonString = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            else if (value.GetType().IsEnum)
            {
                jsonString = value.ToString();
                jsonString = StringUtil.Quote(jsonString);
                return true;
            }
            /*else if(value is UnityEngine.Object so)
            {
                jsonString = ResourceReferenceManager.GetPathOfObject(so);
                return true;
            }*/

            jsonString = null;
            return false;
        }

        //referenced via reflection only
        private void SerializeEnumerable<T>(IEnumerable<T> collection)
        {
            BeginArray();
            bool empty = true;
            foreach (T element in collection)
            {
                Write<T>(element, null);
                empty = false;
            }
            // Use Count property if available, or just check if collection is empty for EndArray
            EndArray(empty);
        }


        private void SerializeList<T>(List<T> list)
        {
            BeginArray();
            foreach (T element in list)
            {
                Write<T>(element, null); // list elements have no field name
            }
            EndArray(list.Count == 0);
        }

        //referenced via reflection only
        private void SerializeDictionary<K, V>(Dictionary<K, V> dict)
        {
            BeginObject();
            foreach (KeyValuePair<K, V> kvp in dict)
            {
                string keyString = WriteString<K>(kvp.Key, "key");
                if(!string.IsNullOrWhiteSpace(keyString))
                //if (TrySerializeAtomicValueJsonString<K>(kvp.Key, out string keyString))
                    Write<V>(kvp.Value, keyString);
                else
                    throw new FormatException("JsonDataWriter string generation failure:  Unable to convert <" + typeof(K) + "> into a json string");
            }
            EndObject(dict.Count == 0);
        }
    }

    /// <summary>
    /// Concrete Json implementation of the IDataReader interface
    /// </summary>
    public class JsonDataReader : IDataReader
    {
        TextReader reader;
        /// <summary>
        /// Creates a new reader using the provided Stream
        /// </summary>
        /// <param name="inputStream"></param>
        public JsonDataReader(StreamReader inputStream)
        {
            reader = inputStream;
            SkipOpeningBrace();
        }

        /// <summary>
        /// this constructor, will not skip the opening bracket, unlike the other constructors. used internally
        /// </summary>
        /// <param name="inputString"></param>
        /// <param name="skipOpeningBrace"></param>
        JsonDataReader(string inputString, bool skipOpeningBrace = false)
        {
            reader = new StringReader(inputString);
            if (skipOpeningBrace) SkipOpeningBrace();
        }



        //IDataReader interface required function
        public T Read<T>(string expectedFieldName)
        {
            return ReadWithKey<T>(expectedFieldName, out string ignored, out bool ignoredBool);
        }
        public T Read<T>(string expectedFieldName, out bool foundNothing)
        {
            return ReadWithKey<T>(expectedFieldName, out string ignored, out foundNothing);
        }
        /// <summary>
        /// assumes passed value has quotes around it- removes them, and unescapes internal quotes before processing.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="input"></param>
        /// <returns></returns>
        private T ReadString<T>(string input)
        {
            // Unquote using your UnQuote utility, handles escaping too.
            input =StringUtil.UnQuote(input);

            // Use a new JsonDataReader for the string.
            var reader = new JsonDataReader(input);

            // Use null as the field name, since we're reading a value, not a named property.
            return reader.Read<T>(null);
        }
        /// <summary>
        /// if object is a dictionary entry, this function will provide the key string (via out param), as well as returning the entry's value.
        /// TODO: make json system more robust in future by allowing user to load fields out of order, by specific name
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expectedFieldName"></param>
        /// <param name="foundFieldName"></param>
        /// <returns></returns>
        private T ReadWithKey<T>(string expectedFieldName, out string foundFieldName, out bool foundNothing)
        {
            Type typeofT = typeof(T);
            string valueString;
            foundFieldName = "";
            if (!TryGetNextJsonObjectString(out foundFieldName, out valueString))
            {
                foundNothing = true;
                return default(T);
            }
            foundNothing = false;
            if (TryParseAtomicJson<T>(foundFieldName, valueString, out T outputValue))
            {
                return outputValue;
            }
            else if (typeof(ISaveLoad).IsAssignableFrom(typeofT))
            {
                T typedResult;
                //MemoryStream subStream = new MemoryStream(System.Text.Encoding.Unicode.GetBytes(valueString ?? ""));
                JsonDataReader subReader = new JsonDataReader(valueString ?? "");
                if (ReaderExtension.TryStaticReadAndCreate<T>(subReader, out typedResult))
                {
                    return typedResult;
                }

                throw new InvalidOperationException(
                    $"Type {typeofT.FullName} implements ISaveLoad but lacks the required function:  static " + typeofT.Name + " ReadAndCreate(IDataReader reader)."
                );

            }
            else if (SaveLoadRegistry.TryGetReader(typeof(T), out Func<IDataReader, object> readFunction))
            {
                JsonDataReader subReader = new JsonDataReader(valueString ?? "");
                return (T)readFunction(subReader);
            }
            else if (typeofT.IsArray)
            {
                //get the appropriate generic method, for the list's element types
                Type[] genericParams = typeof(T).GetGenericArguments();
                MethodInfo gmethod = typeof(JsonDataReader).GetMethod("DeserializeArray", BindingFlags.Instance | BindingFlags.NonPublic);
                if (gmethod == null)
                    throw new Exception("Unable to find DeserializeArray method in class JsonDataReader");
                MethodInfo method = gmethod.MakeGenericMethod(genericParams[0]);

                //create a stream to read the subvalues 
                JsonDataReader subReader = new JsonDataReader(valueString ?? "");

                //invoke the method on this object
                object deserializedArray = method.Invoke(subReader, new object[0]);
                return (T)deserializedArray;
            }
            else if (typeofT.IsGenericType &&
                     typeofT.GetGenericTypeDefinition() == typeof(List<>))
            {
                //get the appropriate generic method, for the list's element types
                Type[] genericParams = typeof(T).GetGenericArguments();
                MethodInfo gmethod = typeof(JsonDataReader).GetMethod("DeserializeList", BindingFlags.Instance | BindingFlags.NonPublic);
                if (gmethod == null)
                    throw new Exception("Unable to find DeserializeList method in class JsonDataReader");
                MethodInfo method = gmethod.MakeGenericMethod(genericParams[0]);

                //create a stream to read the subvalues 
                JsonDataReader subReader = new JsonDataReader(valueString ?? "");

                //invoke the method on this object
                object deserializedList = method.Invoke(subReader, new object[0]);
                return (T)deserializedList;
            }
            else if (typeofT.IsGenericType &&
                     typeofT.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                //get the appropriate generic method, for the dictionary's key and value types
                Type[] genericParams = typeofT.GetGenericArguments();
                MethodInfo gmethod = typeof(JsonDataReader).GetMethod("DeserializeDictionary", BindingFlags.Instance | BindingFlags.NonPublic);
                if (gmethod == null)
                    throw new Exception("Unable to find DeserializeDictionary method in class JsonDataReader");
                MethodInfo method = gmethod.MakeGenericMethod(genericParams[0], genericParams[1]);

                //create a stream to read the subvalues 
                // MemoryStream subStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(valueString ?? ""));
                JsonDataReader subReader = new JsonDataReader(valueString ?? "");

                //invoke the method on this object
                object deserializedDict = method.Invoke(subReader, new object[0]);
                return (T)deserializedDict;
            }

            else
            {
                throw new InvalidOperationException($"JsonDataReader Unsupported type: {typeofT.FullName}");
            }

            // return (T)(object)null;
        }

        #region string processing
        private void SkipOpeningBrace()
        {
            int ch;
            while ((ch = reader.Peek()) != -1 && char.IsWhiteSpace((char)ch))
                reader.Read();

            if (reader.Peek() == '{' || reader.Peek() == '[')
                reader.Read(); // consume opening brace
        }

        /// <summary>
        /// reads the next key value pair of the stream into the outputstring, if possible.  returns false, if there are no such objects found in sourceJson
        /// </summary>
        /// <param name="keyString">a string containing thekey value found</param>
        /// <param name="valueString">a string containg the element value found</param>
        /// <returns></returns>
        private bool TryGetNextJsonObjectString(out string keyString, out string valueString)
        {
            keyString = null;
            valueString = null;

            // Skip leading whitespace
            int ch;
            while ((ch = reader.Peek()) != -1 && char.IsWhiteSpace((char)ch))
                reader.Read();

            if (reader.Peek() == -1)// 
                return false;
            bool escaped = false;
            // Read key
            if (reader.Peek() == '"')
            {
                reader.Read();//get the open quote
                System.Text.StringBuilder keyBuilder = new System.Text.StringBuilder();

                while ((ch = reader.Read()) != -1)
                {
                    char c = (char)ch;
                    if (escaped)
                    {
                        keyBuilder.Append(c);
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        break;
                    }
                    else
                    {
                        keyBuilder.Append(c);
                    }
                }

                keyString = keyBuilder.ToString();

                // Skip to colon
                while ((ch = reader.Read()) != -1 && char.IsWhiteSpace((char)ch)) { }
                if ((char)ch != ':')
                    return false;

            }
            else
                keyString = "";


            // Skip whitespace before value
            while ((ch = reader.Peek()) != -1 && char.IsWhiteSpace((char)ch))
                reader.Read();

            // Read value
            System.Text.StringBuilder valueBuilder = new System.Text.StringBuilder();
            int braceDepth = 0, bracketDepth = 0;
            bool inQuotes = false;

            escaped = false;
            while ((ch = reader.Peek()) != -1)//check if at end
            {
                char c = (char)ch;
                if (inQuotes)
                {
                    reader.Read();
                    valueBuilder.Append(c);

                    if (!escaped && c == '"') inQuotes = false;
                    else escaped = (c == '\\' && !escaped);
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                        escaped = false;
                        reader.Read();
                        valueBuilder.Append(c);
                    }
                    else if (c == '{')
                    {
                        reader.Read();
                        if (braceDepth++ > 0 || bracketDepth > 0)
                            valueBuilder.Append(c);
                    }
                    else if (c == '[')
                    {
                        reader.Read();
                        if (bracketDepth++ > 0 || braceDepth > 0)
                            valueBuilder.Append(c);
                    }
                    else if (c == '}')
                    {
                        reader.Read();
                        if (--braceDepth > 0 || bracketDepth > 0)
                            valueBuilder.Append(c);
                        if (braceDepth < 0)
                            break;
                    }
                    else if (c == ']')
                    {
                        reader.Read();
                        if (--bracketDepth > 0 || braceDepth > 0)
                            valueBuilder.Append(c);
                        if (bracketDepth < 0)
                            break;
                    }
                    else if (c == ',' && braceDepth == 0 && bracketDepth == 0)
                    {
                        reader.Read(); // consume comma
                        break;
                    }
                    else
                    {
                        reader.Read();
                        // if(!char.IsWhiteSpace(c) || inQuotes)
                        valueBuilder.Append(c);
                    }
                }
            }

            valueString = valueBuilder.ToString();//.Trim();
            return true;
        }



        /// <summary>
        /// reads from the provided string to see if it contains an atomic value.  If it does, it will returns the parsed value in the output parameter.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fieldName">field name is ignored for processing, but used when displaying errors</param>
        /// <param name="jsonInput">string to parse</param>
        /// <param name="output">out value of the appropriate type</param>
        /// <returns>true if able to parse the specific atomic value of type T, false otherwise. This does not mean it's invalid; composite objects, for example, will return false.</returns>
        private bool TryParseAtomicJson<T>(string fieldName, string jsonInput, out T output)
        {
            Type typeofT = typeof(T);
            if (jsonInput==null || jsonInput.Trim() == "null")
            {
                output = (T)(object)null;
                return true;
            }
            if (typeofT == typeof(string))
            {
                jsonInput = StringUtil.UnQuote(jsonInput);
                // Proper JSON unescaping- done inside unquote
                //jsonInput = jsonInput.Replace("\\\"", "\"").Replace("\\\\", "\\");

                output = (T)(object)jsonInput;
                return true;
            }
            else if (typeofT == typeof(int))
            {
                int result;
                if (!int.TryParse(jsonInput, out result))
                    throw new DataMisalignedException("Failed to Parse int field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }
            else if (typeofT == typeof(float))
            {
                float result;
                if (!float.TryParse(jsonInput, out result))
                    throw new DataMisalignedException("Failed to Parse float field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }
            else if (typeofT == typeof(long))
            {
                long result;
                if (!long.TryParse(jsonInput, out result))
                    throw new DataMisalignedException("Failed to Parse long field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }
            else if (typeofT == typeof(double))
            {
                double result;
                if (!double.TryParse(jsonInput, out result))
                    throw new DataMisalignedException("Failed to Parse double field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }
            else if (typeofT == typeof(bool))
            {
                bool result;
                if (!bool.TryParse(jsonInput, out result))
                    throw new DataMisalignedException("Failed to Parse bool field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }

            else if (typeofT.IsEnum)
            {
                object result;
                // Trim surrounding quotes
                jsonInput = StringUtil.UnQuote(jsonInput);

                if (!Enum.TryParse(typeof(T), jsonInput, out result))
                    throw new DataMisalignedException("Failed to Parse enum field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)result;
                return true;
            }
            /*else if(typeof(UnityEngine.Object).IsAssignableFrom(typeofT))
            {
                output = (T)(object)ResourceReferenceManager.GetObjectByPath(jsonInput);
                return true;
            }*/

            output = default(T);// (T)(object)null;
            return false;
        }
        #endregion


        //these functions are invoked by reflection in Read<T>, when needed
        private Dictionary<K, V> DeserializeDictionary<K, V>()
        {
            Dictionary<K, V> dict = new Dictionary<K, V>();

            while (reader.Peek() != -1)
            {
                
                bool foundNothing;
                string keyString;
                V elementValue = ReadWithKey<V>("Value", out keyString, out foundNothing);
                if (!foundNothing)
                {
                    K keyValue=ReadString<K>(keyString);
                    //if (TryParseAtomicJson<K>("Key", keyString, out keyValue))
                    dict.Add(keyValue, elementValue);
                }
            }
            return dict;
        }
        private List<T> DeserializeList<T>()
        {
            List<T> list = new List<T>();
            while (reader.Peek() != -1)
            {
                bool foundNothing;
                T elementValue = Read<T>("list element", out foundNothing);
                if(!foundNothing)
                    list.Add(elementValue);
            }
            return list;
        }
        private T[] DeserializeArray<T>()
        {
            List<T> list = new List<T>();
            while (reader.Peek() != -1)
            {
                bool foundNothing;
                T elementValue = Read<T>("list element", out foundNothing);
                if (!foundNothing)
                    list.Add(elementValue);
            }
            
            return list.ToArray();
        }

    }
}