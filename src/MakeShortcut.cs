using System;
using System.Runtime.InteropServices;
using System.Text;

// Writes a .lnk with an explicit AppUserModelID, which WScript.Shell can't do
// and PowerShell can't reach (no casting COM objects to interop interfaces).
//
// Usage: MakeShortcut.exe <lnkPath> <targetExe> <workDir> <iconPath> <appId> <description> [arguments]

class MakeShortcut {
	static int Main(string[] args) {
		if (args.Length < 6) {
			Console.WriteLine("usage: MakeShortcut.exe <lnkPath> <targetExe> <workDir> <iconPath> <appId> <description> [arguments]");
			return 1;
		}

		string lnkPath = args[0], targetExe = args[1], workDir = args[2];
		string iconPath = args[3], appId = args[4], description = args[5];
		string arguments = args.Length > 6 ? args[6] : "";

		Type shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
		object shellLink = Activator.CreateInstance(shellLinkType);

		var link = (IShellLinkW)shellLink;
		link.SetPath(targetExe);
		link.SetWorkingDirectory(workDir);
		if (iconPath.Length > 0)
			link.SetIconLocation(iconPath, 0);
		link.SetDescription(description);
		if (arguments.Length > 0)
			link.SetArguments(arguments);

		var store = (IPropertyStore)shellLink;
		var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
		var value = new PROPVARIANT { vt = 31, pointerValue = Marshal.StringToCoTaskMemUni(appId) };
		store.SetValue(ref key, ref value);
		store.Commit();
		PropVariantClear(ref value);

		((IPersistFile)shellLink).Save(lnkPath, true);
		Marshal.ReleaseComObject(shellLink);

		Console.WriteLine("wrote " + lnkPath);
		return 0;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct PROPERTYKEY { public Guid fmtid; public int pid; }

	[StructLayout(LayoutKind.Sequential)]
	struct PROPVARIANT {
		public ushort vt;
		public ushort reserved1, reserved2, reserved3;
		public IntPtr pointerValue;
		public IntPtr pointerValue2;
	}

	[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IShellLinkW {
		void GetPath(StringBuilder file, int cch, IntPtr fd, uint flags);
		void GetIDList(out IntPtr pidl);
		void SetIDList(IntPtr pidl);
		void GetDescription(StringBuilder name, int cch);
		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
		void GetWorkingDirectory(StringBuilder dir, int cch);
		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
		void GetArguments(StringBuilder args, int cch);
		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
		void GetHotkey(out ushort hotkey);
		void SetHotkey(ushort hotkey);
		void GetShowCmd(out int cmd);
		void SetShowCmd(int cmd);
		void GetIconLocation(StringBuilder path, int cch, out int index);
		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
		void Resolve(IntPtr hwnd, uint flags);
		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
	}

	[ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IPersistFile {
		void GetClassID(out Guid classId);
		[PreserveSig] int IsDirty();
		void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
		void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
		void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
		void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
	}

	[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IPropertyStore {
		int GetCount(out uint count);
		int GetAt(uint index, out PROPERTYKEY key);
		int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
		int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
		int Commit();
	}

	[DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);
}
