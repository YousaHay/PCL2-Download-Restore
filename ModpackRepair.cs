using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

internal static class ModpackRepair
{
    internal const string Restriction = "为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……";
    internal sealed class Plan
    {
        internal MethodDef Entry, Dispatcher, List, ClosureConstructor, Callback, ThreadRunner, InstallAdapter;
        internal FieldDef FileField, TargetType;
        internal Instruction Guard, SavePointer;
        internal Local Archive, BaseFolder, PackType;
        internal ExceptionHandler Finally;
        internal bool EntryBlocked, DispatcherBlocked, ListBlocked;
        internal int ModpackTypeValue;
        internal int Count { get { return (EntryBlocked ? 1 : 0) + (DispatcherBlocked ? 1 : 0) + (ListBlocked ? 1 : 0); } }
    }
    static InvalidOperationException Invalid(string reason) { return new InvalidOperationException("整合包安装无法自动恢复：" + reason + "。未修改任何文件。"); }
    static bool Calls(Instruction i, string name)
    {
        IMethod method = i.Operand as IMethod;
        return (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && method != null && method.Name == name;
    }
    static bool IsBlocked(MethodDef method) { return method != null && method.HasBody && method.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == Restriction); }
    static readonly string[] Factories = { "InstallPackCurseForge", "InstallPackHMCL", "InstallPackMMC", "InstallPackMCBBS", "InstallPackModrinth", "InstallPackLauncherPack", "InstallPackCompress" };
    static readonly int[] Arity = { 5, 3, 3, 4, 5, 3, 2 };

    internal static Plan Inspect(ModuleDefMD module)
    {
        Plan plan = new Plan();
        TypeDef mod = module.GetTypes().SingleOrDefault(t => t.Name == "ModModpack");
        if (mod == null) return plan;
        plan.Entry = mod.Methods.SingleOrDefault(m => m.Name == "ModpackInstall" && m.IsStatic && m.MethodSig.Params.Count == 0);
        plan.Dispatcher = mod.Methods.SingleOrDefault(m => m.Name == "ModpackInstall" && m.IsStatic && m.MethodSig.Params.Count == 3 && m.MethodSig.Params.All(p => p.FullName == "System.String"));
        plan.EntryBlocked = IsBlocked(plan.Entry); plan.DispatcherBlocked = IsBlocked(plan.Dispatcher);
        if (plan.EntryBlocked)
        {
            if (plan.Dispatcher == null) throw Invalid("缺少接收文件路径的安装方法");
            var constructors = plan.Entry.Body.Instructions.Where(i => i.OpCode == OpCodes.Newobj).Select(i => i.Operand as IMethod)
                .Where(m => m != null && m.MethodSig.Params.Count == 0).ToArray();
            if (constructors.Length != 1) throw Invalid("文件选择回调结构不明确");
            TypeDef closure = constructors[0].DeclaringType.ResolveTypeDef();
            if (closure == null || closure.Module != module) throw Invalid("文件选择回调不在当前程序中");
            plan.ClosureConstructor = closure.Methods.Single(m => m.IsInstanceConstructor && m.MethodSig.Params.Count == 0);
            plan.Callback = closure.Methods.SingleOrDefault(m => m.HasBody && !m.IsStatic && m.MethodSig.Params.Count == 0 && m.Body.Instructions.Any(i => Calls(i, "ModpackInstall")));
            if (plan.Callback == null) throw Invalid("缺少原有的安装线程回调");
            plan.FileField = closure.Fields.SingleOrDefault(f => !f.IsStatic && f.FieldSig.Type.FullName == "System.String");
            plan.ThreadRunner = module.GetTypes().Where(t => t.Name == "ModBase").SelectMany(t => t.Methods).SingleOrDefault(m => m.Name == "RunInThread" && m.IsStatic && m.MethodSig.Params.Count == 1);
            if (plan.FileField == null || plan.ThreadRunner == null) throw Invalid("缺少路径字段或工作线程入口");
            if (plan.Entry.Body.Instructions.Any(i => Calls(i, "SelectFile") || Calls(i, "ShowDialog"))) throw Invalid("本地入口包含其他选择逻辑，无法确定恢复方式");
        }
        if (plan.DispatcherBlocked)
        {
            IList<Instruction> body = plan.Dispatcher.Body.Instructions;
            var guards = body.Where(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == Restriction).ToArray();
            if (guards.Length != 1) throw Invalid("安装分流包含多个限制位置");
            plan.Guard = guards[0]; int guard = body.IndexOf(plan.Guard);
            if (guard + 2 >= body.Count || body[guard + 1].OpCode != OpCodes.Newobj || body[guard + 2].OpCode != OpCodes.Throw) throw Invalid("安装分流的限制指令发生变化");
            var error = body[guard + 1].Operand as IMethod;
            if (error == null || error.DeclaringType.FullName != "System.Exception" || error.MethodSig.Params.Count != 1) throw Invalid("限制异常类型不匹配");
            plan.Archive = plan.Dispatcher.Body.Variables.SingleOrDefault(v => v.Type.FullName == "System.IO.Compression.ZipArchive");
            var types = new List<Local>(); var roots = new List<Local>();
            for (int n = 0; n + 1 < guard; n++)
            {
                if (!body[n + 1].IsStloc()) continue;
                if (body[n].IsLdcI4() && body[n].GetLdcI4Value() == -1) types.Add(body[n + 1].GetLocal(plan.Dispatcher.Body.Variables));
                if (body[n].OpCode == OpCodes.Ldstr && (string)body[n].Operand == "") roots.Add(body[n + 1].GetLocal(plan.Dispatcher.Body.Variables));
            }
            if (types.Count != 1 || roots.Count != 1 || plan.Archive == null) throw Invalid("无法识别包类型、归档或包裹目录变量");
            plan.PackType = types[0]; plan.BaseFolder = roots[0];
            plan.Finally = plan.Dispatcher.Body.ExceptionHandlers.SingleOrDefault(h => h.HandlerType == ExceptionHandlerType.Finally && h.TryStart.Offset <= plan.Guard.Offset && h.TryEnd != null && h.TryEnd.Offset > plan.Guard.Offset);
            if (plan.Finally == null || plan.Finally.HandlerStart != plan.Finally.TryEnd) throw Invalid("安装方法的归档释放结构不匹配");
            for (int n = 0; n < Factories.Length; n++)
            {
                var factory = mod.Methods.SingleOrDefault(m => m.Name == Factories[n] && m.IsStatic && m.MethodSig.Params.Count == Arity[n] && m.ReturnType.FullName == plan.Dispatcher.ReturnType.FullName);
                if (factory == null) throw Invalid("缺少原有安装实现 " + Factories[n]);
            }
        }
        var pages = module.GetTypes().Where(t => t.Name == "PageDownloadResourceDetail" || t.Name == "PageDownloadCompDetail").ToArray();
        if (pages.Length == 1)
        {
            TypeDef page = pages[0];
            plan.List = page.Methods.SingleOrDefault(m => m.Name == "InitializeStackResourceVersions" && m.HasBody);
            if (plan.List != null)
            {
                var adapters = page.Methods.Where(m => !m.IsStatic && m.HasBody && m.Name != "Install_Click" && m.Body.Instructions.Any(i => Calls(i, "Install_Click"))).ToArray();
                bool wired = plan.List.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldftn && ((IMethod)i.Operand).ResolveMethodDef() != null &&
                    (((IMethod)i.Operand).Name == "Install_Click" || adapters.Contains(((IMethod)i.Operand).ResolveMethodDef())));
                var saves = plan.List.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldftn && ((IMethod)i.Operand).Name == "Save_Click").ToArray();
                if (!wired && saves.Length == 1)
                {
                    plan.ListBlocked = true; plan.SavePointer = saves[0];
                    if (adapters.Length != 1) throw Invalid("在线列表缺少唯一的安装点击适配器");
                    plan.InstallAdapter = adapters[0];
                    plan.TargetType = page.Fields.SingleOrDefault(f => f.Name == "TargetResourceType");
                    if (plan.TargetType == null) throw Invalid("在线列表缺少资源类型字段");
                    IList<Instruction> list = plan.List.Body.Instructions;
                    int at = list.IndexOf(plan.SavePointer);
                    if (at < 1 || list[at - 1].OpCode != OpCodes.Ldarg_0 || list[at + 1].OpCode != OpCodes.Newobj || list[at + 2].OpCode != OpCodes.Ldnull) throw Invalid("在线列表回调结构发生变化");
                    bool foundType = false;
                    for (int n = 0; n + 3 < list.Count; n++)
                    {
                        if (list[n].OpCode != OpCodes.Ldfld || ((IField)list[n].Operand).Name != plan.TargetType.Name || !list[n + 1].IsLdcI4()) continue;
                        IMethod next = list[n + 3].Operand as IMethod;
                        if (list[n + 2].OpCode.FlowControl != FlowControl.Cond_Branch || list[n + 3].OpCode != OpCodes.Newobj || next == null || !next.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1<System.String>")) continue;
                        plan.ModpackTypeValue = list[n + 1].GetLdcI4Value(); foundType = true; break;
                    }
                    if (!foundType) throw Invalid("无法识别在线整合包资源类型");
                    TypeDef icon = module.GetTypes().SingleOrDefault(t => t.Name == "MyIconButton");
                    if (icon == null || !icon.NestedTypes.Any(t => t.Name == "ClickEventHandler")) throw Invalid("缺少另存为按钮回调类型");
                }
            }
        }
        return plan;
    }

    static void RestoreEntry(ModuleDefMD module, Plan plan)
    {
        Importer importer = new Importer(module);
        CilBody body = new CilBody { InitLocals = true };
        Local dialog = new Local(importer.ImportAsTypeSig(typeof(OpenFileDialog)));
        Local closure = new Local(plan.ClosureConstructor.DeclaringType.ToTypeSig());
        body.Variables.Add(dialog); body.Variables.Add(closure);
        var il = body.Instructions;
        il.Add(Instruction.Create(OpCodes.Newobj, importer.Import(typeof(OpenFileDialog).GetConstructor(Type.EmptyTypes))));
        il.Add(Instruction.Create(OpCodes.Stloc, dialog));
        Instruction begin = Instruction.Create(OpCodes.Ldloc, dialog); il.Add(begin);
        il.Add(Instruction.Create(OpCodes.Ldstr, "选择整合包压缩文件"));
        il.Add(Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(FileDialog).GetProperty("Title").GetSetMethod())));
        il.Add(Instruction.Create(OpCodes.Ldloc, dialog));
        il.Add(Instruction.Create(OpCodes.Ldstr, "整合包文件 (*.zip;*.mrpack;*.rar)|*.zip;*.mrpack;*.rar|所有文件 (*.*)|*.*"));
        il.Add(Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(FileDialog).GetProperty("Filter").GetSetMethod())));
        il.Add(Instruction.Create(OpCodes.Ldloc, dialog));
        il.Add(Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes))));
        il.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        Instruction canceled = Instruction.Create(OpCodes.Leave, Instruction.Create(OpCodes.Ret));
        il.Add(Instruction.Create(OpCodes.Bne_Un, canceled));
        il.Add(Instruction.Create(OpCodes.Newobj, plan.ClosureConstructor)); il.Add(Instruction.Create(OpCodes.Stloc, closure));
        il.Add(Instruction.Create(OpCodes.Ldloc, closure)); il.Add(Instruction.Create(OpCodes.Ldloc, dialog));
        il.Add(Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(FileDialog).GetProperty("FileName").GetGetMethod())));
        il.Add(Instruction.Create(OpCodes.Stfld, plan.FileField));
        il.Add(Instruction.Create(OpCodes.Ldloc, closure)); il.Add(Instruction.Create(OpCodes.Ldftn, plan.Callback));
        il.Add(Instruction.Create(OpCodes.Newobj, importer.Import(typeof(Action).GetConstructor(new[] { typeof(object), typeof(IntPtr) }))));
        il.Add(Instruction.Create(OpCodes.Call, plan.ThreadRunner));
        il.Add(canceled);
        Instruction finallyStart = Instruction.Create(OpCodes.Ldloc, dialog); il.Add(finallyStart);
        Instruction endFinally = Instruction.Create(OpCodes.Endfinally);
        il.Add(Instruction.Create(OpCodes.Brfalse, endFinally)); il.Add(Instruction.Create(OpCodes.Ldloc, dialog));
        il.Add(Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(IDisposable).GetMethod("Dispose"))));
        il.Add(endFinally);
        Instruction end = (Instruction)canceled.Operand; il.Add(end);
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) { TryStart = begin, TryEnd = finallyStart, HandlerStart = finallyStart, HandlerEnd = end });
        plan.Entry.Body = body;
    }
    static void RestoreDispatcher(Plan plan)
    {
        CilBody body = plan.Dispatcher.Body; var il = body.Instructions;
        int guard = il.IndexOf(plan.Guard);
        for (int n = guard; n < guard + 3; n++) { il[n].OpCode = OpCodes.Nop; il[n].Operand = null; }
        Local returned = new Local(plan.Dispatcher.ReturnType); body.Variables.Add(returned); body.InitLocals = true;
        Instruction exit = Instruction.Create(OpCodes.Ldloc, returned);
        Instruction oldHandlerStart = plan.Finally.HandlerStart;
        int insert = il.IndexOf(oldHandlerStart);
        var added = new List<Instruction>();
        Instruction[] cases = Factories.Select(x => Instruction.Create(OpCodes.Nop)).ToArray();
        int[] values = { 0, 1, 2, 3, 4, 9 };
        for (int n = 0; n < values.Length; n++)
        {
            added.Add(Instruction.Create(OpCodes.Ldloc, plan.PackType)); added.Add(Instruction.CreateLdcI4(values[n]));
            added.Add(Instruction.Create(OpCodes.Beq, cases[n]));
        }
        added.Add(Instruction.Create(OpCodes.Br, cases[6]));
        for (int n = 0; n < Factories.Length; n++)
        {
            MethodDef factory = plan.Dispatcher.DeclaringType.Methods.Single(m => m.Name == Factories[n] && m.MethodSig.Params.Count == Arity[n]);
            added.Add(cases[n]); added.Add(Instruction.Create(OpCodes.Ldarg_0)); added.Add(Instruction.Create(OpCodes.Ldloc, plan.Archive));
            if (Arity[n] >= 3) added.Add(Instruction.Create(OpCodes.Ldloc, plan.BaseFolder));
            if (Arity[n] >= 4) added.Add(Instruction.Create(OpCodes.Ldarg_1));
            if (Arity[n] >= 5) added.Add(Instruction.Create(OpCodes.Ldarg_2));
            added.Add(Instruction.Create(OpCodes.Call, factory)); added.Add(Instruction.Create(OpCodes.Stloc, returned)); added.Add(Instruction.Create(OpCodes.Leave, exit));
        }
        foreach (Instruction item in added) il.Insert(insert++, item);
        // The new dispatcher remains inside the original try; the original finally still disposes Archive.
        plan.Finally.HandlerEnd = exit; il.Add(exit); il.Add(Instruction.Create(OpCodes.Ret));
    }
    static void RestoreOnline(Plan plan)
    {
        var il = plan.List.Body.Instructions;
        int at = il.IndexOf(plan.SavePointer);
        Instruction first = il[at - 1];
        IMethod save = (IMethod)plan.SavePointer.Operand;
        IMethod listDelegate = (IMethod)il[at + 1].Operand;
        TypeDef icon = plan.List.Module.GetTypes().Single(t => t.Name == "MyIconButton");
        MethodDef buttonDelegate = icon.NestedTypes.Single(t => t.Name == "ClickEventHandler").Methods.Single(m => m.IsInstanceConstructor);
        Instruction normal = Instruction.Create(OpCodes.Ldarg_0);
        Instruction join = il[at + 3];
        var added = new List<Instruction> {
            Instruction.Create(OpCodes.Ldfld, plan.TargetType), Instruction.CreateLdcI4(plan.ModpackTypeValue), Instruction.Create(OpCodes.Bne_Un, normal),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldftn, plan.InstallAdapter), Instruction.Create(OpCodes.Newobj, listDelegate),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldftn, save), Instruction.Create(OpCodes.Newobj, buttonDelegate), Instruction.Create(OpCodes.Br, join),
            normal, Instruction.Create(OpCodes.Ldftn, save), Instruction.Create(OpCodes.Newobj, listDelegate), Instruction.Create(OpCodes.Ldnull)
        };
        // Keep the original first instruction object, so existing branches and exception boundaries stay valid.
        for (int n = at; n <= at + 2; n++) { il[n].OpCode = OpCodes.Nop; il[n].Operand = null; }
        int insert = il.IndexOf(first) + 1;
        foreach (Instruction item in added) il.Insert(insert++, item);
    }
    internal static byte[] Repair(byte[] bytes)
    {
        using (ModuleDefMD module = ModuleDefMD.Load(bytes))
        {
            Plan plan = Inspect(module);
            if (plan.Count == 0) return bytes;
            if (!module.IsILOnly) throw Invalid("混合模式程序暂不支持整合包方法重建");
            if (module.IsStrongNameSigned) throw Invalid("程序具有强名称签名，无法保证方法重建后正常加载");
            long overlayStart = module.Metadata.PEImage.ImageSectionHeaders.Max(s => (long)s.PointerToRawData + s.SizeOfRawData);
            if (overlayStart < 0 || overlayStart > bytes.Length) throw Invalid("程序节区边界异常");
            if (plan.EntryBlocked) RestoreEntry(module, plan);
            if (plan.DispatcherBlocked) RestoreDispatcher(plan);
            if (plan.ListBlocked) RestoreOnline(plan);
            foreach (MethodDef method in new[] { plan.Entry, plan.Dispatcher, plan.List }.Where(m => m != null && m.HasBody))
            {
                method.Body.KeepOldMaxStack = false; method.Body.SimplifyBranches(); method.Body.OptimizeBranches();
            }
            using (MemoryStream output = new MemoryStream())
            {
                ModuleWriterOptions options = new ModuleWriterOptions(module);
                options.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
                module.Write(output, options);
                // Preserve data outside PE sections, including custom build overlays.
                if (overlayStart < bytes.Length) output.Write(bytes, (int)overlayStart, bytes.Length - (int)overlayStart);
                return output.ToArray();
            }
        }
    }
}
