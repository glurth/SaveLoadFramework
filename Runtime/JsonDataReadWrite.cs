using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;

namespace EyE.Serialization
{
    /// <summary>
    /// Json implementation of the IDataWriter interface
    /// </summary>
    public class JsonDataWriter : IDataWriter
    {
        class JsonKVP
        {

            public string name;  //if this is null- kvp is a list element only
            public bool elementIsNextLevelSet => element == null;
            public string element; //if this is null- kvp is a fieldname only- value is next level down- used for collections/ objects with sub fields
            public int level;

            public JsonKVP(string name, string element, int level)
            {
                this.name = name;
                this.element = element;
                this.level = level;
            }
            public JsonKVP(string name, int level)
            {
                this.name = name;
                this.element = null;
                this.level = level;
            }
        }

        List<JsonKVP> jsonElements = new List<JsonKVP>();
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
        public void Flush()
        {
            string json = GetJson();
            writer.Write(json);
        }

        enum LevelType { objectLevel, listElement, ListStart }
        private string GetJson()
        {

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Stack<LevelType> levelIsAListElement = new Stack<LevelType>();
            int currentLevel = 0;

            void CloseLevels(int toLevel, int currentIndex)
            {
                while (currentLevel > toLevel)
                {
                    currentLevel--;

                    // Find the element that opened the level we are now closing
                    int openingElementIndex = -1;
                    // Start searching from the element before the current one
                    int searchFrom = (currentIndex < jsonElements.Count) ? currentIndex - 1 : jsonElements.Count - 1;
                    for (int j = searchFrom; j >= 0; j--)
                    {
                        // An opening element is a container (element is null) at the level we're closing to
                        if (jsonElements[j].level == currentLevel && jsonElements[j].element == null)
                        {
                            openingElementIndex = j;
                            break;
                        }
                    }

                    LevelType lvlType = levelIsAListElement.Pop();
                    string closeBrcket = "}";
                    if (lvlType == LevelType.listElement)
                    {
                        closeBrcket = "}";
                    }
                    if (lvlType == LevelType.ListStart)
                    {
                        closeBrcket = "]";
                    }
                    sb.Append(new string(' ', (currentLevel + 1) * 2));
                    sb.Append(closeBrcket);

                    // Check if the OPENING element is the last in ITS level.
                    if (openingElementIndex == -1 || isLastElementInLevel(openingElementIndex))
                    {
                        sb.AppendLine(); // No comma needed
                    }
                    else
                    {
                        sb.AppendLine(","); // Add the missing comma
                    }
                }
            }

            bool isLastElementInLevel(int elementIndex)
            {


                int level = jsonElements[elementIndex].level;
                for (int i = elementIndex + 1; i < jsonElements.Count; i++)
                {
                    if (jsonElements[i].level == level) return false;
                    if (jsonElements[i].level < level) return true;
                }
                return true;
            }
            bool nextIsListStart(int index)
            {
                return index + 1 < jsonElements.Count
                    && jsonElements[index + 1].name == null
                    && jsonElements[index + 1].element == null;
            }

            for (int i = 0; i < jsonElements.Count; i++)
            {
                var kvp = jsonElements[i];
                CloseLevels(kvp.level, i);

                string indent = new string(' ', (kvp.level + 1) * 2);

                if (kvp.name == null)// no name means this is a list element
                {
                    // list element
                    if (kvp.element == null)//element is a object with a sub level
                    {
                        sb.AppendLine(indent + "{");
                        currentLevel++;
                        levelIsAListElement.Push(LevelType.listElement);
                    }
                    else
                    {
                        if (isLastElementInLevel(i))
                            sb.AppendLine(indent + kvp.element);
                        else
                            sb.AppendLine(indent + kvp.element + ",");
                    }
                }
                else
                {
                    if (kvp.element == null)//element is a object with a sub level
                    {
                        if (nextIsListStart(i))
                        {
                            sb.AppendLine(indent + $"\"{kvp.name}\": [");
                            currentLevel++;
                            levelIsAListElement.Push(LevelType.ListStart);
                        }
                        else
                        {
                            sb.AppendLine(indent + $"\"{kvp.name}\": {{");
                            currentLevel++;
                            levelIsAListElement.Push(LevelType.objectLevel);
                        }

                        //sb.AppendLine(indent + $"\"{kvp.name}\": {{");//draw name and open curly
                        //currentLevel++;
                        //levelIsAListElement.Push(false);
                    }
                    else
                    {
                        if (isLastElementInLevel(i))
                            sb.AppendLine(indent + $"\"{kvp.name}\": {kvp.element}");
                        else
                            sb.AppendLine(indent + $"\"{kvp.name}\": {kvp.element},");
                    }
                }
            }

            CloseLevels(0, jsonElements.Count);// -1);

            string result = sb.ToString().TrimEnd(',', '\n', '\r');
            return "{" + Environment.NewLine + result + Environment.NewLine + "}";
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

            if (!string.IsNullOrEmpty(fieldName))
            {
                WriteIndent();
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
                writeFunction(this, value);
            }
            else if (typeof(T).IsGenericType &&
                     typeof(T).GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                BeginObject();
                Type[] types = typeof(T).GetGenericArguments();
                var method = typeof(JsonDataWriter).GetMethod("SerializeDictionary", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(types[0], types[1]);
                method.Invoke(this, new object[] { value });
                EndObject();
            }
            else if (typeof(T).IsGenericType &&
                     typeof(T).GetGenericTypeDefinition() == typeof(List<>))
            {
                BeginArray();
                Type elementType = typeof(T).GetGenericArguments()[0];
                var method = typeof(JsonDataWriter).GetMethod("SerializeList", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(elementType);
                method.Invoke(this, new object[] { value });
                EndArray();
            }
            else
            {
                throw new NotSupportedException($"Unsupported type: {typeof(T)}");
            }

            void BeginObject()
            {
                writer.WriteLine("{");
                contextStack.Push(new Context(Context.ContextType.Object));
            }

            void EndObject()
            {
                writer.WriteLine();
                contextStack.Pop();
                WriteIndent();
                writer.Write("}");
            }

            void BeginArray()
            {
                writer.WriteLine("[");
                contextStack.Push(new Context(Context.ContextType.Array));
            }

            void EndArray()
            {
                writer.WriteLine();
                contextStack.Pop();
                WriteIndent();
                writer.Write("]");
            }

            void WriteIndent()
            {
                writer.Write(new string(' ', contextStack.Count * 2));
            }

        }
        private string Quote(string rawString)
        {
            return "\"" + rawString + "\"";
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
                valString = valString.Replace("\\", "\\\\").Replace("\"", "\\\"");//escape internal quotes
                jsonString = Quote(valString);
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
                jsonString = Quote(jsonString);
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
        private void SerializeList<T>(List<T> list)
        {
            foreach (T element in list)
            {
                Write<T>(element, null); // list elements have no field name
            }
        }

        //referenced via reflection only
        private void SerializeDictionary<K, V>(Dictionary<K, V> dict)
        {
            foreach (KeyValuePair<K, V> kvp in dict)
            {
                if (TrySerializeAtomicValueJsonString<K>(kvp.Key, out string keyString))
                    Write<V>(kvp.Value, keyString);
                else
                    throw new FormatException("JsonDataWriter string generation failure:  Key values must be a single atomic element.  <" + typeof(K) + "> is not Atomic.");
            }
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
            return ReadWithKey<T>(expectedFieldName, out string ignored);
        }

        /// <summary>
        /// if object is a dictionary entry, this function will provide the key string (via out param), as well as returning the entry's value.
        /// TODO: make json system more robust in future by allowing user to load fields out of order, by specific name
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expectedFieldName"></param>
        /// <param name="foundFieldName"></param>
        /// <returns></returns>
        private T ReadWithKey<T>(string expectedFieldName, out string foundFieldName)
        {
            Type typeofT = typeof(T);
            string valueString;
            foundFieldName = "";
            if (!TryGetNextJsonObjectString(out foundFieldName, out valueString))
                return default(T);// (object)null;

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
                return (T)readFunction(this);
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
                // MemoryStream subStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(valueString ?? ""));
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

        private string UnQuote(string quotedString)
        {
            quotedString = quotedString.Trim();
            // Trim surrounding quotes
            if (quotedString.StartsWith("\"") && quotedString.EndsWith("\""))
            {
                quotedString = quotedString.Substring(1, quotedString.Length - 2);
            }
            return quotedString;
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
            if (jsonInput.Trim() == "null")
            {
                output = (T)(object)null;
                return true;
            }
            if (typeofT == typeof(string))
            {
                jsonInput = UnQuote(jsonInput);
                // Proper JSON unescaping
                jsonInput = jsonInput.Replace("\\\"", "\"").Replace("\\\\", "\\");

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
                jsonInput = UnQuote(jsonInput);

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

                string keyString;
                V elementValue = ReadWithKey<V>("Value", out keyString);
                K keyValue;
                TryParseAtomicJson<K>("Key", keyString, out keyValue);
                dict.Add(keyValue, elementValue);
            }
            return dict;
        }
        private List<T> DeserializeList<T>()
        {
            List<T> list = new List<T>();
            while (reader.Peek() != -1)
            {
                T elementValue = Read<T>("list element");
                list.Add(elementValue);
            }
            return list;
        }


    }
}