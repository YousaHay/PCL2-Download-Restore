using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
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
   foreach(var pair in new[]{"PCL.PageDownloadInstall|LoaderInit","PCL.PageDownloadModify|LoadInstanceFinished"}) {
    var p=pair.Split('|'); var method=app.GetType(p[0],true).GetMethod(p[1],BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    RuntimeHelpers.PrepareMethod(method.MethodHandle);
    Console.WriteLine("JIT OK: "+method.DeclaringType.FullName+"."+method.Name);
   }
   return 0;
  } catch(Exception ex){Console.WriteLine(ex);return 1;}
 }
}
