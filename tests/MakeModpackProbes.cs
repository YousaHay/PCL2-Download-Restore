using System;
using System.Linq;
using System.Windows.Forms;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

// Test-only images replace interactive decisions or one downstream route with controlled probes.
// These images are never distributed as repaired launchers.
class MakeModpackProbes
{
    static void Main(string[] args)
    {
        using (ModuleDefMD module = ModuleDefMD.Load(args[0]))
        {
            Importer importer = new Importer(module);
            TypeDef mod = module.Find("PCL.ModModpack", false);
            if (args[2] == "launcher")
            {
                MethodDef factory = mod.Methods.Single(m => m.Name == "InstallPackLauncherPack");
                factory.Body = new CilBody();
                factory.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "ROUTE:InstallPackLauncherPack"));
                factory.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, importer.Import(typeof(Exception).GetConstructor(new[] { typeof(string) }))));
                factory.Body.Instructions.Add(Instruction.Create(OpCodes.Throw));
            }
            else
            {
                TypeDef hooks = new TypeDefUser("Tests", "ModpackHooks", module.CorLibTypes.Object.TypeDefOrRef) { Attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed };
                module.Types.Add(hooks);
                FieldDef captured = new FieldDefUser("Captured", new FieldSig(importer.ImportAsTypeSig(typeof(Action))), FieldAttributes.Public | FieldAttributes.Static);
                hooks.Fields.Add(captured);
                MethodDef choose = new MethodDefUser("Choose", MethodSig.CreateStatic(importer.ImportAsTypeSig(typeof(DialogResult)), importer.ImportAsTypeSig(typeof(CommonDialog))), MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
                hooks.Methods.Add(choose);
                choose.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                choose.Body.Instructions.Add(Instruction.Create(OpCodes.Castclass, importer.Import(typeof(FileDialog))));
                choose.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, args[3]));
                choose.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, importer.Import(typeof(FileDialog).GetProperty("FileName").GetSetMethod())));
                choose.Body.Instructions.Add(Instruction.CreateLdcI4(args[2] == "ok" ? 1 : 2));
                choose.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                MethodDef queue = new MethodDefUser("Queue", MethodSig.CreateStatic(module.CorLibTypes.Void, importer.ImportAsTypeSig(typeof(Action))), MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
                hooks.Methods.Add(queue);
                queue.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0)); queue.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, captured)); queue.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                MethodDef entry = mod.Methods.Single(m => m.Name == "ModpackInstall" && m.MethodSig.Params.Count == 0);
                foreach (Instruction instruction in entry.Body.Instructions)
                {
                    IMethod method = instruction.Operand as IMethod;
                    if (method == null) continue;
                    if (method.Name == "ShowDialog") { instruction.OpCode = OpCodes.Call; instruction.Operand = choose; }
                    if (method.Name == "RunInThread") { instruction.OpCode = OpCodes.Call; instruction.Operand = queue; }
                }
            }
            ModuleWriterOptions options = new ModuleWriterOptions(module); options.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
            module.Write(args[1], options);
        }
    }
}
