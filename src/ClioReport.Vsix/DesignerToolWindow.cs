using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ClioReport.Vsix
{
    /// <summary>One designer per window (MultiInstances), shown in the document well like an editor tab.</summary>
    [Guid(PackageGuids.ToolWindow)]
    public sealed class DesignerToolWindow : ToolWindowPane
    {
        private readonly DesignerControl _control = new DesignerControl();

        public DesignerToolWindow() : base(null)
        {
            Caption = "ClioReport Designer";
            Content = _control;
            _control.TitleChanged += title => Caption = title;
        }

        public void OpenFile(string path) => _control.OpenFile(path);

        protected override void Dispose(bool disposing)
        {
            if (disposing) _control.Dispose();
            base.Dispose(disposing);
        }
    }
}
