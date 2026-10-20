using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
class MakeAdaptiveFixtures
{
 static void Main(string[] args)
 {
  Directory.CreateDirectory(args[1]);
  using (var m=ModuleDefMD.Load(args[0]))
  {
   m.Assembly.Version=new Version(9,8,7,6);
   m.Assembly.Name="PCL Custom Build";
   m.GlobalType.Fields.Add(new FieldDefUser("FixturePadding", new FieldSig(m.CorLibTypes.Int32), FieldAttributes.Public|FieldAttributes.Static));
   var init=m.Find("PCL.PageDownloadInstall",false).Methods.Single(x=>x.Name=="LoaderInit");
   for(int n=0;n<7;n++)init.Body.Instructions.Insert(0,Instruction.Create(OpCodes.Nop));
   var modify=m.Find("PCL.PageDownloadModify",false).Methods.Single(x=>x.Name=="LoadInstanceFinished");
   var mi=modify.Body.Instructions;
   var card=mi.Single(i=>i.OpCode==OpCodes.Callvirt && ((IMethod)i.Operand).Name=="get_CardMinecraft");
   var constant=mi[mi.IndexOf(card)+1]; constant.OpCode=OpCodes.Ldc_I4;constant.Operand=2;
   m.Write(Path.Combine(args[1],"shifted-layout.exe"));
  }
  using (var m=ModuleDefMD.Load(args[0]))
  {
   var init=m.Find("PCL.PageDownloadInstall",false).Methods.Single(x=>x.Name=="LoaderInit");
   var marker=init.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="关闭下载");
   marker.Operand="不同的限制实现";
   m.Write(Path.Combine(args[1],"unknown-pattern.exe"));
  }
  using (var m=ModuleDefMD.Load(args[0]))
  {
   var init=m.Find("PCL.PageDownloadInstall",false).Methods.Single(x=>x.Name=="LoaderInit");
   var body=init.Body.Instructions;
   var marker=body.Single(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="关闭下载");
   body.Insert(0,Instruction.Create(OpCodes.Br,body[body.IndexOf(marker)+1]));
   init.Body.KeepOldMaxStack=true;
   m.Write(Path.Combine(args[1],"external-branch.exe"));
  }
  using (var m=ModuleDefMD.Load(args[2]))
  {
   m.Assembly.Version=new Version(2,14,0,0);
   m.Write(Path.Combine(args[1],"already-normal.exe"));
  }
  byte[] original=File.ReadAllBytes(args[0]);
  byte[] appended=new byte[original.Length+4096];Array.Copy(original,appended,original.Length);
  for(int n=original.Length;n<appended.Length;n++)appended[n]=(byte)(n%251);
  File.WriteAllBytes(Path.Combine(args[1],"different-size-and-hash.exe"),appended);
  original[0x30]^=1;
  File.WriteAllBytes(Path.Combine(args[1],"different-hash.exe"),original);
 }
}
