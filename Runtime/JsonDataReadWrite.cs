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
        // actual characters we need to escape line new lines or tabs (single-char entries) [NOT string entries like "\n", that is what we will convert these into
        private static readonly char[] ActualChars = new char[]
        {
    '"', '\\', '\b', '\f', '\n', '\r', '\t'
        };

        // what follows the backslash in JSON for the corresponding ActualChars
        private static readonly char[] EscapeCodes = new char[]
        {
    '"', '\\', 'b', 'f', 'n', 'r', 't'
        };

        // generated maps (actualChar -> escape string, escapeCode -> actualChar)
        private static readonly Dictionary<char, string> EscapeMap;
        private static readonly Dictionary<char, char> UnescapeMap;

        static StringUtil()
        {
            EscapeMap = new Dictionary<char, string>(ActualChars.Length);
            UnescapeMap = new Dictionary<char, char>(EscapeCodes.Length);

            for (int i = 0; i < ActualChars.Length; i++)
            {
                char actual = ActualChars[i];
                char code = EscapeCodes[i];

                // e.g. actual '\n' -> escape string "\\n"
                EscapeMap[actual] = "\\" + code;

                // e.g. code 'n' -> actual '\n'
                UnescapeMap[code] = actual;
            }
        }
        /// <summary>
        /// Escape the characters in rawString so it is safe to place INSIDE JSON quotes.
        /// This will include '\', so escape codes like "\n" typed out in the string, will be .. UNescaped (for later deserialization).
        /// Returns null if input is null. Does NOT add surrounding quotes.
        /// </summary>
        public static string EscapeStringForJson(string rawString)
        {
            if (rawString == null)
                return null;

            if (rawString.Length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder(rawString.Length + 16);
            foreach (char c in rawString)
            {
                if (EscapeMap.TryGetValue(c, out string rep))
                    sb.Append(rep);
                else if (char.IsControl(c) || c < ' ') //it is a control character, but not in our EscapeMap, write character code directly
                    sb.AppendFormat("\\u{0:X4}", (int)c);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Unescape the JSON-escaped content (the interior of a JSON string).
        /// Returns null if input is null.
        /// </summary>
        public static string UnescapeJsonString(string escapedString)
        {
            if (escapedString == null)
                return null;

            if (escapedString.Length == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder(escapedString.Length);
            for (int i = 0; i < escapedString.Length; i++)
            {
                char c = escapedString[i];
                if (c == '\\' && i + 1 < escapedString.Length)
                {
                    i++;
                    char esc = escapedString[i];
                    if (esc == 'u')
                    {
                        // \uXXXX
                        if (i + 4 < escapedString.Length)
                        {
                            string hex = escapedString.Substring(i + 1, 4);
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code))
                            {
                                sb.Append((char)code);
                                i += 4;
                                continue;
                            }
                        }
                        // malformed \u or truncated -> append literal sequence "\u"
                        sb.Append("\\u");
                        continue;
                    }

                    if (UnescapeMap.TryGetValue(esc, out char actual))
                        sb.Append(actual);
                    else // unknown escape sequence -> append the escaped character as-is
                        sb.Append(esc);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// In addition to putting quotes around the provide string, it will escape all literal characters that require it.  This will include '\', so escape codes like "\n" typed out in the string, will be .. double-escaped.
        /// </summary>
        /// <param name="rawString">the string to quote and escape.</param>
        /// <returns>Json friendly representation of a string</returns>
        public static string Quote(string rawString)
        {
            if (rawString == null)
                return "null";

            if (rawString.Length == 0)
                return "\"\"";

            StringBuilder sb = new StringBuilder(rawString.Length + 16);
            sb.Append('"');
            sb.Append(EscapeStringForJson(rawString));
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// Process will, in addition to removing outside quotes, convert all escape sequences found inside into their literal characters in the return string.
        /// </summary>
        /// <param name="quotedString">string to be unquoted: e.g. json encoded string data</param>
        /// <returns></returns>
        public static string UnQuote(string quotedString)
        {
            if (quotedString == null)
                return null;

            string trimmed = quotedString.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return UnescapeJsonString(trimmed.Substring(1, trimmed.Length - 2));
            }

            // Not quoted — return original trimmed string
            return quotedString;
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
            //bool first = true;
            foreach (var item in collection)
            {
            //    if (!first) builder.Append(",");
                Write(item, null); // No field name in array elements
              //  first = false;
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
            return ReadWithKey<T>(expectedFieldName, null,  out string ignored, out bool ignoredBool);
        }
        public T Read<T>(string expectedFieldName, object[] constructorParams)
        {
            return ReadWithKey<T>(expectedFieldName, constructorParams, out string ignored, out bool ignoredBool);
        }
        public T Read<T>(string expectedFieldName, out bool foundNothing)
        {
            return ReadWithKey<T>(expectedFieldName, null, out string ignored, out foundNothing);
        }

        /// <summary>
        /// if object is a dictionary entry, this function will provide the key string (via out param), as well as returning the entry's value.
        /// TODO: make json system more robust in future by allowing user to load fields out of order, by specific name
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expectedFieldName"></param>
        /// <param name="foundFieldName"></param>
        /// <returns></returns>
        private T ReadWithKey<T>(string expectedFieldName,object[] constructorParams, out string foundFieldName, out bool foundNothing)
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
                if (constructorParams == null || constructorParams.Length == 0)
                {
                    if (ReaderExtension.TryStaticReadAndCreate<T>(subReader, out typedResult))
                    {
                        return typedResult;
                    }
                }
                else
                {
                    //new constructor param test
                    if (ReaderExtension.TryStaticReadAndConstructorCreate<T>(subReader, constructorParams, out typedResult))
                    {
                        return typedResult;
                    }
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
        private void ConsumeWhiteSpace()
        {
            int ch;
            while ((ch = reader.Peek()) != -1 && (char.IsWhiteSpace((char)ch) || (char)ch == ','))
                reader.Read();
        }

        private void SkipOpeningBrace()
        {
            ConsumeWhiteSpace();

            if (reader.Peek() == '{' || reader.Peek() == '[')
                reader.Read(); // consume opening brace
        }
        private void SkipClosingBrace()
        {
            ConsumeWhiteSpace();

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
                output = (T)(object)jsonInput;
                return true;
            }
            else if (typeofT == typeof(int))
            {
                int result;
                if (!int.TryParse(jsonInput,System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result))
                    throw new DataMisalignedException("Failed to Parse int field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }
            else if (typeofT == typeof(float))
            {
                float result;
                if (!float.TryParse(jsonInput, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result))
                    throw new DataMisalignedException("Failed to Parse float field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }
            else if (typeofT == typeof(long))
            {
                long result;
                if (!long.TryParse(jsonInput, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result))
                    throw new DataMisalignedException("Failed to Parse long field: " + fieldName + "  Input provided: " + jsonInput);
                output = (T)(object)result;
                return true;
            }
            else if (typeofT == typeof(double))
            {
                double result;
                if (!double.TryParse(jsonInput, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result))
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


            output = default(T);
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
                ConsumeWhiteSpace();
                if (reader.Peek() == '}' || reader.Peek() == ']')//check for immidiate end
                {
                    SkipClosingBrace();
                    return dict;
                }
                K keyValue = Read<K>("key");
                V entryValue = Read<V>("value");
                if(keyValue!=null)
                    dict.Add(keyValue, entryValue);
                SkipClosingBrace();

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