using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Text;

namespace EyE.Serialization
{


    public static class StringUtil
    {

        private const string singleQuote = "\"";
        private const string escapedQuote = "\\\"";

        public static string Quote(string rawString)
        {
            if (rawString[0] == singleQuote[0])
                return rawString;

            return singleQuote + rawString.Replace(singleQuote, escapedQuote) + singleQuote;
        }

        public static string UnQuote(string quotedString)
        {
            string trimmed = quotedString.Trim();
            if (trimmed.Length >= 2 &&
                trimmed[0] == '"' &&
                trimmed[trimmed.Length-1] == '"')
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2);
                return inner.Replace(escapedQuote, singleQuote);
            }
            return quotedString;
        }

        public static string UnBracket(string bracketedString)
        {
            string trimmed = bracketedString.Trim();
            if (trimmed.Length >= 2 &&
                trimmed[0] == '{' &&
                trimmed[trimmed.Length - 1] == '}')
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2);
                return inner.Replace(escapedQuote, singleQuote);
            }
            return bracketedString;
        }

    }


    public class JsonDataWriter:IDataWriter
    {
        private StringBuilder builder = new StringBuilder();
        private enum Context { Object, Array }
        private Stack<Context> contextStack = new Stack<Context>();
        private bool needsComma = false;

        StreamWriter writer;

        #region Creation Flushing Disposal
        public JsonDataWriter(StreamWriter writer) => this.writer = writer;
        bool isClosed = false;
        public void Close()
        {
            if (isClosed) return;

            if (contextStack.Count > 0 && contextStack.Peek() == Context.Object)
            {
                EndObject();
            }

            Flush();
            isClosed = true;
            writer.Close();
        }

        public void Dispose()
        {
            Close();
        }

        public void Flush()
        {
            writer.Write(builder.ToString());
            builder.Clear();
            writer.Flush();
        }
        #endregion

        //IDataWriter implementation
        public void Write<T>(T value, string fieldName)
        {
            if (contextStack.Count == 0)
                BeginObject();

            WriteCommaIfNeeded();
            
            if (fieldName!=null)
                WriteFieldName(fieldName);


            if (value == null)
            {
                builder.Append("null");
                return;
            }


            builder.Append(NoContextValueToString(value));
            

            needsComma = true;
        }

        #region formatting utility functions
        private void BeginObject()
        {
            builder.Append("{");
            indentLevel++;
            NewLine();
            contextStack.Push(Context.Object);
            needsComma = false;
        }

        private void EndObject()
        {
            indentLevel--;
            NewLine();
            builder.Append("}");
            
            contextStack.Pop();
            needsComma = true;
        }

        private void BeginArray()
        {
            builder.Append("[");
            indentLevel++;
            NewLine();
            contextStack.Push(Context.Array);
            needsComma = false;
        }

        private void EndArray()
        {
            indentLevel--;
            NewLine();
            builder.Append("]");
            
            contextStack.Pop();
            needsComma = true;
        }

        private void WriteFieldName(string name)
        {
          //  WriteIndent();
            builder.Append($"\"{name}\": ");
        }

        private void WriteCommaIfNeeded()
        {
            if (needsComma)
            {
                builder.Append(",");
                NewLine();
            }
        }
        private int indentLevel = 0;
        private const string indentString = " ";

        private void WriteIndent()
        {
            for (int i = 0; i < indentLevel; i++)
                builder.Append(indentString);
        }

        /// <summary>
        /// newline plus indent on it
        /// </summary>
        private void NewLine()
        {
            builder.AppendLine();
            WriteIndent();
        }
        #endregion


        //invoked via reflection
        private void SerializeEnumerable<T>(IEnumerable<T> collection)
        {
            BeginArray();
            bool first = true;
            foreach (var item in collection)
            {
            //    if (!first) builder.Append(",");
                Write(item, null); // No field name in array elements
                first = false;
            }
            EndArray();
        }
        //invoked via reflection
        private void SerializeDictionary<K, V>(Dictionary<K, V> dict)
        {
            BeginArray();
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                {
                    builder.Append(",");
                    NewLine();
                }
                BeginObject();
                Write(kvp.Key, "Key");
                Write(kvp.Value, "Value");
                EndObject();
                first = false;
            }
            EndArray();
        }



        /// <summary>
        /// Generate a json string to encode the value.  May contain formatting INSIDE the value, but will not apply any formatting AROUND it.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        private string NoContextValueToString<T>(T value)
        {
            System.Text.StringBuilder str = new System.Text.StringBuilder();
            string jsonString;
            Type typeofT = typeof(T);
            if (value == null)
            {
                jsonString = "null";
                return jsonString;
            }
            if (value is string)
            {
                string valString = value as string;
                //valString = valString.Replace("\\", "\\\\").Replace("\"", "\\\"");//escape internal quotes- now done inside Quote func
                jsonString = StringUtil.Quote(valString);
                return jsonString;
            }
            else if (value is int or float or bool or long or double)
            {
                jsonString = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                return jsonString;
            }
            else if (value.GetType().IsEnum)
            {
                jsonString = value.ToString();
                jsonString = StringUtil.Quote(jsonString);
                return jsonString;
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
            else if (typeofT.IsGenericType &&
                     typeofT.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                Type[] types = typeofT.GetGenericArguments();
                var method = typeof(JsonDataWriter).GetMethod("SerializeDictionary", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(types[0], types[1]);
                method.Invoke(this, new object[] { value });
            }
            else if (typeofT.IsArray ||
                    (typeofT.IsGenericType && typeofT.GetGenericTypeDefinition() == typeof(List<>)))
            {
                Type elementType;
                if (typeofT.IsArray)
                    elementType = typeofT.GetElementType();
                else
                    elementType = typeofT.GetGenericArguments()[0];
                var method = typeof(JsonDataWriter).GetMethod("SerializeEnumerable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(elementType);
                method.Invoke(this, new object[] { value });
            }
            else
            {
                throw new NotSupportedException($"Unsupported type: {typeof(T)}");
            }

            jsonString = null;
            return jsonString;
        }

        public override string ToString() => builder.ToString();
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
            input =StringUtil.UnQuote(input.Trim());
            input = StringUtil.UnBracket(input);
            // Use a new JsonDataReader for the string.
            var reader = new JsonDataReader(input);

            // Use null as the field name, since we're reading a value, not a named property.
            return reader.Read<T>("key");
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
                Type elementType = typeof(T).GetElementType();
                //get the appropriate generic method, for the list's element types
                MethodInfo gmethod = typeof(JsonDataReader).GetMethod("DeserializeArray", BindingFlags.Instance | BindingFlags.NonPublic);
                if (gmethod == null)
                    throw new Exception("Unable to find DeserializeArray method in class JsonDataReader");
                MethodInfo method = gmethod.MakeGenericMethod(elementType);

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
            while ((ch = reader.Peek()) != -1 && (char.IsWhiteSpace((char)ch)|| (char)ch==',') ) 
                reader.Read();

            if (reader.Peek() == '{' || reader.Peek() == '[')
                reader.Read(); // consume opening brace
        }
        private void SkipClosingBrace()
        {
            int ch;
            while ((ch = reader.Peek()) != -1)// && char.IsWhiteSpace((char)ch))
                reader.Read();

            if (reader.Peek() == '}' || reader.Peek() == ']')
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
                SkipOpeningBrace();
                K keyValue = Read<K>("key");
                V entryValue = Read<V>("value");
                dict.Add(keyValue, entryValue);
                SkipClosingBrace();
                /*
                bool foundNothing;
                string keyString;
                V elementValue = ReadWithKey<V>("Value", out keyString, out foundNothing);
                if (!foundNothing)
                {
                    K keyValue=ReadString<K>(keyString);
                    //if (TryParseAtomicJson<K>("Key", keyString, out keyValue))
                    dict.Add(keyValue, elementValue);
                }*/
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
            return DeserializeList<T>().ToArray();
        }

    }
    
}