using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

[assembly: AssemblyTitle("PCL 原版下载恢复工具（第三方）")]
[assembly: AssemblyDescription("自动定位 PCL 原版下载限制，不按版本或整文件指纹筛选")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

class MainWindow : Form
{
    readonly TextBox input = new TextBox();
    readonly Label status = new Label();
    readonly Button apply = new Button();
    readonly Button browse = new Button();
    public MainWindow(string initial)
    {
        Text = "PCL 原版下载恢复工具 v1.3（第三方）";
        Font = new Font("Microsoft YaHei UI", 10F);
        ClientSize = new Size(660, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        Label heading = new Label { Text = "恢复游戏下载与整合包安装", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Location = new Point(26, 24) };
        Label explanation = new Label { Text = "选择现有的 PCL2.exe，生成可单独运行的副本。\n原文件会保留，游戏和配置不会被修改。", Location = new Point(28, 77), Size = new Size(604, 52) };
        input.SetBounds(28, 146, 490, 30);
        input.ReadOnly = true;
        browse.Text = "选择文件…";
        browse.SetBounds(530, 144, 103, 34);
        browse.Click += delegate { Choose(); };
        status.SetBounds(28, 195, 604, 70);
        status.Text = "自动分析代码结构，不按固定版本或指纹筛选。\n恢复原版下载、版本修改，以及本地、拖入和在线整合包安装。";
        apply.Text = "生成恢复下载的副本…";
        apply.SetBounds(28, 278, 280, 42);
        apply.Enabled = false;
        apply.BackColor = Color.FromArgb(226, 239, 255);
        apply.Click += delegate { Apply(); };
        Label foot = new Label { Text = "工具作者：YousaHay · PCL 原作者：龙腾猫跃 · 无需联网", Location = new Point(28, 337), Size = new Size(604, 24), ForeColor = Color.DimGray, Font = new Font(Font.FontFamily, 9F) };
        Controls.AddRange(new Control[] { heading, explanation, input, browse, status, apply, foot });
        if (!String.IsNullOrWhiteSpace(initial)) Shown += delegate { SelectInput(initial); };
    }
    void Choose()
    {
        using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择需要恢复原版下载的 PCL2 启动器", Filter = "PCL 启动器 (*.exe)|*.exe", CheckFileExists = true })
            if (dialog.ShowDialog(this) == DialogResult.OK) SelectInput(dialog.FileName);
    }
    void SelectInput(string path)
    {
        apply.Enabled = false;
        input.Text = path;
        try
        {
            AnalysisResult analysis = Patcher.Analyze(path);
            bool supported = analysis.ChangeCount > 0;
            status.ForeColor = supported ? Color.DarkGreen : Color.DarkBlue;
            status.Text = analysis.Detail;
            apply.Enabled = supported;
        }
        catch (Exception ex) { status.ForeColor = Color.Firebrick; status.Text = ex.Message; }
    }
    void Apply()
    {
        using (SaveFileDialog dialog = new SaveFileDialog { Title = "选择副本的保存位置（不会覆盖原文件）", Filter = "PCL 启动器 (*.exe)|*.exe", InitialDirectory = Path.GetDirectoryName(input.Text), FileName = Path.GetFileNameWithoutExtension(input.Text) + ".第三方原版下载恢复.exe", AddExtension = true, DefaultExt = "exe" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            apply.Enabled = false;
            browse.Enabled = false;
            try
            {
                Patcher.Patch(input.Text, dialog.FileName);
                status.ForeColor = Color.DarkGreen;
                status.Text = "已生成并校验成功。请关闭原启动器，再运行新副本。\n如需恢复，运行原启动器即可。";
                MessageBox.Show(this, "已生成：\n" + dialog.FileName + "\n\n请关闭原启动器后运行此副本。原文件已保留。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "未生成副本", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { apply.Enabled = true; browse.Enabled = true; }
        }
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            LibraryBootstrap.Install();
            if (args.Length == 3 && args[0] == "--patch") { Patcher.Patch(args[1], args[2]); return 0; }
            if (args.Length == 2 && args[0] == "--check") return Patcher.Check(args[1]) ? 0 : 2;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(args.Length == 1 ? args[0] : null));
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 0) MessageBox.Show(ex.Message, "PCL 原版下载恢复工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
