using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

public static class LibraryBootstrap
{
    static readonly object Sync = new object();
    static bool installed;
    public static void Install()
    {
        lock (Sync)
        {
            if (installed) return;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                if (new AssemblyName(e.Name).Name != "dnlib") return null;
                using (Stream stream = typeof(LibraryBootstrap).Assembly.GetManifestResourceStream("Embedded.dnlib.dll"))
                using (MemoryStream buffer = new MemoryStream())
                {
                    if (stream == null) throw new FileNotFoundException("工具内嵌的 dnlib 缺失，请重新解压工具。");
                    stream.CopyTo(buffer);
                    return Assembly.Load(buffer.ToArray());
                }
            };
            installed = true;
        }
    }
}

public sealed class AnalysisResult
{
    public string Version { get; internal set; }
    public string Detail { get; internal set; }
    public int ChangeCount { get { return Changes.Count; } }
    internal readonly List<ByteChange> Changes = new List<ByteChange>();
}

internal sealed class ByteChange
{
    internal int Offset;
    internal byte[] Before, After;
}

public static class Patcher
{
    public static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }
    static byte[] Read(string path)
    {
        using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (file.Length < 64 || file.Length > Int32.MaxValue) throw new InvalidOperationException("文件过小或过大，无法读取为 PCL .NET 启动器。");
            byte[] data = new byte[(int)file.Length];
            int total = 0, count;
            while (total < data.Length && (count = file.Read(data, total, data.Length - total)) > 0) total += count;
            if (total != data.Length) throw new IOException("文件读取不完整，请重新选择。");
            return data;
        }
    }
    public static bool Check(string path) { return Analyze(path).ChangeCount > 0; }
    public static AnalysisResult Analyze(string path)
    {
        LibraryBootstrap.Install();
        return AnalyzeBytes(Read(path));
    }
    static InvalidOperationException Unsupported(string reason)
    {
        return new InvalidOperationException("无法自动定位：" + reason + "。未修改任何文件。");
    }
    static bool CallNamed(Instruction i, string name)
    {
        IMethod method = i.Operand as IMethod;
        return (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && method != null && method.Name == name;
    }
    static bool LoadsField(Instruction i, FieldDef field)
    {
        IField reference = i.Operand as IField;
        return i.OpCode == OpCodes.Ldsfld && reference != null && reference.MDToken == field.MDToken;
    }
    static int CodeOffset(ModuleDefMD module, MethodDef method, byte[] bytes)
    {
        int header = checked((int)module.Metadata.PEImage.ToFileOffset(method.RVA));
        if (header < 0 || header + 2 > bytes.Length) throw Unsupported("方法头越界");
        int length;
        if ((bytes[header] & 3) == 2) length = 1;
        else if ((bytes[header] & 3) == 3) length = (bytes[header + 1] >> 4) * 4;
        else throw Unsupported("不是可识别的 IL 方法体");
        if (length != 1 && length < 12) throw Unsupported("方法头不完整");
        return checked(header + length);
    }
    static void AddChange(AnalysisResult result, byte[] bytes, int start, int count, byte[] replacement)
    {
        if (count < replacement.Length || start < 0 || start > bytes.Length - count) throw Unsupported("修补区域越界");
        if (result.Changes.Any(c => start < c.Offset + c.Before.Length && c.Offset < start + count)) throw Unsupported("修补区域重叠");
        byte[] after = new byte[count];
        Array.Copy(replacement, after, replacement.Length);
        result.Changes.Add(new ByteChange { Offset = start, Before = bytes.Skip(start).Take(count).ToArray(), After = after });
    }
    static bool Inside(Instruction instruction, uint start, uint end)
    {
        return instruction != null && instruction.Offset > start && instruction.Offset < end;
    }
    static void ValidateRegion(MethodDef method, uint start, uint end)
    {
        foreach (Instruction i in method.Body.Instructions)
        {
            if (i.Offset >= start && i.Offset < end) continue;
            Instruction target = i.Operand as Instruction;
            IList<Instruction> targets = i.Operand as IList<Instruction>;
            if (Inside(target, start, end) || (targets != null && targets.Any(t => Inside(t, start, end))))
                throw Unsupported("其他分支跳入待替换代码中间");
        }
        foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
            if (new[] { handler.TryStart, handler.TryEnd, handler.HandlerStart, handler.HandlerEnd, handler.FilterStart }.Any(i => Inside(i, start, end)))
                throw Unsupported("异常处理边界位于待替换代码内");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static AnalysisResult AnalyzeBytes(byte[] bytes)
    {
        ModuleDefMD module;
        try { module = ModuleDefMD.Load(bytes); }
        catch (Exception ex) { throw new InvalidOperationException("无法读取为 .NET 程序：" + ex.Message); }
        using (module)
        {
            TypeDef[] pages = module.GetTypes().Where(t => t.Name == "PageDownloadInstall").ToArray();
            if (pages.Length != 1) throw Unsupported("未找到唯一的 PCL 下载页面，可能使用了不同结构或混淆");
            TypeDef page = pages[0];
            MethodDef[] initializers = page.Methods.Where(m => m.Name == "LoaderInit" && m.HasBody && m.Body.Instructions.Any(i => CallNamed(i, "PageLoaderInit"))).ToArray();
            if (initializers.Length != 1) throw Unsupported("下载页面初始化方法不匹配");
            MethodDef init = initializers[0];
            FieldDef[] loaders = module.GetTypes().Where(t => t.Name == "ModDownload").SelectMany(t => t.Fields)
                .Where(f => f.Name == "DlClientListLoader" && f.IsStatic && f.FieldSig != null && f.FieldSig.Type.FullName.Contains("LoaderTask`2") && f.FieldSig.Type.FullName.Contains("DlClientListResult")).ToArray();
            if (loaders.Length != 1) throw Unsupported("未找到唯一的原版版本列表加载器");
            FieldDef loader = loaders[0];
            AnalysisResult result = new AnalysisResult { Version = module.Assembly == null ? "未知" : module.Assembly.Version.ToString() };
            IList<Instruction> instructions = init.Body.Instructions;
            Instruction[] markers = instructions.Where(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "关闭下载").ToArray();
            if (markers.Length > 1) throw Unsupported("关闭下载代码出现多次，无法确定修补位置");
            if (markers.Length == 1)
            {
                int first = instructions.IndexOf(markers[0]);
                int last = -1;
                for (int n = first + 1; n < instructions.Count; n++)
                {
                    Instruction i = instructions[n];
                    IMethod constructor = i.Operand as IMethod;
                    if (i.OpCode == OpCodes.Newobj && constructor != null && constructor.DeclaringType.FullName == loader.FieldSig.Type.FullName)
                    {
                        if (constructor.MethodSig == null || constructor.MethodSig.Params.Count != 4) throw Unsupported("受限加载器构造参数发生变化");
                        last = n; break;
                    }
                    // This block may construct a delegate, read/cache a closure, and branch internally.
                    if (!(i.OpCode == OpCodes.Nop || i.OpCode == OpCodes.Ldsfld || i.OpCode == OpCodes.Stsfld || i.OpCode == OpCodes.Ldftn ||
                        i.OpCode == OpCodes.Ldnull || i.OpCode == OpCodes.Dup || i.IsLdcI4() || i.OpCode.FlowControl == FlowControl.Branch ||
                        i.OpCode.FlowControl == FlowControl.Cond_Branch || (i.OpCode == OpCodes.Newobj && constructor != null && constructor.DeclaringType.FullName.StartsWith("System.Action`1<"))))
                        throw Unsupported("受限加载器中含有无法识别的指令 " + i.OpCode.Name);
                }
                if (last < 0) throw Unsupported("未找到受限加载器的构造调用");
                uint start = markers[0].Offset;
                uint end = instructions[last].Offset + (uint)instructions[last].GetSize();
                ValidateRegion(init, start, end);
                byte[] replacement = new byte[5]; replacement[0] = 0x7e;
                Array.Copy(BitConverter.GetBytes(loader.MDToken.Raw), 0, replacement, 1, 4);
                AddChange(result, bytes, CodeOffset(module, init, bytes) + (int)start, checked((int)(end - start)), replacement);
            }
            else if (!instructions.Any(i => LoadsField(i, loader)))
                throw Unsupported("未发现已知限制模式，也未确认下载页使用正常加载器");

            string extra = "版本修改页未找到或结构不同，未改动该入口。";
            var visibilitySites = new List<Tuple<MethodDef, Instruction>>();
            foreach (TypeDef type in module.GetTypes().Where(t => t.Name == "PageDownloadModify"))
            foreach (MethodDef method in type.Methods.Where(m => m.HasBody))
            {
                IList<Instruction> body = method.Body.Instructions;
                for (int n = 0; n < body.Count; n++)
                {
                    if (!CallNamed(body[n], "get_CardMinecraft")) continue;
                    int constant = n + 1;
                    while (constant < body.Count && body[constant].OpCode == OpCodes.Nop) constant++;
                    if (constant >= body.Count || !body[constant].IsLdcI4()) continue;
                    int setter = constant + 1;
                    while (setter < body.Count && body[setter].OpCode == OpCodes.Nop) setter++;
                    if (setter < body.Count && CallNamed(body[setter], "set_Visibility")) visibilitySites.Add(Tuple.Create(method, body[constant]));
                }
            }
            if (visibilitySites.Count == 1)
            {
                var site = visibilitySites[0];
                int visibility = site.Item2.GetLdcI4Value();
                if (visibility == 2)
                {
                    AddChange(result, bytes, CodeOffset(module, site.Item1, bytes) + (int)site.Item2.Offset, site.Item2.GetSize(), new byte[] { 0x16 });
                    extra = "同时恢复版本修改页的原版入口。";
                }
                else if (visibility == 0) extra = "版本修改页的原版入口已可见。";
            }
            result.Detail = (markers.Length == 1 ? "已定位原版下载限制，可生成副本。" : "原版下载页已使用正常加载器。") + "\n" + extra;
            return result;
        }
    }
    public static void Patch(string input, string output)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        if (String.Equals(input, output, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请另存为副本，不能覆盖原启动器。");
        LibraryBootstrap.Install();
        byte[] bytes = Read(input);
        AnalysisResult plan = AnalyzeBytes(bytes);
        if (plan.ChangeCount == 0) throw new InvalidOperationException("原版下载入口无需处理，未生成重复副本。");
        foreach (ByteChange change in plan.Changes)
        {
            for (int n = 0; n < change.Before.Length; n++)
                if (bytes[change.Offset + n] != change.Before[n]) throw Unsupported("指令校验失败");
            Array.Copy(change.After, 0, bytes, change.Offset, change.After.Length);
        }
        if (AnalyzeBytes(bytes).ChangeCount != 0) throw Unsupported("输出结构复检未通过");
        string expected = Hash(bytes);
        bool created = false, complete = false;
        try
        {
            using (FileStream stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true; stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            byte[] saved = Read(output);
            if (Hash(saved) != expected || AnalyzeBytes(saved).ChangeCount != 0) throw new IOException("写入后的校验失败。");
            complete = true;
        }
        finally { if (created && !complete) File.Delete(output); }
    }
}
