using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using TcPluginBase;
using TcPluginBase.Content;
using TcPluginBase.FileSystem;
using TcPluginBase.Tools;


namespace WfxWrapper {
    public class FsWrapper {
        static FsWrapper()
        {
            AppDomain.CurrentDomain.AssemblyResolve += new RelativeAssemblyResolver(typeof(FsWrapper).Assembly.Location).AssemblyResolve;
        }


        private static string _callSignature;
        private static FsPlugin _plugin;
        private static FsPlugin Plugin => _plugin ?? (_plugin = TcPluginLoader.GetTcPlugin<FsPlugin>(typeof(PluginClassPlaceholder)));
        private static ContentPlugin ContentPlugin => Plugin.ContentPlugin;


        #region File System Plugin Exported Functions

        //Order of TC calls to FS Plugin methods (before first call to FsFindFirst(W)):
        // - FsGetDefRootName (Is called once, when user installs the plugin in Total Commander)
        // - FsContentGetSupportedField - can be called before FsInit if custom columns set is determined
        //                                and plugin panel is visible
        // - FsInit
        // - FsInitW
        // - FsSetDefaultParams
        // - FsSetCryptCallbackW
        // - FsSetCryptCallback
        // - FsExecuteFile(W) (with verb = "MODE I")
        // - FsContentGetDefaultView(W) - can be called here if custom column set is not determined
        //                                and plugin panel is visible
        // - first call to file list cycle:
        //     FsFindFirst - FsFindNext - FsFindClose
        // - FsLinksToLocalFiles

        #region Mandatory Methods

        #region FsInit

        // FsInit, FsInitW functionality is implemented here, not included to FS Plugin interface.
        [DllExport(EntryPoint = "FsInit")]
        public static int Init(int pluginNumber, ProgressCallback progressProc, LogCallback logProc, RequestCallback requestProc)
        {
            try {
                _callSignature = "FsInit";
                Plugin.PluginNumber = pluginNumber;
                TcCallback.SetFsPluginCallbacks(progressProc, null, logProc, null, requestProc, null, null, null);

                TraceCall(TraceLevel.Warning, $"PluginNumber={pluginNumber}, {progressProc.Method.MethodHandle.GetFunctionPointer().ToString("X")}, {logProc.Method.MethodHandle.GetFunctionPointer().ToString("X")}, {requestProc.Method.MethodHandle.GetFunctionPointer().ToString("X")}");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return 0;
        }

        [DllExport(EntryPoint = "FsInitW")]
        public static int InitW(int pluginNumber, ProgressCallbackW progressProcW, LogCallbackW logProcW, RequestCallbackW requestProcW)
        {
            try {
                _callSignature = "FsInitW";
                Plugin.PluginNumber = pluginNumber;
                TcCallback.SetFsPluginCallbacks(null, progressProcW, null, logProcW, null, requestProcW, null, null);

                TraceCall(TraceLevel.Warning, $"PluginNumber={pluginNumber}, {progressProcW.Method.MethodHandle.GetFunctionPointer().ToString("X")}, {logProcW.Method.MethodHandle.GetFunctionPointer().ToString("X")}, {requestProcW.Method.MethodHandle.GetFunctionPointer().ToString("X")}");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return 0;
        }

        #endregion FsInit


        #region FsFindFirst

        [DllExport(EntryPoint = "FsFindFirst")]
        public static IntPtr FindFirst([MarshalAs(UnmanagedType.LPStr)] string path, IntPtr findFileData)
        {
            return FindFirstInternal(path, findFileData, false);
        }

        [DllExport(EntryPoint = "FsFindFirstW")]
        public static IntPtr FindFirstW([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr findFileData)
        {
            return FindFirstInternal(path, findFileData, true);
        }

        public static IntPtr FindFirstInternal(string path, IntPtr findFileData, bool isUnicode)
        {
            var result = NativeMethods.INVALID_HANDLE;
            _callSignature = $"FindFirst ({path})";
            try {
                var o = Plugin.FindFirst(path, out var findData);
                if (o == null)
                    TraceCall(TraceLevel.Info, "<None>");
                else {
                    findData.CopyTo(findFileData, isUnicode);
                    result = TcHandles.AddHandle(o);
                    TraceCall(TraceLevel.Info, findData.FileName);
                }
            }
            catch (NoMoreFilesException) {
                TraceCall(TraceLevel.Info, "<Nothing>");
                NativeMethods.SetLastError(NativeMethods.ERROR_NO_MORE_FILES);
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsFindFirst


        #region FsFindNext

        [DllExport(EntryPoint = "FsFindNext")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool FindNext(IntPtr hdl, IntPtr findFileData)
        {
            return FindNextInternal(hdl, findFileData, false);
        }

        [DllExport(EntryPoint = "FsFindNextW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool FindNextW(IntPtr hdl, IntPtr findFileData)
        {
            return FindNextInternal(hdl, findFileData, true);
        }

        public static bool FindNextInternal(IntPtr hdl, IntPtr findFileData, bool isUnicode)
        {
            var result = false;
            _callSignature = "FindNext";
            try {
                FindData findData = null;
                var o = TcHandles.GetObject(hdl);
                if (o != null) {
                    result = Plugin.FindNext(ref o, out findData);
                    if (result) {
                        findData.CopyTo(findFileData, isUnicode);
                        TcHandles.UpdateHandle(hdl, o);
                    }
                }

                // !!! may produce much trace info !!!
                TraceCall(TraceLevel.Verbose, result ? findData.FileName : "<None>");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsFindNext


        #region FsFindClose

        [DllExport(EntryPoint = "FsFindClose")]
        public static int FindClose(IntPtr hdl)
        {
            _callSignature = "FindClose";
            try {
                var count = 0;

                var o = TcHandles.GetObject(hdl);
                if (o != null) {
                    Plugin.FindClose(o);
                    (o as IDisposable)?.Dispose();
                    count = TcHandles.RemoveHandle(hdl);
                }

                TraceCall(TraceLevel.Info, $"{count} item(s)");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return 0;
        }

        #endregion FsFindClose

        #endregion Mandatory Methods


        #region Optional Methods

        #region FsSetCryptCallback

        // FsSetCryptCallback & FsSetCryptCallbackW functionality is implemented here, not included to FS Plugin interface.
        [DllExport(EntryPoint = "FsSetCryptCallback")]
        public static void SetCryptCallback(FsCryptCallback cryptProc, int cryptNumber, int flags)
        {
            _callSignature = "SetCryptCallback";
            try {
                TcCallback.SetFsPluginCallbacks(null, null, null, null, null, null, cryptProc, null);
                if (Plugin.Password == null) {
                    Plugin.Password = new FsPassword(Plugin, cryptNumber, flags);
                }

                TraceCall(TraceLevel.Warning, $"CryptoNumber={cryptNumber}, Flags={flags}, {cryptProc.Method.MethodHandle.GetFunctionPointer().ToString("X")}");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }
        }

        [DllExport(EntryPoint = "FsSetCryptCallbackW")]
        public static void SetCryptCallbackW(FsCryptCallbackW cryptProcW, int cryptNumber, int flags)
        {
            _callSignature = "SetCryptCallbackW";
            try {
                TcCallback.SetFsPluginCallbacks(null, null, null, null, null, null, null, cryptProcW);
                if (Plugin.Password == null) {
                    Plugin.Password = new FsPassword(Plugin, cryptNumber, flags);
                }

                TraceCall(TraceLevel.Warning, $"CryptoNumber={cryptNumber}, Flags={flags}, {cryptProcW.Method.MethodHandle.GetFunctionPointer().ToString("X")}");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }
        }

        #endregion FsSetCryptCallback


        #region FsGetDefRootName

        // FsGetDefRootName functionality is implemented here, not included to FS Plugin interface.
        [DllExport(EntryPoint = "FsGetDefRootName")]
        public static void GetDefRootName(IntPtr rootName, int maxLen)
        {
            _callSignature = "GetDefRootName";
            try {
                TcUtils.WriteStringAnsi(Plugin.Title, rootName, maxLen);

                TraceCall(TraceLevel.Warning, Plugin.Title);
            }
            catch (Exception ex) {
                TcUtils.WriteStringAnsi(ex.Message, rootName, maxLen);
                ProcessException(ex);
            }
        }

        #endregion FsGetDefRootName


        #region FsGetFile

        [DllExport(EntryPoint = "FsGetFile"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int GetFile([MarshalAs(UnmanagedType.LPStr)] string remoteName, IntPtr localName, int copyFlags, IntPtr remoteInfo)
        {
            var locName = Marshal.PtrToStringAnsi(localName);
            var inLocName = locName;
            var result = GetFileInternal(remoteName, ref locName, (CopyFlags) copyFlags, remoteInfo);
            if (result == FileSystemExitCode.OK && !locName.Equals(inLocName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringAnsi(locName, localName, 0);
            }

            return (int) result;
        }

        [DllExport(EntryPoint = "FsGetFileW"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int GetFileW([MarshalAs(UnmanagedType.LPWStr)] string remoteName, IntPtr localName, int copyFlags, IntPtr remoteInfo)
        {
            var locName = Marshal.PtrToStringUni(localName);
            var inLocName = locName;
            var result = GetFileInternal(remoteName, ref locName, (CopyFlags) copyFlags, remoteInfo);
            if (result == FileSystemExitCode.OK && !locName.Equals(inLocName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringUni(locName, localName, 0);
            }

            return (int) result;
        }

        private static FileSystemExitCode GetFileInternal(string remoteName, ref string localName, CopyFlags copyFlags, IntPtr rmtInfo)
        {
            FileSystemExitCode result;
            _callSignature = $"GetFile '{remoteName}' => '{localName}' ({copyFlags.ToString()})";
            var remoteInfo = new RemoteInfo(rmtInfo);
            try {
                result = Plugin.GetFile(remoteName, localName, copyFlags, remoteInfo);

                TraceCall(TraceLevel.Info, result.ToString());
            }
            catch (Exception ex) {
                ProcessException(ex);
                result = FileSystemExitCode.ReadError;
            }

            return result;
        }

        #endregion FsGetFile


        #region FsPutFile

        [DllExport(EntryPoint = "FsPutFile"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int PutFile([MarshalAs(UnmanagedType.LPStr)] string localName, IntPtr remoteName, int copyFlags)
        {
            var rmtName = Marshal.PtrToStringAnsi(remoteName);
            var inRmtName = rmtName;
            var result = PutFileInternal(localName, ref rmtName, (CopyFlags) copyFlags);
            if (result == FileSystemExitCode.OK && !rmtName.Equals(inRmtName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringAnsi(rmtName, remoteName, 0);
            }

            return (int) result;
        }

        [DllExport(EntryPoint = "FsPutFileW"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int PutFileW([MarshalAs(UnmanagedType.LPWStr)] string localName, IntPtr remoteName, int copyFlags)
        {
            var rmtName = Marshal.PtrToStringUni(remoteName);
            var inRmtName = rmtName;
            var result = PutFileInternal(localName, ref rmtName, (CopyFlags) copyFlags);
            if (result == FileSystemExitCode.OK && !rmtName.Equals(inRmtName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringUni(rmtName, remoteName, 0);
            }

            return (int) result;
        }

        private static FileSystemExitCode PutFileInternal(string localName, ref string remoteName, CopyFlags copyFlags)
        {
            FileSystemExitCode result;
            _callSignature = $"PutFile '{localName}' => '{remoteName}' ({copyFlags.ToString()})";
            try {
                result = Plugin.PutFile(localName, remoteName, copyFlags);

                TraceCall(TraceLevel.Info, result.ToString());
            }
            catch (Exception ex) {
                ProcessException(ex);
                result = FileSystemExitCode.ReadError;
            }

            return result;
        }

        #endregion FsPutFile


        #region FsRenMovFile

        [DllExport(EntryPoint = "FsRenMovFile")]
        public static int RenMovFile([MarshalAs(UnmanagedType.LPStr)] string oldName, [MarshalAs(UnmanagedType.LPStr)] string newName, [MarshalAs(UnmanagedType.Bool)] bool move, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr remoteInfo)
        {
            return RenMovFileW(oldName, newName, move, overwrite, remoteInfo);
        }

        [DllExport(EntryPoint = "FsRenMovFileW")]
        public static int RenMovFileW([MarshalAs(UnmanagedType.LPWStr)] string oldName, [MarshalAs(UnmanagedType.LPWStr)] string newName, [MarshalAs(UnmanagedType.Bool)] bool move, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr rmtInfo)
        {
            var result = FileSystemExitCode.NotSupported;
            if (oldName == null || newName == null) {
                return (int) result;
            }

            _callSignature = $"RenMovFile '{oldName}' => '{newName}' ({(move ? "M" : " ") + (overwrite ? "O" : " ")})";
            var remoteInfo = new RemoteInfo(rmtInfo);
            try {
                // TODO IgnoreCase ?? noo!
                result = newName.Equals(oldName, StringComparison.CurrentCultureIgnoreCase)
                    ? FileSystemExitCode.OK
                    : Plugin.RenMovFile(oldName, newName, move, overwrite, remoteInfo);

                TraceCall(TraceLevel.Warning, result.ToString());
            }
            catch (Exception ex) {
                ProcessException(ex);
                result = FileSystemExitCode.ReadError;
            }

            return (int) result;
        }

        #endregion FsRenMovFile


        #region FsDeleteFile

        [DllExport(EntryPoint = "FsDeleteFile")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool DeleteFile([MarshalAs(UnmanagedType.LPStr)] string fileName)
        {
            return DeleteFileW(fileName);
        }

        [DllExport(EntryPoint = "FsDeleteFileW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool DeleteFileW([MarshalAs(UnmanagedType.LPWStr)] string fileName)
        {
            var result = false;
            _callSignature = $"DeleteFile '{fileName}'";
            try {
                result = Plugin.DeleteFile(fileName);

                TraceCall(TraceLevel.Warning, result ? "OK" : "No");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsDeleteFile


        #region FsRemoveDir

        [DllExport(EntryPoint = "FsRemoveDir")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool RemoveDir([MarshalAs(UnmanagedType.LPStr)] string dirName)
        {
            return RemoveDirW(dirName);
        }

        [DllExport(EntryPoint = "FsRemoveDirW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool RemoveDirW([MarshalAs(UnmanagedType.LPWStr)] string dirName)
        {
            var result = false;
            _callSignature = $"RemoveDir '{dirName}'";
            try {
                result = Plugin.RemoveDir(dirName);

                TraceCall(TraceLevel.Warning, result ? "OK" : "No");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsRemoveDir


        #region FsMkDir

        [DllExport(EntryPoint = "FsMkDir")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool MkDir([MarshalAs(UnmanagedType.LPStr)] string dirName)
        {
            return MkDirW(dirName);
        }

        [DllExport(EntryPoint = "FsMkDirW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool MkDirW([MarshalAs(UnmanagedType.LPWStr)] string dirName)
        {
            var result = false;
            _callSignature = $"MkDir '{dirName}'";
            try {
                result = Plugin.MkDir(dirName);
                TraceCall(TraceLevel.Warning, result ? "OK" : "No");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsMkDir


        #region FsExecuteFile

        [DllExport(EntryPoint = "FsExecuteFile")]
        public static int ExecuteFile(IntPtr mainWin, IntPtr remoteName, [MarshalAs(UnmanagedType.LPStr)] string verb)
        {
            var rmtName = Marshal.PtrToStringAnsi(remoteName);
            var result = ExecuteFileInternal(mainWin, rmtName, verb);

            if (result.Type == ExecResult.ExecEnum.SymLink && result.SymlinkTarget.HasValue) {
                TcUtils.WriteStringAnsi(result.SymlinkTarget, remoteName, 0);
            }

            return (int) result.Type;
        }

        [DllExport(EntryPoint = "FsExecuteFileW")]
        public static int ExecuteFileW(IntPtr mainWin, IntPtr remoteName, [MarshalAs(UnmanagedType.LPWStr)] string verb)
        {
            var rmtName = Marshal.PtrToStringUni(remoteName);
            var result = ExecuteFileInternal(mainWin, rmtName, verb);

            if (result.Type == ExecResult.ExecEnum.SymLink && result.SymlinkTarget.HasValue) {
                TcUtils.WriteStringUni(result.SymlinkTarget, remoteName, 0);
            }

            return (int) result.Type;
        }

        private static ExecResult ExecuteFileInternal(IntPtr mainWin, RemotePath remoteName, string verb)
        {
            _callSignature = $"ExecuteFile '{remoteName}' - {verb}";
            try {
                var result = Plugin.ExecuteFile(new TcWindow(mainWin), remoteName, verb);

                var resStr = result.Type.ToString();
                if (result.Type == ExecResult.ExecEnum.SymLink && result.SymlinkTarget.HasValue) {
                    resStr += " (" + result.SymlinkTarget.Path + ")";
                }

                TraceCall(TraceLevel.Warning, resStr);
                return result;
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return ExecResult.Ok;
        }

        #endregion FsExecuteFile


        #region FsSetAttr

        [DllExport(EntryPoint = "FsSetAttr")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool SetAttr([MarshalAs(UnmanagedType.LPStr)] string remoteName, int newAttr)
        {
            return SetAttrW(remoteName, newAttr);
        }

        [DllExport(EntryPoint = "FsSetAttrW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool SetAttrW([MarshalAs(UnmanagedType.LPWStr)] string remoteName, int newAttr)
        {
            var result = false;
            var attr = (FileAttributes) newAttr;
            _callSignature = $"SetAttr '{remoteName}' ({attr.ToString()})";
            try {
                result = Plugin.SetAttr(remoteName, attr);

                TraceCall(TraceLevel.Info, result ? "OK" : "No");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsSetAttr


        #region FsSetTime

        [DllExport(EntryPoint = "FsSetTime"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool SetTime([MarshalAs(UnmanagedType.LPStr)] string remoteName, IntPtr creationTime, IntPtr lastAccessTime, IntPtr lastWriteTime)
        {
            return SetTimeW(remoteName, creationTime, lastAccessTime, lastWriteTime);
        }

        [DllExport(EntryPoint = "FsSetTimeW"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool SetTimeW([MarshalAs(UnmanagedType.LPWStr)] string remoteName, IntPtr creationTime, IntPtr lastAccessTime, IntPtr lastWriteTime)
        {
            var crTime = TcUtils.ReadDateTime(creationTime);
            var laTime = TcUtils.ReadDateTime(lastAccessTime);
            var lwTime = TcUtils.ReadDateTime(lastWriteTime);

            _callSignature = $"SetTime '{remoteName}' (" +
                             (crTime.HasValue ? $" {crTime.Value:g} #" : " NULL #") +
                             (laTime.HasValue ? $" {laTime.Value:g} #" : " NULL #") +
                             (lwTime.HasValue ? $" {lwTime.Value:g} #" : " NULL #") +
                             ")";

            var result = false;
            try {
                result = Plugin.SetTime(remoteName, crTime, laTime, lwTime);

                TraceCall(TraceLevel.Info, result ? "OK" : "No");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsSetTime


        #region FsDisconnect

        [DllExport(EntryPoint = "FsDisconnect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool Disconnect([MarshalAs(UnmanagedType.LPStr)] string disconnectRoot)
        {
            return DisconnectW(disconnectRoot);
        }

        [DllExport(EntryPoint = "FsDisconnectW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool DisconnectW([MarshalAs(UnmanagedType.LPWStr)] string disconnectRoot)
        {
            var result = false;
            _callSignature = $"Disconnect '{disconnectRoot}'";
            try {
                result = Plugin.Disconnect(disconnectRoot);
                // TODO: add - unload plugin AppDomain after successful disconnect (configurable)

                TraceCall(TraceLevel.Warning, result ? "OK" : "No");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsDisconnect


        #region FsStatusInfo

        [DllExport(EntryPoint = "FsStatusInfo")]
        public static void StatusInfo([MarshalAs(UnmanagedType.LPStr)] string remoteDir, int startEnd, int operation)
        {
            StatusInfoW(remoteDir, startEnd, operation);
        }

        [DllExport(EntryPoint = "FsStatusInfoW")]
        public static void StatusInfoW([MarshalAs(UnmanagedType.LPWStr)] string remoteDir, int startEnd, int operation)
        {
            try {
#if TRACE
                _callSignature = $"{((InfoOperation) operation).ToString()} - '{remoteDir}': {((InfoStartEnd) startEnd).ToString()}";
                if (Plugin.WriteStatusInfo) {
                    TcTrace.TraceOut(TraceLevel.Warning, _callSignature, Plugin.TraceTitle, startEnd == (int) InfoStartEnd.End ? -1 : startEnd == (int) InfoStartEnd.Start ? 1 : 0);
                }
#endif
                Plugin.StatusInfo(remoteDir, (InfoStartEnd) startEnd, (InfoOperation) operation);
            }
            catch (Exception ex) {
                ProcessException(ex);
            }
        }

        #endregion FsStatusInfo

        #region FsExtractCustomIcon

        [DllExport(EntryPoint = "FsExtractCustomIcon")]
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int ExtractCustomIcon(IntPtr remoteName, int extractFlags, IntPtr theIcon)
        {
            var rmtName = Marshal.PtrToStringAnsi(remoteName);
            var inRmtName = rmtName;
            var result = ExtractIconInternal(ref rmtName, extractFlags, theIcon);
            if (result != ExtractIconResult.ExtractIconEnum.UseDefault && !rmtName.Equals(inRmtName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringAnsi(rmtName, remoteName, 0);
            }

            return (int) result;
        }

        [DllExport(EntryPoint = "FsExtractCustomIconW")]
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int ExtractCustomIconW(IntPtr remoteName, int extractFlags, IntPtr theIcon)
        {
            var rmtName = Marshal.PtrToStringUni(remoteName);
            var inRmtName = rmtName;
            var result = ExtractIconInternal(ref rmtName, extractFlags, theIcon);
            if (result != ExtractIconResult.ExtractIconEnum.UseDefault && !rmtName.Equals(inRmtName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringUni(rmtName, remoteName, 0);
            }

            return (int) result;
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        internal static ExtractIconResult.ExtractIconEnum ExtractIconInternal(ref string remoteName, int extractFlags, IntPtr theIcon)
        {
            var flags = (ExtractIconFlags) extractFlags;
            _callSignature = $"ExtractCustomIcon '{remoteName}' ({flags.ToString()})";

            var ret = ExtractIconResult.ExtractIconEnum.UseDefault;
            try {
                var result = Plugin.ExtractCustomIcon(remoteName, flags);
                var resultStr = result.ToString();

                if (result.IconName != null) {
                    remoteName = result.IconName;
                }

                if (result.Icon != null) {
                    Marshal.WriteIntPtr(theIcon, result.Icon.Handle);
                    ret = result.Value;
                }

                // !!! may produce much trace info !!!
                TraceCall(TraceLevel.Verbose, resultStr);
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return ret;
        }

        #endregion FsExtractCustomIcon


        #region FsSetDefaultParams

        // FsSetDefaultParams functionality is implemented here, not included to FS Plugin interface.
        [DllExport(EntryPoint = "FsSetDefaultParams")]
        public static void SetDefaultParams(ref PluginDefaultParams defParams)
        {
            _callSignature = "SetDefaultParams";
            try {
                Plugin.DefaultParams = defParams;

                TraceCall(TraceLevel.Info, null);
            }
            catch (Exception ex) {
                ProcessException(ex);
            }
        }

        #endregion FsSetDefaultParams


        #region FsGetPreviewBitmap

        [DllExport(EntryPoint = "FsGetPreviewBitmap"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int GetPreviewBitmap(IntPtr remoteName, int width, int height, IntPtr returnedBitmap)
        {
            var rmtName = Marshal.PtrToStringAnsi(remoteName);
            var inRmtName = rmtName;
            var result = GetPreviewBitmapInternal(ref rmtName, width, height, returnedBitmap);
            if (result != PreviewBitmapResult.PreviewBitmapEnum.None && !string.IsNullOrEmpty(rmtName) && !rmtName.Equals(inRmtName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringAnsi(rmtName, remoteName, 0);
            }

            return (int) result;
        }

        [DllExport(EntryPoint = "FsGetPreviewBitmapW"), SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static int GetPreviewBitmapW(IntPtr remoteName, int width, int height, IntPtr returnedBitmap)
        {
            var rmtName = Marshal.PtrToStringUni(remoteName);
            var inRmtName = rmtName;
            var result = GetPreviewBitmapInternal(ref rmtName, width, height, returnedBitmap);
            if (result != PreviewBitmapResult.PreviewBitmapEnum.None && !string.IsNullOrEmpty(rmtName) && !rmtName.Equals(inRmtName, StringComparison.CurrentCultureIgnoreCase)) {
                TcUtils.WriteStringUni(rmtName, remoteName, 0);
            }

            return (int) result;
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        internal static PreviewBitmapResult.PreviewBitmapEnum GetPreviewBitmapInternal(ref string remoteName, int width, int height, IntPtr returnedBitmap)
        {
            _callSignature = $"GetPreviewBitmap '{remoteName}' ({width} x {height})";

            var ret = PreviewBitmapResult.PreviewBitmapEnum.None;
            try {
                var result = Plugin.GetPreviewBitmap(remoteName, width, height);

                if (result.Bitmap != null) {
                    var extrBitmap = result.Bitmap.GetHbitmap();
                    Marshal.WriteIntPtr(returnedBitmap, extrBitmap);
                }

                if (result.BitmapName != null) {
                    remoteName = result.BitmapName;
                }

                ret = result.Value;
                if (result.Cache) {
                    ret |= PreviewBitmapResult.PreviewBitmapEnum.Cache;
                }

                // !!! may produce much trace info !!!
                TraceCall(TraceLevel.Verbose, $"{ret} ({result.BitmapName})");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return ret;
        }

        #endregion FsGetPreviewBitmap


        #region FsLinksToLocalFiles

        // FsLinksToLocalFiles functionality is implemented here, not included to FS Plugin interface.
        [DllExport(EntryPoint = "FsLinksToLocalFiles")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool LinksToLocalFiles()
        {
            var result = false;
            _callSignature = "LinksToLocalFiles";
            try {
                result = Plugin.IsTempFilePanel;

                TraceCall(TraceLevel.Info, result ? "Yes" : "No");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsLinksToLocalFiles


        #region FsGetLocalName

        [DllExport(EntryPoint = "FsGetLocalName")]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static bool GetLocalName(IntPtr remoteName, int maxLen)
        {
            var rmtName = Marshal.PtrToStringAnsi(remoteName);
            var result = GetLocalNameInternal(ref rmtName, maxLen);
            if (result) {
                TcUtils.WriteStringAnsi(rmtName, remoteName, 0);
            }

            return result;
        }

        [DllExport(EntryPoint = "FsGetLocalNameW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static bool GetLocalNameW(IntPtr remoteName, int maxLen)
        {
            var rmtName = Marshal.PtrToStringUni(remoteName);
            var result = GetLocalNameInternal(ref rmtName, maxLen);
            if (result) {
                TcUtils.WriteStringUni(rmtName, remoteName, 0);
            }

            return result;
        }

        public static bool GetLocalNameInternal(ref string remoteName, int maxLen)
        {
            var result = false;
            _callSignature = $"GetLocalName '{remoteName}'";
            try {
                var localName = Plugin.GetLocalName(remoteName, maxLen);
                if (localName != null) {
                    remoteName = localName;
                    result = true;
                }

                // !!! may produce much trace info !!!
                TraceCall(TraceLevel.Verbose, result ? remoteName : "<N/A>");
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsGetLocalName


        // FsGetBackgroundFlags functionality is implemented here, not included to FS Plugin interface.
        [DllExport(EntryPoint = "FsGetBackgroundFlags")]
        public static int GetBackgroundFlags()
        {
            var result = FsBackgroundFlags.None;
            _callSignature = "GetBackgroundFlags";
            try {
                result = Plugin.BackgroundFlags;

                TraceCall(TraceLevel.Info, result.ToString());
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return (int) result;
        }

        #endregion Optional Methods

        #endregion File System Plugin Exported Functions


        #region Content Plugin Exported Functions

        #region FsContentGetSupportedField

        [DllExport(EntryPoint = "FsContentGetSupportedField")]
        public static int GetSupportedField(int fieldIndex, IntPtr fieldName, IntPtr units, int maxLen)
        {
            var result = ContentFieldType.NoMoreFields;
            _callSignature = $"ContentGetSupportedField ({fieldIndex})";
            try {
                if (ContentPlugin != null) {
                    result = ContentPlugin.GetSupportedField(fieldIndex, out var fieldNameStr, out var unitsStr, maxLen);
                    if (result != ContentFieldType.NoMoreFields) {
                        if (string.IsNullOrEmpty(fieldNameStr))
                            result = ContentFieldType.NoMoreFields;
                        else {
                            TcUtils.WriteStringAnsi(fieldNameStr, fieldName, maxLen);
                            if (string.IsNullOrEmpty(unitsStr))
                                units = IntPtr.Zero;
                            else
                                TcUtils.WriteStringAnsi(unitsStr, units, maxLen);
                        }
                    }

                    // !!! may produce much trace info !!!
                    TraceCall(TraceLevel.Verbose, $"{result.ToString()} - {fieldNameStr} - {unitsStr}");
                }
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return (int) result;
        }

        #endregion FsContentGetSupportedField

        #region FsContentGetValue

        [DllExport(EntryPoint = "FsContentGetValue")]
        public static int GetValue([MarshalAs(UnmanagedType.LPStr)] string fileName, int fieldIndex, int unitIndex, IntPtr fieldValue, int maxLen, int flags)
        {
            return GetValueW(fileName, fieldIndex, unitIndex, fieldValue, maxLen, flags);
        }

        [DllExport(EntryPoint = "FsContentGetValueW")]
        public static int GetValueW([MarshalAs(UnmanagedType.LPWStr)] string fileName, int fieldIndex, int unitIndex, IntPtr fieldValue, int maxLen, int flags)
        {
            GetValueResult result;
            var fieldType = ContentFieldType.NoMoreFields;
            var gvFlags = (GetValueFlags) flags;
            fileName = fileName.Substring(1);
            _callSignature = $"ContentGetValue '{fileName}' ({fieldIndex}/{unitIndex}/{gvFlags.ToString()})";
            try {
                result = ContentPlugin.GetValue(fileName, fieldIndex, unitIndex, maxLen, gvFlags, out var fieldValueStr, out fieldType);
                if (
                    result == GetValueResult.Success ||
                    result == GetValueResult.Delayed ||
                    result == GetValueResult.OnDemand
                ) {
                    var resultType = result == GetValueResult.Success ? fieldType : ContentFieldType.WideString;
                    new ContentValue(fieldValueStr, resultType).CopyTo(fieldValue);
                }

                // !!! may produce much trace info !!!
                TraceCall(TraceLevel.Verbose, $"{result.ToString()} - {fieldValueStr}");
            }
            catch (Exception ex) {
                ProcessException(ex);
                result = GetValueResult.NoSuchField;
            }

            return result == GetValueResult.Success ? (int) fieldType : (int) result;
        }

        #endregion FsContentGetValue

        #region FsContentStopGetValue

        [DllExport(EntryPoint = "FsContentStopGetValue")]
        public static void StopGetValue([MarshalAs(UnmanagedType.LPStr)] string fileName)
        {
            StopGetValueW(fileName);
        }

        [DllExport(EntryPoint = "FsContentStopGetValueW")]
        public static void StopGetValueW([MarshalAs(UnmanagedType.LPWStr)] string fileName)
        {
            _callSignature = "ContentStopGetValue";
            try {
                fileName = fileName.Substring(1);
                ContentPlugin.StopGetValue(fileName);

                TraceCall(TraceLevel.Info, null);
            }
            catch (Exception ex) {
                ProcessException(ex);
            }
        }

        #endregion FsContentStopGetValue

        #region FsContentGetDefaultSortOrder

        [DllExport(EntryPoint = "FsContentGetDefaultSortOrder")]
        public static int GetDefaultSortOrder(int fieldIndex)
        {
            var result = DefaultSortOrder.Asc;
            _callSignature = $"ContentGetDefaultSortOrder ({fieldIndex})";
            try {
                result = ContentPlugin.GetDefaultSortOrder(fieldIndex);

                TraceCall(TraceLevel.Info, result.ToString());
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return (int) result;
        }

        #endregion FsContentStopGetValue

        #region FsContentPluginUnloading

        [DllExport(EntryPoint = "FsContentPluginUnloading")]
        public static void PluginUnloading()
        {
            if (ContentPlugin != null) {
                _callSignature = "ContentPluginUnloading";
                try {
                    ContentPlugin.PluginUnloading();

                    TraceCall(TraceLevel.Info, null);
                }
                catch (Exception ex) {
                    ProcessException(ex);
                }
            }
        }

        #endregion FsContentPluginUnloading

        #region FsContentGetSupportedFieldFlags

        [DllExport(EntryPoint = "FsContentGetSupportedFieldFlags")]
        public static int GetSupportedFieldFlags(int fieldIndex)
        {
            var result = SupportedFieldOptions.None;
            _callSignature = $"ContentGetSupportedFieldFlags ({fieldIndex})";
            try {
                result = ContentPlugin.GetSupportedFieldFlags(fieldIndex);

                TraceCall(TraceLevel.Verbose, result.ToString());
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return (int) result;
        }

        #endregion FsContentGetSupportedFieldFlags

        #region FsContentSetValue

        [DllExport(EntryPoint = "FsContentSetValue")]
        public static int SetValue([MarshalAs(UnmanagedType.LPStr)] string fileName, int fieldIndex, int unitIndex, int fieldType, IntPtr fieldValue, int flags)
        {
            return SetValueW(fileName, fieldIndex, unitIndex, fieldType, fieldValue, flags);
        }

        [DllExport(EntryPoint = "FsContentSetValueW")]
        public static int SetValueW([MarshalAs(UnmanagedType.LPWStr)] string fileName, int fieldIndex, int unitIndex, int fieldType, IntPtr fieldValue, int flags)
        {
            SetValueResult result;
            var fldType = (ContentFieldType) fieldType;
            var svFlags = (SetValueFlags) flags;
            fileName = fileName.Substring(1);
            _callSignature = $"ContentSetValue '{fileName}' ({fieldIndex}/{unitIndex}/{svFlags.ToString()})";
            try {
                var value = new ContentValue(fieldValue, fldType);
                result = ContentPlugin.SetValue(fileName, fieldIndex, unitIndex, fldType, value.StrValue, svFlags);

                TraceCall(TraceLevel.Info, $"{result.ToString()} - {value.StrValue}");
            }
            catch (Exception ex) {
                ProcessException(ex);
                result = SetValueResult.NoSuchField;
            }

            return (int) result;
        }

        #endregion FsContentSetValue

        #region FsContentGetDefaultView

        [DllExport(EntryPoint = "FsContentGetDefaultView")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool GetDefaultView(IntPtr viewContents, IntPtr viewHeaders, IntPtr viewWidths, IntPtr viewOptions, int maxLen)
        {
            var result = GetDefaultViewFs(out var contents, out var headers, out var widths, out var options, maxLen);
            if (result) {
                TcUtils.WriteStringAnsi(contents, viewContents, maxLen);
                TcUtils.WriteStringAnsi(headers, viewHeaders, maxLen);
                TcUtils.WriteStringAnsi(widths, viewWidths, maxLen);
                TcUtils.WriteStringAnsi(options, viewOptions, maxLen);

                return true;
            }

            return false;
        }

        [DllExport(EntryPoint = "FsContentGetDefaultViewW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static bool GetDefaultViewW(IntPtr viewContents, IntPtr viewHeaders, IntPtr viewWidths, IntPtr viewOptions, int maxLen)
        {
            var result = GetDefaultViewFs(out var contents, out var headers, out var widths, out var options, maxLen);
            if (result) {
                TcUtils.WriteStringUni(contents, viewContents, maxLen);
                TcUtils.WriteStringUni(headers, viewHeaders, maxLen);
                TcUtils.WriteStringUni(widths, viewWidths, maxLen);
                TcUtils.WriteStringUni(options, viewOptions, maxLen);

                return true;
            }

            return false;
        }

        public static bool GetDefaultViewFs(out string viewContents, out string viewHeaders, out string viewWidths, out string viewOptions, int maxLen)
        {
            var result = false;
            viewContents = null;
            viewHeaders = null;
            viewWidths = null;
            viewOptions = null;
            _callSignature = "ContentGetDefaultView";
            try {
                if (ContentPlugin != null) {
                    result = ContentPlugin.GetDefaultView(out viewContents, out viewHeaders, out viewWidths, out viewOptions, maxLen);

                    TraceCall(TraceLevel.Info, $"\n  {viewContents}\n  {viewHeaders}\n  {viewWidths}\n  {viewOptions}");
                }
            }
            catch (Exception ex) {
                ProcessException(ex);
            }

            return result;
        }

        #endregion FsContentGetDefaultView

        #endregion Content Plugin Exported Functions


        #region Tracing & Exceptions

        private static void ProcessException(Exception ex)
        {
            TcPluginLoader.ProcessException(_plugin, _callSignature, ex);
        }

        private static void TraceCall(TraceLevel level, string result)
        {
#if TRACE
            TcTrace.TraceCall(_plugin, level, _callSignature, result);
            _callSignature = null;
#endif
        }

        #endregion Tracing & Exceptions
    }
}
