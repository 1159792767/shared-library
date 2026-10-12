namespace Company.UpdateChecker.Setup;

internal static class Program
{
    /// <summary>WinForms 向导入口。net10 SDK 自动生成 ApplicationConfiguration.Initialize()。</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new WizardForm());
    }
}
