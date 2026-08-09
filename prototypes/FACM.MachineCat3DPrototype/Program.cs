using System.Windows;

namespace FACM.MachineCat3DPrototype;

internal static class Program
{
    private const string ExpectedModelName = "664230004_doraemon_model.glb";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            return MachineCat3DSelfTest.Run();

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        if (args.Any(arg => arg.Equals("--window-smoke-test", StringComparison.OrdinalIgnoreCase)))
        {
            var smoke = new MachineCat3DWindow(MachineCat3DSelfTest.CreateRigFixture(), "CI fixture", smokeTest: true);
            app.Run(smoke);
            return 0;
        }

        try
        {
            var modelPath = ResolveModelPath(args);
            if (modelPath is null)
            {
                MessageBox.Show(
                    $"没有找到 3D 模型。\n\n把 {ExpectedModelName} 放到本 EXE 同目录，或者直接把 GLB 文件拖到 EXE 上运行。\n\n模型文件不会包含在 FACM 的公开测试包中。",
                    "FACM Machine Cat 3D Gate 1",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return 62;
            }

            var model = GlbRigidModelLoader.Load(modelPath);
            MachineCatModelRequirements.Validate(model);
            var window = new MachineCat3DWindow(model, Path.GetFileName(modelPath));
            app.Run(window);
            return 0;
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "machine-cat-3d-error.txt"), exception.ToString()); }
            catch { }
            MessageBox.Show(
                $"3D 模型加载失败：\n{exception.Message}\n\n详细信息已尝试写入 machine-cat-3d-error.txt。",
                "FACM Machine Cat 3D Gate 1",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 63;
        }
    }

    private static string? ResolveModelPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--model", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && File.Exists(args[i + 1]))
                return Path.GetFullPath(args[i + 1]);
            if (args[i].EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && File.Exists(args[i]))
                return Path.GetFullPath(args[i]);
        }

        var expected = Path.Combine(AppContext.BaseDirectory, ExpectedModelName);
        if (File.Exists(expected)) return expected;
        return Directory.EnumerateFiles(AppContext.BaseDirectory, "*.glb", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }
}
