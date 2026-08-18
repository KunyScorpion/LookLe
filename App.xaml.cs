using System.Text;
using System.Windows;

namespace LeeyesViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Register CodePages encoding provider for Shift-JIS / CP932 / EUC-JP support
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Run self-verification tests
        Tests.VerificationTests.RunAllTests();

        base.OnStartup(e);
    }
}
