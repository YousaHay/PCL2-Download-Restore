using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Generic;

public sealed class ResourceSnapshot : MarshalByRefObject {
 public Dictionary<string,byte[]> Read(byte[] bytes) {
  var assembly=Assembly.ReflectionOnlyLoad(bytes);var resources=new Dictionary<string,byte[]>();
  foreach(string name in assembly.GetManifestResourceNames())using(var stream=assembly.GetManifestResourceStream(name))using(var buffer=new MemoryStream()) {
   stream.CopyTo(buffer);resources[name]=buffer.ToArray();
  }
  return resources;
 }
}
class AdaptiveTests
{
 static int count;
 static void Assert(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,name);}
 static Dictionary<string,byte[]> Resources(byte[] bytes) {
  AppDomain domain=AppDomain.CreateDomain("ResourceInspection-"+Guid.NewGuid().ToString("N"));
  try{return ((ResourceSnapshot)domain.CreateInstanceAndUnwrap(typeof(ResourceSnapshot).Assembly.FullName,typeof(ResourceSnapshot).FullName)).Read(bytes);}
  finally{AppDomain.Unload(domain);}
 }
 static void MainCase(string path,string folder)
 {
  var source=File.ReadAllBytes(path);var before=Patcher.Hash(source);
  Assert(Patcher.Check(path),"Recognizes "+Path.GetFileName(path));
  var analysis=Patcher.Analyze(path);Assert(analysis.ModpackChangeCount==3,"Locates all pack sites "+Path.GetFileName(path));
  string target=Path.Combine(folder,Path.GetFileName(path)+".恢复.exe");
  Patcher.Patch(path,target);
  var output=File.ReadAllBytes(target);
  Assert(output.Length>0,"Creates complete rebuilt executable");
  Assert(Patcher.Hash(File.ReadAllBytes(path))==before,"Preserves original");
  Assert(!Patcher.Check(target),"Recognizes patched result");
  var oldResources=Resources(source);var newResources=Resources(output);
  Assert(oldResources.Keys.OrderBy(n=>n).SequenceEqual(newResources.Keys.OrderBy(n=>n)),"Preserves embedded resource names");
  foreach(string resource in oldResources.Keys) {
   Assert(oldResources[resource].SequenceEqual(newResources[resource]),"Preserves resource payload "+resource);
  }
  Reject(()=>Patcher.Patch(target,target+".duplicate.exe"),"Rejects duplicate patch");
  Assert(!File.Exists(target+".duplicate.exe"),"Duplicate creates no output");
  Reject(()=>Patcher.Patch(path,path),"Rejects source overwrite");
  Reject(()=>Patcher.Patch(path,target),"Rejects existing destination");
  Assert(!Patcher.Check(target),"Destination remains intact");
 }
 [STAThread]
 static int Main(string[] args)
 {
  try
  {
   string fixtures=args[0],folder=args[1];Directory.CreateDirectory(folder);
   Assert(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dnlib.dll")),"Works with embedded dependency only");
   MainCase(args[2],folder);
   // Different file names prevent collision with the first real sample output.
   string user=Path.Combine(fixtures,"user-current.exe");File.Copy(args[3],user,true);
   MainCase(user,folder);
   foreach(string name in new[]{"shifted-layout.exe","different-size-and-hash.exe","different-hash.exe"})MainCase(Path.Combine(fixtures,name),folder);
   var appended=File.ReadAllBytes(Path.Combine(fixtures,"different-size-and-hash.exe"));
   var appendedOutput=File.ReadAllBytes(Path.Combine(folder,"different-size-and-hash.exe.恢复.exe"));
   Assert(appended.Skip(appended.Length-4096).SequenceEqual(appendedOutput.Skip(appendedOutput.Length-4096)),"Preserves custom overlay bytes");
   var shifted=Patcher.Analyze(Path.Combine(fixtures,"shifted-layout.exe"));
   Assert(shifted.Version=="9.8.7.6","Does not restrict assembly version");
   Assert(!Patcher.Check(Path.Combine(fixtures,"already-normal.exe")),"Accepts already normal different-version fixture");
   foreach(string name in new[]{"unknown-pattern.exe","external-branch.exe"})
   {
    var path=Path.Combine(fixtures,name);Reject(()=>Patcher.Check(path),"Rejects ambiguous structure "+name);
    var target=Path.Combine(folder,name+".bad.exe");Reject(()=>Patcher.Patch(path,target),"Does not patch ambiguous structure");
    Assert(!File.Exists(target),"Ambiguous structure creates no output");
   }
   var invalid=Path.Combine(fixtures,"not-dotnet.exe");File.WriteAllBytes(invalid,new byte[100]);
   Reject(()=>Patcher.Check(invalid),"Rejects non .NET file");
   Reject(()=>Patcher.Check(Path.Combine(fixtures,"missing.exe")),"Handles missing file");
   Application.EnableVisualStyles();
   var tool=typeof(Patcher).Assembly;
   using(var form=(Form)Activator.CreateInstance(tool.GetType("MainWindow"),new object[]{null}))
   {
    Assert(form.Text.Contains("v1.3"),"GUI displays new version");
    Assert(form.Controls.OfType<Button>().Any(b=>!b.Enabled),"Patch disabled before selection");
    var select=tool.GetType("MainWindow").GetMethod("SelectInput",BindingFlags.Instance|BindingFlags.NonPublic);
    select.Invoke(form,new object[]{Path.Combine(fixtures,"shifted-layout.exe")});
    Assert(form.Controls.OfType<Button>().All(b=>b.Enabled),"Different layout enables patch button");
    select.Invoke(form,new object[]{Path.Combine(fixtures,"already-normal.exe")});
    Assert(form.Controls.OfType<Button>().Any(b=>!b.Enabled),"Already normal requires no patch");
    select.Invoke(form,new object[]{invalid});
    Assert(form.Controls.OfType<Button>().Any(b=>!b.Enabled),"Invalid file disables patch button");
   }
   Console.WriteLine("TOTAL "+count+" passed");return 0;
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
}
