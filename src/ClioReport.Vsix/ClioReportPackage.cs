using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace ClioReport.Vsix
{
    /// <summary>
    /// Tools &gt; ClioReport Designer opens an empty designer; double-clicking (or right-clicking) a .clio file in Solution Explorer
    /// opens it in the designer. Each file gets its own designer tab.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("ClioReport Designer", "Visual designer for ClioReport report files.", "0.1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(DesignerToolWindow), Style = VsDockStyle.MDI, MultiInstances = true, Transient = true)]
    [Guid(PackageGuids.Package)]
    public sealed class ClioReportPackage : AsyncPackage
    {
        private readonly Dictionary<string, int> _windowByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _nextWindowId = 1;

        private DocumentRedirector _redirector;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var commands = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commands == null) return;

            commands.AddCommand(new MenuCommand(
                (s, e) => Open(null),
                new CommandID(PackageGuids.CmdSetGuid, PackageIds.OpenDesignerCmd)));

            var openFile = new OleMenuCommand(
                (s, e) => Open(SelectedReportPath()),
                new CommandID(PackageGuids.CmdSetGuid, PackageIds.OpenFileCmd));
            openFile.BeforeQueryStatus += (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ((OleMenuCommand)s).Visible = SelectedReportPath() != null;
            };
            commands.AddCommand(openFile);

            var rdt = await GetServiceAsync(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt != null) _redirector = new DocumentRedirector(rdt, JoinableTaskFactory, Open);
        }

        private void Open(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int id;
            if (path == null) id = _nextWindowId++;
            else if (!_windowByPath.TryGetValue(path, out id)) _windowByPath[path] = id = _nextWindowId++;

            var window = FindToolWindow(typeof(DesignerToolWindow), id, true) as DesignerToolWindow;
            if (window == null || window.Frame == null)
                throw new NotSupportedException("Cannot create the ClioReport Designer window.");
            ((IVsWindowFrame)window.Frame).Show();
            if (path != null) window.OpenFile(path);
        }

        private static string SelectedReportPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = SelectedPathFromHierarchy() ?? SelectedPathFromDte();
            return path != null && path.EndsWith(".clio", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
        }

        // Works for Solution Explorer in both Solution view and Folder View (Open Folder), where DTE has no ProjectItem.
        private static string SelectedPathFromHierarchy()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var monitor = GetGlobalService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (monitor == null) return null;
            IntPtr hierarchyPtr = IntPtr.Zero, containerPtr = IntPtr.Zero;
            try
            {
                if (monitor.GetCurrentSelection(out hierarchyPtr, out uint itemId, out IVsMultiItemSelect multi, out containerPtr) != 0
                    || hierarchyPtr == IntPtr.Zero || multi != null || itemId == (uint)Microsoft.VisualStudio.VSConstants.VSITEMID.Selection) return null;
                var hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;
                if (hierarchy == null) return null;
                return hierarchy.GetCanonicalName(itemId, out string name) == 0 ? name : null;
            }
            finally
            {
                if (hierarchyPtr != IntPtr.Zero) Marshal.Release(hierarchyPtr);
                if (containerPtr != IntPtr.Zero) Marshal.Release(containerPtr);
            }
        }

        private static string SelectedPathFromDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = GetGlobalService(typeof(DTE)) as DTE2;
            if (dte == null || dte.SelectedItems == null || dte.SelectedItems.Count != 1) return null;
            var item = dte.SelectedItems.Item(1).ProjectItem;
            return item != null && item.FileCount >= 1 ? item.FileNames[1] : null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _redirector?.Dispose();
                HostProcess.Stop();
            }
            base.Dispose(disposing);
        }
    }
}
