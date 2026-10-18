using System;

namespace ClioReport.Vsix
{
    internal static class PackageGuids
    {
        public const string Package = "37f51c2f-2d7f-4cce-9002-f0f183125a24";
        public const string CmdSet = "4ba6350c-86e8-4c3e-b993-4b1c62a2e097";
        public const string ToolWindow = "9185b529-c99d-4d35-a8dc-783b3265a59d";
        public static readonly Guid CmdSetGuid = new Guid(CmdSet);
    }

    internal static class PackageIds
    {
        public const int OpenDesignerCmd = 0x0100;
        public const int OpenFileCmd = 0x0101;
    }
}
