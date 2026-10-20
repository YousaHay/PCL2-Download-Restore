using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Linq;
class VerifyRuntime {
 static Assembly app;
 static Dictionary<string,byte[]> libs=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
 [STAThread]
 static int Main(string[] args) {
  try {
   app=Assembly.Load(File.ReadAllBytes(args[0]));
   foreach(var name in app.GetManifestResourceNames()) {
    if(!name.EndsWith(".resources"))continue;
    using(var r=new ResourceReader(app.GetManifestResourceStream(name))) foreach(DictionaryEntry e in r) {
     var bytes=e.Value as byte[]; if(bytes!=null)libs[(string)e.Key]=bytes;
    }
   }
   AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){
    string key=new AssemblyName(e.Name).Name;
    byte[] bytes; return libs.TryGetValue(key,out bytes)?Assembly.Load(bytes):null;
   };
   foreach(var pair in new[]{"PCL.PageDownloadInstall|LoaderInit","PCL.PageDownloadModify|LoadInstanceFinished","PCL.ModModpack|ModpackInstall","PCL.PageDownloadResourceDetail|InitializeStackResourceVersions"}) {
    var p=pair.Split('|');
    foreach(var method in app.GetType(p[0],true).GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>m.Name==p[1])) {
     RuntimeHelpers.PrepareMethod(method.MethodHandle);
     Console.WriteLine("JIT OK: "+method.DeclaringType.FullName+"."+method.Name+"/"+method.GetParameters().Length);
    }
   }
   return 0;
  } catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
}
