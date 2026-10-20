using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization;
using System.Windows.Controls;
using System.Threading;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

class ModpackRuntimeTests
{
    static int passed;
    static Assembly app;
    static readonly Dictionary<string, byte[]> libraries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static void Assert(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    static void Load(string path)
    {
        app = Assembly.Load(File.ReadAllBytes(path));
        foreach (string name in app.GetManifestResourceNames().Where(n => n.EndsWith(".resources")))
            using (ResourceReader reader = new ResourceReader(app.GetManifestResourceStream(name)))
                foreach (DictionaryEntry entry in reader) if (entry.Value is byte[]) libraries[(string)entry.Key] = (byte[])entry.Value;
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            byte[] bytes; return libraries.TryGetValue(new AssemblyName(e.Name).Name, out bytes) ? Assembly.Load(bytes) : null;
        };
    }
    static void ArchiveCase(string folder, string name, string entry, string json, string expected, bool nested)
    {
        string path = Path.Combine(folder, name + (nested ? "-nested.zip" : ".zip"));
        using (FileStream file = File.Create(path))
        using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create))
        using (StreamWriter writer = new StreamWriter(archive.CreateEntry((nested ? "wrapped/" : "") + entry).Open())) writer.Write(json);
        MethodInfo dispatcher = app.GetType("PCL.ModModpack", true).GetMethods(All).Single(m => m.Name == "ModpackInstall" && m.GetParameters().Length == 3);
        Exception error = null;
        try { dispatcher.Invoke(null, new object[] { path, "ProbeInstance", "ProbeLogo" }); }
        catch (TargetInvocationException ex) { error = ex.InnerException; }
        if (error != null) Console.WriteLine("  Result: " + error.Message);
        if (error != null && !error.ToString().Contains(expected)) Console.WriteLine(error);
        Assert(error != null && error.ToString().Contains(expected) && !error.Message.Contains("暂时不再提供"), "Real pack route " + name + (nested ? " wrapped" : " root"));
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Assert(true, "Archive disposed after installer returns or throws");
    }
    static void OnlineCase(int resourceType, bool install)
    {
        Type pageType = app.GetType("PCL.PageDownloadResourceDetail", true);
        object page = FormatterServices.GetUninitializedObject(pageType);
        FieldInfo target = pageType.GetField("TargetResourceType", All);
        target.SetValue(page, Enum.ToObject(target.FieldType, resourceType));
        Type versionType = app.GetType("PCL.ResourceVersion", true);
        object version = Activator.CreateInstance(versionType);
        versionType.GetField("Display", All).SetValue(version, "Offline probe");
        versionType.GetField("ReleaseDate", All).SetValue(version, DateTime.UtcNow);
        versionType.GetField("FileName", All).SetValue(version, "offline-probe.zip");
        versionType.GetField("GameVersions", All).SetValue(version, new List<string> { "26.2" });
        IList versions = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(versionType)); versions.Add(version);
        StackPanel stack = new StackPanel { Tag = versions };
        pageType.GetMethod("InitializeStackResourceVersions", All).Invoke(page, new object[] { stack });
        object item = stack.Children.Cast<object>().First(c => c.GetType().Name.StartsWith("MyVirtualizingElement"));
        Delegate factory = (Delegate)item.GetType().GetField("Initializer", All).GetValue(item);
        var handlers = factory.Target.GetType().GetFields(All).Select(f => f.GetValue(factory.Target)).OfType<Delegate>().ToArray();
        Assert(handlers.Length == (install ? 2 : 1), install ? "Online pack has install and save callbacks" : "Ordinary resource retains save-only callback");
        Assert(handlers.Any(d => d.Method.Name == "Save_Click"), "Save As callback preserved");
        Assert(handlers.Any(d => d.Method.Name != "Save_Click") == install, "Install callback limited to modpacks");
        Assert(stack.Tag == null, "Resource list completion preserved");
    }
    static void ExtractionCase(string folder)
    {
        string archivePath=Path.Combine(folder,"valid-client.zip");
        string destination=Path.GetFullPath(Path.Combine(folder,"extraction-target"));
        if(!destination.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Extraction destination escaped test folder");
        using(FileStream file=File.Create(archivePath))using(ZipArchive archive=new ZipArchive(file,ZipArchiveMode.Create)) {
            using(StreamWriter writer=new StreamWriter(archive.CreateEntry("versions/probe/probe.json").Open()))writer.Write("{\"id\":\"probe\"}");
            using(StreamWriter writer=new StreamWriter(archive.CreateEntry("mods/offline-probe.txt").Open()))writer.Write("offline fixture");
        }
        app.GetType("PCL.ModModpack",true).GetMethod("ExtractModpackFiles",All).Invoke(null,new object[]{destination,archivePath,CancellationToken.None,null});
        Assert(File.ReadAllText(Path.Combine(destination,"versions/probe/probe.json"))=="{\"id\":\"probe\"}","Real extraction writes version JSON");
        Assert(File.ReadAllText(Path.Combine(destination,"mods/offline-probe.txt"))=="offline fixture","Real extraction preserves mod payload");
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Load(args[0]); string folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
            app.GetType("PCL.ModMinecraft", true).GetField("_McFolderSelected", All).SetValue(null, folder + Path.DirectorySeparatorChar);
            if(args.Length>2 && args[2].StartsWith("picker-"))
            {
                app.GetType("PCL.ModModpack",true).GetMethods(All).Single(m=>m.Name=="ModpackInstall"&&m.GetParameters().Length==0).Invoke(null,null);
                Delegate captured=(Delegate)app.GetType("Tests.ModpackHooks",true).GetField("Captured",All).GetValue(null);
                bool expected=args[2]=="picker-ok";
                Assert((captured!=null)==expected,expected?"Chosen file queues original install callback":"Canceled file dialog queues no install");
                if(expected)Assert(captured.Target.GetType().GetFields(All).Any(f=>f.FieldType==typeof(string)&&(string)f.GetValue(captured.Target)==args[3]),"Selected path reaches original closure unchanged");
            }
            else if (args.Length > 2 && args[2] == "launcher-probe")
            {
                ArchiveCase(folder, "launcher", "modpack.zip", "stub", "ROUTE:InstallPackLauncherPack", false);
                ArchiveCase(folder, "launcher", "modpack.mrpack", "stub", "ROUTE:InstallPackLauncherPack", true);
            }
            else
            {
                foreach (bool nested in new[] { false, true })
                {
                    ArchiveCase(folder, "curseforge", "manifest.json", "{}", "CurseForge 整合包未提供 Minecraft 版本信息", nested);
                    ArchiveCase(folder, "modrinth", "modrinth.index.json", "{}", "Modrinth 整合包未提供 Minecraft 版本信息", nested);
                    ArchiveCase(folder, "hmcl", "modpack.json", "[", "HMCL 整合包安装信息存在问题", nested);
                    ArchiveCase(folder, "mmc", "mmc-pack.json", "{}", "MMC 整合包安装信息存在问题", nested);
                    ArchiveCase(folder, "mcbbs", "mcbbs.packmeta", "[", "MCBBS 整合包安装信息存在问题", nested);
                    ArchiveCase(folder, "compress", "readme.txt", "not a game", "文件结构不匹配", nested);
                }
                OnlineCase(2, true); OnlineCase(1, false); OnlineCase(4, false); ExtractionCase(folder);
            }
            Console.WriteLine("TOTAL " + passed + " passed"); return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
}
