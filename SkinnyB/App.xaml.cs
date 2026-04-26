namespace SkinnyB
{
    public partial class App : Application
    {
        public App(AppShell shell)
        {
            InitializeComponent();
            try
            {
                MainPage = shell;
            }
            catch (Exception ex)
            {
                var inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                System.Diagnostics.Debug.WriteLine($"[App] STARTUP CRASH: {inner.GetType().Name}: {inner.Message}");
                System.Diagnostics.Debug.WriteLine(inner.StackTrace);
                throw;
            }
        }
    }
}