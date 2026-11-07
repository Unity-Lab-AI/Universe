// Universe · core · the save file: our own versioned binary chunks.
// A file starts with "DNAS" and the file version, then holds chunks: a four-letter name, the chunk's own version,
// its length, then its bytes. A reader skips chunks it does not know, so newer saves still open in older games
// as far as they can, and each part of the game (universe, planets, life, civilizations, player) owns its own chunk.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Universe.Core
{
    internal static class SaveFile
    {
        public const uint FileVersion = 1;

        /// <summary>Writes a save file from named chunks (name, version, bytes).</summary>
        public static void Write(string path, IList<Chunk> chunks)
        {
            string tmp = path + ".part";                                         // written aside first: a crash never leaves half a save
            using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                var o = new BinaryWriter(f);
                o.Write(new[] { (byte)'D', (byte)'N', (byte)'A', (byte)'S' }); o.Write(FileVersion); o.Write(chunks.Count);
                foreach (var c in chunks)
                {
                    if (c.Name.Length != 4) throw new ArgumentException("chunk names are four letters: " + c.Name);
                    o.Write(System.Text.Encoding.ASCII.GetBytes(c.Name)); o.Write(c.Version); o.Write((long)c.Data.Length); o.Write(c.Data);
                }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Reads every chunk of a save file.</summary>
        public static List<Chunk> Read(string path)
        {
            var list = new List<Chunk>();
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var r = new BinaryReader(f);
                var magic = r.ReadBytes(4);
                if (magic.Length < 4 || magic[0] != 'D' || magic[1] != 'N' || magic[2] != 'A' || magic[3] != 'S') throw new InvalidDataException("not a " + Universe.Shell.PreviewText.AppName + " save");
                uint ver = r.ReadUInt32();
                if (ver > FileVersion) throw new InvalidDataException("this save is from a newer game (file version " + ver + ")");
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string name = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)); uint cv = r.ReadUInt32(); long len = r.ReadInt64();
                    if (len < 0 || len > f.Length) throw new InvalidDataException("a damaged chunk: " + name);
                    list.Add(new Chunk { Name = name, Version = cv, Data = r.ReadBytes((int)len) });
                }
            }
            return list;
        }

        /// <summary>Writes every field of a struct by name, so fields can be added later and old saves still read.</summary>
        public static void WriteFields<T>(BinaryWriter o, T value) where T : struct
        {
            var fs = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            o.Write(fs.Length);
            object boxed = value;
            foreach (var fi in fs)
            {
                o.Write(fi.Name);
                if (fi.FieldType == typeof(float)) { o.Write((byte)1); o.Write((float)fi.GetValue(boxed)); }
                else if (fi.FieldType == typeof(int)) { o.Write((byte)2); o.Write((int)fi.GetValue(boxed)); }
                else if (fi.FieldType == typeof(double)) { o.Write((byte)3); o.Write((double)fi.GetValue(boxed)); }
                else throw new NotSupportedException("save: field type " + fi.FieldType.Name);
            }
        }

        /// <summary>Reads a struct written by <see cref="WriteFields"/>; fields the save does not have keep their defaults.</summary>
        public static T ReadFields<T>(BinaryReader r) where T : struct
        {
            object boxed = default(T);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadString(); byte kind = r.ReadByte();
                object v = kind == 1 ? (object)r.ReadSingle() : kind == 2 ? (object)r.ReadInt32() : kind == 3 ? (object)r.ReadDouble() : null;
                if (v == null) throw new InvalidDataException("save: unknown field kind for " + name);
                var fi = typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (fi != null && fi.FieldType == v.GetType()) fi.SetValue(boxed, v);
            }
            return (T)boxed;
        }
    }

    internal sealed class Chunk { public string Name; public uint Version; public byte[] Data; }
}
