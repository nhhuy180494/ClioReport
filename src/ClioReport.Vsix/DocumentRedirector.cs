using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace ClioReport.Vsix
{
    /// <summary>
    /// Double-clicking a .clio file makes Visual Studio open it in the text editor. This listens for that, closes the text
    /// tab and opens the file in the designer instead. Works in Solution view and Folder View alike.
    /// </summary>
    internal sealed class DocumentRedirector : IVsRunningDocTableEvents, IDisposable
    {
        private readonly IVsRunningDocumentTable _rdt;
        private readonly JoinableTaskFactory _jtf;
        private readonly Action<string> _openInDesigner;
        private uint _cookie;

        public DocumentRedirector(IVsRunningDocumentTable rdt, JoinableTaskFactory jtf, Action<string> openInDesigner)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _rdt = rdt;
            _jtf = jtf;
            _openInDesigner = openInDesigner;
            _rdt.AdviseRunningDocTableEvents(this, out _cookie);
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie != 0) { _rdt.UnadviseRunningDocTableEvents(_cookie); _cookie = 0; }
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (fFirstShow == 0 || pFrame == null) return 0;

            IntPtr docData = IntPtr.Zero;
            string path;
            try
            {
                _rdt.GetDocumentInfo(docCookie, out uint flags, out uint readLocks, out uint editLocks,
                    out path, out IVsHierarchy hierarchy, out uint itemId, out docData);
            }
            finally
            {
                if (docData != IntPtr.Zero) Marshal.Release(docData);
            }
            if (path == null || !path.EndsWith(".clio", StringComparison.OrdinalIgnoreCase)) return 0;

            // Closing the frame from inside this callback is unsafe: do it right after it returns.
            _ = _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();
                await System.Threading.Tasks.Task.Yield();
                pFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                _openInDesigner(path);
            });
            return 0;
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) { return 0; }
        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) { return 0; }
        public int OnAfterSave(uint docCookie) { return 0; }
        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) { return 0; }
        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) { return 0; }
    }
}
