using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;

namespace WickedFontTool;

public record FontObject(string Entry, long Id, string Name, string Hash, bool Target);

// Open one container at a time; LZ4 blocks are read lazily by AssetsTools.NET.
public sealed class UnityFonts : IDisposable
{
    static readonly (string Code, Regex Pattern)[] Patterns =
    {
        ("SC", new Regex(@"^NotoSerif(?:CJK)?SC[-_]?(?:Regular|Bold)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("TC", new Regex(@"^NotoSerif(?:CJK)?TC[-_]?(?:Regular|Bold)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("JP", new Regex(@"^NotoSerif(?:CJK)?JP[-_]?(?:Regular|Bold)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("KR", new Regex(@"^NotoSerif(?:CJK)?KR[-_]?(?:Regular|Bold)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };
    readonly AssetsManager manager = new();
    readonly List<(string Name, AssetsFileInstance File, int Index)> files = new();
    readonly BundleFileInstance? bundle;
    readonly IReadOnlySet<string> languages;
    readonly byte originalCompression;
    string? unpacked;
    public long ExpandedSize { get; }

    public static bool IsTarget(string name, IReadOnlySet<string> selected) =>
        Patterns.Any(p => selected.Contains(p.Code) && p.Pattern.IsMatch(name.Replace(" ", "")));
    public bool IsTarget(string name) => IsTarget(name, languages);

    public UnityFonts(string path, string scratch, IReadOnlySet<string>? selected = null)
    {
        languages = selected ?? L10n.All;
        try
        {
            manager.UseTemplateFieldCache = false;
            using var schema = typeof(UnityFonts).Assembly.GetManifestResourceStream("WickedFontTool.Resources.classdata.tpk")!;
            manager.LoadClassPackage(schema);
            using var probe = File.OpenRead(path);
            byte[] magic = new byte[8]; probe.ReadExactly(magic);
            if (Encoding.ASCII.GetString(magic).StartsWith("UnityFS"))
            {
                bundle = manager.LoadBundleFile(path, false);
                // Preserve the container's original storage: repacking an uncompressed
                // bundle as LZ4 breaks Unity 6 streaming reads of .resS texture data.
                originalCompression = bundle.file.BlockAndDirInfo.BlockInfos is { Length: > 0 } originalBlocks
                    ? originalBlocks[0].GetCompressionType() : (byte)0;
                if (bundle.file.DataIsCompressed)
                {
                    Directory.CreateDirectory(scratch);
                    unpacked = Path.Combine(scratch, Guid.NewGuid() + ".unpack");
                    bundle.file = BundleHelper.UnpackBundleToStream(bundle.file,
                        new FileStream(unpacked, FileMode.CreateNew, FileAccess.ReadWrite));
                }
                ExpandedSize = bundle.file.BlockAndDirInfo.DirectoryInfos.Sum(x => x.DecompressedSize);
                for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                    if ((bundle.file.BlockAndDirInfo.DirectoryInfos[i].Flags & 4) != 0)
                        files.Add((bundle.file.BlockAndDirInfo.DirectoryInfos[i].Name,
                            manager.LoadAssetsFileFromBundle(bundle, i, false), i));
            }
            else
            {
                files.Add(("", manager.LoadAssetsFile(path, false), -1));
                ExpandedSize = probe.Length;
            }
        }
        catch { Dispose(); throw; }
    }

    AssetTypeValueField Read(AssetsFileInstance file, AssetFileInfo info)
    {
        if (!file.file.Metadata.TypeTreeEnabled)
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
        var template = manager.GetTemplateBaseField(file, info);
        void Optimize(AssetTypeTemplateField t)
        {
            if (t.IsArray && t.Children.Count == 2 &&
                (t.Children[1].ValueType == AssetValueType.Int8 || t.Children[1].ValueType == AssetValueType.UInt8))
                t.ValueType = AssetValueType.ByteArray;
            foreach (var child in t.Children) Optimize(child);
        }
        Optimize(template);
        file.file.Reader.Position = info.GetAbsoluteByteOffset(file.file);
        var value = template.MakeValue(file.file.Reader);
        // Reject a mismatched schema before a single byte can be rewritten.
        byte[] serialized = value.WriteToByteArray(file.file.Header.Endianness);
        file.file.Reader.Position = info.GetAbsoluteByteOffset(file.file);
        byte[] original = file.file.Reader.ReadBytes(checked((int)info.ByteSize));
        if (!serialized.AsSpan().SequenceEqual(original))
            throw new InvalidDataException(L10n.S($"{file.name} 的 Font 结构往返校验失败（Unity {file.file.Metadata.UnityVersion}），停止写入。",
                $"{file.name} 的 Font 結構往返校驗失敗（Unity {file.file.Metadata.UnityVersion}），停止寫入。"));
        return value;
    }

    static AssetTypeValueField FontBytes(AssetTypeValueField field)
    {
        var data = field["m_FontData"];
        if (!data.IsDummy && data.TemplateField.ValueType == AssetValueType.ByteArray) return data;
        var array = data["Array"];
        if (array.IsDummy || array.TemplateField.ValueType != AssetValueType.ByteArray)
            throw new InvalidDataException(L10n.S("Font.m_FontData 结构已改变，停止写入。", "Font.m_FontData 結構已改變，停止寫入。"));
        return array;
    }

    public List<FontObject> Inspect()
    {
        var result = new List<FontObject>();
        foreach (var entry in files)
        foreach (var info in entry.File.file.GetAssetsOfType(AssetClassID.Font))
        {
            var field = Read(entry.File, info);
            string name = field["m_Name"].AsString;
            result.Add(new(entry.Name, info.PathId, name, Store.Hash(FontBytes(field).AsByteArray), IsTarget(name)));
        }
        return result;
    }

    // Ignore only the selected Font byte payloads. All other object bytes and
    // bundle entries must match when adopting backups from the Python tool.
    public string ContentIdentity()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        void Text(string s) { var b = Encoding.UTF8.GetBytes(s); hash.AppendData(BitConverter.GetBytes(b.Length)); hash.AppendData(b); }
        void Range(Stream stream, long count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (read == 0) throw new EndOfStreamException();
                hash.AppendData(buffer.AsSpan(0, read)); count -= read;
            }
        }
        foreach (var entry in files.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            Text(entry.Name);
            var file = entry.File.file;
            Text(file.Header.Version.ToString()); Text(file.Header.Endianness.ToString());
            Text(file.Metadata.UnityVersion); Text(file.Metadata.TargetPlatform.ToString());
            Text(file.Metadata.TypeTreeEnabled.ToString()); Text(file.Metadata.UserInformation ?? "");
            Text(JsonSerializer.Serialize(file.Metadata.Externals)); Text(JsonSerializer.Serialize(file.Metadata.ScriptTypes));
            using (var meta = new MemoryStream())
            {
                using var writer = new AssetsFileWriter(meta);
                foreach (var type in file.Metadata.TypeTreeTypes) type.Write(writer, file.Header.Version, file.Metadata.TypeTreeEnabled);
                foreach (var type in file.Metadata.RefTypes) type.Write(writer, file.Header.Version, file.Metadata.TypeTreeEnabled);
                hash.AppendData(meta.ToArray());
            }
            foreach (var info in file.Metadata.AssetInfos.OrderBy(x => x.PathId))
            {
                Text(info.PathId.ToString()); Text(info.TypeId.ToString()); Text(info.GetScriptIndex(file).ToString());
                if (info.TypeId == (int)AssetClassID.Font)
                {
                    var field = Read(entry.File, info);
                    if (IsTarget(field["m_Name"].AsString)) FontBytes(field).AsByteArray = Array.Empty<byte>();
                    var data = field.WriteToByteArray(file.Header.Endianness);
                    Text(data.Length.ToString()); hash.AppendData(data);
                }
                else
                {
                    Text(info.ByteSize.ToString());
                    file.Reader.Position = info.GetAbsoluteByteOffset(file);
                    Range(file.Reader.BaseStream, info.ByteSize);
                }
            }
        }
        if (bundle != null)
        {
            var assetIndexes = files.Select(f => f.Index).ToHashSet();
            for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                var dir = bundle.file.BlockAndDirInfo.DirectoryInfos[i]; Text(dir.Name); Text(dir.Flags.ToString());
                if (assetIndexes.Contains(i)) continue;
                Text(dir.DecompressedSize.ToString());
                bundle.file.DataReader.Position = dir.Offset;
                Range(bundle.file.DataReader.BaseStream, dir.DecompressedSize);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public void Write(string output, byte[] font, Action<string> report)
    {
        int changed = 0;
        foreach (var entry in files)
        {
            bool dirty = false;
            foreach (var info in entry.File.file.GetAssetsOfType(AssetClassID.Font))
            {
                var field = Read(entry.File, info);
                if (!IsTarget(field["m_Name"].AsString)) continue;
                FontBytes(field).AsByteArray = font;
                info.SetNewData(field.WriteToByteArray(entry.File.file.Header.Endianness));
                changed++; dirty = true;
            }
            if (dirty && bundle != null)
                bundle.file.BlockAndDirInfo.DirectoryInfos[entry.Index].SetNewData(entry.File.file);
        }
        if (changed == 0) throw new InvalidDataException(L10n.S("没有可替换的目标 Font 对象。", "沒有可替換的目標 Font 物件。"));
        if (bundle == null)
        {
            using var writer = new AssetsFileWriter(output);
            files[0].File.file.Write(writer);
        }
        else
        {
            string raw = output + ".raw";
            try
            {
                // Keep the container's original storage. An uncompressed bundle that
                // is repacked as LZ4 loses Unity 6 streaming reads of .resS data,
                // which corrupts baked atlases on load.
                var compression = originalCompression switch
                {
                    (byte)AssetBundleCompressionType.LZMA => AssetBundleCompressionType.LZMA,
                    (byte)AssetBundleCompressionType.LZ4 => AssetBundleCompressionType.LZ4,
                    (byte)AssetBundleCompressionType.LZ4Fast => AssetBundleCompressionType.LZ4Fast,
                    _ => AssetBundleCompressionType.None,
                };
                if (compression == AssetBundleCompressionType.None)
                    report(L10n.S("按原始未压缩方式重打包资源容器…", "按原始未壓縮方式重新打包資源容器…"));
                else
                    report(L10n.S("写出资源，随后进行分块压缩…", "寫出資源，隨後進行分塊壓縮…"));
                using (var writer = new AssetsFileWriter(raw)) bundle.file.Write(writer);
                var repack = new AssetBundleFile();
                try
                {
                    repack.Read(new AssetsFileReader(File.OpenRead(raw)));
                    using var writer = new AssetsFileWriter(output);
                    repack.Pack(writer, compression, true, new PackProgress(report));
                }
                finally { repack.Close(); }
            }
            finally { if (File.Exists(raw)) File.Delete(raw); }
        }
    }

    public void Dispose()
    {
        manager.UnloadAll();
        if (unpacked != null && File.Exists(unpacked)) File.Delete(unpacked);
    }

    sealed class PackProgress(Action<string> report) : IAssetBundleCompressProgress
    {
        int last = -1;
        public void SetProgress(float progress)
        {
            int percent = (int)(progress * 100);
            if (percent / 5 == last / 5 && last >= 0) return;
            last = percent; report(L10n.S($"压缩 {percent}%", $"壓縮 {percent}%"));
        }
    }
}
