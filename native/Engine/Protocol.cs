using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Collections.Generic;
namespace LocalCorrect {
    [DataContract] internal sealed class Request {
        [DataMember(IsRequired=true)] public int version;
        [DataMember(IsRequired=true)] public string operation;
        [DataMember] public string text;
        [DataMember] public string language;
        [DataMember] public string mode;
        [DataMember] public string context;
        [DataMember] public int contextOffset;
    }
    [DataContract] internal sealed class Response {
        [DataMember(IsRequired=true)] public int version;
        [DataMember(IsRequired=true)] public string status;
        [DataMember] public Result result;
    }
    internal static class Frames {
        internal const int MaxBytes = 8 * 1024 * 1024;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static void ReadExactly(Stream stream, byte[] bytes, int length) {
            int offset = 0;
            while (offset < length) { int read = stream.Read(bytes, offset, length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        }
        static void SingleObject(byte[] bytes) {
            int depth = 0; bool quoted = false, escaped = false, started = false, complete = false;
            foreach (byte value in bytes) {
                if (complete) { if (value != 32 && value != 9 && value != 10 && value != 13) throw new InvalidDataException(); continue; }
                if (!started) { if (value == 32 || value == 9 || value == 10 || value == 13) continue; if (value != 123) throw new InvalidDataException(); started = true; }
                if (quoted) { if (escaped) escaped = false; else if (value == 92) escaped = true; else if (value == 34) quoted = false; continue; }
                if (value == 34) quoted = true;
                else if (value == 123 || value == 91) { if (++depth > 32) throw new InvalidDataException(); }
                else if (value == 125 || value == 93) { if (--depth < 0) throw new InvalidDataException(); if (depth == 0) complete = true; }
            }
            if (!complete || quoted) throw new InvalidDataException();
        }
        internal static T Read<T>(Stream stream) {
            var prefix = new byte[4]; ReadExactly(stream, prefix, 4);
            uint length = (uint)prefix[0] | (uint)prefix[1] << 8 | (uint)prefix[2] << 16 | (uint)prefix[3] << 24;
            if (length == 0 || length > MaxBytes) throw new InvalidDataException();
            var bytes = new byte[(int)length]; ReadExactly(stream, bytes, bytes.Length);
            SingleObject(bytes);
            Utf8.GetString(bytes); // Reject malformed UTF-8 before JSON deserialization.
            // A complete JSON reader pass rejects trailing documents, duplicate
            // envelope members and unknown envelope fields before deserializing.
            var allowed = typeof(T) == typeof(Request) ? new HashSet<string> { "version", "operation", "text", "language", "mode", "context", "contextOffset" } : new HashSet<string> { "version", "status", "result" };
            var seen = new HashSet<string>();
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, new XmlDictionaryReaderQuotas { MaxDepth = 32, MaxStringContentLength = MaxBytes, MaxArrayLength = MaxBytes, MaxBytesPerRead = 4096, MaxNameTableCharCount = 4096 })) {
                while (reader.Read()) {
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0 && (reader.LocalName != "root" || reader.GetAttribute("type") != "object")) throw new InvalidDataException();
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && (!allowed.Contains(reader.LocalName) || !seen.Add(reader.LocalName))) throw new InvalidDataException();
                }
            }
            using (var data = new MemoryStream(bytes, false)) {
                var result = (T)new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = MaxBytes }).ReadObject(data);
                if (data.Position != data.Length || result == null) throw new InvalidDataException();
                return result;
            }
        }
        internal static void Write<T>(Stream stream, T value) {
            using (var data = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = MaxBytes }).WriteObject(data, value);
                if (data.Length == 0 || data.Length > MaxBytes) throw new InvalidDataException();
                uint length = (uint)data.Length;
                stream.Write(new[] { (byte)length, (byte)(length >> 8), (byte)(length >> 16), (byte)(length >> 24) }, 0, 4);
                data.Position = 0; data.CopyTo(stream); stream.Flush();
            }
        }
    }
}
